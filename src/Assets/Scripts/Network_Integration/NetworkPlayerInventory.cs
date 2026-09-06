using System;
using PurrNet;
using UnityEngine;

public partial class NetworkPlayer
{
    // Lifetime follows this connected player. A new connection starts with an empty ledger.
    public readonly ServerInventoryLedger ServerInventory = new();
    private Vector3 _inventoryDeathPosition;
    public static NetworkPlayer Local
    {
        get
        {
            foreach (var pawn in PlayerPawn.All)
                if (pawn != null && pawn.isSpawned && pawn.isOwner) return pawn.GetComponent<NetworkPlayer>();
            return null;
        }
    }

    public static NetworkPlayer FindPlayer(PlayerID id)
    {
        foreach (var pawn in PlayerPawn.All)
            if (pawn != null && pawn.isSpawned && pawn.owner.HasValue && pawn.owner.Value == id)
                return pawn.GetComponent<NetworkPlayer>();
        return null;
    }

    public bool CanUseStation(Component station, float distance = 4f)
    {
        var vitals = GetComponent<PlayerVitals>();
        if (!isServer || station == null || vitals == null || vitals.IsDead) return false;
        if (station is Behaviour behaviour && !behaviour.isActiveAndEnabled) return false;
        Vector3 closest = station.TryGetComponent<Collider>(out var c) && c.enabled
            ? c.ClosestPoint(transform.position) : station.transform.position;
        return Vector3.SqrMagnitude(closest - transform.position) <= distance * distance;
    }

    public static InventoryReceipt ReceiptFor(Item item)
    {
        var w = item as WeaponItem;
        return new InventoryReceipt { token = Guid.NewGuid().ToString("N"), itemName = item.ItemName,
            weaponDataName = w != null && w.WeaponData != null ? w.WeaponData.name : string.Empty,
            price = item.Price, rarity = (int)item.Rarity, upgradeTier = w != null ? w.UpgradeTier : 0,
            ammo = w != null ? w.CurrentAmmo : 0,
            category = item.Definition != null ? (int)item.Definition.category : -1 };
    }

    public void CompleteItemUse(string token, string message)
    {
        if (isServer && owner.HasValue) ItemUsedTargetRpc(owner.Value, token, message);
    }

    [TargetRpc]
    private void ItemUsedTargetRpc(PlayerID target, string token, string message)
    {
        if (InstanceHandler.TryGetInstance(out InventoryManager inventory)) inventory.RemoveReceipt(token);
        if (!string.IsNullOrEmpty(message)) PromptPresenter.ShowPrompt(message);
    }

    [ServerRpc]
    public void DiscardReceiptServerRpc(string token)
    {
        // Owner may abandon a held item, but this grants no currency, item, or progression reward.
        ServerInventory.Consume(token, out _);
    }

    [ServerRpc]
    public void RequestDropReceiptServerRpc(string token)
    {
        if (!ServerInventory.TryGet(token, out var entry)) return;
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        var prefab = inventory != null ? inventory.GetItemPrefab(entry.itemName, entry.weaponDataName) : null;
        var vitals = GetComponent<PlayerVitals>();
        var origin = vitals != null && vitals.IsDead ? _inventoryDeathPosition : transform.position;
        if (!TrySpawnReceipt(prefab, entry, origin + Vector3.up + transform.forward))
        {
            ShowMessageTargetRpc(owner.Value, "Could not drop this item. It remains in your inventory.");
            return;
        }
        ServerInventory.Consume(token, out _);
        CompleteItemUse(token, null);
    }

    public static bool TrySpawnReceipt(GameObject prefab, InventoryReceipt entry, Vector3 position)
        => TrySpawnReceipt(prefab, entry, position, out _);

    public static bool TrySpawnReceipt(GameObject prefab, InventoryReceipt entry, Vector3 position, out Item spawned)
    {
        spawned = null;
        var manager = NetworkManager.main;
        if (manager == null || !manager.isServer || prefab == null || prefab.GetComponent<Item>() == null
            || manager.prefabProvider == null || !manager.prefabProvider.TryGetPrefabData(prefab, out _)) return false;
        GameObject obj = null;
        try
        {
            obj = UnityProxy.InstantiateDirectly(prefab, position, Quaternion.identity);
            var item = obj.GetComponent<Item>();
            item.SetPriceImmediateOnServer(entry.price);
            item.SetRarityImmediateOnServer(entry.rarity);
            if (item is WeaponItem weapon)
            {
                weapon.SetAmmoImmediateOnServer(entry.ammo);
                weapon.SetUpgradeTierImmediateOnServer(entry.upgradeTier);
            }
            item.Spawn(prefab);
            if (!item.isSpawned) { UnityEngine.Object.Destroy(obj); return false; }
            if (item.TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = false;
            spawned = item;
            return true;
        }
        catch (Exception e)
        {
            if (obj != null) UnityEngine.Object.Destroy(obj);
            Debug.LogException(e);
            return false;
        }
    }

    public void DropInventoryOnServerDeath()
    {
        if (!isServer) return;
        _inventoryDeathPosition = transform.position;
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        foreach (var entry in ServerInventory.Snapshot())
        {
            var prefab = inventory != null ? inventory.GetItemPrefab(entry.itemName, entry.weaponDataName) : null;
            var scatter = UnityEngine.Random.insideUnitCircle * 0.65f;
            if (!TrySpawnReceipt(prefab, entry, _inventoryDeathPosition + new Vector3(scatter.x, 1f, scatter.y))) continue;
            ServerInventory.Consume(entry.token, out _);
            CompleteItemUse(entry.token, null);
        }
    }

    [ServerRpc]
    public void ReportRemainingAmmoServerRpc(string token, int remaining)
    {
        // The existing weapon controller predicts shots locally. Its reports may only reduce ammo.
        ServerInventory.ReduceAmmo(token, remaining);
    }

    [ServerRpc]
    public void ReloadReceiptServerRpc(string weaponToken, string ammoToken)
    {
        if (GetComponent<PlayerVitals>().IsDead || !ServerInventory.TryGet(weaponToken, out var entry)) return;
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        var prefab = inventory != null ? inventory.GetItemPrefab(entry.itemName, entry.weaponDataName) : null;
        var weapon = prefab != null ? prefab.GetComponent<WeaponItem>() : null;
        if (weapon == null || weapon.WeaponData == null) return;
        int magazine = weapon.WeaponData.magazineSize;
        var bridge = GetComponent<WeaponInventoryBridge>();
        if (bridge != null)
            foreach (var recipe in bridge.UpgradeRecipes)
                if (recipe != null && recipe.baseItemName == entry.itemName)
                {
                    magazine = recipe.CalcMagazine(magazine, entry.upgradeTier);
                    break;
                }
        if (ServerInventory.Reload(weaponToken, ammoToken, weapon.WeaponData.GetAmmoItemNameKey(), magazine))
            CompleteItemUse(ammoToken, null);
    }

    public void ResetInventoryOnRunRestart(string reason)
    {
        ServerInventory.Clear();
        if (isOwner && InstanceHandler.TryGetInstance(out InventoryManager inventory)) inventory.ClearReceipts();
    }
}

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using PurrNet;

/// <summary>
/// 총기 상점 - 매일 4개의 아이템을 스폰 (총기 + 탄약)
/// TimeManager와 연동하여 매일 9시에 리셋
/// </summary>
public class WeaponShop : ShopSpawnerBase
{
    private static string _lastReplicationDebug = "none";
    private int?[] _currentLayout;
    private const int ClientLayoutRequestAttempts = 5;
    private const float ClientLayoutRequestInterval = 1.0f;

    [Header("Weapon Shop Catalog")]
    [SerializeField] private GameObject[] shopPrefabs;

    [Header("Spawn Rules")]
    [SerializeField] private int guaranteedWeapons = 1;
    [SerializeField] private float weaponSpawnChance = 0.5f;

    protected override string ShopLogName => "WeaponShop";
    public int PrefabSlotCount => shopPrefabs?.Length ?? 0;
    public int WeaponPrefabCount => CountRegisteredPrefabs(isWeapon: true);
    public int AmmoPrefabCount => CountRegisteredPrefabs(isWeapon: false);

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isServer)
            StartCoroutine(RequestCurrentLayoutAfterDelay());
    }

    protected override void ValidateConfiguration()
    {
        ValidateSpawnPoints();
        _currentLayout = new int?[spawnPoints != null ? spawnPoints.Length : 0];

        if (shopPrefabs == null || shopPrefabs.Length == 0)
        {
            Debug.LogWarning("[WeaponShop] No shop prefabs assigned!");
            return;
        }

        for (int i = 0; i < shopPrefabs.Length; i++)
        {
            GameObject prefab = shopPrefabs[i];
            if (prefab == null)
            {
                Debug.LogWarning($"[WeaponShop] Shop catalog contains a null entry at index {i}");
                continue;
            }

            if (prefab.GetComponent<WeaponShopItem>() == null)
                Debug.LogError($"[WeaponShop] Shop prefab {prefab.name} missing WeaponShopItem component!");
        }

        if (WeaponPrefabCount == 0)
            Debug.LogWarning("[WeaponShop] No weapon entries assigned!");
        if (AmmoPrefabCount == 0)
            Debug.LogWarning("[WeaponShop] No ammo entries assigned!");
    }

    protected override IEnumerator SpawnDailyEntriesCoroutine()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[WeaponShop] No spawn points available!");
            yield break;
        }

        List<int> itemsToSpawn = DetermineItemsToSpawn();
        itemsToSpawn = itemsToSpawn.OrderBy(x => Random.value).ToList();

        int spawnCount = Mathf.Min(spawnPoints.Length, itemsToSpawn.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            if (spawnPoints[i] != null)
            {
                SpawnItemAtPosition(itemsToSpawn[i], spawnPoints[i]);

                if (spawnInterval > 0 && i < spawnCount - 1)
                    yield return new WaitForSeconds(spawnInterval);
            }
        }

        Debug.Log($"[WeaponShop] Daily spawn complete: {spawnedEntries.Count} items spawned");
    }

    private List<int> DetermineItemsToSpawn()
    {
        List<int> items = new();
        List<int> weaponPrefabIndices = GetValidPrefabIndices(isWeapon: true);
        List<int> ammoPrefabIndices = GetValidPrefabIndices(isWeapon: false);

        for (int i = 0; i < guaranteedWeapons && items.Count < 4; i++)
        {
            if (weaponPrefabIndices.Count > 0)
                items.Add(weaponPrefabIndices[Random.Range(0, weaponPrefabIndices.Count)]);
        }

        while (items.Count < 4)
        {
            float rand = Random.Range(0f, 1f);

            if (rand < weaponSpawnChance && weaponPrefabIndices.Count > 0)
                items.Add(weaponPrefabIndices[Random.Range(0, weaponPrefabIndices.Count)]);
            else if (ammoPrefabIndices.Count > 0)
                items.Add(ammoPrefabIndices[Random.Range(0, ammoPrefabIndices.Count)]);
            else
                break;
        }

        if (debugMode)
        {
            string itemList = string.Join(", ", items.Select(DescribePrefab));
            Debug.Log($"[WeaponShop] Items to spawn: {itemList}");
        }

        return items;
    }

    private List<int> GetValidPrefabIndices(bool isWeapon)
    {
        List<int> indices = new();
        if (shopPrefabs == null)
            return indices;

        for (int i = 0; i < shopPrefabs.Length; i++)
        {
            WeaponShopItem item = shopPrefabs[i] != null
                ? shopPrefabs[i].GetComponent<WeaponShopItem>()
                : null;
            if (item != null && item.IsWeaponType == isWeapon)
                indices.Add(i);
        }

        return indices;
    }

    private int CountRegisteredPrefabs(bool isWeapon)
    {
        return GetValidPrefabIndices(isWeapon).Count;
    }

    private string DescribePrefab(int prefabIndex)
    {
        return TryGetPrefab(prefabIndex, out GameObject prefab, out WeaponShopItem item)
            ? $"{(item.IsWeaponType ? "Weapon" : "Ammo")}:{prefab.name}"
            : $"Invalid:{prefabIndex}";
    }

    private bool TryGetPrefab(int prefabIndex, out GameObject prefab, out WeaponShopItem item)
    {
        prefab = null;
        item = null;

        if (shopPrefabs == null || prefabIndex < 0 || prefabIndex >= shopPrefabs.Length)
            return false;

        prefab = shopPrefabs[prefabIndex];
        if (prefab == null)
            return false;

        item = prefab.GetComponent<WeaponShopItem>();
        return item != null;
    }

    private void SpawnItemAtPosition(int prefabIndex, Transform spawnPoint)
    {
        if (!TryGetPrefab(prefabIndex, out GameObject prefab, out WeaponShopItem prefabItem))
        {
            Debug.LogError($"[WeaponShop] Invalid shop prefab index {prefabIndex}");
            return;
        }

        if (!TryGetSpawnPointIndex(spawnPoint, out int spawnIndex))
        {
            Debug.LogError("[WeaponShop] Failed to resolve spawn point index for item spawn");
            return;
        }

        string itemTypeName = prefabItem.IsWeaponType ? "weapon" : "ammo";
        Debug.Log($"[WeaponShop] Server spawning entry type={itemTypeName}, prefabIndex={prefabIndex}, spawnIndex={spawnIndex}, prefab={prefab.name}");
        _lastReplicationDebug = $"server:{itemTypeName}:{prefabIndex}:{spawnIndex}:{prefab.name}";

        GameObject instance = InstantiateEntryPrefab(prefab, spawnPoint);
        if (instance == null)
        {
            Debug.LogError($"[WeaponShop] Failed to instantiate {prefab.name}");
            return;
        }

        WeaponShopItem shopItem = instance.GetComponent<WeaponShopItem>();
        if (shopItem == null)
        {
            Debug.LogError($"[WeaponShop] WeaponShopItem component missing on {prefab.name}!");
            Destroy(instance);
            return;
        }

        TrackSpawnedEntry(instance);
        if (_currentLayout != null && spawnIndex >= 0 && spawnIndex < _currentLayout.Length)
            _currentLayout[spawnIndex] = prefabIndex;
        TriggerSpawnEffect(instance.transform.position);
        SpawnEntryOnClientsRpc(prefabIndex, spawnIndex);

        if (debugMode)
        {
            Debug.Log($"[WeaponShop] Spawned {shopItem.ItemName} at {spawnPoint.name} (as child)");
        }
    }

    private IEnumerator RequestCurrentLayoutAfterDelay()
    {
        for (int attempt = 1; attempt <= ClientLayoutRequestAttempts; attempt++)
        {
            yield return new WaitForSeconds(ClientLayoutRequestInterval);

            var networkManager = NetworkManager.main;
            if (networkManager == null || !networkManager.isClient)
                continue;

            if (GetComponentsInChildren<WeaponShopItem>(true).Length > 0)
            {
                _lastReplicationDebug = $"client-layout-already-present:{attempt}";
                yield break;
            }

            _lastReplicationDebug = $"client-layout-request:{attempt}";
            RequestCurrentLayoutRpc();
        }
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestCurrentLayoutRpc()
    {
        if (_currentLayout == null)
            return;

        for (int i = 0; i < _currentLayout.Length; i++)
        {
            if (!_currentLayout[i].HasValue)
                continue;

            SpawnEntryOnClientsRpc(_currentLayout[i].Value, i);
        }
    }

    [ObserversRpc]
    private void SpawnEntryOnClientsRpc(int prefabIndex, int spawnIndex)
    {
        if (isServer)
            return;

        Transform spawnPoint = GetSpawnPoint(spawnIndex);
        if (spawnPoint == null)
        {
            Debug.LogWarning($"[WeaponShop] Client spawn skipped - spawn point {spawnIndex} missing");
            _lastReplicationDebug = $"client-skip:missing-spawn:{spawnIndex}";
            return;
        }

        if (spawnPoint.GetComponentInChildren<WeaponShopItem>() != null)
        {
            _lastReplicationDebug = $"client-skip:already-present:{spawnIndex}";
            return;
        }

        if (!TryGetPrefab(prefabIndex, out GameObject prefab, out WeaponShopItem prefabItem))
        {
            Debug.LogWarning($"[WeaponShop] Client spawn skipped - invalid prefab index {prefabIndex} (catalogLength={PrefabSlotCount})");
            _lastReplicationDebug = $"client-skip:invalid-prefab:{prefabIndex}:{PrefabSlotCount}";
            return;
        }

        Debug.Log($"[WeaponShop] Client spawning entry type={(prefabItem.IsWeaponType ? "weapon" : "ammo")}, prefabIndex={prefabIndex}, spawnIndex={spawnIndex}, prefab={prefab.name}");

        GameObject instance = InstantiateEntryPrefab(prefab, spawnPoint);
        if (instance == null)
        {
            Debug.LogWarning("[WeaponShop] Client spawn skipped - InstantiateEntryPrefab returned null");
            _lastReplicationDebug = $"client-skip:instantiate-null:{prefabIndex}:{spawnIndex}";
            return;
        }

        WeaponShopItem shopItem = instance.GetComponent<WeaponShopItem>();
        if (shopItem == null)
        {
            Debug.LogWarning($"[WeaponShop] Client spawn destroyed - WeaponShopItem missing on {prefab.name}");
            _lastReplicationDebug = $"client-skip:missing-component:{prefab.name}";
            Destroy(instance);
            return;
        }

        TrackSpawnedEntry(instance);
        Debug.Log($"[WeaponShop] Client spawned visual shop entry: {shopItem.ItemName}");
        _lastReplicationDebug = $"client-ok:{shopItem.ItemName}:{prefabIndex}:{spawnIndex}";
    }

    public static string GetReplicationDebug()
    {
        return _lastReplicationDebug;
    }

    public bool TryRequestPurchase(WeaponShopItem item, string purchaseToken)
    {
        if (item == null || item.transform.parent == null)
            return false;

        if (!TryGetSpawnPointIndex(item.transform.parent, out int spawnIndex))
            return false;

        if (isServer)
        {
            if (NetworkPlayer.Local == null || !NetworkPlayer.Local.owner.HasValue) return false;
            ProcessPurchaseOnServer(spawnIndex, purchaseToken, NetworkPlayer.Local.owner.Value);
        }
        else
        {
            RequestPurchaseAtIndexRpc(spawnIndex, purchaseToken);
        }

        return true;
    }

    public bool TryRequestPurchaseFeedback(WeaponShopItem item, bool success)
    {
        if (item == null || item.transform.parent == null)
            return false;

        if (!TryGetSpawnPointIndex(item.transform.parent, out int spawnIndex))
            return false;

        if (isServer)
            PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
        else
            RequestPurchaseFeedbackAtIndexRpc(spawnIndex, success);

        return true;
    }

    private WeaponShopItem GetWeaponShopItemAtIndex(int spawnIndex)
    {
        Transform spawnPoint = GetSpawnPoint(spawnIndex);
        if (spawnPoint == null || spawnPoint.childCount == 0)
            return null;

        return spawnPoint.GetComponentInChildren<WeaponShopItem>();
    }

    private void ProcessPurchaseOnServer(int spawnIndex, string purchaseToken, PlayerID sender)
    {
        WeaponShopItem item = GetWeaponShopItemAtIndex(spawnIndex);
        var player = NetworkPlayer.FindPlayer(sender);
        var prefab = item != null ? item.PurchasedItemPrefab : null;
        if (item == null || item.SoldOnServer || player == null || !player.CanUseStation(item)
            || prefab == null || prefab.GetComponent<Item>() == null
            || NetworkManager.main.prefabProvider == null || !NetworkManager.main.prefabProvider.TryGetPrefabData(prefab, out _))
        {
            // Already sold: the client-side removal is a delayed coroutine, so a second request
            // for the same entry can still arrive (audit M1).
            ResolvePurchaseAtIndexRpc(spawnIndex, purchaseToken, false, 0, default, feedbackWasBroadcast: false);
            return;
        }

        // Initialize authored prices/ammo locally before charging; this is never a world spawn.
        var template = UnityProxy.InstantiateDirectly(prefab);
        template.SetActive(false);
        var receipt = NetworkPlayer.ReceiptFor(template.GetComponent<Item>());
        UnityProxy.DestroyDirectly(template);
        bool success = item.TryExecutePurchaseOnServer(out int currentCurrency);
        if (success)
        {
            player.ServerInventory.Grant(receipt);
            item.SoldOnServer = true;
        }
        PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
        ResolvePurchaseAtIndexRpc(spawnIndex, purchaseToken, success, currentCurrency, receipt, feedbackWasBroadcast: true);

        if (success)
            RemovePurchasedEntryAtIndexRpc(spawnIndex);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestPurchaseAtIndexRpc(int spawnIndex, string purchaseToken, RPCInfo info = default)
    {
        ProcessPurchaseOnServer(spawnIndex, purchaseToken, info.sender);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestPurchaseFeedbackAtIndexRpc(int spawnIndex, bool success)
    {
        PlayPurchaseFeedbackAtIndexRpc(spawnIndex, success);
    }

    [ObserversRpc]
    private void ResolvePurchaseAtIndexRpc(int spawnIndex, string purchaseToken, bool success, int currentCurrency, InventoryReceipt receipt, bool feedbackWasBroadcast)
    {
        WeaponShopItem item = GetWeaponShopItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.ResolvePurchaseLocally(purchaseToken, success, currentCurrency, receipt, feedbackWasBroadcast);
    }

    [ObserversRpc]
    private void PlayPurchaseFeedbackAtIndexRpc(int spawnIndex, bool success)
    {
        WeaponShopItem item = GetWeaponShopItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.PlayPurchaseAudioFromNetwork(success);
    }

    [ObserversRpc]
    private void RemovePurchasedEntryAtIndexRpc(int spawnIndex)
    {
        WeaponShopItem item = GetWeaponShopItemAtIndex(spawnIndex);
        if (item == null)
            return;

        item.RemovePurchasedEntryLocally();
    }

    #region Debug Methods

    [ContextMenu("Manual Reset Shop")]
    private void ManualResetShop()
    {
        TriggerManualReset();
    }

    [ContextMenu("Clear All Items")]
    private void DebugClearAllItems()
    {
        ClearTrackedEntriesForDebug();
    }

    [ContextMenu("Spawn Single Weapon")]
    private void DebugSpawnWeapon()
    {
        int prefabIndex = GetFirstPrefabIndex(isWeapon: true);
        if (Application.isPlaying && isServer && spawnPoints.Length > 0 && prefabIndex >= 0)
            SpawnItemAtPosition(prefabIndex, spawnPoints[0]);
    }

    [ContextMenu("Spawn Single Ammo")]
    private void DebugSpawnAmmo()
    {
        int prefabIndex = GetFirstPrefabIndex(isWeapon: false);
        if (Application.isPlaying && isServer && spawnPoints.Length > 0 && prefabIndex >= 0)
            SpawnItemAtPosition(prefabIndex, spawnPoints[0]);
    }

    private int GetFirstPrefabIndex(bool isWeapon)
    {
        List<int> indices = GetValidPrefabIndices(isWeapon);
        return indices.Count > 0 ? indices[0] : -1;
    }

    #endregion
}

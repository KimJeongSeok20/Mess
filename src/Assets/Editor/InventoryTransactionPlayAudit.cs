using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using PurrNet;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in live StartMap transaction checks. Fixtures are spawned through the registered
/// network prefabs and picked up through Item.Interact; no direct inventory grants are used.</summary>
public static class InventoryTransactionPlayAudit
{
    // PurrNet emits the callable RPC wrapper as public; always call it, never the _Original body.
    private const BindingFlags RpcMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static IEnumerator _steps;
    private static double _next;
    private static readonly List<string> Checks = new();
    public static string Status { get; private set; } = "idle";
    private static string _output;
    private static NetworkPlayer Player => NetworkPlayer.Local;
    private static InventoryManager Inventory => InstanceHandler.GetInstance<InventoryManager>();

    public static string Start(string output)
    {
        if (!Application.isPlaying || Player == null || !Player.isServer) return "Requires a running StartMap host.";
        if (_steps != null) return Status;
        _output = output;
        Checks.Clear();
        Status = "running";
        _steps = Run();
        _next = 0;
        EditorApplication.update += Tick;
        return Status;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        try
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode ended before completion.");
            if (_steps.MoveNext()) { _next = EditorApplication.timeSinceStartup + 1.0; return; }
            Finish("passed");
        }
        catch (Exception e) { Finish("failed: " + e); }
    }

    private static void Finish(string status)
    {
        Status = status;
        _steps = null;
        EditorApplication.update -= Tick;
        Directory.CreateDirectory(Path.GetDirectoryName(_output));
        File.WriteAllText(_output, JsonConvert.SerializeObject(new { status, checks = Checks,
            utc = DateTime.UtcNow, multiplayer = "UNVERIFIED", fun = "UNREVIEWED" }, Formatting.Indented));
    }

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new InvalidOperationException(name);
        Checks.Add(name);
        Status = "running: " + name;
    }

    public static void MoveLocalTo(Vector3 position)
    {
        var controller = Player.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        Player.transform.position = position;
        if (enabled) controller.enabled = true;
        Physics.SyncTransforms();
    }

    public static Item SpawnFixture(string name, int price = 340, int rarity = 2, int ammo = 13)
    {
        var prefab = Inventory.GetItemPrefab(name);
        if (prefab == null) throw new InvalidOperationException("No fixture prefab: " + name);
        var entry = NetworkPlayer.ReceiptFor(prefab.GetComponent<Item>());
        entry.price = price; entry.rarity = rarity; entry.ammo = ammo;
        if (!NetworkPlayer.TrySpawnReceipt(prefab, entry, Player.transform.position + Vector3.up + Player.transform.forward, out var item))
            throw new InvalidOperationException("Unregistered fixture: " + name);
        return item;
    }

    public static void Request(Component station, string method, params object[] args)
    {
        var rpc = station.GetType().GetMethod(method, RpcMethods)
            ?? throw new MissingMethodException(station.GetType().Name, method);
        rpc.Invoke(station, args);
    }

    private static IEnumerator Run()
    {
        var currency = InstanceHandler.GetInstance<CurrencyManager>();
        var mailbox = UnityEngine.Object.FindFirstObjectByType<Mailbox>();
        var processor = UnityEngine.Object.FindFirstObjectByType<CorpseProcessor>();
        var anvil = UnityEngine.Object.FindFirstObjectByType<AnvilInteraction>();
        Check(currency != null && mailbox != null && processor != null && anvil != null, "StartMap services exist");
        Check(Player.ServerInventory.Count == 0, "Clean inventory baseline");
        var item = SpawnFixture("Banana");
        yield return null;
        item.Interact();
        yield return null;
        var token = Inventory.FindReceipt("Banana");
        Check(!string.IsNullOrEmpty(token) && Player.ServerInventory.TryGet(token, out _), "Pickup grants matching server and local receipt");
        Check(item == null || !item.isSpawned, "Picked world item despawns");
        Player.RequestDropReceiptServerRpc(token);
        Player.RequestDropReceiptServerRpc(token);
        yield return null;
        var dropped = UnityEngine.Object.FindObjectsByType<Item>(FindObjectsSortMode.None)
            .Where(x => x.isSpawned && x.ItemName == "Banana" && x.Price == 340 && Vector3.Distance(x.transform.position, Player.transform.position) < 5f).ToArray();
        Check(dropped.Length == 1 && Inventory.FindReceipt("Banana") == null, "Duplicate drop creates exactly one item and clears local slot");
        Check((int)dropped[0].Rarity == 2, "Dropped trophy grade is retained");
        dropped[0].Interact();
        yield return null;
        token = Inventory.FindReceipt("Banana");
        Check(!string.IsNullOrEmpty(token), "Dropped item can be repicked");
        int beforeSale = currency.SharedCurrency;
        // An out-of-range request must leave both the receipt and money intact.
        MoveLocalTo(mailbox.transform.position + Vector3.forward * 20f);
        Request(mailbox, "SellReceiptServerRpc", token, default(RPCInfo));
        yield return null;
        Check(currency.SharedCurrency == beforeSale && Player.ServerInventory.TryGet(token, out _), "Distant sale is rejected without consuming item");
        MoveLocalTo(mailbox.transform.position + Vector3.up);
        Request(mailbox, "SellReceiptServerRpc", token, default(RPCInfo));
        Request(mailbox, "SellReceiptServerRpc", token, default(RPCInfo));
        yield return null;
        Check(currency.SharedCurrency == beforeSale + 340 && Inventory.FindReceipt("Banana") == null, "Sale credits server price once and removes exact item");
        int points = TeamProgress.SkillPointsEarned;
        MoveLocalTo(processor.transform.position + Vector3.up);
        Request(processor, "ProcessCorpseServerRpc", "Octopus", default(RPCInfo));
        yield return null;
        Check(TeamProgress.SkillPointsEarned == points, "Octopus name alone gives no points");
        item = SpawnFixture("Octopus", 180, 0);
        yield return null;
        item.Interact();
        yield return null;
        token = Inventory.FindReceipt("Octopus");
        Check(!string.IsNullOrEmpty(token), "Octopus trophy pickup obtains ownership");
        var priorOrbs = new HashSet<SkillPointOrb>(UnityEngine.Object.FindObjectsByType<SkillPointOrb>(FindObjectsSortMode.None));
        Request(processor, "ProcessCorpseServerRpc", token, default(RPCInfo));
        Request(processor, "ProcessCorpseServerRpc", token, default(RPCInfo));
        yield return null;
        var rewardOrbs = UnityEngine.Object.FindObjectsByType<SkillPointOrb>(FindObjectsSortMode.None)
            .Where(orb => !priorOrbs.Contains(orb)).ToArray();
        Check(rewardOrbs.Length == 1 && TeamProgress.SkillPointsEarned == points && Inventory.FindReceipt("Octopus") == null,
            "Processing consumes the trophy once and creates one orb without granting points directly");
        MoveLocalTo(rewardOrbs[0].transform.position - Vector3.up * 0.65f);
        yield return null;
        Check(TeamProgress.SkillPointsEarned == points + 1,
            "Collecting the processor orb grants its point once");
        MoveLocalTo(anvil.transform.position + Vector3.up);
        item = SpawnFixture("AK", 800, 0);
        yield return null;
        item.Interact();
        yield return null;
        token = Inventory.FindReceipt("AK");
        Check(!string.IsNullOrEmpty(token), "Weapon pickup obtains receipt");
        currency.AddCurrency(10000);
        int beforeUpgrade = currency.SharedCurrency;
        Request(anvil, "RequestUpgradeRollServerRpc", token, token, default(RPCInfo));
        yield return null;
        Check(currency.SharedCurrency == beforeUpgrade && Player.ServerInventory.TryGet(token, out _), "Same weapon cannot also be its material");
        Request(anvil, "RequestUpgradeRollServerRpc", token, null, default(RPCInfo));
        yield return null;
        string upgraded = Inventory.FindReceipt("AK");
        Check(upgraded != token && Player.ServerInventory.TryGet(upgraded, out var weapon) && weapon.upgradeTier == 1 && weapon.ammo == 13,
            "Guaranteed first upgrade rotates receipt and preserves ammo");
        int afterUpgrade = currency.SharedCurrency;
        Request(anvil, "RequestUpgradeRollServerRpc", token, null, default(RPCInfo));
        yield return null;
        Check(currency.SharedCurrency == afterUpgrade && Inventory.FindReceipt("AK") == upgraded, "Replayed upgrade cannot charge or alter inventory");
        Player.RequestDropReceiptServerRpc(upgraded);
        yield return null;
        Check(Inventory.FindReceipt("AK") == null, "Upgraded weapon remains droppable");
    }
}

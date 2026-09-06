#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Reflection;
using PurrNet;
using UnityEngine;

// Opt-in commands for the existing standalone job runner. Requests use the generated RPC
// wrappers, including rejected/replayed requests; they never grant server inventory directly.
public partial class DebugRemoteControl
{
    private static string _validationReceipt;

    public static string TransactionProbe()
    {
        var player = NetworkPlayer.Local;
        if (player == null) return "NOT_READY";
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        var entries = (InventoryManager.InventoryItemData[])typeof(InventoryManager)
            .GetField("_inventoryData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory);
        var held = entries.Where(x => !string.IsNullOrEmpty(x.receiptToken)).ToArray();
        var currency = InstanceHandler.GetInstance<CurrencyManager>();
        return $"{(player.isServer ? "HOST_READY" : "CLIENT_READY")} players={PlayerPawn.All.Count()} held={held.Length} "
            + $"currency={currency.SharedCurrency} dead={player.GetComponent<PlayerVitals>().IsDead} points={TeamProgress.SkillPointsEarned} "
            + string.Join(";", held.Select(x => $"{x.itemName}:price={x.price}:rarity={x.rarity}:tier={x.upgradeTier}:ammo={x.currentAmmo}"));
    }

    public static string TransactionMoveTo(string station)
    {
        var target = TransactionStation(station);
        var player = NetworkPlayer.Local;
        if (player == null) throw new InvalidOperationException("Local player missing");
        Vector3 position = target.transform.position + Vector3.up;
        // Transaction fixtures do not require the production FPS rig or weapon assets.
        var controller = player.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        player.transform.position = position;
        Physics.SyncTransforms();
        if (enabled) controller.enabled = true;
        return "MOVED_TO_TRANSACTION_STATION " + station;
    }

    public static string TransactionPickup(string itemName)
    {
        var player = NetworkPlayer.Local;
        if (player == null) throw new InvalidOperationException("Local player missing");
        var item = UnityEngine.Object.FindObjectsByType<Item>(FindObjectsSortMode.None)
            .Where(x => x.isSpawned && x.enabled && x.ItemName == itemName
                && Vector3.Distance(player.transform.position, x.transform.position) <= 4f)
            .OrderBy(x => Vector3.SqrMagnitude(x.transform.position - player.transform.position)).FirstOrDefault();
        if (item == null) throw new InvalidOperationException("No nearby network item: " + itemName);
        item.Interact();
        return "PICKUP_REQUESTED " + itemName;
    }

    public static string TransactionRequest(string action, string itemName, int repeat = 1)
    {
        var inventory = InstanceHandler.GetInstance<InventoryManager>();
        string token = inventory.FindReceipt(itemName);
        if (!string.IsNullOrEmpty(token)) _validationReceipt = token;
        // A missing item replays the last receipt rather than fabricating ownership by name.
        token = token ?? _validationReceipt ?? itemName;
        for (int i = 0; i < Mathf.Clamp(repeat, 1, 3); i++)
        {
            if (action == "drop") NetworkPlayer.Local.RequestDropReceiptServerRpc(token);
            else if (action == "recharge") UnityEngine.Object.FindAnyObjectByType<NetworkDungeonController>().RequestRechargeServerRpc(token);
            else
            {
                var station = TransactionStation(action);
                string method = action switch
                {
                    "sale" => "SellReceiptServerRpc",
                    "process" => "ProcessCorpseServerRpc",
                    "gift" => "WrapReceiptServerRpc",
                    _ => throw new ArgumentException("Unknown transaction: " + action)
                };
                station.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(station, new object[] { token, default(RPCInfo) });
            }
        }
        return "REQUESTED " + action;
    }

    private static Component TransactionStation(string station) => station switch
    {
        "sale" => UnityEngine.Object.FindAnyObjectByType<Mailbox>(),
        "process" => UnityEngine.Object.FindAnyObjectByType<CorpseProcessor>(),
        "gift" => UnityEngine.Object.FindAnyObjectByType<GiftBox>(),
        "anvil" => UnityEngine.Object.FindAnyObjectByType<AnvilInteraction>(),
        _ => throw new ArgumentException("Unknown station: " + station)
    };
}
#endif

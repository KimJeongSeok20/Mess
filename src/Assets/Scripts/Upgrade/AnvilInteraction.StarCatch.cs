using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public partial class AnvilInteraction
{
    public const float StarCatchBonus = 0.05f;
    public const float StarCatchDuration = 3.6f;
    public const float StarCatchSweep = 1.2f;
    public const float StarCatchTargetMin = 0.43f;
    public const float StarCatchTargetMax = 0.57f;
    public event Action OnStarCatchStarted;
    public bool IsUpgradePending => _pendingUpgrade || _awaitingStarCatch;

    private struct StarCatchChallenge
    {
        public string id, main, material;
        public double issuedAt;
    }

    private readonly Dictionary<PlayerID, StarCatchChallenge> _starCatchChallenges = new();
    private string _localStarCatchId;
    private bool _awaitingStarCatch;

    public static float StarCatchPosition(float elapsed) => Mathf.PingPong(elapsed / StarCatchSweep, 1f);

    public static bool IsStarCatchHit(float elapsed)
    {
        if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0f || elapsed >= StarCatchDuration)
            return false;
        float position = StarCatchPosition(elapsed);
        return position >= StarCatchTargetMin && position <= StarCatchTargetMax;
    }

    public static bool IsValidUpgradePair(InventoryReceipt main, InventoryReceipt material)
    {
        return !string.IsNullOrEmpty(main.token) && !string.IsNullOrEmpty(material.token)
            && main.token != material.token && !string.IsNullOrEmpty(main.itemName)
            && main.itemName == material.itemName && main.upgradeTier >= 0 && material.upgradeTier == 0;
    }

    public bool BeginStarCatch(UpgradeOption option)
    {
        CacheManagers();
        if (IsUpgradePending || !CanUpgrade(option)) return false;
        if (!isSpawned)
        {
            OnStarCatchStarted?.Invoke();
            return true;
        }
        _inventoryManager.SaveActiveAmmo();
        string main = _inventoryManager.FindReceipt(option.recipe.baseItemName, highest: true);
        string material = _inventoryManager.FindReceipt(option.recipe.baseItemName, main);
        _awaitingStarCatch = true;
        RequestStarCatchServerRpc(main, material);
        return true;
    }

    public void CancelStarCatch()
    {
        _localStarCatchId = null;
        _awaitingStarCatch = false;
        if (isSpawned) CancelStarCatchServerRpc();
    }

    [ServerRpc(requireOwnership: false)]
    private void CancelStarCatchServerRpc(RPCInfo info = default) => _starCatchChallenges.Remove(info.sender);

    [ServerRpc(requireOwnership: false)]
    private void RequestStarCatchServerRpc(string main, string material, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this)
            || !player.ServerInventory.TryGet(main, out var held)
            || !player.ServerInventory.TryGet(material, out var spare) || !IsValidUpgradePair(held, spare))
        {
            StartStarCatchTargetRpc(info.sender, string.Empty);
            return;
        }
        var recipe = FindRecipe(held.itemName);
        if (recipe == null || held.upgradeTier >= recipe.EffectiveMaxTier)
        {
            StartStarCatchTargetRpc(info.sender, string.Empty);
            return;
        }
        var challenge = new StarCatchChallenge
        {
            id = Guid.NewGuid().ToString("N"), main = main, material = material,
            issuedAt = Time.realtimeSinceStartupAsDouble
        };
        _starCatchChallenges[info.sender] = challenge;
        StartStarCatchTargetRpc(info.sender, challenge.id);
    }

    [TargetRpc]
    private void StartStarCatchTargetRpc(PlayerID player, string id)
    {
        // Ignore a delayed reply after the panel was closed/cancelled.
        if (!_awaitingStarCatch) return;
        _awaitingStarCatch = false;
        _localStarCatchId = id;
        if (string.IsNullOrEmpty(id))
        {
            _prompt?.Show("Requires one matching unenhanced (+0) weapon.");
            RefreshOpenUi();
            return;
        }
        OnStarCatchStarted?.Invoke();
    }

    private bool ConsumeStarCatch(PlayerID player, string main, string material, string id, float elapsed)
    {
        if (!_starCatchChallenges.TryGetValue(player, out var challenge)) return false;
        _starCatchChallenges.Remove(player);
        double age = Time.realtimeSinceStartupAsDouble - challenge.issuedAt;
        return challenge.id == id && challenge.main == main && challenge.material == material
            && age >= 0d && age <= StarCatchDuration + 5d && elapsed <= age + 0.1d && IsStarCatchHit(elapsed);
    }
}

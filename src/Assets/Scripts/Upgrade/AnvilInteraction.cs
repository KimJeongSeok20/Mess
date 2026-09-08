using UnityEngine;
using PurrNet;
using System.Collections.Generic;
using Demo.Scripts.Runtime.Character;
using UnityEngine.Audio;

/// <summary>
/// 동적 업그레이드 옵션 (인벤토리 스캔 결과)
/// </summary>
public struct UpgradeOption
{
    public ItemUpgradeRecipe recipe;
    public int highestOwnedTier;   // 보유 중 최고 티어
    public int materialCount;      // 재료로 사용할 수 있는 아이템 수 (전체 - 1)
    public int resultTier;         // 강화 결과 = highestOwnedTier + 1
}

/// <summary>
/// 대장간 모루 상호작용
/// - 같은 무기 2개 이상 → 최고 티어 + 1 강화
/// - +999까지 무한 강화 지원
/// - 스탯은 ItemUpgradeRecipe의 공식으로 계산
/// </summary>
public class AnvilInteraction : AInteractable
{
    private static string _lastUpgradeAudioDebug = "none";

    [Header("Upgrade Recipes")]
    [SerializeField] private List<ItemUpgradeRecipe> recipes = new();

    [Header("Prompt")]
    [SerializeField] private string interactPrompt = "[F] Upgrade Items";

    [Header("Audio")]
    [SerializeField] private AudioClip upgradeSound;
    [SerializeField] [Range(0f, 1f)] private float upgradeSoundVolume = 1f;
    [SerializeField] private AudioClip upgradeFailureSound;
    [SerializeField, Range(0f, 1f)] private float upgradeFailureSoundVolume = 0.35f;
    [SerializeField] private AudioMixerGroup upgradeMixerGroup;
    [SerializeField] private AudioSource _audioSource;

    // 캐시
    private PromptPresenter _prompt;
    private InventoryManager _inventoryManager;
    private CurrencyManager _currencyManager;
    private AnvilUI _anvilUI;

    public IReadOnlyList<ItemUpgradeRecipe> Recipes => recipes;

    #region Lifecycle

    protected override void OnSpawned()
    {
        base.OnSpawned();
        CacheManagers();
        EnsureAudioSource();
    }

    private void CacheManagers()
    {
        if (_prompt == null)
            _prompt = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        if (_inventoryManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _inventoryManager))
                _inventoryManager = FindObjectOfType<InventoryManager>();
        }

        if (_currencyManager == null)
        {
            if (!InstanceHandler.TryGetInstance(out _currencyManager))
                _currencyManager = FindObjectOfType<CurrencyManager>();
        }
    }

    private void EnsureAudioSource()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1f;
        }

        _audioSource.minDistance = 1f;
        _audioSource.maxDistance = 12f;

        if (upgradeMixerGroup != null)
            _audioSource.outputAudioMixerGroup = upgradeMixerGroup;
    }

    #endregion

    #region Interaction

    public override void Interact(InteractionManager interactor)
    {
        CacheManagers();

        if (_inventoryManager == null)
        {
            Debug.LogError("[AnvilInteraction] InventoryManager not found!");
            return;
        }

        if (_anvilUI != null && _anvilUI.IsOpen)
        {
            _anvilUI.Close();
            return;
        }

        OpenUpgradeUI();
    }

    public override void OnHover()
    {
        base.OnHover();

        if (_prompt == null)
            _prompt = PromptPresenter.Instance ?? FindObjectOfType<PromptPresenter>();

        _prompt?.Show(interactPrompt);
    }

    public override void OnStopHover()
    {
        base.OnStopHover();
        _prompt?.Hide();
    }

    public override bool CanInteract()
    {
        return base.CanInteract() && gameObject.activeInHierarchy;
    }

    #endregion

    #region Upgrade UI

    private void OpenUpgradeUI()
    {
        if (_anvilUI == null)
            _anvilUI = FindObjectOfType<AnvilUI>(true);

        if (_anvilUI == null)
        {
            Debug.LogError("[AnvilInteraction] AnvilUI not found in scene! " +
                "Canvas 하위에 AnvilUI 컴포넌트가 있는 GameObject를 배치하세요.");
            return;
        }

        _anvilUI.Open(this, _inventoryManager, _currencyManager);
    }

    #endregion

    #region Dynamic Detection

    /// <summary>
    /// 인벤토리 스캔 → 강화 가능한 옵션 목록.
    /// 규칙(스타포스식): 무기 1개만 있어도 한 단계 도전 가능. 같은 무기가 더 있으면 재료로 써서 성공률 보너스.
    /// </summary>
    public List<UpgradeOption> DetectAvailableUpgrades()
    {
        var result = new List<UpgradeOption>();

        if (_inventoryManager == null) return result;

        foreach (var recipe in recipes)
        {
            if (recipe == null || !recipe.IsValid()) continue;

            string baseName = recipe.baseItemName;
            int totalCount = _inventoryManager.CountItemsByName(baseName);

            if (totalCount < 1) continue;

            int highestTier = _inventoryManager.GetHighestUpgradeTier(baseName);
            int resultTier = highestTier + 1;

            if (resultTier > recipe.EffectiveMaxTier) continue; // 최대 티어 초과

            result.Add(new UpgradeOption
            {
                recipe = recipe,
                highestOwnedTier = highestTier,
                materialCount = totalCount - 1, // 최고 티어 아이템 제외한 나머지 (선택 재료)
                resultTier = resultTier
            });
        }

        return result;
    }

    #endregion

    #region Upgrade Execution — StarForce roll

    public enum RollOutcome
    {
        Success,
        Fail,
        Downgrade,
        Rejected
    }

    /// <summary>(recipe, resultTier, outcome, chance) — UI 연출용.</summary>
    public event System.Action<ItemUpgradeRecipe, int, RollOutcome, float> OnRollResolved;

    private readonly Dictionary<string, int> _serverPity = new();
    private readonly Dictionary<string, int> _localPity = new();
    private bool _pendingUpgrade;
    private string _pendingBaseName;
    private int _pendingHighestTier;

    /// <summary>
    /// 업그레이드 가능 여부 확인 (무기 1개 이상)
    /// </summary>
    public bool CanUpgrade(UpgradeOption option)
    {
        if (_inventoryManager == null || option.recipe == null || !option.recipe.IsValid())
            return false;

        string baseName = option.recipe.baseItemName;
        int totalCount = _inventoryManager.CountItemsByName(baseName);

        if (totalCount < 1) return false;
        if (option.resultTier > option.recipe.EffectiveMaxTier) return false;

        return true;
    }

    /// <summary>클라이언트 표시용 성공률 (서버와 같은 공식; 퍼크는 로컬 추정).</summary>
    public float PreviewSuccessChance(UpgradeOption option, bool useMaterial)
    {
        if (option.recipe == null)
            return 0f;

        bool material = useMaterial && option.materialCount > 0;
        float perk = LocalSteadyHandsBonus() + (material ? LocalMasterSmithBonus() : 0f);
        return option.recipe.GetSuccessChance(option.resultTier, material, GetLocalPity(option.recipe, option.resultTier), perk);
    }

    public bool TryUpgrade(UpgradeOption option) => TryUpgrade(option, false);

    /// <summary>
    /// 강화 시도. 서버가 결제와 판정을 함께 처리하고(TargetRpc로 결과 통보) 클라이언트는 결과만 인벤토리에 적용한다.
    /// 오프라인(미스폰)에서는 로컬에서 굴린다.
    /// </summary>
    public bool TryUpgrade(UpgradeOption option, bool useMaterial)
    {
        CacheManagers();

        if (_inventoryManager == null || option.recipe == null)
            return false;

        var recipe = option.recipe;

        if (!recipe.IsValid())
        {
            Debug.LogError($"[AnvilInteraction] Invalid recipe: {recipe.name}");
            return false;
        }

        if (!CanUpgrade(option))
        {
            _prompt?.Show("You need the weapon in your inventory.");
            return false;
        }

        string baseName = recipe.baseItemName;
        int highestTier = _inventoryManager.GetHighestUpgradeTier(baseName);
        int resultTier = highestTier + 1;
        useMaterial = useMaterial && _inventoryManager.CountItemsByName(baseName) >= 2;

        if (!isSpawned)
        {
            RollOfflineAndApply(recipe, highestTier, useMaterial);
            return true;
        }

        if (_pendingUpgrade)
        {
            _prompt?.Show("Upgrade already in progress...");
            return false;
        }

        int cost = recipe.GetUpgradeCost(resultTier);
        if (cost > 0 && !LocalFreeForgeAvailable() && (_currencyManager == null || !_currencyManager.CanAfford(cost)))
        {
            _prompt?.Show($"Need ${cost:N0}");
            return false;
        }

        _inventoryManager.SaveActiveAmmo();
        _pendingUpgrade = true;
        _pendingBaseName = baseName;
        _pendingHighestTier = highestTier;
        _prompt?.Show("Rolling...");
        string mainToken = _inventoryManager.FindReceipt(baseName, highest: true);
        string materialToken = useMaterial ? _inventoryManager.FindReceipt(baseName, mainToken) : null;
        RequestUpgradeRollServerRpc(mainToken, materialToken);
        return true;
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestUpgradeRollServerRpc(string mainToken, string materialToken, RPCInfo info = default)
    {
        var player = NetworkPlayer.FindPlayer(info.sender);
        if (player == null || !player.CanUseStation(this) || !player.ServerInventory.TryGet(mainToken, out var held))
        {
            ConfirmUpgradeRollTargetRpc(info.sender, "", 0, (int)RollOutcome.Rejected, 0, 0f, false, mainToken, materialToken, default);
            return;
        }
        string baseName = held.itemName;
        int currentTier = held.upgradeTier;
        bool useMaterial = !string.IsNullOrEmpty(materialToken);
        ItemUpgradeRecipe recipe = FindRecipe(baseName);
        int resultTier = currentTier + 1;
        if (recipe == null || !recipe.IsValid() || currentTier < 0 || resultTier > recipe.EffectiveMaxTier
            || (useMaterial && (materialToken == mainToken || !player.ServerInventory.TryGet(materialToken, out var material)
                || material.itemName != held.itemName)))
        {
            ConfirmUpgradeRollTargetRpc(info.sender, baseName, currentTier, (int)RollOutcome.Rejected, 0, 0f, false, mainToken, materialToken, default);
            return;
        }

        PlayerVitals payer = FindVitalsForPlayer(info.sender);

        // ── 비용: 서버가 다시 계산. Free Forge는 하루 첫 강화 무료.
        int cost = recipe.GetUpgradeCost(resultTier);
        if (cost > 0 && payer != null && payer.HasServerPerk(ServerPerkFlags.FreeForge) && !payer.FreeForgeUsedToday)
        {
            payer.MarkFreeForgeUsed();
            cost = 0;
        }

        if (cost > 0)
        {
            CacheManagers();
            if (_currencyManager == null || !_currencyManager.TrySpendCurrencyImmediateOnServer(cost))
            {
                ConfirmUpgradeRollTargetRpc(info.sender, baseName, currentTier, (int)RollOutcome.Rejected, cost, 0f, false, mainToken, materialToken, default);
                return;
            }
        }

        // ── 성공률: 기본표 + 재료 + pity + 퍼크
        float perkBonus = payer != null ? payer.ServerSteadyHandsBonus : 0f;
        if (useMaterial && payer != null)
            perkBonus += payer.ServerMasterSmithBonus;

        string pityKey = $"{info.sender}|{baseName}|{resultTier}";
        _serverPity.TryGetValue(pityKey, out int fails);
        float chance = recipe.GetSuccessChance(resultTier, useMaterial, fails, perkBonus);

        RollOutcome outcome;
        if (Random.value < chance)
        {
            outcome = RollOutcome.Success;
            _serverPity.Remove(pityKey);
        }
        else
        {
            _serverPity[pityKey] = fails + 1;

            bool downgrade = recipe.DowngradesOnFail(resultTier) && currentTier > 0;
            if (downgrade && payer != null && payer.HasServerPerk(ServerPerkFlags.SafetyNet) && !payer.SafetyNetUsedToday)
            {
                payer.MarkSafetyNetUsed();
                downgrade = false;
            }

            outcome = downgrade ? RollOutcome.Downgrade : RollOutcome.Fail;

            // Second Chance: 하루 1회 같은 단계 즉시 무료 재시도
            if (payer != null && payer.HasServerPerk(ServerPerkFlags.SecondChance) && !payer.SecondChanceUsedToday)
            {
                payer.MarkSecondChanceUsed();
                if (Random.value < chance)
                {
                    outcome = RollOutcome.Success;
                    _serverPity.Remove(pityKey);
                }
            }
        }

        Debug.Log($"[AnvilInteraction] {info.sender} rolled {baseName} +{currentTier}→+{resultTier}: chance={chance:P0} outcome={outcome} cost={cost} material={useMaterial}");
        var replacement = held;
        replacement.token = System.Guid.NewGuid().ToString("N");
        replacement.upgradeTier = outcome == RollOutcome.Success ? resultTier
            : outcome == RollOutcome.Downgrade ? Mathf.Max(0, currentTier - 1) : currentTier;
        if (!player.ServerInventory.Replace(mainToken, materialToken, replacement))
        {
            if (cost > 0) _currencyManager.AddCurrency(cost);
            ConfirmUpgradeRollTargetRpc(info.sender, baseName, currentTier, (int)RollOutcome.Rejected, 0, chance, false, mainToken, materialToken, default);
            return;
        }
        PlayUpgradeSoundObserversRpc(outcome == RollOutcome.Success);
        ConfirmUpgradeRollTargetRpc(info.sender, baseName, currentTier, (int)outcome, cost, chance, useMaterial, mainToken, materialToken, replacement);
    }

    [TargetRpc]
    private void ConfirmUpgradeRollTargetRpc(PlayerID target, string baseName, int currentTier, int outcomeRaw, int cost, float chance, bool usedMaterial,
        string mainToken, string materialToken, InventoryReceipt replacement)
    {
        _pendingUpgrade = false;
        var outcome = (RollOutcome)outcomeRaw;
        ItemUpgradeRecipe recipe = FindRecipe(baseName);

        if (recipe == null || outcome == RollOutcome.Rejected)
        {
            _prompt?.Show(cost > 0 ? $"Team funds insufficient for ${cost:N0}" : "Upgrade rejected.");
            RefreshOpenUi();
            return;
        }

        CacheManagers();
        _inventoryManager?.ReplaceReceipt(mainToken, materialToken, replacement);
        AnnounceOutcome(recipe, currentTier, outcome, chance, cost);
        OnRollResolved?.Invoke(recipe, currentTier + 1, outcome, chance);
        RefreshOpenUi();
    }

    private void RollOfflineAndApply(ItemUpgradeRecipe recipe, int currentTier, bool useMaterial)
    {
        int resultTier = currentTier + 1;
        int fails = GetLocalPity(recipe, resultTier);
        float chance = recipe.GetSuccessChance(resultTier, useMaterial, fails, LocalSteadyHandsBonus());
        RollOutcome outcome;
        if (Random.value < chance)
        {
            outcome = RollOutcome.Success;
            _localPity.Remove(PityKey(recipe, resultTier));
        }
        else
        {
            _localPity[PityKey(recipe, resultTier)] = fails + 1;
            outcome = recipe.DowngradesOnFail(resultTier) && currentTier > 0 ? RollOutcome.Downgrade : RollOutcome.Fail;
        }

        if (ApplyRollOutcomeLocally(recipe, currentTier, outcome, useMaterial))
            PlayUpgradeSoundLocal(outcome == RollOutcome.Success);
        AnnounceOutcome(recipe, currentTier, outcome, chance, recipe.GetUpgradeCost(resultTier));
        OnRollResolved?.Invoke(recipe, resultTier, outcome, chance);
        RefreshOpenUi();
    }

    private void AnnounceOutcome(ItemUpgradeRecipe recipe, int currentTier, RollOutcome outcome, float chance, int cost)
    {
        string name = recipe.baseItemName;
        switch (outcome)
        {
            case RollOutcome.Success:
                var milestone = recipe.GetMilestoneAt(currentTier + 1);
                _prompt?.Show(milestone.HasValue
                    ? $"SUCCESS! {name} +{currentTier + 1} — ★ {milestone.Value.label} unlocked"
                    : $"SUCCESS! {name} +{currentTier + 1} ({chance:P0})");
                break;
            case RollOutcome.Downgrade:
                _prompt?.Show($"FAILED... {name} dropped to +{Mathf.Max(0, currentTier - 1)} ({chance:P0})");
                break;
            default:
                _prompt?.Show($"Failed. {name} stays +{currentTier} ({chance:P0}, ${cost:N0} spent)");
                break;
        }
    }

    /// <summary>
    /// 결과를 인벤토리에 반영. 재료는 결과와 무관하게 소모된다.
    /// </summary>
    private bool ApplyRollOutcomeLocally(ItemUpgradeRecipe recipe, int currentTier, RollOutcome outcome, bool usedMaterial)
    {
        CacheManagers();
        if (_inventoryManager == null || recipe == null)
            return false;

        string baseName = recipe.baseItemName;

        if (usedMaterial && _inventoryManager.CountItemsByName(baseName) >= 2)
        {
            if (!_inventoryManager.RemoveItemLowestTier(baseName))
                Debug.LogWarning($"[AnvilInteraction] Could not consume material for {baseName}.", this);
        }

        if (outcome == RollOutcome.Fail || outcome == RollOutcome.Rejected)
            return true;

        // 메인 무기가 그 사이 바뀌었을 수 있으니 실제 보유 티어로 다시 맞춘다.
        int liveHighest = _inventoryManager.GetHighestUpgradeTier(baseName);
        if (_inventoryManager.CountItemsByName(baseName) < 1)
            return false;
        if (liveHighest != currentTier)
            currentTier = liveHighest;

        int toTier = outcome == RollOutcome.Success ? currentTier + 1 : Mathf.Max(0, currentTier - 1);
        if (!ReplaceWeaponTier(recipe, currentTier, toTier))
            return false;

        Debug.Log($"[AnvilInteraction] {baseName} +{currentTier} → +{toTier} ({outcome})");
        return true;
    }

    private bool ReplaceWeaponTier(ItemUpgradeRecipe recipe, int fromTier, int toTier)
    {
        string baseName = recipe.baseItemName;

        if (!_inventoryManager.RemoveItemWithTier(baseName, fromTier))
        {
            Debug.LogError($"[AnvilInteraction] Failed to remove {baseName} +{fromTier}");
            return false;
        }

        GameObject resultObj = Instantiate(recipe.baseItemPrefab);
        Item resultItem = resultObj.GetComponent<Item>();
        if (resultItem == null)
        {
            Debug.LogError("[AnvilInteraction] Base prefab has no Item component!");
            Destroy(resultObj);
            return false;
        }

        bool added = _inventoryManager.AddItem(resultItem, toTier);
        if (added)
        {
            Destroy(resultObj);
        }
        else
        {
            DropResultItemNearPlayer(resultObj, toTier);
            _prompt?.Show("Inventory full — weapon dropped at your feet");
        }

        return true;
    }

    private static string PityKey(ItemUpgradeRecipe recipe, int resultTier) => $"{recipe.baseItemName}|{resultTier}";

    private int GetLocalPity(ItemUpgradeRecipe recipe, int resultTier)
    {
        return recipe != null && _localPity.TryGetValue(PityKey(recipe, resultTier), out int fails) ? fails : 0;
    }

    private static PlayerPerks LocalPerks()
    {
        var movement = FPSMovement.LocalPlayerPosition;
        return movement != null ? movement.transform.root.GetComponent<PlayerPerks>() : null;
    }

    private static float LocalSteadyHandsBonus()
    {
        PlayerPerks perks = LocalPerks();
        return perks != null ? perks.Total(PlayerPerkKind.SteadyHands) : 0f;
    }

    private static float LocalMasterSmithBonus()
    {
        PlayerPerks perks = LocalPerks();
        return perks != null ? perks.Total(PlayerPerkKind.MasterSmith) : 0f;
    }

    private static bool LocalFreeForgeAvailable()
    {
        var movement = FPSMovement.LocalPlayerPosition;
        PlayerVitals vitals = movement != null ? movement.transform.root.GetComponent<PlayerVitals>() : null;
        return vitals != null && vitals.HasServerPerk(ServerPerkFlags.FreeForge) && !vitals.FreeForgeUsedToday;
    }

    private static PlayerVitals FindVitalsForPlayer(PlayerID player)
    {
        PlayerVitals[] all = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            PlayerVitals vitals = all[i];
            if (vitals != null && vitals.isSpawned && vitals.owner.HasValue && vitals.owner.Value == player)
                return vitals;
        }

        return null;
    }

    private ItemUpgradeRecipe FindRecipe(string baseName)
    {
        for (int i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (recipe != null && string.Equals(recipe.baseItemName, baseName, System.StringComparison.Ordinal))
                return recipe;
        }

        return null;
    }

    private void RefreshOpenUi()
    {
        if (_anvilUI != null && _anvilUI.IsOpen)
            _anvilUI.RefreshRecipeList();
    }


    private void PlayUpgradeSoundLocal(bool succeeded)
    {
        AudioClip clip = succeeded ? upgradeSound : upgradeFailureSound;
        if (clip == null) return;
        EnsureAudioSource();
        _lastUpgradeAudioDebug = $"{name}|success={succeeded}|pos={transform.position}";
        Debug.Log($"[AnvilUpgradeAudio] {_lastUpgradeAudioDebug}");
        _audioSource.PlayOneShot(clip, succeeded ? upgradeSoundVolume : upgradeFailureSoundVolume);
    }

    public static string GetLastUpgradeAudioDebug()
    {
        return _lastUpgradeAudioDebug;
    }

    [ObserversRpc]
    private void PlayUpgradeSoundObserversRpc(bool succeeded)
    {
        PlayUpgradeSoundLocal(succeeded);
    }

    private void DropResultItemNearPlayer(GameObject itemObj, int upgradeTier)
    {
        if (itemObj == null)
            return;

        ApplyLocalDropState(itemObj, upgradeTier);
        DropNearPlayer(itemObj);
    }



    private void ApplyLocalDropState(GameObject itemObj, int upgradeTier)
    {
        if (itemObj.TryGetComponent<Item>(out var item))
            item.SetPriceImmediateOnServer(item.Price);

        if (itemObj.TryGetComponent<WeaponItem>(out var weaponItem))
            weaponItem.SetUpgradeTierImmediateOnServer(upgradeTier);
    }

    private void DropNearPlayer(GameObject itemObj)
    {
        if (FPSMovement.LocalPlayerPosition != null)
        {
            Transform player = FPSMovement.LocalPlayerPosition.transform;
            itemObj.transform.position = player.position + player.forward * 1.5f + Vector3.up;
        }
        else
        {
            itemObj.transform.position = transform.position + Vector3.up;
        }

        Rigidbody rb = itemObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }
    }

    #endregion
}

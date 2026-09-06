using System.Collections.Generic;
using Demo.Scripts.Runtime.Character;
using UnityEngine;

/// <summary>
/// Owner-side perk applier. Polls the SkillWeb for obtained levels and pushes the result into
/// the systems that own each stat:
///   - movement (walk / sprint / jump / double jump)      → FPSMovement.SetPerkModifiers
///   - vitals (health / stamina / regen / damage reduction) → PlayerVitals.SetSkillWebModifier (server-validated)
///   - attack perks (ground slam, adrenaline)             → local triggers + server damage RPC
/// Added at runtime by PlayerVitals, so no prefab wiring is required.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPerks : MonoBehaviour
{
    private const string VitalsModifierKey = "skillweb.perks";
    private const float RefreshInterval = 0.5f;
    private const float GroundSlamRadius = 3.2f;
    private const float GroundSlamMinImpactSpeed = 6f;
    private const float AdrenalineDuration = 3f;

    /// <summary>Plain data so the default table never touches Unity objects in a static initializer.</summary>
    public readonly struct PerkEntry
    {
        public readonly string skillName;
        public readonly PlayerPerkKind kind;
        public readonly float valuePerLevel;
        public readonly string description;

        public PerkEntry(string skillName, PlayerPerkKind kind, float valuePerLevel, string description = "")
        {
            this.skillName = skillName;
            this.kind = kind;
            this.valuePerLevel = valuePerLevel;
            this.description = description;
        }
    }

    private static PerkEntry[] _codeDefaults;

    private PlayerVitals _vitals;
    private FPSMovement _movement;
    private NetworkPlayer _networkPlayer;
    private IReadOnlyList<PerkEntry> _definitions;
    private readonly Dictionary<PlayerPerkKind, float> _totals = new();
    private float _nextRefresh;
    private float _adrenalineUntil;
    private float _adrenalineBonus;
    private int _previousHealth;
    private bool _hasHealthSnapshot;
    private bool _landingHooked;
    private string _lastSignature = string.Empty;

    public float Total(PlayerPerkKind kind) => _totals.TryGetValue(kind, out float v) ? v : 0f;

    private void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _movement = GetComponentInChildren<FPSMovement>(true);
        _networkPlayer = GetComponent<NetworkPlayer>();
        _definitions = LoadDefinitions();
    }

    private void OnEnable()
    {
        if (_vitals != null)
        {
            // PlayerVitals can add this component before its Awake initializes the health values.
            ResetHealthSnapshot(_vitals.CurrentHealth, _vitals.MaxHealth);
            _vitals.OnHealthChanged += HandleHealthChanged;
        }
    }

    // Initialization and checkpoint restoration establish a baseline without dealing damage.
    public void ResetHealthSnapshot(int current, int max)
    {
        _previousHealth = current;
        _hasHealthSnapshot = max > 0;
    }

    private void OnDisable()
    {
        if (_vitals != null)
            _vitals.OnHealthChanged -= HandleHealthChanged;

        if (_movement != null && _landingHooked)
        {
            _movement.onLanded -= HandleLanded;
            _landingHooked = false;
        }
    }

    private void Update()
    {
        if (!IsLocalPlayer())
            return;

        if (_movement != null && !_landingHooked)
        {
            _movement.onLanded += HandleLanded;
            _landingHooked = true;
        }

        if (Time.unscaledTime < _nextRefresh)
        {
            TickAdrenaline();
            return;
        }

        _nextRefresh = Time.unscaledTime + RefreshInterval;
        TeamProgress.ApplyToSkillWeb();
        RefreshFromSkillWeb();
        TickAdrenaline();
    }

    // ───────────────────────── SkillWeb → totals ─────────────────────────

    public void RefreshCheckpointModifiers()
    {
        if (!IsLocalPlayer()) return;
        TeamProgress.ApplyToSkillWeb();
        _lastSignature = null;
        RefreshFromSkillWeb();
    }

    private void RefreshFromSkillWeb()
    {
        if (!SkillWebQuery.IsAvailable)
            return;

        var signature = new System.Text.StringBuilder();
        var next = new Dictionary<PlayerPerkKind, float>();

        for (int i = 0; i < _definitions.Count; i++)
        {
            PerkEntry def = _definitions[i];
            if (string.IsNullOrWhiteSpace(def.skillName))
                continue;

            if (!SkillWebQuery.TryGetObtainedLevel(def.skillName, out int level))
                return; // web not loaded yet; keep the last applied values

            if (level <= 0)
                continue;

            float value = def.valuePerLevel * level;
            next[def.kind] = next.TryGetValue(def.kind, out float existing) ? existing + value : value;
            signature.Append(def.skillName).Append('=').Append(level).Append(';');
        }

        string sig = signature.ToString();
        if (sig == _lastSignature)
            return;

        _lastSignature = sig;
        _totals.Clear();
        foreach (var pair in next)
            _totals[pair.Key] = pair.Value;

        ApplyTotals();
    }

    private void ApplyTotals()
    {
        if (_movement != null)
        {
            _movement.SetPerkModifiers(
                1f + Total(PlayerPerkKind.WalkSpeed),
                1f + Total(PlayerPerkKind.SprintSpeed) + _adrenalineBonus,
                1f + Total(PlayerPerkKind.JumpHeight),
                Mathf.RoundToInt(Total(PlayerPerkKind.ExtraAirJump)));
        }

        if (_vitals != null)
        {
            int maxHealth = Mathf.RoundToInt(Total(PlayerPerkKind.MaxHealth));
            float maxStamina = Total(PlayerPerkKind.MaxStamina);
            float regen = Total(PlayerPerkKind.StaminaRegen);
            float damageReduction = Total(PlayerPerkKind.DamageReduction);

            if (maxHealth == 0 && maxStamina <= 0f && regen <= 0f && damageReduction <= 0f)
                _vitals.RemoveSkillWebModifier(VitalsModifierKey);
            else
                _vitals.SetSkillWebModifier(VitalsModifierKey, maxHealth, maxStamina, regen, damageReduction);

            // 강화/죽음 퍼크는 서버가 판정하므로 플래그를 올려 보낸다.
            ServerPerkFlags flags = ServerPerkFlags.None;
            if (Total(PlayerPerkKind.SafetyNet) > 0f) flags |= ServerPerkFlags.SafetyNet;
            if (Total(PlayerPerkKind.SecondChance) > 0f) flags |= ServerPerkFlags.SecondChance;
            if (Total(PlayerPerkKind.FreeForge) > 0f) flags |= ServerPerkFlags.FreeForge;
            if (Total(PlayerPerkKind.MasterSmith) > 0f) flags |= ServerPerkFlags.MasterSmith;
            if (Total(PlayerPerkKind.TeamInsurance) > 0f) flags |= ServerPerkFlags.TeamInsurance;
            if (Total(PlayerPerkKind.LastStand) > 0f) flags |= ServerPerkFlags.LastStand;

            _vitals.ReportPerks(flags, Total(PlayerPerkKind.SteadyHands), Total(PlayerPerkKind.MasterSmith), Total(PlayerPerkKind.Haggler), Total(PlayerPerkKind.BatteryCapacity));
        }
    }

    // ───────────────────────── Progression → SkillWeb ─────────────────────────

    /// <summary>Owner-side: called through PlayerVitals when the server grants trophy points.</summary>
    public void ReceiveSkillPoints(int amount, string reason)
    {
        TeamProgress.AddSkillPoints(amount, reason);
        TeamProgress.ApplyToSkillWeb();
        PromptPresenter.ShowPrompt($"+{amount} skill point{(amount == 1 ? string.Empty : "s")} ({reason})");
    }

    // ───────────────────────── Attack perks ─────────────────────────

    private void HandleLanded()
    {
        if (!IsLocalPlayer() || _movement == null || _vitals == null || _vitals.IsDead)
            return;

        float slam = Total(PlayerPerkKind.GroundSlam);
        if (slam <= 0f)
            return;

        if (!_movement.LastAirborneWasPlayerJump || _movement.LastLandingImpactSpeed < GroundSlamMinImpactSpeed)
            return;

        _vitals.RequestPerkAreaDamageServerRpc(transform.position, GroundSlamRadius, Mathf.RoundToInt(slam));
    }

    private void HandleHealthChanged(int current, int max)
    {
        // A max-health change can alter the health ratio or clamp current HP without damage.
        bool tookDamage = _hasHealthSnapshot && current < Mathf.Min(_previousHealth, max);
        _previousHealth = current;
        _hasHealthSnapshot = true;

        // Cache before ApplyTotals: it raises this event again while applying the sprint bonus.
        if (!tookDamage || !IsLocalPlayer())
            return;

        float adrenaline = Total(PlayerPerkKind.Adrenaline);
        if (adrenaline <= 0f)
            return;

        _adrenalineUntil = Time.time + AdrenalineDuration;
        if (!Mathf.Approximately(_adrenalineBonus, adrenaline))
        {
            _adrenalineBonus = adrenaline;
            ApplyTotals();
        }
    }

    private void TickAdrenaline()
    {
        if (_adrenalineBonus <= 0f || Time.time < _adrenalineUntil)
            return;

        _adrenalineBonus = 0f;
        ApplyTotals();
    }

    // ───────────────────────── Helpers ─────────────────────────

    private bool IsLocalPlayer()
    {
        if (_networkPlayer != null && _networkPlayer.isSpawned)
            return _networkPlayer.isOwner;

        Camera cam = GetComponentInChildren<Camera>(true);
        return cam != null && cam.isActiveAndEnabled;
    }

    public static IReadOnlyList<PerkEntry> LoadDefinitions()
    {
        PlayerPerkDefinition[] fromResources = Resources.LoadAll<PlayerPerkDefinition>("Perks");
        if (fromResources != null && fromResources.Length > 0)
        {
            var list = new List<PerkEntry>(fromResources.Length);
            for (int i = 0; i < fromResources.Length; i++)
            {
                PlayerPerkDefinition def = fromResources[i];
                if (def != null && !string.IsNullOrWhiteSpace(def.skillName))
                    list.Add(new PerkEntry(def.skillName, def.kind, def.valuePerLevel, def.description));
            }

            if (list.Count > 0)
                return list;
        }

        return _codeDefaults ??= BuildCodeDefaults();
    }

    /// <summary>모든 퍼크 정의 (문서/디버그용).</summary>
    public static IReadOnlyList<PerkEntry> CodeDefaults => _codeDefaults ??= BuildCodeDefaults();

    /// <summary>
    /// 스킬웹에 이 이름들로 노드를 만들면 바로 동작한다. Resources/Perks에 에셋을 두면 이 표를 대체한다.
    /// </summary>
    private static PerkEntry[] BuildCodeDefaults()
    {
        return new[]
        {
            Make("Health Boost I", PlayerPerkKind.MaxHealth, 25f, "Increases maximum health by 25 and fully restores health."),
            Make("Health Boost II", PlayerPerkKind.MaxHealth, 25f, "Increases maximum health by another 25 and fully restores health."),
            Make("Fleet Foot I", PlayerPerkKind.WalkSpeed, 0.08f, "Increases walking speed by 8%."),
            Make("Fleet Foot II", PlayerPerkKind.WalkSpeed, 0.08f, "Increases walking speed by another 8%."),
            Make("Sprinter I", PlayerPerkKind.SprintSpeed, 0.10f, "Increases sprinting speed by 10%."),
            Make("Sprinter II", PlayerPerkKind.SprintSpeed, 0.10f, "Increases sprinting speed by another 10%."),
            Make("Iron Lungs", PlayerPerkKind.MaxStamina, 30f, "Increases maximum stamina by 30."),
            Make("Second Wind", PlayerPerkKind.StaminaRegen, 6f, "Regenerates 6 additional stamina per second."),
            Make("Long Jump", PlayerPerkKind.JumpHeight, 0.15f, "Increases upward jump speed by 15%."),
            Make("Double Jump", PlayerPerkKind.ExtraAirJump, 1f, "Allows one additional jump while airborne."),
            Make("Thick Skin", PlayerPerkKind.DamageReduction, 0.10f, "Reduces incoming damage by 10%."),
            Make("Ground Slam", PlayerPerkKind.GroundSlam, 30f, "Deals 30 damage to nearby monsters after a hard landing from a jump."),
            Make("Adrenaline", PlayerPerkKind.Adrenaline, 0.25f, "Increases sprinting speed by 25% for 3 seconds after taking damage."),
            // 강화 미니게임
            Make("Steady Hands I", PlayerPerkKind.SteadyHands, 0.05f, "Adds 5 percentage points to upgrade success chance."),
            Make("Steady Hands II", PlayerPerkKind.SteadyHands, 0.05f, "Adds another 5 percentage points to upgrade success chance."),
            Make("Safety Net", PlayerPerkKind.SafetyNet, 1f, "Prevents one upgrade downgrade per day."),
            Make("Second Chance", PlayerPerkKind.SecondChance, 1f, "Grants one free retry after a failed upgrade per day."),
            Make("Free Forge", PlayerPerkKind.FreeForge, 1f, "Makes the first upgrade each day free."),
            Make("Master Smith", PlayerPerkKind.MasterSmith, 0.10f, "Adds 10 percentage points to the material upgrade bonus."),
            // 죽음
            Make("Team Insurance", PlayerPerkKind.TeamInsurance, 1f, "A team wipe costs one day and 25% of shared currency instead of ending the run."),
            Make("Last Stand", PlayerPerkKind.LastStand, 1f, "Once per day, revives you after 5 seconds with 30% health."),
            Make("Haggler", PlayerPerkKind.Haggler, 0.20f, "Reduces revival cost by 20%."),
            // 전력
            Make("Battery Cell I", PlayerPerkKind.BatteryCapacity, 90f, "Adds 90 to the team's dungeon battery capacity."),
            Make("Battery Cell II", PlayerPerkKind.BatteryCapacity, 90f, "Adds another 90 to the team's dungeon battery capacity."),
            Make("Battery Cell III", PlayerPerkKind.BatteryCapacity, 120f, "Adds another 120 to the team's dungeon battery capacity."),
        };
    }

    private static PerkEntry Make(string skillName, PlayerPerkKind kind, float value, string description)
    {
        return new PerkEntry(skillName, kind, value, description);
    }
}

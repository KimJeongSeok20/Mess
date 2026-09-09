using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative player health plus owner-local stamina.
///
/// Health only changes on the server (monster hits, projectiles, friendly fire all run there);
/// the value and the dead flag replicate through SyncVars, so the owning client sees its own HP
/// drop and its own death sequence. Stamina never leaves the owner.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerDeath))]
public class PlayerVitals : NetworkBehaviour
{
    [Serializable]
    public struct StatModifier
    {
        public int maxHealthBonus;
        public float maxStaminaBonus;
        public float staminaRegenBonus;
        [Range(0f, 0.9f)] public float damageReductionBonus;
    }

    [Header("Base Stats")]
    [SerializeField, Min(1)] private int baseMaxHealth = 100;
    [SerializeField, Min(1f)] private float baseMaxStamina = 100f;
    [SerializeField, Min(0f)] private float staminaDrainPerSecond = 22f;
    [SerializeField, Min(0f)] private float staminaRegenPerSecond = 18f;
    [SerializeField, Min(0f)] private float staminaRegenDelay = 0.35f;
    [SerializeField, Min(0f)] private float sprintStartThreshold = 12f;

    [Header("Damage")]
    [SerializeField, Range(0f, 0.9f)] private float baseDamageReduction = 0f;

    [Header("SkillWeb Base Modifier")]
    [SerializeField] private StatModifier skillWebBaseModifier;

    [Header("HUD")]
    [SerializeField] private bool autoCreateHud = true;
    [SerializeField] private Canvas hudCanvasOverride;
    [SerializeField] private Vector2 hudAnchorOffset = new Vector2(24f, -24f);
    [SerializeField] private Sprite dungeonBatteryIcon;

    public event Action<int, int> OnHealthChanged;
    public event Action<float, float> OnStaminaChanged;
    public event Action OnDied;

    public int MaxHealth => _effectiveMaxHealth;
    public int CurrentHealth => _currentHealth;
    public float MaxStamina => _effectiveMaxStamina;
    public float CurrentStamina => _currentStamina;
    public float HealthNormalized => _effectiveMaxHealth <= 0 ? 0f : (float)_currentHealth / _effectiveMaxHealth;
    public float StaminaNormalized => _effectiveMaxStamina <= 0f ? 0f : _currentStamina / _effectiveMaxStamina;
    public bool IsDead => _isDead;

    /// <summary>Apply a living solo checkpoint after its skill-derived maximums have been restored.</summary>
    public bool RestoreCheckpointVitals(int health, float stamina)
    {
        if (!isSpawned || !isServer || _isDead || health <= 0) return false;
        _currentHealth = Mathf.Clamp(health, 1, _effectiveMaxHealth);
        _currentStamina = Mathf.Clamp(stamina, 0f, _effectiveMaxStamina);
        _regenCooldown = staminaRegenDelay;
        _syncedHealth.value = _currentHealth;
        _perks?.ResetHealthSnapshot(_currentHealth, _effectiveMaxHealth);
        RaiseHealthChanged();
        RaiseStaminaChanged();
        return true;
    }

    /// <summary>True on the server, or when running without a network session (test scenes).</summary>
    public bool HasAuthority => !isSpawned || isServer;

    // Replicated by the server. Clients only mirror these into the local fields below.
    private readonly SyncVar<int> _syncedHealth = new(100, ownerAuth: false);
    private readonly SyncVar<bool> _syncedDead = new(false, ownerAuth: false);

    private readonly Dictionary<string, StatModifier> _skillWebRuntimeModifiers = new();

    private PlayerDeath _playerDeath;
    private PlayerPerks _perks;
    private Animator _animator;
    private NetworkPlayer _networkPlayer;

    private int _effectiveMaxHealth;
    private float _effectiveMaxStamina;
    private float _effectiveStaminaRegen;
    private float _effectiveDamageReduction;

    private int _currentHealth;
    private float _currentStamina;
    private float _regenCooldown;
    private bool _isDead;
    private bool _syncEventsBound;

    private bool _hudBuilt;
    private PlayerVitalsHud _hudPresenter;
    private Canvas _runtimeHudCanvas;

    private void Awake()
    {
        _playerDeath = GetComponent<PlayerDeath>();
        _animator = GetComponent<Animator>();
        _networkPlayer = GetComponent<NetworkPlayer>();

        // Perk applier lives next to the vitals; added here so the player prefab needs no wiring.
        if (!TryGetComponent(out _perks))
            _perks = gameObject.AddComponent<PlayerPerks>();

        RecalculateDerivedStats(resetCurrentValues: true);
    }

    // ───────────────────────── Server-side perk state ─────────────────────────
    // The SkillWeb lives on the owner; the server keeps a clamped copy so upgrade rolls, revive
    // pricing and death rules can be judged authoritatively. Daily "once" perks reset on OnDayReset.

    public ServerPerkFlags ServerPerks { get; private set; }
    public float ServerSteadyHandsBonus { get; private set; }
    public float ServerMasterSmithBonus { get; private set; }
    public float ServerHagglerDiscount { get; private set; }
    public float ServerBatteryBonus { get; private set; }

    /// <summary>Server → owner: trophy processing (or any progression source) awarded skill points.</summary>
    public void GrantSkillPoints(int amount, string reason)
    {
        if (!isSpawned)
        {
            var perks = GetComponent<PlayerPerks>();
            if (perks != null)
                perks.ReceiveSkillPoints(amount, reason);
            return;
        }

        if (!isServer || !owner.HasValue)
            return;

        GrantSkillPointsTargetRpc(owner.Value, amount, reason);
    }

    [TargetRpc]
    private void GrantSkillPointsTargetRpc(PlayerID target, int amount, string reason)
    {
        var perks = GetComponent<PlayerPerks>();
        if (perks != null)
            perks.ReceiveSkillPoints(amount, reason);
    }
    public bool SafetyNetUsedToday { get; private set; }
    public bool SecondChanceUsedToday { get; private set; }
    public bool FreeForgeUsedToday { get; private set; }
    public bool LastStandUsedToday { get; private set; }

    public void ReportPerks(ServerPerkFlags flags, float steadyHands, float masterSmith, float haggler, float battery)
    {
        if (!isSpawned || isServer)
        {
            ApplyPerkReport(flags, steadyHands, masterSmith, haggler, battery);
            return;
        }

        if (isOwner)
            ReportPerksServerRpc((int)flags, steadyHands, masterSmith, haggler, battery);
    }

    [ServerRpc]
    private void ReportPerksServerRpc(int flags, float steadyHands, float masterSmith, float haggler, float battery)
    {
        ApplyPerkReport((ServerPerkFlags)flags, steadyHands, masterSmith, haggler, battery);
    }

    private void ApplyPerkReport(ServerPerkFlags flags, float steadyHands, float masterSmith, float haggler, float battery)
    {
        ServerPerks = flags;
        ServerSteadyHandsBonus = Mathf.Clamp(steadyHands, 0f, 0.3f);
        ServerMasterSmithBonus = Mathf.Clamp(masterSmith, 0f, 0.3f);
        ServerHagglerDiscount = Mathf.Clamp(haggler, 0f, 0.6f);
        ServerBatteryBonus = Mathf.Clamp(battery, 0f, 600f);
    }

    public bool HasServerPerk(ServerPerkFlags flag) => (ServerPerks & flag) != 0;

    public void MarkSafetyNetUsed() => SafetyNetUsedToday = true;
    public void MarkSecondChanceUsed() => SecondChanceUsedToday = true;
    public void MarkFreeForgeUsed() => FreeForgeUsedToday = true;

    private void ResetDailyPerkUsage()
    {
        SafetyNetUsedToday = false;
        SecondChanceUsedToday = false;
        FreeForgeUsedToday = false;
        LastStandUsedToday = false;
    }

    /// <summary>Server: revive with a fraction of max health (Last Stand). 1.0 = full.</summary>
    public void ReviveWithHealthFraction(float fraction)
    {
        ReviveToFull();
        if (!HasAuthority)
            return;

        int hp = Mathf.Clamp(Mathf.RoundToInt(_effectiveMaxHealth * Mathf.Clamp01(fraction)), 1, _effectiveMaxHealth);
        SetHealthAuthoritative(hp);
    }

    /// <summary>
    /// Test hook (development builds and the editor only): the owner asks the server to hurt it.
    /// Lets a client-side tester trigger the death/revive flow without a monster.
    /// </summary>
    [ServerRpc]
    public void DebugRequestDamageServerRpc(int amount)
    {
        if (!Debug.isDebugBuild && !Application.isEditor)
            return;

        ApplyDamage(DamageRequest.Melee(Mathf.Clamp(amount, 1, 10000)), null);
    }

    private const float MaxPerkAreaRadius = 5f;
    private const int MaxPerkAreaDamage = 80;

    /// <summary>
    /// Attack perks (ground slam) hit monsters through the server so the damage is authoritative.
    /// The owner reports where it landed; the server re-checks the distance to the player.
    /// </summary>
    [ServerRpc]
    public void RequestPerkAreaDamageServerRpc(Vector3 center, float radius, int damage)
    {
        if (_isDead)
            return;

        if ((center - transform.position).sqrMagnitude > 9f)
            return; // must be where the player actually is

        radius = Mathf.Clamp(radius, 0.5f, MaxPerkAreaRadius);
        damage = Mathf.Clamp(damage, 1, MaxPerkAreaDamage);

        Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);
        var damaged = new HashSet<UnityEngine.Object>();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            MonsterHealth monster = hit.GetComponentInParent<MonsterHealth>();
            if (monster != null && !monster.IsDead && damaged.Add(monster))
            {
                monster.TakeDamage(DamageRequest.Explosive(damage, center, (hit.transform.position - center).normalized));
                continue;
            }

            OctopusSwarmMember octopus = hit.GetComponentInParent<OctopusSwarmMember>();
            if (octopus != null && damaged.Add(octopus))
                octopus.TakeDamage(DamageRequest.Explosive(damage, center, (hit.transform.position - center).normalized));
        }
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();

        BindSyncEvents();

        if (isServer)
        {
            _syncedHealth.value = _currentHealth;
            _syncedDead.value = _isDead;
            TimeManager.OnDayReset += HandleDayResetServer;
        }
        else
        {
            // Late joiners and remote copies adopt the server's view immediately.
            MirrorSyncedHealth(_syncedHealth.value);
            if (_syncedDead.value)
                HandleDeathLocally();
        }
    }

    protected override void OnDespawned()
    {
        CancelLastStand();
        if (_runFailureOwner == this)
            _runFailureOwner = null;
        TimeManager.OnDayReset -= HandleDayResetServer;
        UnbindSyncEvents();
        base.OnDespawned();
    }

    private void Update()
    {
        if (_isDead)
            return;

        if (!IsLocalPlayer())
            return;

        if (!_hudBuilt)
            TryBuildHud();

        if (_hudPresenter != null)
            _hudPresenter.SetPerks(_perks);

        TickStamina(Time.deltaTime);
    }

    public bool CanStartSprint()
    {
        if (_isDead)
            return false;

        return _currentStamina >= sprintStartThreshold;
    }

    public bool CanSustainSprint()
    {
        if (_isDead)
            return false;

        return _currentStamina > 0.01f;
    }

    public void TakeDamage(int amount)
    {
        ApplyDamage(DamageRequest.Melee(amount), null);
    }

    public void TakeDamage(DamageRequest request)
    {
        ApplyDamage(request, null);
    }

    public void ApplyDamage(int amount, IMonsterDeathSequence killerSequence)
    {
        ApplyDamage(DamageRequest.Melee(amount), killerSequence);
    }

    /// <summary>
    /// Server-only. Damage applied on a client is ignored so a client can never hurt (or kill)
    /// itself or anyone else out of sync with the server.
    /// </summary>
    public void ApplyDamage(DamageRequest request, IMonsterDeathSequence killerSequence)
    {
        if (!HasAuthority)
            return;

        if (_isDead || request.Amount <= 0)
            return;

        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(request.Amount * (1f - _effectiveDamageReduction)));
        int nextHealth = Mathf.Max(0, _currentHealth - finalDamage);

        if (nextHealth == _currentHealth)
            return;

        SetHealthAuthoritative(nextHealth);

        if (_currentHealth <= 0)
            KillAuthoritative(killerSequence);
    }

    /// <summary>
    /// Called by <see cref="PlayerDeath.Kill"/> for deaths that bypass damage (instant kills).
    /// On the server this replicates the death; on a client it only mirrors local bookkeeping.
    /// </summary>
    public void ApplyExternalDeathState()
    {
        if (_isDead)
            return;

        if (HasAuthority)
        {
            SetHealthAuthoritative(0);
            _isDead = true;
            _diedAt = Time.time;
            if (isServer) GetComponent<NetworkPlayer>()?.DropInventoryOnServerDeath();
            if (isSpawned)
                _syncedDead.value = true;
            OnDied?.Invoke();
            if (isSpawned && isServer)
                HandleServerDeathRules();
            return;
        }

        _currentHealth = 0;
        _isDead = true;
        _diedAt = Time.time;
        RaiseHealthChanged();
        OnDied?.Invoke();
    }

    public void ReviveToFull()
    {
        if (isSpawned && !isServer)
        {
            // Presentation may finish before/after replication; it never authorizes a revive.
            if (!_syncedDead.value)
            {
                _isDead = false;
                MirrorSyncedHealth(_syncedHealth.value);
            }
            return;
        }
        CancelLastStand();
        _isDead = false;
        _regenCooldown = 0f;
        RecalculateDerivedStats(resetCurrentValues: true);

        if (!isSpawned)
            return;

        if (isServer)
        {
            _syncedDead.value = false;
            _syncedHealth.value = _currentHealth;
            // SyncVar callbacks intentionally do nothing on the server. Restore its remote
            // proxy explicitly so collisions, monster targeting and future deaths work again.
            if (!isOwner)
                _playerDeath?.ReviveFromNetwork();
        }
    }

    public void Heal(int amount)
    {
        if (!HasAuthority || _isDead || amount <= 0)
            return;

        int nextHealth = Mathf.Min(_effectiveMaxHealth, _currentHealth + amount);
        if (nextHealth == _currentHealth)
            return;

        SetHealthAuthoritative(nextHealth);
    }

    /// <summary>
    /// SkillWeb progression lives on the owning client, but max health is enforced on the server,
    /// so the owner forwards its modifiers; the server clamps them to sane bounds and applies them
    /// to its authoritative copy.
    /// </summary>
    public void SetSkillWebModifier(string modifierKey, int maxHealthBonus, float maxStaminaBonus, float staminaRegenBonus, float damageReductionBonus)
    {
        if (string.IsNullOrWhiteSpace(modifierKey))
            return;

        ApplySkillWebModifierLocal(modifierKey, maxHealthBonus, maxStaminaBonus, staminaRegenBonus, damageReductionBonus);

        if (isSpawned && isOwner && !isServer)
            SetSkillWebModifierServerRpc(modifierKey, maxHealthBonus, maxStaminaBonus, staminaRegenBonus, damageReductionBonus);
    }

    public void RemoveSkillWebModifier(string modifierKey)
    {
        if (string.IsNullOrWhiteSpace(modifierKey))
            return;

        if (_skillWebRuntimeModifiers.Remove(modifierKey))
            RecalculateDerivedStats(resetCurrentValues: false);

        if (isSpawned && isOwner && !isServer)
            RemoveSkillWebModifierServerRpc(modifierKey);
    }

    private void ApplySkillWebModifierLocal(string modifierKey, int maxHealthBonus, float maxStaminaBonus, float staminaRegenBonus, float damageReductionBonus)
    {
        _skillWebRuntimeModifiers[modifierKey] = new StatModifier
        {
            maxHealthBonus = maxHealthBonus,
            maxStaminaBonus = maxStaminaBonus,
            staminaRegenBonus = staminaRegenBonus,
            damageReductionBonus = damageReductionBonus
        };

        RecalculateDerivedStats(resetCurrentValues: false);
    }

    private const int MaxSkillWebHealthBonus = 400;
    private const float MaxSkillWebStaminaBonus = 300f;
    private const float MaxSkillWebRegenBonus = 60f;
    private const float MaxSkillWebDamageReduction = 0.6f;

    [ServerRpc]
    private void SetSkillWebModifierServerRpc(string modifierKey, int maxHealthBonus, float maxStaminaBonus, float staminaRegenBonus, float damageReductionBonus)
    {
        if (string.IsNullOrWhiteSpace(modifierKey) || modifierKey.Length > 64)
            return;

        ApplySkillWebModifierLocal(
            modifierKey,
            Mathf.Clamp(maxHealthBonus, 0, MaxSkillWebHealthBonus),
            Mathf.Clamp(maxStaminaBonus, 0f, MaxSkillWebStaminaBonus),
            Mathf.Clamp(staminaRegenBonus, 0f, MaxSkillWebRegenBonus),
            Mathf.Clamp(damageReductionBonus, 0f, MaxSkillWebDamageReduction));
    }

    [ServerRpc]
    private void RemoveSkillWebModifierServerRpc(string modifierKey)
    {
        if (string.IsNullOrWhiteSpace(modifierKey))
            return;

        if (_skillWebRuntimeModifiers.Remove(modifierKey))
            RecalculateDerivedStats(resetCurrentValues: false);
    }

    public void ClearSkillWebModifiers()
    {
        if (_skillWebRuntimeModifiers.Count == 0)
            return;

        _skillWebRuntimeModifiers.Clear();
        RecalculateDerivedStats(resetCurrentValues: false);
    }

    // ───────────────────────── Authority / replication ─────────────────────────

    private void SetHealthAuthoritative(int value)
    {
        _currentHealth = Mathf.Clamp(value, 0, _effectiveMaxHealth);
        RaiseHealthChanged();

        if (isSpawned && isServer)
            _syncedHealth.value = _currentHealth;
    }

    private void KillAuthoritative(IMonsterDeathSequence killerSequence)
    {
        if (_isDead)
            return;

        _isDead = true;
        _diedAt = Time.time;
        if (isSpawned && isServer)
        {
            GetComponent<NetworkPlayer>()?.DropInventoryOnServerDeath();
            _syncedDead.value = true;
        }

        OnDied?.Invoke();

        if (_playerDeath != null)
            _playerDeath.Kill(killerSequence);

        if (isSpawned && isServer)
            HandleServerDeathRules();
    }

    // ───────────────────────── Death rules (server) ─────────────────────────

    private const float LastStandDelay = 5f;
    private const float LastStandHealthFraction = 0.3f;
    private const float RunFailureNoticeSeconds = 6f;
    private static PlayerVitals _runFailureOwner;
    private Coroutine _lastStandRoutine;
    private bool _lastStandPending;

    private void HandleServerDeathRules()
    {
        // Last Stand: once a day, come back where you fell instead of being locked up.
        if (HasServerPerk(ServerPerkFlags.LastStand) && !LastStandUsedToday)
        {
            LastStandUsedToday = true;
            _lastStandPending = true;
            _lastStandRoutine = StartCoroutine(LastStandRoutine());
            return;
        }

        CheckTeamWipe();
    }

    private System.Collections.IEnumerator LastStandRoutine()
    {
        Vector3 fellAt = transform.position;
        yield return new WaitForSeconds(LastStandDelay);

        _lastStandRoutine = null;
        _lastStandPending = false;
        if (!_isDead || !isSpawned)
            yield break;

        ReviveWithHealthFraction(LastStandHealthFraction);

        NetworkPlayer player = GetComponent<NetworkPlayer>();
        if (player != null && owner.HasValue)
            player.ReviveAtOwnerTarget(owner.Value, fellAt);
    }

    private void CancelLastStand()
    {
        if (_lastStandRoutine != null)
            StopCoroutine(_lastStandRoutine);
        _lastStandRoutine = null;
        _lastStandPending = false;
    }

    private static bool CanTeamRecover(IEnumerable<PlayerVitals> participants)
    {
        foreach (PlayerVitals player in participants)
            if (player != null && (!player.IsDead || player._lastStandPending))
                return true;
        return false;
    }

    /// <summary>
    /// Solo death or every player dead = the run ends (same restart as a tax eviction).
    /// Team Insurance downgrades that to "the day is lost": currency -25%, everyone revives next day.
    /// </summary>
    private void CheckTeamWipe()
    {
        if (_runFailureOwner != null)
            return;

        PlayerVitals[] participants = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None)
            .Where(player => player.isSpawned).ToArray();
        if (participants.Length == 0 || CanTeamRecover(participants))
            return;

        bool insured = participants.Any(player => player.HasServerPerk(ServerPerkFlags.TeamInsurance));
        _runFailureOwner = this;
        StartCoroutine(RunFailureRoutine(insured, participants.Length));
    }

    private System.Collections.IEnumerator RunFailureRoutine(bool insured, int playerCount)
    {
        string title = insured ? "DAY LOST" : "TEAM WIPED";
        string body = insured
            ? "Everyone is down. Team Insurance kicks in:\nthe day is lost, the shared account pays 25%,\nand you wake up at camp tomorrow."
            : playerCount == 1
                ? "You died with nobody left to pay for a revive.\nThe run restarts from day 1."
                : "Everyone is dead and nobody can pay for a revive.\nThe run restarts from day 1.";

        AnnounceRunFailureObserversRpc(title, body, RunFailureNoticeSeconds);

        yield return new WaitForSecondsRealtime(RunFailureNoticeSeconds * 0.75f);

        // A day reset, a new teammate or a revive can resolve the wipe while the notice shows.
        if (_runFailureOwner != this)
            yield break;
        PlayerVitals[] participants = FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None)
            .Where(player => player.isSpawned).ToArray();
        if (participants.Length == 0 || CanTeamRecover(participants))
        {
            _runFailureOwner = null;
            yield break;
        }

        CurrencyManager currency = InstanceHandler.TryGetInstance(out CurrencyManager found)
            ? found
            : FindFirstObjectByType<CurrencyManager>();
        TimeManager time = TimeManager.Active;

        if (insured)
        {
            if (currency != null)
                currency.SetCurrencyImmediateOnServer(Mathf.RoundToInt(currency.SharedCurrency * 0.75f));

            if (time != null)
                time.ServerForceEndDay("team wipe (insured)");
        }
        else
        {
            if (currency != null)
                currency.SetCurrencyImmediateOnServer(0);

            if (time != null)
                time.ServerRestartFromDayOne("team wipe");
        }

        _runFailureOwner = null;
    }

    [ObserversRpc(runLocally: true)]
    private void AnnounceRunFailureObserversRpc(string title, string body, float seconds)
    {
        PromptPresenter.ShowPrompt(title);
        CompanyHud.GetOrCreate().ShowEvictionNotice(title, body, seconds);
    }


    private void BindSyncEvents()
    {
        if (_syncEventsBound)
            return;

        _syncedHealth.onChanged += OnSyncedHealthChanged;
        _syncedDead.onChanged += OnSyncedDeadChanged;
        _syncEventsBound = true;
    }

    private void UnbindSyncEvents()
    {
        if (!_syncEventsBound)
            return;

        _syncedHealth.onChanged -= OnSyncedHealthChanged;
        _syncedDead.onChanged -= OnSyncedDeadChanged;
        _syncEventsBound = false;
    }

    private void OnSyncedHealthChanged(int value)
    {
        if (isServer)
            return; // The server already applied it locally.

        // While this peer's own death presentation is still running, keep showing 0 HP; the local
        // respawn (ReviveToFull) restores the value. Otherwise the server's earlier revive would
        // flash a full bar behind the "YOU DIED" overlay.
        if (_isDead && _playerDeath != null && _playerDeath.IsDead)
            return;

        MirrorSyncedHealth(value);
    }

    private void OnSyncedDeadChanged(bool dead)
    {
        if (isServer)
            return;

        if (dead)
        {
            HandleDeathLocally();
            return;
        }

        if (!_isDead)
            return;

        if (_playerDeath == null || !_playerDeath.IsDead)
        {
            _isDead = false;
            MirrorSyncedHealth(_syncedHealth.value);
            return;
        }

        if (isOwner)
        {
            // Wait for the explicit server destination. A SyncVar alone must not teleport a
            // reception revive to camp before the destination RPC arrives.
            return;
        }

        // A remote copy: bring the proxy body back; the NetworkTransform supplies the position.
        _isDead = false;
        _playerDeath.ReviveFromNetwork();
        MirrorSyncedHealth(_syncedHealth.value);
    }

    /// <summary>Seconds since this player died (server + clients, approximate). 0 when alive.</summary>
    public float DeadForSeconds => _isDead ? Mathf.Max(0f, Time.time - _diedAt) : 0f;
    private float _diedAt;

    private void HandleDayResetServer()
    {
        if (!isServer)
            return;

        ResetDailyPerkUsage();

        // New day: everyone waiting in the revive room comes back for free.
        if (_isDead)
        {
            ReviveToFull();
            var player = GetComponent<NetworkPlayer>();
            if (player != null && owner.HasValue)
                player.ReviveAtOwnerTarget(owner.Value, StartMapReturnPoint.Instance != null
                    ? StartMapReturnPoint.Instance.position : transform.position);
        }
        else
        {
            Heal(MaxHealth);
        }
    }

    private void MirrorSyncedHealth(int value)
    {
        int clamped = Mathf.Clamp(value, 0, _effectiveMaxHealth);
        if (clamped == _currentHealth)
            return;

        _currentHealth = clamped;
        RaiseHealthChanged();
    }

    private void HandleDeathLocally()
    {
        if (_isDead)
            return;

        _currentHealth = 0;
        _isDead = true;
        _diedAt = Time.time;
        RaiseHealthChanged();
        OnDied?.Invoke();

        if (_playerDeath != null)
            _playerDeath.Kill(null);
    }

    // ───────────────────────── Stamina / stats ─────────────────────────

    private void TickStamina(float deltaTime)
    {
        if (_animator == null)
            _animator = GetComponent<Animator>();

        bool sprinting = _animator != null && _animator.GetFloat("Sprinting") > 0.5f;

        if (sprinting)
        {
            _regenCooldown = staminaRegenDelay;
            SetCurrentStamina(_currentStamina - staminaDrainPerSecond * deltaTime);
            return;
        }

        if (_regenCooldown > 0f)
        {
            _regenCooldown = Mathf.Max(0f, _regenCooldown - deltaTime);
            return;
        }

        SetCurrentStamina(_currentStamina + _effectiveStaminaRegen * deltaTime);
    }

    private void RecalculateDerivedStats(bool resetCurrentValues)
    {
        int previousMaxHealth = _effectiveMaxHealth;
        int maxHealthBonus = skillWebBaseModifier.maxHealthBonus;
        float maxStaminaBonus = skillWebBaseModifier.maxStaminaBonus;
        float staminaRegenBonus = skillWebBaseModifier.staminaRegenBonus;
        float damageReductionBonus = skillWebBaseModifier.damageReductionBonus;

        foreach (var modifier in _skillWebRuntimeModifiers.Values)
        {
            maxHealthBonus += modifier.maxHealthBonus;
            maxStaminaBonus += modifier.maxStaminaBonus;
            staminaRegenBonus += modifier.staminaRegenBonus;
            damageReductionBonus += modifier.damageReductionBonus;
        }

        _effectiveMaxHealth = Mathf.Max(1, baseMaxHealth + maxHealthBonus);
        _effectiveMaxStamina = Mathf.Max(1f, baseMaxStamina + maxStaminaBonus);
        _effectiveStaminaRegen = Mathf.Max(0f, staminaRegenPerSecond + staminaRegenBonus);
        _effectiveDamageReduction = Mathf.Clamp(baseDamageReduction + damageReductionBonus, 0f, 0.95f);

        if (resetCurrentValues)
        {
            _currentHealth = _effectiveMaxHealth;
            _currentStamina = _effectiveMaxStamina;
        }
        else
        {
            // A purchased health-capacity increase also fills the new capacity.
            // Reapplying the same bonuses must not heal again or revive a dead player.
            _currentHealth = _effectiveMaxHealth > previousMaxHealth && !_isDead && _currentHealth > 0
                ? _effectiveMaxHealth
                : Mathf.Clamp(_currentHealth, 0, _effectiveMaxHealth);
            _currentStamina = Mathf.Clamp(_currentStamina, 0f, _effectiveMaxStamina);
        }

        RaiseHealthChanged();
        RaiseStaminaChanged();

        if (isSpawned && isServer)
            _syncedHealth.value = _currentHealth;
    }

    private void SetCurrentStamina(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, _effectiveMaxStamina);
        if (Mathf.Approximately(clamped, _currentStamina))
            return;

        _currentStamina = clamped;
        RaiseStaminaChanged();
    }

    private bool IsLocalPlayer()
    {
        if (isSpawned)
            return isOwner;

        if (_networkPlayer != null && _networkPlayer.isSpawned)
            return _networkPlayer.isOwner;

        Camera ownerCamera = GetComponentInChildren<Camera>(true);
        return ownerCamera != null && ownerCamera.isActiveAndEnabled;
    }

    private void RaiseHealthChanged()
    {
        OnHealthChanged?.Invoke(_currentHealth, _effectiveMaxHealth);
        RefreshHud();
    }

    private void RaiseStaminaChanged()
    {
        OnStaminaChanged?.Invoke(_currentStamina, _effectiveMaxStamina);
        RefreshHud();
    }

    // ───────────────────────── HUD ─────────────────────────

    private void TryBuildHud()
    {
        if (!autoCreateHud || _hudBuilt)
            return;

        Canvas targetCanvas = ResolveHudCanvas();
        if (targetCanvas == null)
            return;

        _hudPresenter = PlayerVitalsHud.Create(targetCanvas, hudAnchorOffset, dungeonBatteryIcon);
        _hudBuilt = true;
        RefreshHud();
    }

    private Canvas ResolveHudCanvas()
    {
        if (hudCanvasOverride != null)
            return hudCanvasOverride;

        var ownedCanvases = GetComponentsInChildren<Canvas>(includeInactive: false);
        for (int i = 0; i < ownedCanvases.Length; i++)
        {
            Canvas canvas = ownedCanvases[i];
            if (IsUsableHudCanvas(canvas))
                return canvas;
        }

        return CreateRuntimeHudCanvas();
    }

    private static bool IsUsableHudCanvas(Canvas canvas)
    {
        return canvas != null
            && canvas.isActiveAndEnabled
            && canvas.renderMode == RenderMode.ScreenSpaceOverlay
            && canvas.transform.lossyScale.sqrMagnitude > 0.0001f;
    }

    private Canvas CreateRuntimeHudCanvas()
    {
        if (_runtimeHudCanvas != null)
            return _runtimeHudCanvas;

        var canvasObject = new GameObject(
            "PlayerVitalsRuntimeCanvas",
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        _runtimeHudCanvas = canvasObject.GetComponent<Canvas>();
        _runtimeHudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _runtimeHudCanvas.sortingOrder = 1000;

        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return _runtimeHudCanvas;
    }

    private void RefreshHud()
    {
        if (!_hudBuilt || _hudPresenter == null)
            return;

        _hudPresenter.SetValues(
            _currentHealth,
            _effectiveMaxHealth,
            _currentStamina,
            _effectiveMaxStamina);
        _hudPresenter.SetPerks(_perks);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        UnbindSyncEvents();

        if (_hudPresenter != null)
            Destroy(_hudPresenter.gameObject);

        if (_runtimeHudCanvas != null)
            Destroy(_runtimeHudCanvas.gameObject);
    }
}

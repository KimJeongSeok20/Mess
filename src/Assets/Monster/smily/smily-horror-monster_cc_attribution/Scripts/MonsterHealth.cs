using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using PurrNet;
using GoreSimulatorComponent = PampelGames.GoreSimulator.GoreSimulator;

public enum SmilyAudioEvent
{
    StopLoop = 0,
    IdleLoop,
    PatrolLoop,
    Detect,
    WarningSmile,
    TeleportWindup,
    Teleport,
    AttackWindup,
    AttackCommit,
    JumpLand,
    FleeStart,
    DoorOpen,
    Death,
    GoreExplosion,
}

[DisallowMultipleComponent]
public class MonsterHealth : NetworkBehaviour
{
    [Header("Configuration")]
    [SerializeField] private MonsterConfig config;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private SmilyAudioController smilyAudioController;

    [Header("Death Cleanup")]
    [SerializeField] private bool stopNavMeshAgentOnDeath = true;
    [SerializeField] private bool disableCollidersOnDeath = true;
    [SerializeField] private bool disableAnimatorOnDeath;
    [SerializeField] private Behaviour[] disableBehavioursOnDeath;

    [Header("Despawn / Destroy (Optional)")]
    [SerializeField] private bool despawnOnDeath = false;
    [SerializeField] private float despawnDelay = 5f;

    [Header("Gore Death")]
    [SerializeField] private GoreSimulatorComponent _goreSimulator;
    [Tooltip("일반 총탄/근접 사망에서도 지정 뼈를 절단할지 여부입니다. 기본값은 온전한 아이템형 시체를 위해 false입니다.")]
    [SerializeField] private bool enableNormalDeathCut;
    [Tooltip("enableNormalDeathCut이 켜졌을 때만 사용하는 절단 뼈 이름입니다.")]
    [SerializeField] private string normalGoreBoneName;
    [SerializeField] private float normalGoreImpulse = 2.5f;
    [SerializeField] private float goreExplosionForce = 5f;
    [SerializeField] private float goreExplosionRadius = 1.5f;
    [SerializeField, Min(0f)] private float gorePartImpulse = 0.9f;
    [SerializeField, Min(0f)] private float gorePartTorque = 0.6f;
    [SerializeField, Min(0.1f)] private float gorePartMaxSpeed = 3f;
    [SerializeField, Min(0.1f)] private float gorePartMaxAngularSpeed = 8f;
    [SerializeField, Min(0.1f)] private float goreRemainsLifetime = 20f;
    [SerializeField] private bool despawnExplodedBody = true;

    [Header("Corpse Ragdoll Stability")]
    [SerializeField, Min(0.1f)] private float corpseRagdollMaxSpeed = 6f;
    [SerializeField, Min(0.1f)] private float corpseRagdollMaxAngularSpeed = 12f;
    [SerializeField, Min(0.1f)] private float corpseRagdollMaxDepenetrationVelocity = 2f;
    [SerializeField, Min(0f)] private float corpseDeathUpwardImpulse = 0.3f;
    [SerializeField, Min(0f)] private float corpseDeathTorque = 0.35f;

    [Header("Hit VFX (Optional)")]
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField, Min(0.1f)] private float hitVfxScale = 1f;
    [SerializeField, Min(0.1f)] private float hitVfxLifetime = 2f;

    [Header("Death VFX (Optional)")]
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private Vector3 deathVfxOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0.1f)] private float deathVfxScale = 1f;
    [SerializeField, Min(0f)] private float deathVfxLifetime = 5f;
    [SerializeField] private GameObject deathPoolPrefab;
    [SerializeField, Min(0.1f)] private float deathDecalScale = 1.5f;
    [SerializeField, Min(0f)] private float deathDecalLifetime = 20f;

    private readonly SyncVar<int> _netHealth = new(ownerAuth: false);
    private readonly SyncVar<int> _netMaxHealth = new(ownerAuth: false);
    private readonly SyncVar<bool> _netDead = new(ownerAuth: false);
    private readonly SyncVar<int> _netDeathDamageType = new(ownerAuth: false);
    private readonly SyncVar<Vector3> _netDeathHitPoint = new(ownerAuth: false);
    private readonly SyncVar<Vector3> _netDeathHitDirection = new(ownerAuth: false);
    // Smily audio relay: the brain runs on the server only, so loops and one-shots are relayed
    // to every peer from here. The loop SyncVar lets late joiners start the right loop.
    private readonly SyncVar<int> _netSmilyLoop = new((int)SmilyAudioEvent.StopLoop, ownerAuth: false);
    private SmilyAudioController _smilyAudio;

    public int MaxHealth => _netMaxHealth.value > 0 ? _netMaxHealth.value : ResolveScaledMaxHealth();
    public int Health => _netHealth.value;
    public bool IsDead => _netDead.value;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    private Collider[] _colliders;
    private bool _deathAppliedLocal;
    private bool _initialized;
    private MaterialPropertyBlock _propBlock;
    private Coroutine _flashCoroutine;

    [SerializeField] private Collider corpsePickupCollider;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
        if (smilyAudioController == null) smilyAudioController = GetComponent<SmilyAudioController>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _propBlock = new MaterialPropertyBlock();
    }

    private bool IsServerAuthority
    {
        get
        {
            if (isSpawned) return isServer;
            var nm = NetworkManager.main;
            return nm == null || nm.isServer;
        }
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        InitHealth();
    }

    private void Start()
    {
        if (!_initialized)
            InitHealth();
    }

    private void InitHealth()
    {
        if (_initialized) return;
        _initialized = true;

        _netHealth.onChanged += OnNetHealthChanged;
        _netMaxHealth.onChanged += OnNetMaxHealthChanged;
        _netDead.onChanged += OnNetDeadChanged;

        if (IsServerAuthority)
        {
            _netMaxHealth.value = ResolveScaledMaxHealth();
            _netDead.value = false;
            _netHealth.value = MaxHealth;
            _netDeathDamageType.value = (int)DamageType.Bullet;
            _netDeathHitPoint.value = transform.position;
            _netDeathHitDirection.value = Vector3.zero;
        }

        if (_netDead.value)
            ApplyDeathLocal(
                (DamageType)_netDeathDamageType.value,
                _netDeathHitPoint.value,
                _netDeathHitDirection.value);

        if (!IsServerAuthority && !_netDead.value)
            ApplySmilyAudio(ResolveSmilyAudio(), (SmilyAudioEvent)_netSmilyLoop.value);

        if (!IsServerAuthority)
            Debug.Log($"[MonsterHealth] Client received monster '{name}' hp={_netHealth.value}/{MaxHealth} dead={_netDead.value}", this);
    }

    // ───────────────────────── Smily audio relay ─────────────────────────

    private SmilyAudioController ResolveSmilyAudio()
    {
        if (_smilyAudio == null)
            _smilyAudio = GetComponent<SmilyAudioController>() ?? GetComponentInChildren<SmilyAudioController>(true);
        return _smilyAudio;
    }

    /// <summary>Server only: play an audio event on every peer (including the host, once).</summary>
    public void RelaySmilyAudio(SmilyAudioEvent audioEvent)
    {
        if (!IsServerAuthority)
            return;

        if (audioEvent == SmilyAudioEvent.IdleLoop || audioEvent == SmilyAudioEvent.PatrolLoop || audioEvent == SmilyAudioEvent.StopLoop)
            _netSmilyLoop.value = (int)audioEvent;

        if (isSpawned)
            SmilyAudioObserversRpc((int)audioEvent);
        else
            ApplySmilyAudio(ResolveSmilyAudio(), audioEvent);
    }

    [ObserversRpc(runLocally: true)]
    private void SmilyAudioObserversRpc(int audioEvent)
    {
        ApplySmilyAudio(ResolveSmilyAudio(), (SmilyAudioEvent)audioEvent);
    }

    public static void ApplySmilyAudio(SmilyAudioController audio, SmilyAudioEvent audioEvent)
    {
        if (audio == null)
            return;

        switch (audioEvent)
        {
            case SmilyAudioEvent.StopLoop: audio.StopMovementLoop("Relay"); break;
            case SmilyAudioEvent.IdleLoop: audio.PlayIdleLoop(); break;
            case SmilyAudioEvent.PatrolLoop: audio.PlayPatrolLoop(); break;
            case SmilyAudioEvent.Detect: audio.PlayDetect(); break;
            case SmilyAudioEvent.WarningSmile: audio.PlayWarningSmile(); break;
            case SmilyAudioEvent.TeleportWindup: audio.PlayTeleportWindup(); break;
            case SmilyAudioEvent.Teleport: audio.PlayTeleport(); break;
            case SmilyAudioEvent.AttackWindup: audio.PlayAttackWindup(); break;
            case SmilyAudioEvent.AttackCommit: audio.PlayAttackCommit(); break;
            case SmilyAudioEvent.JumpLand: audio.PlayJumpLand(); break;
            case SmilyAudioEvent.FleeStart: audio.PlayFleeStart(); break;
            case SmilyAudioEvent.DoorOpen: audio.PlayDoorOpen(); break;
            case SmilyAudioEvent.Death: audio.PlayDeath(); break;
            case SmilyAudioEvent.GoreExplosion: audio.PlayGoreExplosion(); break;
        }
    }

    protected override void OnDespawned()
    {
        _netHealth.onChanged -= OnNetHealthChanged;
        _netMaxHealth.onChanged -= OnNetMaxHealthChanged;
        _netDead.onChanged -= OnNetDeadChanged;
        base.OnDespawned();
    }

    private int BaseMaxHealth => config != null ? Mathf.Max(1, config.maxHealth) : 100;

    private int ResolveScaledMaxHealth()
    {
        float hour = TimeManager.Active != null ? TimeManager.Active.GetCurrentTime() : 12f;
        return Mathf.Max(1, Mathf.RoundToInt(BaseMaxHealth * TimeManager.CurrentDailyScalingMultiplier * NightHealthMultiplier(hour)));
    }

    /// <summary>Night hour threshold (24 h clock) from which spawned monsters get the night health bonus.</summary>
    public const float NightStartHour = 18f;
    public const float NightHealthBonus = 0.3f;

    /// <summary>Monsters that spawn after dusk are 30% tougher: staying late in the dungeon should cost something.</summary>
    public static float NightHealthMultiplier(float hour)
    {
        hour = Mathf.Repeat(hour, 24f);
        return hour >= NightStartHour || hour < 6f ? 1f + NightHealthBonus : 1f;
    }

    private void OnNetMaxHealthChanged(int newValue)
    {
        OnHealthChanged?.Invoke(_netHealth.value, MaxHealth);
    }

    private void OnNetHealthChanged(int newValue)
    {
        OnHealthChanged?.Invoke(newValue, MaxHealth);
    }

    private void OnNetDeadChanged(bool newValue)
    {
        if (newValue)
        {
            ApplyDeathLocal(
                (DamageType)_netDeathDamageType.value,
                _netDeathHitPoint.value,
                _netDeathHitDirection.value);
            OnDied?.Invoke();
        }
    }

    public void TakeDamage(int amount, Vector3 hitPoint = default)
    {
        TakeDamage(DamageRequest.Bullet(amount, hitPoint));
    }

    public void TakeDamage(DamageRequest request)
    {
        if (!IsServerAuthority) return;
        if (_netDead.value) return;
        if (request.Amount <= 0) return;

        Vector3 hitPoint = request.HasHitPoint ? request.HitPoint : transform.position;
        int newHp = Mathf.Max(0, _netHealth.value - request.Amount);
        _netHealth.value = newHp;

        if (isSpawned)
            HitObserversRpc(hitPoint, request.HitDirection);
        else
            PlayHitLocal(hitPoint, request.HitDirection);

        if (newHp <= 0)
        {
            _netDeathDamageType.value = (int)request.DamageType;
            _netDeathHitPoint.value = hitPoint;
            _netDeathHitDirection.value = request.HitDirection;
            MarkTrophyGrade(request);
            _netDead.value = true;

            if (isSpawned)
                DieObserversRpc((int)request.DamageType, hitPoint, request.HitDirection);
            else
                PlayDieLocal(request.DamageType, hitPoint, request.HitDirection);

            if (request.DamageType == DamageType.Explosive && despawnExplodedBody)
                Invoke(nameof(DespawnServer), Mathf.Max(0.1f, goreRemainsLifetime));
            else if (despawnOnDeath)
                Invoke(nameof(DespawnServer), Mathf.Max(0f, despawnDelay));
        }
    }

    // ───────────────────────── Trophy grade ─────────────────────────

    [Header("Trophy Grade")]
    [SerializeField, Tooltip("A killing blow within this distance of the head bone leaves an intact head (Rare trophy, double processor points, double price).")]
    private float headshotRadius = 0.38f;
    [SerializeField, Tooltip("Head bone name fragment searched in the hierarchy (case-insensitive).")]
    private string headBoneName = "head";

    private Transform _headBone;
    private bool _headBoneSearched;

    /// <summary>Server only: grade the corpse item on this monster by how it was killed.</summary>
    private void MarkTrophyGrade(DamageRequest request)
    {
        Item trophy = GetComponentInChildren<Item>(true);
        if (trophy == null)
            return;

        bool intact = request.HasHitPoint
                      && request.DamageType != DamageType.Explosive
                      && IsHeadHit(request.HitPoint);

        trophy.SetRarityImmediateOnServer(intact ? (int)ItemRarity.Rare : (int)ItemRarity.Common);
        if (intact)
        {
            trophy.SetPriceImmediateOnServer(Mathf.Max(1, trophy.Price * 2));
            Debug.Log($"[MonsterHealth] {name}: intact head trophy (kill within {headshotRadius:0.00} m of the head).", this);
        }
    }

    private bool IsHeadHit(Vector3 hitPoint)
    {
        if (!_headBoneSearched)
        {
            _headBoneSearched = true;
            _headBone = FindBone(transform, headBoneName);
        }

        if (_headBone == null)
            return false;

        float radius = headshotRadius * Mathf.Max(0.1f, transform.lossyScale.y);
        return (hitPoint - _headBone.position).sqrMagnitude <= radius * radius;
    }

    private static Transform FindBone(Transform root, string fragment)
    {
        if (root == null || string.IsNullOrEmpty(fragment))
            return null;

        Transform best = null;
        string lowerFragment = fragment.ToLowerInvariant();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.ToLowerInvariant().Contains(lowerFragment))
                continue;
            // Prefer the shortest name ("head" over "headTop_End" or "head_gore_cap").
            if (best == null || child.name.Length < best.name.Length)
                best = child;
        }

        return best;
    }

    [ObserversRpc]
    private void HitObserversRpc(Vector3 hitPoint, Vector3 hitDirection)
    {
        PlayHitLocal(hitPoint, hitDirection);
    }

    private void PlayHitLocal(Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_netDead.value) return;

        if (hitVfxPrefab != null)
        {
            Vector3 bodyCenter = rb != null ? rb.worldCenterOfMass : transform.position + deathVfxOffset;
            Vector3 direction = hitDirection.sqrMagnitude > 0.0001f
                ? -hitDirection
                : hitPoint - bodyCenter;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.up;

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized)
                * hitVfxPrefab.transform.rotation;
            BloodVfxVisual.Spawn(hitVfxPrefab, hitPoint, rotation, hitVfxScale, hitVfxLifetime);
        }

        // 1. Animation Trigger (if available)
        if (config != null && animator != null && !string.IsNullOrEmpty(config.hitTrigger))
            animator.SetTrigger(config.hitTrigger);

        // 2. Visual Flicker (Flinch Feedback)
        if (renderers != null && config != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine());
        }

        // 3. Physical Impulse (Server only or local visual only?)
        // If it's a server authority move system, we should apply force on server.
        if (IsServerAuthority && rb != null && !rb.isKinematic && config != null && hitPoint != default)
        {
            Vector3 pushDir = (transform.position - hitPoint).normalized;
            pushDir.y = 0.1f; // Slight pop up
            rb.AddForce(pushDir * config.knockbackForce, ForceMode.Impulse);
        }
    }

    private IEnumerator FlashCoroutine()
    {
        float duration = config != null ? config.flashDuration : 0.1f;
        Color flashColor = config != null ? config.hitFlashColor : Color.white;

        SetRenderersColor(flashColor);
        yield return new WaitForSeconds(duration);
        SetRenderersColor(Color.black); // Reset emission/overlay
        _flashCoroutine = null;
    }

    private void SetRenderersColor(Color color)
    {
        if (renderers == null) return;
        
        // Using _EmissionColor as a common flicker property for Lit shaders
        _propBlock.SetColor("_EmissionColor", color);
        foreach (var r in renderers)
        {
            if (r != null)
            {
                r.SetPropertyBlock(_propBlock);
                if (color != Color.black) r.material.EnableKeyword("_EMISSION");
            }
        }
    }

    [ObserversRpc]
    private void DieObserversRpc(int damageType, Vector3 hitPoint, Vector3 hitDirection)
    {
        PlayDieLocal((DamageType)damageType, hitPoint, hitDirection);
    }

    private void PlayDieLocal(DamageType damageType, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (damageType != DamageType.Explosive
            && config != null
            && animator != null
            && !string.IsNullOrEmpty(config.dieTrigger))
            animator.SetTrigger(config.dieTrigger);

        ApplyDeathLocal(damageType, hitPoint, hitDirection);
    }

    private void ApplyDeathLocal(DamageType damageType, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_deathAppliedLocal) return;
        _deathAppliedLocal = true;

        SpawnDeathVfx(hitPoint, damageType == DamageType.Explosive ? 1.5f : 1f);
        SpawnDeathDecal(hitPoint);

        var jump = GetComponent<JumpToPoint>();
        if (jump != null) { jump.CancelJump(); jump.enabled = false; }

        if (stopNavMeshAgentOnDeath && agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }

            agent.enabled = false;
        }

        smilyAudioController?.PlayDeath();

        if (damageType == DamageType.Explosive)
            ApplyExplosiveDeath(hitPoint);
        else
            ApplyCorpseDeath(hitPoint, hitDirection);

        DisableAIs();
    }

    private void ApplyExplosiveDeath(Vector3 hitPoint)
    {
        if (animator != null)
            animator.enabled = false;

        if (_colliders != null)
        {
            foreach (Collider collider in _colliders)
            {
                if (collider != null)
                    collider.enabled = false;
            }
        }

        if (_goreSimulator != null && _goreSimulator.smr != null && _goreSimulator.meshCutInitialized)
        {
            uint layer = ResolveSourceRenderingLayerMask();
            smilyAudioController?.PlayGoreExplosion();
            _goreSimulator.ExecuteExplosion(hitPoint, goreExplosionForce, out var parts);
            _goreSimulator.smr.enabled = false;
            ApplyRenderingLayerToExplosionParts(parts, layer);
            EnsureExplosionPartPhysics(parts, hitPoint);
            return;
        }

        Debug.LogError($"[MonsterHealth] Explosive death requires an initialized GoreSimulator on '{name}'.");
        if (renderers != null)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = false;
            }
        }
    }

    private void ApplyCorpseDeath(Vector3 hitPoint, Vector3 hitDirection)
    {
        if (animator != null)
            animator.enabled = false;

        Vector3 impulse = hitDirection.sqrMagnitude > 0.0001f
            ? hitDirection.normalized * normalGoreImpulse
            : Vector3.zero;

        if (_goreSimulator != null && _goreSimulator.ragdollInitialized)
        {
            PrepareCorpseRagdollPhysics();

            bool canCut = enableNormalDeathCut
                && _goreSimulator.meshCutInitialized
                && !string.IsNullOrWhiteSpace(normalGoreBoneName)
                && _goreSimulator.bones != null
                && _goreSimulator.bones.Any(bone => bone != null && bone.name == normalGoreBoneName);

            if (canCut)
                _goreSimulator.ExecuteRagdollCut(normalGoreBoneName, hitPoint, impulse);
            else
                _goreSimulator.ExecuteRagdoll(impulse);

            SeedCorpseFallPose(hitDirection);
            ApplyCorpseDeathImpulse(hitDirection);
        }

        int deadExcludeMask = LayerMask.GetMask("Player", "Item");
        if (_colliders != null)
        {
            foreach (Collider collider in _colliders)
            {
                if (collider == null)
                    continue;

                collider.excludeLayers = deadExcludeMask;
                if (_goreSimulator == null || !_goreSimulator.ragdollInitialized)
                    collider.enabled = collider == corpsePickupCollider;
            }
        }

        if (corpsePickupCollider != null)
        {
            corpsePickupCollider.enabled = true;
            int itemLayer = LayerMask.NameToLayer("Item");
            corpsePickupCollider.gameObject.layer = itemLayer;
            gameObject.layer = itemLayer;
        }
    }

    private void PrepareCorpseRagdollPhysics()
    {
        if (_goreSimulator == null)
            return;

        Rigidbody[] ragdollBodies = _goreSimulator.GetComponentsInChildren<Rigidbody>(true);
        var ragdollColliders = new List<Collider>();
        foreach (Rigidbody body in ragdollBodies)
        {
            if (body == null || body == rb)
                continue;

            foreach (Collider collider in body.GetComponents<Collider>())
            {
                NormalizeUndersizedRagdollCollider(collider);
                if (collider != null && collider.enabled)
                    ragdollColliders.Add(collider);
            }

            body.useGravity = true;
            body.detectCollisions = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = corpseRagdollMaxDepenetrationVelocity;
            body.maxLinearVelocity = corpseRagdollMaxSpeed;
            body.maxAngularVelocity = corpseRagdollMaxAngularSpeed;
            body.ResetInertiaTensor();
        }

        // This creature dies from a compact crouched pose. Its generated
        // capsules overlap before the first physics step; allowing self-contact
        // makes PhysX depenetrate the whole corpse upward instead of letting it
        // fall. Keep world/floor collision, but suppress internal corpse contact.
        for (int i = 0; i < ragdollColliders.Count; i++)
        {
            for (int j = i + 1; j < ragdollColliders.Count; j++)
                Physics.IgnoreCollision(ragdollColliders[i], ragdollColliders[j], true);
        }
    }

    private void ApplyCorpseDeathImpulse(Vector3 hitDirection)
    {
        if (_goreSimulator == null || _goreSimulator.center == null)
            return;

        Rigidbody centerBody = _goreSimulator.center.GetComponent<Rigidbody>();
        if (centerBody == null)
            return;

        Vector3 planarDirection = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.001f)
            planarDirection = transform.forward;

        planarDirection.Normalize();
        float fallSpeed = Mathf.Clamp(normalGoreImpulse, 1.8f, 2.6f);
        Vector3 velocityChange = planarDirection * fallSpeed
            + Vector3.up * Mathf.Min(corpseDeathUpwardImpulse, 0.18f);
        centerBody.linearVelocity = Vector3.ClampMagnitude(
            centerBody.linearVelocity + velocityChange,
            3f);

        // The crouched source pose is a mechanically stable tripod. A small
        // linear hit leaves it frozen upright, so give the pelvis a deliberate
        // fall rotation around the axis perpendicular to the shot direction.
        Vector3 fallAxis = Vector3.Cross(Vector3.up, planarDirection).normalized;
        centerBody.angularVelocity = Vector3.ClampMagnitude(
            centerBody.angularVelocity
            + fallAxis * Mathf.Clamp(corpseDeathTorque, 1.25f, 1.8f),
            corpseRagdollMaxAngularSpeed);
    }

    private void SeedCorpseFallPose(Vector3 hitDirection)
    {
        if (_goreSimulator == null || _goreSimulator.center == null)
            return;

        Vector3 planarDirection = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.001f)
            planarDirection = transform.forward;
        planarDirection.Normalize();

        Rigidbody[] bodies = _goreSimulator.GetComponentsInChildren<Rigidbody>(true)
            .Where(body => body != null && body != rb && !body.isKinematic)
            .ToArray();
        if (bodies.Length == 0)
            return;

        Vector3 pivot = _goreSimulator.center.transform.position;
        Quaternion fallRotation = Quaternion.AngleAxis(
            52f,
            Vector3.Cross(Vector3.up, planarDirection).normalized)
            * Quaternion.AngleAxis(18f, planarDirection);
        Vector3[] positions = bodies.Select(body => body.position).ToArray();
        Quaternion[] rotations = bodies.Select(body => body.rotation).ToArray();

        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].position = pivot + fallRotation * (positions[i] - pivot);
            bodies[i].rotation = fallRotation * rotations[i];
            bodies[i].WakeUp();
        }

        Vector3 torsoShift = planarDirection * 0.48f + transform.right * 0.16f;
        foreach (Rigidbody body in bodies)
        {
            if (body.name is "spinebase" or "belly" or "chest"
                or "shoulder.L" or "Arm.L" or "Forearm.L"
                or "shoulder.R" or "Arm.R" or "Forearm.R")
            {
                body.position += torsoShift;
            }
        }

        Physics.SyncTransforms();
        LiftSeededCorpseAboveGround(bodies);
    }

    private void LiftSeededCorpseAboveGround(Rigidbody[] bodies)
    {
        Collider[] colliders = bodies
            .SelectMany(body => body.GetComponents<Collider>())
            .Where(collider => collider != null && collider.enabled)
            .ToArray();
        if (colliders.Length == 0)
            return;

        Bounds bounds = colliders[0].bounds;
        foreach (Collider collider in colliders.Skip(1))
            bounds.Encapsulate(collider.bounds);

        Vector3 origin = bounds.center + Vector3.up * (bounds.extents.y + 1f);
        RaycastHit groundHit = Physics.RaycastAll(
                origin,
                Vector3.down,
                bounds.size.y + 4f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance)
            .FirstOrDefault(hit => hit.collider != null
                && !hit.collider.transform.IsChildOf(transform));
        if (groundHit.collider == null)
            return;

        float lift = groundHit.point.y + 0.02f - bounds.min.y;
        if (lift <= 0f)
            return;

        Vector3 correction = Vector3.up * Mathf.Min(lift, 1.5f);
        foreach (Rigidbody body in bodies)
            body.position += correction;
        Physics.SyncTransforms();
    }

    private static void NormalizeUndersizedRagdollCollider(Collider collider)
    {
        if (collider == null)
            return;

        Vector3 scale = collider.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        // Gore Simulator's generated humanoid ragdolls may sit under imported
        // 0.01-scale bones while their collider dimensions are already authored
        // in metres. That produces millimetre-sized inertia tensors and makes
        // CharacterJoints explode on the first dynamic physics step.
        const float minimumHumanoidWorldSize = 0.03f;
        const float minimumScale = 0.0001f;

        switch (collider)
        {
            case CapsuleCollider capsule:
            {
                float heightScale = capsule.direction switch
                {
                    0 => scale.x,
                    2 => scale.z,
                    _ => scale.y
                };
                float radiusScale = capsule.direction switch
                {
                    0 => Mathf.Max(scale.y, scale.z),
                    2 => Mathf.Max(scale.x, scale.y),
                    _ => Mathf.Max(scale.x, scale.z)
                };

                float worldRadius = capsule.radius * radiusScale;
                float worldHeight = capsule.height * heightScale;
                if (Mathf.Max(worldRadius, worldHeight) >= minimumHumanoidWorldSize)
                    return;

                if (capsule.radius >= minimumHumanoidWorldSize && radiusScale > minimumScale)
                    capsule.radius /= radiusScale;
                if (capsule.height >= minimumHumanoidWorldSize && heightScale > minimumScale)
                    capsule.height /= heightScale;
                break;
            }
            case BoxCollider box:
            {
                Vector3 worldSize = Vector3.Scale(box.size, scale);
                if (Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z))
                    >= minimumHumanoidWorldSize)
                    return;

                Vector3 localSize = box.size;
                if (localSize.x >= minimumHumanoidWorldSize && scale.x > minimumScale)
                    localSize.x /= scale.x;
                if (localSize.y >= minimumHumanoidWorldSize && scale.y > minimumScale)
                    localSize.y /= scale.y;
                if (localSize.z >= minimumHumanoidWorldSize && scale.z > minimumScale)
                    localSize.z /= scale.z;
                box.size = localSize;
                break;
            }
            case SphereCollider sphere:
            {
                float radiusScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                if (sphere.radius * radiusScale < minimumHumanoidWorldSize
                    && sphere.radius >= minimumHumanoidWorldSize
                    && radiusScale > minimumScale)
                    sphere.radius /= radiusScale;
                break;
            }
        }
    }

    private void SpawnDeathVfx(Vector3 hitPoint, float scaleMultiplier)
    {
        if (deathVfxPrefab == null) return;

        Vector3 position = hitPoint != default
            ? hitPoint
            : transform.position + deathVfxOffset;
        BloodVfxVisual.Spawn(
            deathVfxPrefab,
            position,
            deathVfxPrefab.transform.rotation,
            deathVfxScale * Mathf.Max(0.1f, scaleMultiplier),
            deathVfxLifetime);
    }

    private void SpawnDeathDecal(Vector3 hitPoint)
    {
        Vector3 source = hitPoint != default ? hitPoint : transform.position + Vector3.up;
        BloodPoolVisual.SpawnOnGround(
            source,
            transform,
            deathDecalScale,
            deathPoolPrefab,
            deathDecalLifetime);
    }

    private void DisableAIs()
    {
        if (HasExplicitDeathBehaviours())
        {
            foreach (var b in disableBehavioursOnDeath) if (b != null) b.enabled = false;
            return;
        }

        var behaviours = GetComponents<Behaviour>();
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            string n = b.GetType().Name;
            if (n.Contains("BehaviorAgent") || n.Contains("BehaviorGraph") || IsKnownAiBehaviourName(n))
                b.enabled = false;
        }
    }

    private bool HasExplicitDeathBehaviours()
    {
        if (disableBehavioursOnDeath == null || disableBehavioursOnDeath.Length == 0)
            return false;

        foreach (var behaviour in disableBehavioursOnDeath)
        {
            if (behaviour != null)
                return true;
        }

        return false;
    }

    private static bool IsKnownAiBehaviourName(string behaviourName)
    {
        return behaviourName == nameof(SmilyBrain)
            || behaviourName == nameof(FleeFromTarget)
            || behaviourName == nameof(DoorAutoOpener)
            || behaviourName == nameof(SmilyWallClearanceController);
    }

    private void EnsureExplosionPartPhysics(List<GameObject> parts, Vector3 explosionCenter)
    {
        if (parts == null || parts.Count == 0) return;

        var rigidbodies = new List<Rigidbody>();
        var colliders = new List<Collider>();

        foreach (var part in parts)
        {
            if (part == null) continue;

            foreach (Joint joint in part.GetComponentsInChildren<Joint>(true))
                Destroy(joint);

            Rigidbody[] partBodies = part.GetComponentsInChildren<Rigidbody>(true);
            if (partBodies.Length == 0)
            {
                MeshFilter meshFilter = part.GetComponentInChildren<MeshFilter>(true);
                GameObject physicsObject = meshFilter != null ? meshFilter.gameObject : part;
                if (!physicsObject.TryGetComponent<Collider>(out _))
                {
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        var boxCollider = physicsObject.AddComponent<BoxCollider>();
                        boxCollider.center = meshFilter.sharedMesh.bounds.center;
                        boxCollider.size = meshFilter.sharedMesh.bounds.size;
                    }
                    else
                    {
                        physicsObject.AddComponent<BoxCollider>();
                    }
                }

                partBodies = new[] { physicsObject.AddComponent<Rigidbody>() };
            }

            foreach (Rigidbody body in partBodies)
            {
                if (body == null || rigidbodies.Contains(body)) continue;
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.75f;
                body.angularDamping = 0.8f;
                body.maxLinearVelocity = gorePartMaxSpeed;
                body.maxAngularVelocity = gorePartMaxAngularSpeed;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, gorePartMaxSpeed);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, gorePartMaxAngularSpeed);
                rigidbodies.Add(body);
            }

            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.enabled && !colliders.Contains(collider))
                    colliders.Add(collider);
            }
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            for (int j = i + 1; j < colliders.Count; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        foreach (Rigidbody body in rigidbodies)
        {
            Vector3 direction = Vector3.ProjectOnPlane(
                body.worldCenterOfMass - explosionCenter,
                Vector3.up);
            if (direction.sqrMagnitude < 0.001f)
            {
                Vector2 planar = UnityEngine.Random.insideUnitCircle.normalized;
                direction = new Vector3(planar.x, 0f, planar.y);
            }
            direction = (direction.normalized + Vector3.up * 0.08f).normalized;
            body.linearVelocity = new Vector3(
                body.linearVelocity.x,
                Mathf.Min(body.linearVelocity.y, 0.75f),
                body.linearVelocity.z);
            body.AddForce(direction * gorePartImpulse, ForceMode.Impulse);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * gorePartTorque, ForceMode.Impulse);
            body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, gorePartMaxSpeed);
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, gorePartMaxAngularSpeed);
        }
    }

    private uint ResolveSourceRenderingLayerMask()
    {
        var r = GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x != null);
        return r != null ? r.renderingLayerMask : 1u;
    }

    private static void ApplyRenderingLayerToExplosionParts(List<GameObject> parts, uint mask)
    {
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p == null) continue;
            var rs = p.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs) if (r != null) r.renderingLayerMask = mask;
        }
    }

    private void DespawnServer()
    {
        if (!IsServerAuthority) return;
        Destroy(gameObject);
    }

}

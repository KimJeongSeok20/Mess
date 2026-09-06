using System;
using System.Collections.Generic;
using DunGen;
using PurrNet;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class ClownMonsterActor : NetworkBehaviour, IMonsterFlowTelemetry
{
    private enum ThrowPhase
    {
        Idle,
        WindingUp,
        Released,
        Teleporting,
        RepeatPending
    }

    private enum AudioMovementLoop
    {
        None,
        Idle,
        Patrol
    }

    private enum AudioEvent
    {
        Detect,
        AttackWindup,
        AttackCommit,
        TeleportWindup,
        Teleport
    }

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string moveSpeedParameter = "MoveSpeed";
    [SerializeField] private string throwTrigger = "Throw";
    [SerializeField] private string repeatThrowParameter = "RepeatThrow";

    [Header("Refs")]
    [SerializeField] private MonsterHealth health;
    [SerializeField] private RangeDetector rangeDetector;
    [SerializeField] private LineOfSightDetector lineOfSightDetector;
    [SerializeField] private MonsterPatrolSelector patrolSelector;
    [SerializeField] private DoorAutoOpener doorAutoOpener;
    [SerializeField] private BehaviorGraphAgent behaviorGraphAgent;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Transform throwOrigin;
    [SerializeField] private GameObject heldGiftVisualPrefab;
    [SerializeField] private GameObject thrownGiftVisualPrefab;
    [SerializeField] private Item[] randomItemDrops;
    [SerializeField] private ClownDeathSequence deathSequence;
    [SerializeField] private ClownGiftSettings giftSettings = new();

    [Header("SFX")]
    [SerializeField] private SmilyAudioController audioController;

    [Header("Combat")]
    [SerializeField] private float chaseDistance = 16f;
    [SerializeField] private float throwDistance = 10f;
    [SerializeField] private float targetMemoryDuration = 1.25f;
    [SerializeField] private float throwCooldown = 2.75f;
    [SerializeField, Range(0f, 1f)] private float repeatThrowChance = 0.5f;
    [SerializeField] private float throwSpeed = 11f;
    [SerializeField] private float upwardArc = 2.2f;
    [SerializeField] private float throwSpawnForwardOffset = 0.75f;
    [SerializeField] private float throwSpawnUpOffset = 0.08f;
    [SerializeField] private float throwReleaseNormalizedTime = 24f / 66f;
    [SerializeField] private float repeatThrowDelay = 0.6f;
    [SerializeField] private float teleportSequenceTimeout = 10f;
    [SerializeField] private bool throwDebugLog = true;
    [SerializeField] private bool networkDebugLog;

    [Header("Patrol")]
    [SerializeField] private float patrolMoveSpeed = 0.65f;
    [SerializeField] private float patrolArrivalDistance = 0.8f;
    [SerializeField] private float patrolPauseDuration = 0.35f;
    [SerializeField] private float destinationRepathThreshold = 0.35f;
    [SerializeField] private float partialPathRefreshInterval = 0.75f;

    [Header("Teleport")]
    [SerializeField] private float teleportNavMeshSampleRadius = 5f;
    [SerializeField] private int teleportRegistryTries = 8;
    [SerializeField] private int teleportTileTries = 16;
    [SerializeField] private float teleportMinDistance = 6f;
    [SerializeField] private float teleportSurfaceProbeHeight = 6f;
    [SerializeField] private float teleportSurfaceProbeDistance = 16f;
    [SerializeField] private float teleportSurfaceMinNormalY = 0.85f;
    [SerializeField] private float teleportSurfaceToNavMeshTolerance = 1.5f;
    [SerializeField] private float teleportNearbyRadius = 12f;
    [SerializeField] private bool teleportDebugLog;

    [Header("Teleport Feedback")]
    [SerializeField] private GameObject teleportEffectPrefab;
    [SerializeField] private float teleportEffectLifetime = 1.25f;
    [SerializeField] private Vector3 teleportEffectOffset = new(0f, 1f, 0f);

    [Header("Held Gift")]
    [SerializeField] private Vector3 heldGiftLocalPosition = new(0.04f, -0.04f, 0.08f);
    [SerializeField] private Vector3 heldGiftLocalEulerAngles = new(0f, 0f, 90f);
    [SerializeField] private Vector3 heldGiftLocalScale = Vector3.one;
    [SerializeField] private Vector3 thrownGiftLocalScale = new(0.3333f, 0.3333f, 0.3333f);

    private GameObject _heldGiftInstance;
    private GameObject _currentTarget;
    private Vector3 _desiredPosition;
    private Vector3 _reportedDestination;
    private float _desiredYaw;
    private Vector3 _lastKnownTargetPosition;
    private float _lastKnownTargetExpiresAt;
    private bool _hasLastKnownTargetPosition;
    private bool _investigatingLastKnownPosition;
    private Vector3 _pendingThrowTargetPosition;
    private GameObject _pendingThrowTarget;
    private float _nextThrowAt;
    private float _nextPatrolPickAt;
    private float _throwSequenceStartedAt;
    private ThrowPhase _throwPhase;
    private bool _throwReleased;
    private bool _disappearEntered;
    private bool _queueRepeatThrow;
    private GameObject _scheduledRepeatTarget;
    private Transform _patrolPoint;
    private Vector3 _requestedNavigationDestination;
    private int _moveSpeedHash;
    private int _throwTriggerHash;
    private int _repeatThrowHash;
    private int _locomotionStateHash;
    private int _throwStateHash;
    private int _disappearStateHash;
    private float _nextPartialPathRefreshAt;
    private bool _hasRequestedNavigationDestination;
    private AudioMovementLoop _audioMovementLoop;

    private void OnValidate()
    {
        giftSettings?.SyncRandomItemDropWeightsFromSources();
    }

    public bool IsDead => health != null && health.IsDead;
    public bool IsBusy => _throwPhase != ThrowPhase.Idle;
    public bool HasPendingRepeatThrow => _throwPhase == ThrowPhase.RepeatPending;
    public bool ThrowReleased => _throwPhase is ThrowPhase.Released or ThrowPhase.Teleporting;
    public bool TeleportTriggered => _throwPhase == ThrowPhase.Teleporting;
    public float ChaseDistance => chaseDistance;
    public float ThrowDistance => throwDistance;
    public bool IsPatrolling => !IsDead && !IsBusy && _currentTarget == null && _patrolPoint != null;
    public string CurrentPatrolPointName => _patrolPoint != null ? _patrolPoint.name : string.Empty;
    public bool IsTraversingLink => doorAutoOpener != null && doorAutoOpener.IsTraversing;
    public bool CanBroadcastNetworkPresentation => isSpawned;
    public MonsterIntent CurrentIntent
    {
        get
        {
            if (IsDead)
                return MonsterIntent.Dead;

            if (IsTraversingLink)
                return MonsterIntent.Traverse;

            if (IsAttacking)
                return MonsterIntent.Attack;

            if (_investigatingLastKnownPosition)
                return MonsterIntent.Investigate;

            if (_currentTarget != null)
                return MonsterIntent.Chase;

            if (_patrolPoint != null)
                return MonsterIntent.Patrol;

            return MonsterIntent.Idle;
        }
    }

    public bool HasTarget => _currentTarget != null || _pendingThrowTarget != null || _scheduledRepeatTarget != null;

    public string CurrentTargetName
    {
        get
        {
            if (_pendingThrowTarget != null)
                return _pendingThrowTarget.name;

            if (_scheduledRepeatTarget != null)
                return _scheduledRepeatTarget.name;

            return _currentTarget != null ? _currentTarget.name : string.Empty;
        }
    }

    public bool IsTraversing => IsTraversingLink;

    public bool IsAttacking => IsBusy || HasPendingRepeatThrow;

    public string CurrentAttackPhase => IsAttacking ? _throwPhase.ToString() : string.Empty;

    public Vector3 DesiredDestination => _reportedDestination;
    public int NavMeshAgentTypeId => agent != null ? agent.agentTypeID : ClownNavMeshConfig.AgentTypeId;
    public int NavMeshAreaMask => agent != null ? agent.areaMask : NavMesh.AllAreas;

    private bool HasServerAuthority()
    {
        if (isSpawned)
            return isServer;

        NetworkManager nm = NetworkManager.main;
        return nm == null || nm.isServer;
    }

    public void TickBehaviorLoop()
    {
        if (!HasServerAuthority())
            return;

        if (IsDead)
        {
            SetAudioMovementLoopNetworked(AudioMovementLoop.None);
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);
            SetHeldGiftVisible(false);
            return;
        }

        TickThrowSequence();
        if (IsBusy)
            return;

        if (IsTraversingLink)
        {
            SetAudioMovementLoopNetworked(AudioMovementLoop.None);
            CaptureCurrentTransformAsDesired();
            _desiredYaw = transform.eulerAngles.y;
            return;
        }

        if (_currentTarget == null)
        {
            _currentTarget = AcquireTarget(rangeDetector, lineOfSightDetector, chaseDistance);
            if (_currentTarget == null)
            {
                // A heard noise (gunfire, sprinting) is worth a look before falling back to patrol.
                if (CanInvestigateLastKnownTarget())
                {
                    _investigatingLastKnownPosition = true;
                    SetAudioMovementLoopNetworked(AudioMovementLoop.Patrol);
                    MoveTowards(_lastKnownTargetPosition);
                    if (HasReachedPosition(_lastKnownTargetPosition, patrolArrivalDistance))
                        ClearLastKnownTarget();
                    return;
                }

                _investigatingLastKnownPosition = false;
                PatrolForTarget();
                return;
            }

            PlayAudioEventNetworked(AudioEvent.Detect);
            RememberVisibleTarget(_currentTarget);
            ClearPatrolPoint();
        }

        bool valid = MoveTowardsTarget(_currentTarget, lineOfSightDetector, out bool readyToThrow);
        if (!valid)
        {
            _currentTarget = null;
            return;
        }

        if (readyToThrow && Time.time >= _nextThrowAt)
            BeginThrowSequence(_currentTarget);
    }

    private void Awake()
    {
        giftSettings?.SyncRandomItemDropWeightsFromSources();

        health ??= GetComponent<MonsterHealth>();
        rangeDetector ??= GetComponent<RangeDetector>();
        lineOfSightDetector ??= GetComponent<LineOfSightDetector>();
        patrolSelector ??= GetComponent<MonsterPatrolSelector>();
        doorAutoOpener ??= GetComponent<DoorAutoOpener>();
        behaviorGraphAgent ??= GetComponent<BehaviorGraphAgent>();
        agent ??= GetComponent<NavMeshAgent>();
        animator ??= GetComponentInChildren<Animator>(true);
        deathSequence ??= GetComponent<ClownDeathSequence>();
        audioController ??= GetComponent<SmilyAudioController>();

        if (throwOrigin == null)
            throwOrigin = FindThrowOrigin();

        if (heldGiftVisualPrefab == null)
            heldGiftVisualPrefab = thrownGiftVisualPrefab;

        if (!string.IsNullOrEmpty(moveSpeedParameter))
            _moveSpeedHash = Animator.StringToHash(moveSpeedParameter);

        if (!string.IsNullOrEmpty(throwTrigger))
            _throwTriggerHash = Animator.StringToHash(throwTrigger);

        if (!string.IsNullOrEmpty(repeatThrowParameter))
            _repeatThrowHash = Animator.StringToHash(repeatThrowParameter);

        _locomotionStateHash = Animator.StringToHash("Locomotion");
        _throwStateHash = Animator.StringToHash("Throw");
        _disappearStateHash = Animator.StringToHash("Disappear");

        EnsureHeldGiftVisual();
        SetHeldGiftVisible(true);
        CaptureDesiredTransform();
    }

    private void OnEnable()
    {
        MonsterNoise.Reported += OnNoiseReported;
    }

    private void OnDisable()
    {
        MonsterNoise.Reported -= OnNoiseReported;
        CancelInvoke(nameof(BeginDelayedRepeatThrow));
        ClearRequestedNavigationDestination();

        if (_throwPhase == ThrowPhase.RepeatPending)
        {
            _throwPhase = ThrowPhase.Idle;
            _scheduledRepeatTarget = null;
        }

        SetAudioMovementLoop(AudioMovementLoop.None);
    }

    private void LateUpdate()
    {
        if (!HasServerAuthority())
            return;

        if (IsDead)
            return;

        if (IsTraversingLink)
        {
            CaptureCurrentTransformAsDesired();
            return;
        }

        TryEnsureAgentOnNavMesh();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            _desiredPosition = agent.nextPosition;
            _reportedDestination = agent.hasPath ? agent.destination : agent.nextPosition;
            _desiredYaw = transform.eulerAngles.y;

            if (networkDebugLog && _throwPhase != ThrowPhase.Idle)
                Debug.Log($"[ClownNetDebug] {name} LateUpdate authoritative pos={agent.nextPosition} dest={_reportedDestination} yaw={_desiredYaw:F1}", this);

            return;
        }

        transform.position = _desiredPosition;
        transform.rotation = Quaternion.Euler(0f, _desiredYaw, 0f);

        if (networkDebugLog && _throwPhase != ThrowPhase.Idle)
            Debug.Log($"[ClownNetDebug] {name} LateUpdate fallback pos={_desiredPosition} yaw={_desiredYaw:F1}", this);
    }

    private void Update()
    {
        if (!HasServerAuthority())
            return;

        if (IsTraversingLink)
        {
            CaptureCurrentTransformAsDesired();
            return;
        }

        TryEnsureAgentOnNavMesh();

        if (behaviorGraphAgent != null && behaviorGraphAgent.enabled && behaviorGraphAgent.Graph != null)
            return;

        TickBehaviorLoop();
    }

    public GameObject AcquireTarget(RangeDetector rangeDetector, LineOfSightDetector lineOfSightDetector, float maxDistance)
    {
        if (IsDead || IsBusy)
            return null;

        GameObject candidate = rangeDetector != null ? rangeDetector.UpdateDetector() : null;
        if (candidate != null && HasLineOfSight(candidate, lineOfSightDetector))
            return candidate;

        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float maxDistanceSqr = maxDistance * maxDistance;
        float bestLineOfSightDistance = float.MaxValue;
        GameObject bestLineOfSightTarget = null;

        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null)
                continue;

            // Stragglers (no teammate nearby) score as if they were closer.
            float scored = MonsterTargeting.ScoredDistance(transform.position, player.transform);
            float sqrDistance = scored * scored;
            if (sqrDistance > maxDistanceSqr)
                continue;

            if (HasLineOfSight(player, lineOfSightDetector) && sqrDistance < bestLineOfSightDistance)
            {
                bestLineOfSightDistance = sqrDistance;
                bestLineOfSightTarget = player;
            }
        }

        return bestLineOfSightTarget;
    }

    public bool MoveTowardsTarget(GameObject target, LineOfSightDetector lineOfSightDetector, out bool readyToThrow)
    {
        readyToThrow = false;

        if (target == null || IsDead)
            return false;

        float sqrDistance = (target.transform.position - transform.position).sqrMagnitude;
        if (sqrDistance > chaseDistance * chaseDistance)
        {
            SetAudioMovementLoopNetworked(AudioMovementLoop.None);
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);
            CaptureDesiredTransform();
            return false;
        }

        bool hasLineOfSight = HasLineOfSight(target, lineOfSightDetector);
        if (hasLineOfSight && sqrDistance <= throwDistance * throwDistance)
        {
            RememberVisibleTarget(target);
            SetAudioMovementLoopNetworked(AudioMovementLoop.None);
            StopMovement();
            FaceTarget(target.transform.position);
            UpdateAnimatorMoveSpeed(0f);
            CaptureDesiredTransform();
            readyToThrow = true;
            return true;
        }

        if (hasLineOfSight)
        {
            RememberVisibleTarget(target);
            SetAudioMovementLoopNetworked(AudioMovementLoop.Patrol);
            MoveTowards(target.transform.position);
        }
        else if (CanInvestigateLastKnownTarget())
        {
            _investigatingLastKnownPosition = true;
            SetAudioMovementLoopNetworked(AudioMovementLoop.Patrol);
            MoveTowards(_lastKnownTargetPosition);
            if (HasReachedPosition(_lastKnownTargetPosition, patrolArrivalDistance))
            {
                _currentTarget = null;
                ClearLastKnownTarget();
            }
        }
        else
        {
            ClearLastKnownTarget();
            SetAudioMovementLoopNetworked(AudioMovementLoop.None);
            StopMovement();
            return false;
        }

        UpdateAnimatorMoveSpeed(1f);
        CaptureDesiredTransform();
        return true;
    }

    public bool BeginThrowSequence(GameObject target)
    {
        if (!HasServerAuthority())
            return false;

        CancelInvoke(nameof(BeginDelayedRepeatThrow));

        if (target == null || IsDead || IsBusy || Time.time < _nextThrowAt)
            return false;

        _pendingThrowTarget = target;
        _pendingThrowTargetPosition = target.transform.position;
        _throwSequenceStartedAt = Time.time;
        _throwPhase = ThrowPhase.WindingUp;
        _throwReleased = false;
        _disappearEntered = false;
        _queueRepeatThrow = false;
        _scheduledRepeatTarget = null;
        SetRepeatThrowAnimatorFlag(false);
        SetHeldGiftVisible(true);
        if (CanBroadcastNetworkPresentation)
            SetHeldGiftVisibleObserversRpc(true);
        PlayAudioEventNetworked(AudioEvent.AttackWindup);
        StopMovement();
        UpdateAnimatorMoveSpeed(0f);
        FaceTarget(_pendingThrowTargetPosition);
        CaptureDesiredTransform();

        if (animator != null && animator.runtimeAnimatorController != null && _throwTriggerHash != 0)
            animator.SetTrigger(_throwTriggerHash);
        else
            ReleaseThrow();

        return true;
    }

    public bool TickThrowSequence()
    {
        if (_throwPhase == ThrowPhase.Idle || _throwPhase == ThrowPhase.RepeatPending)
            return true;

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            if (!_throwReleased)
                ReleaseThrow();

            if (_throwPhase != ThrowPhase.Teleporting)
                TeleportNow();

            FinishThrowSequence();
            return true;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = state.shortNameHash;
        float sequenceElapsed = Time.time - _throwSequenceStartedAt;
        bool isInTransition = animator.IsInTransition(0);

        if (!_throwReleased && _pendingThrowTarget != null)
        {
            _pendingThrowTargetPosition = _pendingThrowTarget.transform.position;
            FaceTarget(_pendingThrowTargetPosition);
        }

        if (stateHash == _disappearStateHash && !_disappearEntered)
        {
            _disappearEntered = true;
            PlayAudioEventNetworked(AudioEvent.TeleportWindup);
        }

        if (!_throwReleased && ((stateHash == _throwStateHash && state.normalizedTime >= throwReleaseNormalizedTime) || sequenceElapsed >= 1.0f))
            ReleaseThrow();

        bool disappearExiting = _disappearEntered && isInTransition && stateHash == _disappearStateHash;
        bool disappearFinished = _disappearEntered && stateHash != _disappearStateHash;
        bool repeatReturnedToLocomotion = _queueRepeatThrow && _throwReleased && stateHash == _locomotionStateHash && !isInTransition;
        bool timedOut = sequenceElapsed >= teleportSequenceTimeout;

        if (repeatReturnedToLocomotion)
        {
            FinishThrowSequence();
            return true;
        }

        if (_throwPhase != ThrowPhase.Teleporting && !_queueRepeatThrow && (disappearExiting || disappearFinished || timedOut))
            TeleportNow();

        if ((_throwReleased && !_queueRepeatThrow && (disappearExiting || disappearFinished)) || timedOut)
        {
            if (_throwPhase != ThrowPhase.Teleporting && !_queueRepeatThrow)
                TeleportNow();

            FinishThrowSequence();
            return true;
        }

        CaptureDesiredTransform();
        return false;
    }

    public void ClearTarget()
    {
        _currentTarget = null;
        ClearLastKnownTarget();
        StopMovement();
        UpdateAnimatorMoveSpeed(0f);
        CaptureDesiredTransform();
    }

    public void ConfigurePresentation(GameObject heldPrefab, GameObject thrownPrefab, Item[] drops)
    {
        if (heldPrefab != null)
            heldGiftVisualPrefab = heldPrefab;

        if (thrownPrefab != null)
            thrownGiftVisualPrefab = thrownPrefab;

        if (drops != null && drops.Length > 0)
            randomItemDrops = drops;
    }

    private void ReleaseThrow()
    {
        if (!HasServerAuthority())
            return;

        if (_throwReleased)
            return;

        Vector3 currentTargetPosition = ResolveThrowTargetPosition();
        LogThrow($"ReleaseThrow frame={Time.frameCount} repeatRollPending={repeatThrowChance:F2} target={currentTargetPosition}");
        ThrowGift(currentTargetPosition);
        PlayAudioEventNetworked(AudioEvent.AttackCommit);
        _throwReleased = true;
        _throwPhase = ThrowPhase.Released;
        _queueRepeatThrow = UnityEngine.Random.value < repeatThrowChance;
        SetRepeatThrowAnimatorFlag(_queueRepeatThrow);
        LogThrow($"ReleaseThrowResult frame={Time.frameCount} repeatQueued={_queueRepeatThrow}");
        SetHeldGiftVisible(false);
        if (CanBroadcastNetworkPresentation)
            SetHeldGiftVisibleObserversRpc(false);
        _nextThrowAt = Time.time + Mathf.Max(0.25f, throwCooldown);
    }

    private void TeleportNow()
    {
        if (!HasServerAuthority())
            return;

        Vector3 origin = transform.position;

        if (!TryPickTeleportDestination(out Vector3 destination, out string source, out string reason))
        {
            LogTeleport($"FAILED reason={reason}");
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
            bool warped = agent.Warp(destination);
            LogTeleport($"{(warped ? "WARP" : "WARP_FAILED")} source={source} destination={destination}");
            if (!warped)
                transform.position = destination;
        }
        else
        {
            transform.position = destination;
            LogTeleport($"TRANSFORM source={source} destination={destination}");
        }

        _throwPhase = ThrowPhase.Teleporting;
        ClearPatrolPoint();
        _nextPatrolPickAt = Time.time + patrolPauseDuration;
        SetDesiredPosition(destination);
        PlayAudioEventNetworked(AudioEvent.Teleport);
        PlayTeleportFeedback(origin, destination);
    }

    private void FinishThrowSequence()
    {
        bool repeatThrow = _queueRepeatThrow;
        GameObject lastThrowTarget = _pendingThrowTarget;
        LogThrow($"FinishThrowSequence frame={Time.frameCount} repeatThrow={repeatThrow} teleportTriggered={_throwPhase == ThrowPhase.Teleporting}");
        CancelInvoke(nameof(BeginDelayedRepeatThrow));
        _throwPhase = ThrowPhase.Idle;
        _throwReleased = false;
        _disappearEntered = false;
        _queueRepeatThrow = false;
        _scheduledRepeatTarget = null;
        SetRepeatThrowAnimatorFlag(false);
        CaptureDesiredTransform();
        SetAudioMovementLoop(AudioMovementLoop.None);

        if (!repeatThrow || IsDead)
        {
            _pendingThrowTarget = null;
            return;
        }

        GameObject repeatTarget = lastThrowTarget != null ? lastThrowTarget : _currentTarget;
        if (repeatTarget == null)
            repeatTarget = AcquireTarget(rangeDetector, lineOfSightDetector, chaseDistance);

        _pendingThrowTarget = null;

        if (repeatTarget == null)
        {
            LogThrow($"FinishThrowSequence repeat cancelled frame={Time.frameCount} reason=no-target");
            return;
        }

        _currentTarget = repeatTarget;
        _scheduledRepeatTarget = repeatTarget;
        _nextThrowAt = Time.time + Mathf.Max(0.05f, repeatThrowDelay);
        _throwPhase = ThrowPhase.RepeatPending;
        StopMovement();
        UpdateAnimatorMoveSpeed(0f);
        FaceTarget(repeatTarget.transform.position);
        CaptureDesiredTransform();
        LogThrow($"FinishThrowSequence delayed repeat scheduled frame={Time.frameCount} delay={repeatThrowDelay:F2} target={repeatTarget.name}");
        Invoke(nameof(BeginDelayedRepeatThrow), Mathf.Max(0.05f, repeatThrowDelay));
    }

    private void BeginDelayedRepeatThrow()
    {
        if (!HasServerAuthority() || _throwPhase != ThrowPhase.RepeatPending || IsDead)
            return;

        GameObject repeatTarget = _scheduledRepeatTarget != null ? _scheduledRepeatTarget : _currentTarget;
        if (repeatTarget == null)
            repeatTarget = AcquireTarget(rangeDetector, lineOfSightDetector, chaseDistance);

        _scheduledRepeatTarget = null;

        if (repeatTarget == null)
        {
            _throwPhase = ThrowPhase.Idle;
            CaptureDesiredTransform();
            LogThrow($"BeginDelayedRepeatThrow cancelled frame={Time.frameCount} reason=no-target");
            return;
        }

        _throwPhase = ThrowPhase.Idle;
        LogThrow($"BeginDelayedRepeatThrow start frame={Time.frameCount} target={repeatTarget.name}");
        BeginThrowSequence(repeatTarget);
    }

    private Vector3 ResolveThrowTargetPosition()
    {
        if (_pendingThrowTarget != null)
            _pendingThrowTargetPosition = _pendingThrowTarget.transform.position;

        return _pendingThrowTargetPosition;
    }

    private void ThrowGift(Vector3 targetPosition)
    {
        if (!HasServerAuthority())
            return;

        if (thrownGiftVisualPrefab == null)
            return;

        LogThrow($"ThrowGift frame={Time.frameCount} target={targetPosition} phase={_throwPhase} released={_throwReleased}");

        Vector3 origin = throwOrigin != null ? throwOrigin.position : transform.position + Vector3.up * 1.4f;
        Vector3 aimPoint = targetPosition + Vector3.up * 0.8f;
        Vector3 velocity = CalculateThrowVelocity(origin, aimPoint);
        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 launchDirection = flatVelocity.sqrMagnitude > 0.0001f ? flatVelocity.normalized : transform.forward;
        Vector3 spawnPosition = origin + launchDirection * throwSpawnForwardOffset + Vector3.up * throwSpawnUpOffset;

        if (CanBroadcastNetworkPresentation)
            SpawnGiftVisualObserversRpc(spawnPosition, launchDirection, velocity, Mathf.Max(0.5f, giftSettings.fuseDelay + 0.5f));

        if (networkDebugLog)
            Debug.Log($"[ClownNetDebug] {name} ThrowGift authoritative spawn={spawnPosition} velocity={velocity} broadcast={CanBroadcastNetworkPresentation}", this);

        GameObject giftObject = Instantiate(thrownGiftVisualPrefab, spawnPosition, Quaternion.LookRotation(launchDirection, Vector3.up));
        giftObject.name = "ClownGift_Runtime";
        giftObject.transform.localScale = thrownGiftLocalScale;

        Rigidbody rigidbody = giftObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = giftObject.AddComponent<Rigidbody>();

        rigidbody.useGravity = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.mass = 0.35f;

        ClownGift gift = giftObject.GetComponent<ClownGift>();
        if (gift == null)
            gift = giftObject.AddComponent<ClownGift>();

        gift.ApplySettings(giftSettings, randomItemDrops);
        gift.InitializeServer(velocity, deathSequence, randomItemDrops, GetComponentsInChildren<Collider>(true), this);
    }

    [ObserversRpc]
    private void SetHeldGiftVisibleObserversRpc(bool visible)
    {
        SetHeldGiftVisible(visible);

        if (networkDebugLog)
            Debug.Log($"[ClownNetDebug] {name} Observers heldGift visible={visible}", this);
    }

    [ObserversRpc]
    private void SpawnGiftVisualObserversRpc(Vector3 spawnPosition, Vector3 launchDirection, Vector3 initialVelocity, float lifetime)
    {
        if (HasServerAuthority() || thrownGiftVisualPrefab == null)
            return;

        if (networkDebugLog)
            Debug.Log($"[ClownNetDebug] {name} Observers visual gift spawn={spawnPosition} velocity={initialVelocity}", this);

        GameObject visual = Instantiate(thrownGiftVisualPrefab, spawnPosition, Quaternion.LookRotation(launchDirection, Vector3.up));
        visual.name = "ClownGift_ClientVisual";
        visual.transform.localScale = thrownGiftLocalScale;

        foreach (ClownGift gift in visual.GetComponentsInChildren<ClownGift>(true))
            Destroy(gift);

        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody rb = visual.GetComponent<Rigidbody>();
        if (rb == null)
            rb = visual.AddComponent<Rigidbody>();

        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.isKinematic = false;
        rb.linearVelocity = initialVelocity;

        Destroy(visual, lifetime);
    }

    [ObserversRpc]
    public void PlayGiftExplosionObserversRpc(Vector3 center, GiftEffectType effect, Vector3 teleportDestination)
    {
        if (HasServerAuthority())
            return;

        if (networkDebugLog)
        Debug.Log($"[ClownNetDebug] {name} Observers explosion center={center} effect={effect} teleport={teleportDestination}", this);

        ClownGift.ApplyRemoteExplosionFeedback(
            giftSettings.explosionEffectPrefab,
            Mathf.Max(0.1f, giftSettings.explosionEffectLifetime),
            Mathf.Max(0.01f, giftSettings.explosionEffectBaseRadius),
            center,
            effect,
            teleportDestination,
            giftSettings.flashColor,
            Mathf.Clamp01(giftSettings.flashMaxAlpha),
            Mathf.Max(0.05f, giftSettings.flashDuration),
            Mathf.Max(0.1f, giftSettings.explosionRadius),
            Mathf.Max(0f, giftSettings.knockbackImpulse),
            Mathf.Max(0f, giftSettings.knockbackUpwardImpulse),
            Mathf.Max(0.05f, giftSettings.knockbackDuration),
            Mathf.Max(0.01f, giftSettings.knockbackDamping),
            giftSettings.teleportVerticalOffset);
    }

    [ObserversRpc]
    private void PlayTeleportFeedbackObserversRpc(Vector3 origin, Vector3 destination)
    {
        if (HasServerAuthority())
            return;

        SpawnTeleportFeedback(origin);
        SpawnTeleportFeedback(destination);
    }

    private void LogThrow(string message)
    {
        if (!throwDebugLog)
            return;

        Debug.Log($"[ClownThrowDebug] {name} {message}", this);
    }

    public bool TryPickTeleportDestinationFrom(Vector3 originPosition, out Vector3 destination, out string source, out string reason)
    {
        return TryPickTeleportDestination(originPosition, out destination, out source, out reason);
    }

    private void MoveTowards(Vector3 targetPosition)
    {
        if (IsTraversingLink)
            return;

        TryEnsureAgentOnNavMesh();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            Vector3 destination = ResolveNavigationDestination(targetPosition);
            agent.isStopped = false;
            _reportedDestination = destination;

            bool destinationChanged = !_hasRequestedNavigationDestination ||
                Vector3.Distance(_requestedNavigationDestination, destination) > destinationRepathThreshold;
            bool partialPathRefreshDue = agent.hasPath &&
                agent.pathStatus == NavMeshPathStatus.PathPartial &&
                Time.time >= _nextPartialPathRefreshAt;
            bool shouldRepath = !agent.pathPending &&
                (!agent.hasPath || destinationChanged || partialPathRefreshDue);

            if (shouldRepath)
            {
                if (agent.SetDestination(destination))
                {
                    _requestedNavigationDestination = destination;
                    _hasRequestedNavigationDestination = true;
                    _nextPartialPathRefreshAt = Time.time + partialPathRefreshInterval;
                }
            }

            return;
        }

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        transform.position += direction * 3f * Time.deltaTime;
        transform.forward = Vector3.Slerp(transform.forward, direction, Time.deltaTime * 8f);
        _reportedDestination = targetPosition;
    }

    private Vector3 ResolveNavigationDestination(Vector3 targetPosition)
    {
        if (agent == null || !agent.enabled)
            return targetPosition;

        float sampleRadius = Mathf.Max(1f, agent.radius + 1.5f);
        if (TrySampleAgentNavMesh(targetPosition, sampleRadius, out NavMeshHit hit))
            return hit.position;

        return targetPosition;
    }

    private void PatrolForTarget()
    {
        _investigatingLastKnownPosition = false;

        if (patrolSelector == null)
        {
            SetAudioMovementLoopNetworked(AudioMovementLoop.Idle);
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);
            CaptureDesiredTransform();
            return;
        }

        if (_patrolPoint != null && HasReachedPatrolPoint())
        {
            ClearPatrolPoint();
            _nextPatrolPickAt = Time.time + patrolPauseDuration;
        }

        if (_patrolPoint == null)
        {
            SetAudioMovementLoopNetworked(AudioMovementLoop.Idle);
            StopMovement();
            UpdateAnimatorMoveSpeed(0f);

            if (Time.time < _nextPatrolPickAt)
            {
                CaptureDesiredTransform();
                return;
            }

            if (!patrolSelector.TryPickNextPoint(out _patrolPoint) || _patrolPoint == null)
            {
                CaptureDesiredTransform();
                return;
            }
        }

        SetAudioMovementLoopNetworked(AudioMovementLoop.Patrol);
        MoveTowards(_patrolPoint.position);
        UpdateAnimatorMoveSpeed(patrolMoveSpeed);
        CaptureDesiredTransform();
    }

    private void StopMovement()
    {
        if (IsTraversingLink)
            return;

        TryEnsureAgentOnNavMesh();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        ClearRequestedNavigationDestination();
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    private bool HasReachedPatrolPoint()
    {
        if (_patrolPoint == null)
            return true;

        float arrivalDistance = Mathf.Max(0.1f, patrolArrivalDistance);
        if (agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending)
            return agent.remainingDistance <= Mathf.Max(arrivalDistance, agent.stoppingDistance + 0.05f);

        Vector3 flat = _patrolPoint.position - transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= arrivalDistance * arrivalDistance;
    }

    private void ClearPatrolPoint()
    {
        _patrolPoint = null;
    }

    private void UpdateAnimatorMoveSpeed(float value)
    {
        if (animator == null || animator.runtimeAnimatorController == null || _moveSpeedHash == 0)
            return;

        animator.SetFloat(_moveSpeedHash, value);
    }

    private void CaptureDesiredTransform()
    {
        TryEnsureAgentOnNavMesh();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            _desiredPosition = agent.nextPosition;
            _reportedDestination = agent.hasPath ? agent.destination : agent.nextPosition;
        }
        else
        {
            CaptureCurrentTransformAsDesired();
        }

        _desiredYaw = transform.eulerAngles.y;
    }

    private void CaptureCurrentTransformAsDesired()
    {
        _desiredPosition = transform.position;
        _reportedDestination = transform.position;
        _desiredYaw = transform.eulerAngles.y;
    }

    private void SetDesiredPosition(Vector3 position)
    {
        _desiredPosition = position;
        _reportedDestination = position;
        _desiredYaw = transform.eulerAngles.y;
    }

    private void RememberVisibleTarget(GameObject target)
    {
        if (target == null)
            return;

        _lastKnownTargetPosition = target.transform.position;
        _lastKnownTargetExpiresAt = Time.time + Mathf.Max(0f, targetMemoryDuration);
        _hasLastKnownTargetPosition = true;
        _investigatingLastKnownPosition = false;
    }

    [SerializeField, Min(0f), Tooltip("How long a heard noise stays worth investigating.")]
    private float noiseMemoryDuration = 6f;

    /// <summary>Server only: loud noises pull an idle Clown toward the spot.</summary>
    private void OnNoiseReported(Vector3 position, float radius, NoiseKind kind)
    {
        if (!HasServerAuthority() || !isActiveAndEnabled || IsDead || IsBusy || HasTarget)
            return;

        if ((position - transform.position).sqrMagnitude > radius * radius)
            return;

        _lastKnownTargetPosition = position;
        _lastKnownTargetExpiresAt = Time.time + Mathf.Max(0f, noiseMemoryDuration);
        _hasLastKnownTargetPosition = true;
        _investigatingLastKnownPosition = false;
    }

    private bool CanInvestigateLastKnownTarget()
    {
        return _hasLastKnownTargetPosition && Time.time <= _lastKnownTargetExpiresAt;
    }

    private void ClearLastKnownTarget()
    {
        _hasLastKnownTargetPosition = false;
        _lastKnownTargetPosition = Vector3.zero;
        _lastKnownTargetExpiresAt = 0f;
        _investigatingLastKnownPosition = false;
    }

    private bool HasReachedPosition(Vector3 position, float arrivalDistance)
    {
        float threshold = Mathf.Max(0.1f, arrivalDistance);
        Vector3 flat = position - transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= threshold * threshold;
    }

    private bool HasLineOfSight(GameObject candidate, LineOfSightDetector lineOfSightDetector)
    {
        if (candidate == null)
            return false;

        if (lineOfSightDetector == null)
            return true;

        return lineOfSightDetector.PerformDetection(candidate) != null;
    }

    private bool IsActionState(int stateHash)
    {
        return stateHash == _throwStateHash || stateHash == _disappearStateHash;
    }

    private void SetRepeatThrowAnimatorFlag(bool enabled)
    {
        if (animator == null || animator.runtimeAnimatorController == null || _repeatThrowHash == 0)
            return;

        animator.SetBool(_repeatThrowHash, enabled);
    }

    private void PlayTeleportFeedback(Vector3 origin, Vector3 destination)
    {
        SpawnTeleportFeedback(origin);
        SpawnTeleportFeedback(destination);

        if (CanBroadcastNetworkPresentation)
            PlayTeleportFeedbackObserversRpc(origin, destination);
    }

    private void SetAudioMovementLoopNetworked(AudioMovementLoop loop)
    {
        if (_audioMovementLoop == loop)
            return;

        AudioMovementLoop previous = _audioMovementLoop;
        SetAudioMovementLoop(loop);
        if (_audioMovementLoop == previous && _audioMovementLoop != loop)
            return;

        if (CanBroadcastNetworkPresentation)
            SetAudioMovementLoopObserversRpc((int)loop);
    }

    private void SetAudioMovementLoop(AudioMovementLoop loop)
    {
        audioController ??= GetComponent<SmilyAudioController>();

        if (audioController == null)
        {
            _audioMovementLoop = AudioMovementLoop.None;
            return;
        }

        if (loop == AudioMovementLoop.None)
        {
            audioController.StopMovementLoop("ClownMovementStop");
            _audioMovementLoop = AudioMovementLoop.None;
            return;
        }

        _audioMovementLoop = loop;

        switch (loop)
        {
            case AudioMovementLoop.Idle:
                audioController.PlayIdleLoop();
                break;
            case AudioMovementLoop.Patrol:
                audioController.PlayPatrolLoop();
                break;
        }
    }

    private void PlayAudioEventNetworked(AudioEvent audioEvent)
    {
        PlayAudioEventLocal(audioEvent);

        if (CanBroadcastNetworkPresentation)
            PlayAudioEventObserversRpc((int)audioEvent);
    }

    private void PlayAudioEventLocal(AudioEvent audioEvent)
    {
        audioController ??= GetComponent<SmilyAudioController>();
        if (audioController == null)
            return;

        switch (audioEvent)
        {
            case AudioEvent.Detect:
                audioController.PlayDetect();
                break;
            case AudioEvent.AttackWindup:
                audioController.StopMovementLoop("ClownAttackWindup");
                audioController.PlayAttackWindup();
                _audioMovementLoop = AudioMovementLoop.None;
                break;
            case AudioEvent.AttackCommit:
                audioController.StopMovementLoop("ClownAttackCommit");
                audioController.PlayAttackCommit();
                _audioMovementLoop = AudioMovementLoop.None;
                break;
            case AudioEvent.TeleportWindup:
                audioController.StopMovementLoop("ClownTeleportWindup");
                audioController.PlayTeleportWindup();
                _audioMovementLoop = AudioMovementLoop.None;
                break;
            case AudioEvent.Teleport:
                audioController.StopMovementLoop("ClownTeleport");
                audioController.PlayTeleport();
                _audioMovementLoop = AudioMovementLoop.None;
                break;
        }
    }

    [ObserversRpc]
    private void SetAudioMovementLoopObserversRpc(int loop)
    {
        if (HasServerAuthority())
            return;

        SetAudioMovementLoop((AudioMovementLoop)loop);
    }

    [ObserversRpc]
    private void PlayAudioEventObserversRpc(int audioEvent)
    {
        if (HasServerAuthority())
            return;

        PlayAudioEventLocal((AudioEvent)audioEvent);
    }

    private void SpawnTeleportFeedback(Vector3 position)
    {
        if (teleportEffectPrefab == null)
            return;

        GameObject effect = Instantiate(teleportEffectPrefab, position + teleportEffectOffset, Quaternion.identity);
        Destroy(effect, Mathf.Max(0.1f, teleportEffectLifetime));
    }

    private void EnsureHeldGiftVisual()
    {
        if (_heldGiftInstance != null || heldGiftVisualPrefab == null || throwOrigin == null)
            return;

        _heldGiftInstance = Instantiate(heldGiftVisualPrefab, throwOrigin);
        _heldGiftInstance.name = "ClownHeldGift";
        _heldGiftInstance.transform.localPosition = heldGiftLocalPosition;
        _heldGiftInstance.transform.localEulerAngles = heldGiftLocalEulerAngles;
        _heldGiftInstance.transform.localScale = heldGiftLocalScale;

        foreach (Collider collider in _heldGiftInstance.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        foreach (Rigidbody rigidbody in _heldGiftInstance.GetComponentsInChildren<Rigidbody>(true))
            Destroy(rigidbody);

        foreach (ClownGift gift in _heldGiftInstance.GetComponentsInChildren<ClownGift>(true))
            Destroy(gift);
    }

    private void SetHeldGiftVisible(bool visible)
    {
        if (_heldGiftInstance == null)
            return;

        foreach (Renderer renderer in _heldGiftInstance.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
    }

    private bool TryPickTeleportDestination(out Vector3 destination, out string source, out string reason)
    {
        return TryPickTeleportDestination(transform.position, out destination, out source, out reason);
    }

    private bool TryPickTeleportDestination(Vector3 originPosition, out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = string.Empty;

        string tileReason = string.Empty;
        string registryReason = string.Empty;

        if (TryPickDungeonTileDestination(originPosition, out destination, out source, out tileReason))
            return true;

        if (TryPickRegistryDestination(originPosition, out destination, out source, out registryReason))
            return true;

        if (TrySampleNearbyFallback(originPosition, out destination))
        {
            source = "nearby-navmesh";
            reason = $"nearby-fallback tileReason={tileReason} registryReason={registryReason}";
            return true;
        }

        reason = $"tileReason={tileReason} registryReason={registryReason} nearbyFallback=no-valid-point";

        return false;
    }

    private bool TryPickRegistryDestination(Vector3 originPosition, out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = "registry-empty";

        List<Transform> candidates = GetRegistryPoints();

        if (candidates.Count == 0)
            return false;

        int tries = Mathf.Max(1, Mathf.Min(candidates.Count, teleportRegistryTries));
        for (int i = 0; i < tries; i++)
        {
            Transform picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (picked == null)
            {
                reason = "registry-picked-null";
                continue;
            }

            if (!TrySampleTeleportNavMesh(picked.position, out NavMeshHit hit))
            {
                reason = $"registry-sample-failed:{picked.name}";
                continue;
            }

            if (!IsTeleportDistanceValid(originPosition, hit.position, out float distance))
            {
                reason = $"registry-too-close:{picked.name}:{distance:F2}";
                continue;
            }

            destination = hit.position;
            source = $"registry:{picked.name}";
            return true;
        }

        return false;
    }

    private bool TryPickDungeonTileDestination(Vector3 originPosition, out Vector3 destination, out string source, out string reason)
    {
        destination = default;
        source = string.Empty;
        reason = "runtime-dungeon-missing";

        RuntimeDungeon runtimeDungeon = FindFirstObjectByType<RuntimeDungeon>();
        if (runtimeDungeon == null)
        {
            reason = "runtime-dungeon-missing";
            return false;
        }

        if (runtimeDungeon.Generator == null)
        {
            reason = "runtime-dungeon-generator-missing";
            return false;
        }

        if (runtimeDungeon.Generator.CurrentDungeon == null)
        {
            reason = "runtime-current-dungeon-missing";
            return false;
        }

        IList<Tile> tiles = runtimeDungeon.Generator.CurrentDungeon.AllTiles;
        if (tiles == null || tiles.Count == 0)
        {
            reason = "runtime-dungeon-tiles-empty";
            return false;
        }

        int tries = Mathf.Max(1, teleportTileTries);
        for (int i = 0; i < tries; i++)
        {
            Tile tile = tiles[UnityEngine.Random.Range(0, tiles.Count)];
            if (tile == null)
            {
                reason = "runtime-dungeon-null-tile";
                continue;
            }

            Bounds bounds = tile.Bounds;
            Vector3 sample = new Vector3(
                UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                Mathf.Max(bounds.max.y, originPosition.y) + teleportSurfaceProbeHeight,
                UnityEngine.Random.Range(bounds.min.z, bounds.max.z));

            if (!TryResolveTeleportDestination(originPosition, sample, tile, out RaycastHit surfaceHit, out NavMeshHit hit, out float distance, out string failureCode))
            {
                string sourceKind = failureCode == "surface-miss" ? "tile-surface" : failureCode == "navmesh-gap" ? "tile-navmesh-gap" : "tile";
                LogTeleportCandidateFailure(sourceKind, tile.name, sample, bounds, surfaceHit.collider != null ? surfaceHit.point : null, hit.mask != 0 ? hit.position : null);
                reason = failureCode switch
                {
                    "surface-miss" => $"tile-surface-miss:{tile.name}",
                    "navmesh-gap" => $"tile-surface-not-navmesh:{tile.name}:{Vector3.Distance(surfaceHit.point, hit.position):F2}",
                    "too-close" => $"tile-too-close:{tile.name}:{distance:F2}",
                    _ => $"tile-sample-failed:{tile.name}"
                };
                continue;
            }

            destination = hit.position;
            source = $"tile:{tile.name}";
            return true;
        }

        return false;
    }

    private bool TrySampleNearbyFallback(Vector3 originPosition, out Vector3 destination)
    {
        destination = default;
        if (transform == null)
            return false;

        float radius = Mathf.Max(teleportNearbyRadius, teleportMinDistance + 2f);
        Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
        Vector3 sample = originPosition + new Vector3(offset2D.x, teleportSurfaceProbeHeight, offset2D.y);

        if (!TryResolveTeleportDestination(originPosition, sample, null, out RaycastHit surfaceHit, out NavMeshHit hit, out float distance, out string failureCode))
        {
            LogNearbySampleFailure(radius, sample, surfaceHit.collider != null ? surfaceHit.point : null, hit.mask != 0 ? hit.position : null);
            if (failureCode == "navmesh-gap" && surfaceHit.collider != null && hit.mask != 0)
                LogTeleport($"nearby-surface-not-navmesh surface={surfaceHit.point} navmesh={hit.position} gap={Vector3.Distance(surfaceHit.point, hit.position):F2}");

            if (failureCode == "too-close")
                LogTeleport($"nearby-sample-too-close hit={hit.position} current={originPosition} minDistance={teleportMinDistance:F2}");

            return false;
        }

        destination = hit.position;
        return true;
    }

    private bool IsTeleportDistanceValid(Vector3 originPosition, Vector3 candidate, out float distance)
    {
        Vector3 delta = candidate - originPosition;
        delta.y = 0f;
        distance = delta.magnitude;
        return distance >= Mathf.Max(0f, teleportMinDistance);
    }

    private bool TryResolveTeleportDestination(Vector3 originPosition, Vector3 sample, Tile expectedTile, out RaycastHit surfaceHit, out NavMeshHit navMeshHit, out float distance, out string failureCode)
    {
        surfaceHit = default;
        navMeshHit = default;
        distance = 0f;
        failureCode = "surface-miss";

        RaycastHit[] hits = Physics.RaycastAll(sample, Vector3.down, teleportSurfaceProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, static (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];
            if (!IsTeleportSurfaceHitValid(candidate, expectedTile))
                continue;

            surfaceHit = candidate;
            failureCode = "navmesh-miss";

            if (!TrySampleTeleportNavMesh(candidate.point, out NavMeshHit candidateNavHit))
                continue;

            navMeshHit = candidateNavHit;

            if (!IsSurfaceAlignedWithNavMesh(candidate.point, candidateNavHit.position, out _))
            {
                failureCode = "navmesh-gap";
                continue;
            }

            if (!IsTeleportDistanceValid(originPosition, candidateNavHit.position, out distance))
            {
                failureCode = "too-close";
                continue;
            }

            return true;
        }

        return false;
    }

    private bool IsTeleportSurfaceHitValid(RaycastHit hit, Tile expectedTile)
    {
        if (hit.collider == null)
            return false;

        if (hit.normal.y < teleportSurfaceMinNormalY)
            return false;

        if (expectedTile == null)
            return true;

        Tile hitTile = hit.collider.GetComponentInParent<Tile>();
        return hitTile == expectedTile;
    }

    private bool IsSurfaceAlignedWithNavMesh(Vector3 surfacePoint, Vector3 navMeshPoint, out float gap)
    {
        gap = Vector3.Distance(surfacePoint, navMeshPoint);
        return gap <= Mathf.Max(0.05f, teleportSurfaceToNavMeshTolerance);
    }

    private void LogTeleportCandidateFailure(string sourceKind, string sourceName, Vector3 sample, Bounds bounds, Vector3? surfacePoint, Vector3? navMeshPoint)
    {
        if (!teleportDebugLog)
            return;

        bool agentHit = TrySampleTeleportNavMesh(sample, out NavMeshHit agentSample);
        string agentSamplePart = agentHit
            ? $"agentSample=hit:{agentSample.position}"
            : "agentSample=miss";

        string surfacePart = surfacePoint.HasValue ? $"surface={surfacePoint.Value}" : "surface=miss";
        string navMeshPart = navMeshPoint.HasValue ? $"navmesh={navMeshPoint.Value}" : string.Empty;

        string agentState = DescribeAgentState();
        Debug.Log($"[ClownTeleport] {name} {sourceKind}-sample-failed source={sourceName} sample={sample} boundsCenter={bounds.center} boundsSize={bounds.size} radius={teleportNavMeshSampleRadius:F2} {surfacePart} {navMeshPart} {agentSamplePart} {agentState}", this);
    }

    private void LogNearbySampleFailure(float radius, Vector3 sample, Vector3? surfacePoint, Vector3? navMeshPoint)
    {
        if (!teleportDebugLog)
            return;

        bool agentHit = TrySampleAgentNavMesh(transform.position, radius, out NavMeshHit agentSample);
        string agentSamplePart = agentHit
            ? $"agentSample=hit:{agentSample.position}"
            : "agentSample=miss";

        string surfacePart = surfacePoint.HasValue ? $"surface={surfacePoint.Value}" : "surface=miss";
        string navMeshPart = navMeshPoint.HasValue ? $"navmesh={navMeshPoint.Value}" : string.Empty;

        Debug.Log($"[ClownTeleport] {name} nearby-sample-failed current={transform.position} sample={sample} radius={radius:F2} {surfacePart} {navMeshPart} {agentSamplePart} {DescribeAgentState()}", this);
    }

    private string DescribeAgentState()
    {
        if (agent == null)
            return "agent=null";

        return $"agentEnabled={agent.enabled} isOnNavMesh={agent.isOnNavMesh} areaMask={agent.areaMask} agentTypeID={agent.agentTypeID} nextPosition={agent.nextPosition} velocity={agent.velocity}";
    }

    private void LogTeleport(string message)
    {
        if (!teleportDebugLog)
            return;

        Debug.Log($"[ClownTeleport] {name} {message}", this);
    }

    private List<Transform> GetRegistryPoints()
    {
        var points = new List<Transform>(global::DungeonPointRegistry.PatrolPoints.Count + global::DungeonPointRegistry.SpawnPoints.Count);

        for (int i = 0; i < global::DungeonPointRegistry.PatrolPoints.Count; i++)
        {
            Transform point = global::DungeonPointRegistry.PatrolPoints[i];
            if (point != null)
                points.Add(point);
        }

        for (int i = 0; i < global::DungeonPointRegistry.SpawnPoints.Count; i++)
        {
            Transform point = global::DungeonPointRegistry.SpawnPoints[i];
            if (point != null)
                points.Add(point);
        }

        return points;
    }

    private Vector3 CalculateThrowVelocity(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 flat = targetPosition - origin;
        float vertical = flat.y;
        flat.y = 0f;

        Vector3 direction = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
        Vector3 velocity = direction * Mathf.Max(1f, throwSpeed);
        velocity.y = upwardArc + Mathf.Clamp(vertical, -1.5f, 2.5f);
        return velocity;
    }

    private Transform FindThrowOrigin()
    {
        string[] preferred = { "BoxPoint", "boxpoint", "hand_r", "ik_hand_gun", "ik_hand_r", "Hand.R" };
        Transform[] transforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < preferred.Length; i++)
        {
            for (int j = 0; j < transforms.Length; j++)
            {
                if (string.Equals(transforms[j].name, preferred[i], StringComparison.OrdinalIgnoreCase))
                    return transforms[j];
            }
        }

        return transform;
    }

    private bool TryEnsureAgentOnNavMesh()
    {
        if (agent == null || !agent.enabled)
            return false;

        if (agent.isOnNavMesh)
            return true;

        float sampleRadius = Mathf.Max(1.5f, agent.radius + 0.5f);
        if (!TrySampleAgentNavMesh(transform.position, sampleRadius, out NavMeshHit hit))
            return false;

        bool warped = agent.Warp(hit.position);
        if (warped)
        {
            SetDesiredPosition(hit.position);
            return true;
        }

        transform.position = hit.position;
        SetDesiredPosition(hit.position);
        return false;
    }

    private void ClearRequestedNavigationDestination()
    {
        _hasRequestedNavigationDestination = false;
        _requestedNavigationDestination = Vector3.zero;
        _nextPartialPathRefreshAt = 0f;
    }

    private bool TrySampleTeleportNavMesh(Vector3 sourcePosition, out NavMeshHit hit)
    {
        return TrySampleAgentNavMesh(sourcePosition, teleportNavMeshSampleRadius, out hit);
    }

    public bool TrySampleOwnedNavMesh(Vector3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        return TrySampleAgentNavMesh(sourcePosition, maxDistance, out hit);
    }

    private bool TrySampleAgentNavMesh(Vector3 sourcePosition, float maxDistance, out NavMeshHit hit)
    {
        hit = default;
        if (agent == null || !agent.enabled)
            return false;

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        return NavMesh.SamplePosition(sourcePosition, out hit, maxDistance, filter);
    }
}

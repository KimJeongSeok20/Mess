using System.Collections;
using PurrNet;
using UnityEngine.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class SmilyBrain : MonoBehaviour
{
    public enum SmilyState
    {
        Idle,
        Patrol,
        Investigate,
        Chase,
        LinkWarningSmile,
        LinkSnap,
        TeleportWindup,
        TeleportRecovery,
        AttackWindup,
        JumpAttack,
        AttackRecover,
        Flee,
        Dead
    }

    [Header("References")]
    [SerializeField] private MonsterHealth health;
    [SerializeField] private DoorAutoOpener doorAutoOpener;
    [SerializeField] private RangeDetector rangeDetector;
    [SerializeField] private LineOfSightDetector lineOfSightDetector;
    [SerializeField] private AssignJumpPoint assignJumpPoint;
    [SerializeField] private JumpToPoint jumpToPoint;
    [SerializeField] private AttackHitbox attackHitbox;
    [SerializeField] private FleeFromTarget fleeFromTarget;
    [SerializeField] private MonsterPatrolSelector patrolSelector;
    [SerializeField] private SmilyAttackState attackState;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private SmilyAudioController audioController;

    [Header("Detection")]
    [SerializeField] private float requiredChaseSeconds = 0.15f;
    [SerializeField] private float requiredSeenSeconds = 0.15f;
    [SerializeField] private float minAmbushTargetDistance = 1.5f;
    [SerializeField] private float maxAmbushTargetDistance = 13f;
    [SerializeField] private float decisionInterval = 0.05f;
    [SerializeField] private float chaseRepathInterval = 0.2f;
    [SerializeField] private float patrolArrivalDistance = 0.75f;

    [Header("Investigation")]
    [SerializeField, Min(0f)] private float targetMemoryDuration = 8f;
    [SerializeField, Min(0f), Tooltip("How long a heard noise stays worth investigating.")]
    private float noiseMemoryDuration = 6f;
    [SerializeField, Min(0f)] private float investigationSearchDuration = 1f;
    [SerializeField, Min(0.1f)] private float investigationArrivalDistance = 0.8f;
    [SerializeField, Min(0f)] private float investigationTurnSpeed = 180f;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float fleeSpeedMultiplier = 4f;

    [Header("Teleport Warning")]
    [SerializeField] private bool enableLinkWarningTeleport = true;
    [SerializeField] private string teleportSmileTrigger = "TeleportSmile";
    [SerializeField] private AnimationClip teleportSmileAnimationClip;
    [SerializeField] private bool useTeleportSmileAnimationLength = true;
    [SerializeField] private float teleportWarningSeconds = 1f;
    [SerializeField] private float teleportSmileExtraDelay;

    [Header("Teleport VFX")]
    [SerializeField] private float teleportRecoverySeconds = 0.35f;
    [SerializeField] private float snapColliderGraceSeconds = 0.2f;
    [FormerlySerializedAs("teleportEffectPrefab")]
    [SerializeField] private GameObject teleportOutEffectPrefab;
    [FormerlySerializedAs("teleportEffectOffset")]
    [SerializeField] private Vector3 teleportOutEffectOffset;
    [FormerlySerializedAs("teleportEffectLifetime")]
    [SerializeField] private float teleportOutEffectLifetime = 2f;
    [SerializeField] private GameObject teleportInEffectPrefab;
    [SerializeField] private Vector3 teleportInEffectOffset;
    [SerializeField] private float teleportInEffectLifetime = 2f;
    [SerializeField] private GameObject attackPortalEffectPrefab;
    [SerializeField] private Vector3 attackPortalEffectOffset;
    [SerializeField] private float attackPortalEffectLifetime = 2f;
    [SerializeField] private bool hideRenderersDuringLinkSnap = true;
    [SerializeField] private bool playWarningBeforeAttackTeleport = true;

    [Header("Ambush Point")]
    [SerializeField] private int candidateCount = 14;
    [SerializeField] private float minAmbushDistance = 5f;
    [SerializeField] private float maxAmbushDistance = 9f;
    [SerializeField, Range(-1f, 1f)] private float maxForwardDot = 0.15f;
    [SerializeField] private float navMeshSampleRadius = 2.5f;
    [SerializeField] private LayerMask sightBlockMask = ~0;
    [SerializeField] private float frontClearanceDistance = 0.65f;
    [SerializeField] private float frontClearanceRadius = 0.38f;
    [SerializeField] private float frontClearanceHeight = 0.55f;

    [Header("Attack")]
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private float attackWindupSeconds = 0.3f;
    [SerializeField] private float attackRecoverSeconds = 1.1f;
    [SerializeField] private float jumpDuration = 1.1f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField, Range(0f, 1f)] private float repeatAttackChance = 0.5f;
    [SerializeField, Min(1)] private int defaultMaxConsecutiveAttacks = 2;

    [Header("Look")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private float bodyLookTurnSpeed = 240f;
    [SerializeField] private float attackFacingAngleTolerance = 6f;
    [SerializeField, Range(0f, 1f)] private float headLookWeight = 0.65f;
    [SerializeField] private float headLookTurnSpeed = 540f;
    [SerializeField] private float maxHeadLookAngle = 75f;

    [Header("Animator")]
    [SerializeField] private string locomotionSpeedParameter = "SpeedMagnitude";
    [SerializeField] private float locomotionDampTime = 0.08f;
    [SerializeField] private float animatorStopDeadZone = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges;
    [SerializeField] private bool drawDebug;

    private NavMeshPath _path;
    private GameObject _target;
    private SmilyState _state = SmilyState.Idle;
    private Vector3 _ambushPoint;
    private Vector3 _jumpPoint;
    private float _stateUntil;
    private float _chaseStartedAt = -1f;
    private float _seenStartedAt = -1f;
    private float _nextDecisionTime;
    private float _nextChaseRepathAt;
    private Vector3 _lastKnownTargetPosition;
    private float _lastKnownTargetExpiresAt;
    private float _investigationSearchEndsAt;
    private bool _hasLastKnownTargetPosition;
    private bool _isSearchingLastKnownPosition;
    private bool _jumpStarted;
    private bool _teleportWindupUsesWarning;
    private bool _linkSnapTraversalCompletionRequested;
    private int _consecutiveAttackCount;
    private Coroutine _linkSnapRoutine;
    private Renderer[] _snapHiddenRenderers;
    private bool[] _snapRendererEnabledStates;
    private PlayableGraph _teleportSmileGraph;
    private bool _teleportSmilePlayableActive;
    private System.Predicate<GameObject> _visibleTargetFilter;

    public SmilyState CurrentState => _state;
    public string CurrentStateLabel => _state.ToString();
    public bool IsAmbushing => _state == SmilyState.TeleportWindup
        || _state == SmilyState.LinkWarningSmile
        || _state == SmilyState.LinkSnap
        || _state == SmilyState.AttackWindup
        || _state == SmilyState.JumpAttack;
    public bool IsInRecovery => _state == SmilyState.TeleportRecovery || _state == SmilyState.AttackRecover;
    public bool HasTarget => _target != null;
    public string CurrentTargetName => _target != null ? _target.name : string.Empty;
    public bool IsInvestigating => _state == SmilyState.Investigate;
    public bool IsSearchingLastKnownPosition => IsInvestigating && _isSearchingLastKnownPosition;
    public bool HasLastKnownTargetPosition => _hasLastKnownTargetPosition;
    public Vector3 LastKnownTargetPosition => _lastKnownTargetPosition;
    public Vector3 DesiredDestination => agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath ? agent.destination : transform.position;

    private bool HasServerAuthority
    {
        get
        {
            var networkManager = NetworkManager.main;
            return networkManager == null || networkManager.isServer;
        }
    }

    private void Awake()
    {
        _path = new NavMeshPath();
        _visibleTargetFilter = HasLineOfSight;
    }

    private void OnEnable()
    {
        MonsterNoise.Reported += OnNoiseReported;
        ApplyAudioForState(_state);
    }

    private void OnDisable()
    {
        MonsterNoise.Reported -= OnNoiseReported;
        if (_linkSnapRoutine != null)
        {
            StopCoroutine(_linkSnapRoutine);
            _linkSnapRoutine = null;
        }

        _linkSnapTraversalCompletionRequested = false;
        if (doorAutoOpener != null && doorAutoOpener.IsManualTraversalPaused)
            doorAutoOpener.SetManualTraversalPaused(false);

        RestoreRenderersAfterLinkSnap();
        attackState?.EndAttackWindow("SmilyBrainDisabled");
        audioController?.StopMovementLoop("SmilyBrainDisabled");
        StopTeleportSmileAnimation();
        ClearTargetMemory();

        SetAnimatorPlaybackSpeed(1f);
    }

    private void Update()
    {
        if (!HasServerAuthority)
            return;

        ApplyMovementSettings();
        UpdateAnimatorSpeed();

        if (health != null && health.IsDead)
        {
            _linkSnapTraversalCompletionRequested = false;
            if (doorAutoOpener != null && doorAutoOpener.IsManualTraversalPaused)
                doorAutoOpener.SetManualTraversalPaused(false);

            EnterState(SmilyState.Dead);
            return;
        }

        TickState();
    }

    private void TickState()
    {
        switch (_state)
        {
            case SmilyState.Dead:
                StopAgent();
                return;
            case SmilyState.LinkWarningSmile:
                TickLinkWarningSmile();
                return;
            case SmilyState.LinkSnap:
                TickLinkSnap();
                return;
            case SmilyState.TeleportWindup:
                TickTeleportWindup();
                return;
            case SmilyState.TeleportRecovery:
                if (Time.time >= _stateUntil)
                    ReevaluateAfterRecovery();
                return;
            case SmilyState.AttackWindup:
                TickAttackWindup();
                return;
            case SmilyState.JumpAttack:
                TickJumpAttack();
                return;
            case SmilyState.AttackRecover:
                if (Time.time >= _stateUntil)
                    BeginFleeOrReevaluate();
                return;
        }

        if (Time.time < _nextDecisionTime)
            return;

        _nextDecisionTime = Time.time + decisionInterval;

        if (_state == SmilyState.Flee)
        {
            TickFlee();
            return;
        }

        TickDetectionAndMovement();
    }

    private void TickDetectionAndMovement()
    {
        if (IsDoorTraversalBlockingBrain())
        {
            ResetDetectionTimers();

            if (doorAutoOpener.IsWaitingForDoor)
                EnterState(SmilyState.Idle);

            if (doorAutoOpener.IsMovingAcrossLink)
                TickLinkEncounterDuringTraversal();

            return;
        }

        GameObject detectedTarget = DetectVisibleTarget();
        bool hasVisibleTarget = detectedTarget != null;

        if (fleeFromTarget != null && fleeFromTarget.IsInReAggroCooldown)
        {
            ClearTargetMemory();
            ResetDetectionTimers();
            TickPatrol();
            return;
        }

        if (!hasVisibleTarget)
        {
            if (CanInvestigateLastKnownTarget())
            {
                if (_state != SmilyState.Investigate)
                    ResetDetectionTimers();

                TickInvestigate();
                return;
            }

            ClearTargetMemory();
            ResetDetectionTimers();
            TickPatrol();
            return;
        }

        if (_target != detectedTarget || _state == SmilyState.Investigate)
            ResetDetectionTimers();

        _target = detectedTarget;
        RememberVisibleTarget(detectedTarget);
        TickChase(_target);

        if (_seenStartedAt < 0f)
            _seenStartedAt = Time.time;

        if (_chaseStartedAt < 0f)
            _chaseStartedAt = Time.time;

        bool chasedLongEnough = Time.time - _chaseStartedAt >= Mathf.Max(0f, requiredChaseSeconds);
        bool seenLongEnough = _seenStartedAt >= 0f && Time.time - _seenStartedAt >= Mathf.Max(0f, requiredSeenSeconds);
        if (!chasedLongEnough || !seenLongEnough || !CanBeginAttack(_target))
            return;

        if (TryFindAmbushPoint(_target, out _ambushPoint))
        {
            audioController?.PlayDetect();
            BeginTeleportWindup(false);
        }
    }

    private void TickInvestigate()
    {
        if (!CanInvestigateLastKnownTarget())
        {
            ClearTargetMemory();
            ResetDetectionTimers();
            TickPatrol();
            return;
        }

        EnterState(SmilyState.Investigate);

        if (_isSearchingLastKnownPosition)
        {
            StopAgent();
            float searchTickDelta = Mathf.Max(Time.deltaTime, decisionInterval);
            transform.Rotate(0f, Mathf.Max(0f, investigationTurnSpeed) * searchTickDelta, 0f, Space.World);

            if (Time.time >= _investigationSearchEndsAt)
            {
                ClearTargetMemory();
                ResetDetectionTimers();
                TickPatrol();
            }

            return;
        }

        if (HasReachedPosition(_lastKnownTargetPosition, investigationArrivalDistance))
        {
            StopAgent();

            float searchDuration = Mathf.Max(0f, investigationSearchDuration);
            if (searchDuration <= 0f)
            {
                ClearTargetMemory();
                ResetDetectionTimers();
                TickPatrol();
                return;
            }

            _isSearchingLastKnownPosition = true;
            _investigationSearchEndsAt = Time.time + searchDuration;
            audioController?.PlayIdleLoop();
            return;
        }

        ResumeAgent();

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        if (Time.time < _nextChaseRepathAt && agent.hasPath)
            return;

        _nextChaseRepathAt = Time.time + Mathf.Max(0.05f, chaseRepathInterval);
        agent.SetDestination(_lastKnownTargetPosition);
    }

    private void TickLinkEncounterDuringTraversal()
    {
        if (!enableLinkWarningTeleport)
            return;

        if (_state != SmilyState.Idle && _state != SmilyState.Patrol && _state != SmilyState.Investigate && _state != SmilyState.Chase)
            return;

        GameObject target = DetectVisibleTarget();
        if (!TryBeginLinkWarning(target))
            return;

        doorAutoOpener.SetManualTraversalPaused(true);
    }

    private void TickPatrol()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            EnterState(SmilyState.Idle);
            return;
        }

        ResumeAgent();

        float arrivalDistance = Mathf.Max(agent.stoppingDistance, patrolArrivalDistance);
        bool hasArrived = !agent.pathPending && agent.remainingDistance <= arrivalDistance;
        bool needsNewPoint = !agent.hasPath || hasArrived;

        bool destinationQueued = false;
        if (needsNewPoint && patrolSelector.TryPickNextPoint(out Transform point))
            destinationQueued = agent.SetDestination(point.position);

        bool hasPatrolPath = agent.hasPath || agent.pathPending || destinationQueued;
        EnterState(hasPatrolPath ? SmilyState.Patrol : SmilyState.Idle);
    }

    private void TickChase(GameObject target)
    {
        EnterState(SmilyState.Chase);
        ResumeAgent();

        if (agent == null || !agent.enabled || !agent.isOnNavMesh || target == null)
            return;

        if (Time.time < _nextChaseRepathAt && agent.hasPath)
            return;

        _nextChaseRepathAt = Time.time + Mathf.Max(0.05f, chaseRepathInterval);
        agent.SetDestination(target.transform.position);
    }

    private bool HasLineOfSight(GameObject target)
    {
        return target != null && (lineOfSightDetector == null || lineOfSightDetector.PerformDetection(target) != null);
    }

    private GameObject DetectVisibleTarget()
    {
        if (rangeDetector == null)
            return null;

        if (_visibleTargetFilter == null)
            _visibleTargetFilter = HasLineOfSight;

        return rangeDetector.UpdateDetector(_visibleTargetFilter);
    }

    /// <summary>
    /// Server only: a loud noise within its radius makes an idle/patrolling Smily walk over and look
    /// (uses the existing last-known-position investigation). A Smily that can already see a
    /// player ignores noise.
    /// </summary>
    private void OnNoiseReported(Vector3 position, float radius, NoiseKind kind)
    {
        if (!HasServerAuthority || !isActiveAndEnabled)
            return;

        if (_state != SmilyState.Idle && _state != SmilyState.Patrol && _state != SmilyState.Investigate)
            return;

        if (_target != null)
            return;

        if ((position - transform.position).sqrMagnitude > radius * radius)
            return;

        _lastKnownTargetPosition = position;
        _lastKnownTargetExpiresAt = Time.time + Mathf.Max(0f, noiseMemoryDuration);
        _hasLastKnownTargetPosition = true;
        _isSearchingLastKnownPosition = false;
        _investigationSearchEndsAt = 0f;
    }

    private void RememberVisibleTarget(GameObject target)
    {
        if (target == null)
            return;

        _lastKnownTargetPosition = target.transform.position;
        _lastKnownTargetExpiresAt = Time.time + Mathf.Max(0f, targetMemoryDuration);
        _hasLastKnownTargetPosition = true;
        _isSearchingLastKnownPosition = false;
        _investigationSearchEndsAt = 0f;
    }

    private bool CanInvestigateLastKnownTarget()
    {
        if (!_hasLastKnownTargetPosition)
            return false;

        return _isSearchingLastKnownPosition
            ? Time.time <= _investigationSearchEndsAt
            : Time.time <= _lastKnownTargetExpiresAt;
    }

    private bool HasReachedPosition(Vector3 position, float arrivalDistance)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        float threshold = Mathf.Max(0.1f, arrivalDistance);
        return offset.sqrMagnitude <= threshold * threshold;
    }

    private void ClearTargetMemory()
    {
        _target = null;
        _lastKnownTargetPosition = Vector3.zero;
        _lastKnownTargetExpiresAt = 0f;
        _investigationSearchEndsAt = 0f;
        _hasLastKnownTargetPosition = false;
        _isSearchingLastKnownPosition = false;
    }

    private void BeginTeleportWindup(bool fromLinkEncounter)
    {
        bool continuingAttackCombo = _state == SmilyState.AttackRecover;
        if (!continuingAttackCombo)
            _consecutiveAttackCount = 0;

        _teleportWindupUsesWarning = fromLinkEncounter || playWarningBeforeAttackTeleport;
        _stateUntil = _teleportWindupUsesWarning
            ? Time.time + ResolveTeleportWarningDuration()
            : Time.time;
        _jumpStarted = false;
        StopAgent();
        if (_teleportWindupUsesWarning)
            BeginTeleportWarning();
        EnterState(fromLinkEncounter ? SmilyState.LinkWarningSmile : SmilyState.TeleportWindup);
    }

    private bool TryBeginLinkWarning(GameObject target)
    {
        if (!HasServerAuthority || target == null)
            return false;

        if (!HasLineOfSight(target))
            return false;

        if (_state != SmilyState.Idle && _state != SmilyState.Patrol && _state != SmilyState.Investigate && _state != SmilyState.Chase)
            return false;

        if (health != null && health.IsDead)
            return false;

        if (!CanBeginAttack(target, true))
            return false;

        if (!TryFindAmbushPoint(target, out _ambushPoint))
            return false;

        _target = target;
        _linkSnapTraversalCompletionRequested = false;
        audioController?.PlayDetect();
        BeginTeleportWindup(true);
        return true;
    }

    private void PrepareForLinkSnap(GameObject target)
    {
        if (_state != SmilyState.LinkSnap)
            return;

        if (target != null)
            _target = target;

        HideRenderersForLinkSnap();
    }

    private void NotifyLinkSnapCompleted(GameObject target)
    {
        if (_state != SmilyState.LinkSnap)
            return;

        if (_linkSnapRoutine != null)
            return;

        if (target != null)
            _target = target;

        StopAgent();

        _linkSnapRoutine = StartCoroutine(LinkSnapTeleportAfterGrace());
    }

    private IEnumerator LinkSnapTeleportAfterGrace()
    {
        using (new ColliderGraceScope(this, _target, snapColliderGraceSeconds))
        {
            float grace = Mathf.Max(0f, snapColliderGraceSeconds);
            if (grace > 0f)
                yield return new WaitForSeconds(grace);
        }

        _linkSnapRoutine = null;
        _linkSnapTraversalCompletionRequested = false;
        CompleteTeleport();
        RestoreRenderersAfterLinkSnap();
    }

    private void TickLinkSnap()
    {
        FaceTarget();
        ApplyHeadLook();

        if (!_linkSnapTraversalCompletionRequested)
        {
            PrepareForLinkSnap(_target);
            doorAutoOpener.RequestManualTraversalCompleteAtEnd(true);
            _linkSnapTraversalCompletionRequested = true;
        }

        if (!IsDoorTraversalBlockingBrain())
            NotifyLinkSnapCompleted(_target);
    }

    private void TickLinkWarningSmile()
    {
        if (_target == null || health != null && health.IsDead)
        {
            ReevaluateAfterRecovery();
            return;
        }

        StopAgent();
        FaceTarget();
        ApplyHeadLook();

        if (Time.time >= _stateUntil)
        {
            StopTeleportSmileAnimation();
            EnterState(SmilyState.LinkSnap);
        }
    }

    private void TickTeleportWindup()
    {
        if (_target == null || !CanKeepAttack())
        {
            ReevaluateAfterRecovery();
            return;
        }

        StopAgent();

        if (_teleportWindupUsesWarning)
        {
            FaceTarget();
            ApplyHeadLook();
        }

        if (Time.time >= _stateUntil)
        {
            StopTeleportSmileAnimation();
            CompleteTeleport();
        }
    }

    private void CompleteTeleport()
    {
        StopTeleportSmileAnimation();

        if (!TryTeleportToAmbushPoint())
        {
            BeginTeleportRecovery();
            return;
        }

        FaceTarget();

        if (!TryResolveJumpPoint(out _jumpPoint))
        {
            BeginTeleportRecovery();
            return;
        }

        SpawnAttackPortalEffect();
        _stateUntil = Time.time + Mathf.Max(0.05f, attackWindupSeconds);
        _jumpStarted = false;
        StopAgent();
        EnterState(SmilyState.AttackWindup);
    }

    private bool TryTeleportToAmbushPoint()
    {
        int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(_ambushPoint, out NavMeshHit hit, navMeshSampleRadius, areaMask))
            return false;

        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;
        Vector3 teleportPoint = hit.position;

        if (agent != null && agent.enabled)
        {
            if (!agent.Warp(teleportPoint))
                return false;

            agent.velocity = Vector3.zero;
            agent.nextPosition = teleportPoint;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
            }
        }
        else
        {
            transform.position = teleportPoint;
        }

        SpawnEffect(teleportOutEffectPrefab, previousPosition, previousRotation, teleportOutEffectOffset, teleportOutEffectLifetime);
        SpawnEffect(teleportInEffectPrefab, teleportPoint, transform.rotation, teleportInEffectOffset, teleportInEffectLifetime);
        audioController?.PlayTeleport();
        return true;
    }

    private void BeginTeleportRecovery()
    {
        _consecutiveAttackCount = 0;
        _stateUntil = Time.time + Mathf.Max(0f, teleportRecoverySeconds);
        StopAgent();
        EnterState(SmilyState.TeleportRecovery);
    }

    private void TickAttackWindup()
    {
        if (_target == null || !CanKeepAttack())
        {
            ReevaluateAfterRecovery();
            return;
        }

        StopAgent();
        bool facingTarget = FaceTarget();

        if (Time.time < _stateUntil || !facingTarget)
            return;

        if (!_jumpStarted)
        {
            if (!StartJumpAttack())
            {
                ReevaluateAfterRecovery();
                return;
            }

            _jumpStarted = true;
            EnterState(SmilyState.JumpAttack);
        }
    }

    private void TickJumpAttack()
    {
        if (jumpToPoint != null && jumpToPoint.IsJumping)
            return;

        audioController?.PlayJumpLand();
        _stateUntil = Time.time + Mathf.Max(0f, attackRecoverSeconds);
        EnterState(SmilyState.AttackRecover);
    }

    private bool StartJumpAttack()
    {
        if (jumpToPoint == null || !jumpToPoint.CanStartAttack(_target, true))
            return false;

        FaceTarget(true);

        bool started = jumpToPoint.TryStartJump(
            _jumpPoint,
            jumpDuration,
            jumpHeight,
            _target != null ? _target.transform.position : _jumpPoint,
            _target,
            true);

        if (!started)
            return false;

        _consecutiveAttackCount = Mathf.Clamp(_consecutiveAttackCount + 1, 1, ResolveMaxConsecutiveAttacks());

        if (HasAnimatorParameter(attackTrigger, AnimatorControllerParameterType.Trigger))
            SetAnimatorTrigger(attackTrigger);

        return true;
    }

    private void BeginFleeOrReevaluate()
    {
        bool hitPlayer = attackHitbox != null && attackHitbox.HasHitThisAttack;
        attackHitbox?.ResetHit();

        if (hitPlayer)
        {
            BeginFleeOrFallback();
            return;
        }

        if (ShouldRepeatAttack() && TryFindAmbushPoint(_target, out _ambushPoint))
        {
            BeginTeleportWindup(false);
            return;
        }

        BeginFleeOrFallback();
    }

    private bool ShouldRepeatAttack()
    {
        if (_target == null || !HasLineOfSight(_target))
            return false;

        if (_consecutiveAttackCount >= ResolveMaxConsecutiveAttacks())
            return false;

        return Random.value < Mathf.Clamp01(repeatAttackChance);
    }

    private int ResolveMaxConsecutiveAttacks()
    {
        return Mathf.Max(1, defaultMaxConsecutiveAttacks);
    }

    private void BeginFleeOrFallback()
    {
        _consecutiveAttackCount = 0;

        if (fleeFromTarget != null && _target != null)
        {
            fleeFromTarget.StartFlee(_target.transform);
            audioController?.PlayFleeStart();
            EnterState(SmilyState.Flee);
            return;
        }

        ReevaluateAfterRecovery();
    }

    private void TickFlee()
    {
        if (fleeFromTarget == null || !fleeFromTarget.IsFleeing)
        {
            ResetDetectionTimers();
            EnterState(SmilyState.Idle);
            return;
        }

        ResumeAgent();
        SetAnimatorSpeedParameter(1f, true);
    }

    private bool CanBeginAttack(GameObject target)
    {
        return CanBeginAttack(target, false);
    }

    private bool CanBeginAttack(GameObject target, bool allowCurrentDoorTraversal)
    {
        if (target == null)
            return false;

        if (!HasLineOfSight(target))
            return false;

        if (jumpToPoint != null && jumpToPoint.IsJumping)
            return false;

        if (attackState != null && (attackState.IsAttackActive || attackState.IsRecentlyAttacked))
            return false;

        if (fleeFromTarget != null && (fleeFromTarget.IsFleeing || fleeFromTarget.IsInReAggroCooldown))
            return false;

        if (!allowCurrentDoorTraversal && IsDoorTraversalBlockingBrain())
            return false;

        float distance = Vector3.Distance(transform.position, target.transform.position);
        return distance >= minAmbushTargetDistance && distance <= maxAmbushTargetDistance;
    }

    private bool CanKeepAttack()
    {
        if (health != null && health.IsDead)
            return false;

        if (doorAutoOpener != null && doorAutoOpener.IsTraversing)
            return false;

        return true;
    }

    private bool IsDoorTraversalBlockingBrain()
    {
        return doorAutoOpener != null && (doorAutoOpener.IsTraversing || doorAutoOpener.IsWaitingForDoor || doorAutoOpener.IsMovingAcrossLink);
    }

    private void ReevaluateAfterRecovery()
    {
        StopTeleportSmileAnimation();
        _linkSnapTraversalCompletionRequested = false;
        if (doorAutoOpener != null && doorAutoOpener.IsManualTraversalPaused)
            doorAutoOpener.SetManualTraversalPaused(false);

        _consecutiveAttackCount = 0;
        ResetDetectionTimers();
        ResumeAgent();
        EnterState(SmilyState.Idle);
    }

    private void ResetDetectionTimers()
    {
        _chaseStartedAt = -1f;
        _seenStartedAt = -1f;
        _nextChaseRepathAt = 0f;
    }

    private void BeginTeleportWarning()
    {
        attackState?.EndAttackWindow("TeleportWarning");

        bool hasTeleportTrigger = HasAnimatorParameter(teleportSmileTrigger, AnimatorControllerParameterType.Trigger);
        if (CanSynchronizeAnimator() && hasTeleportTrigger)
        {
            StopTeleportSmileAnimation();
            SetAnimatorTrigger(teleportSmileTrigger);
        }
        else
        {
            bool playedClip = PlayTeleportSmileAnimation();
            if (!playedClip && hasTeleportTrigger)
                SetAnimatorTrigger(teleportSmileTrigger);
        }

        audioController?.PlayWarningSmile();
    }

    private float ResolveTeleportWarningDuration()
    {
        if (useTeleportSmileAnimationLength && teleportSmileAnimationClip != null)
            return Mathf.Max(0.05f, teleportSmileAnimationClip.length + Mathf.Max(0f, teleportSmileExtraDelay));

        return Mathf.Max(0.05f, teleportWarningSeconds);
    }

    private bool PlayTeleportSmileAnimation()
    {
        StopTeleportSmileAnimation();

        if (animator == null || teleportSmileAnimationClip == null)
            return false;

        _teleportSmileGraph = PlayableGraph.Create($"{name}_TeleportSmile");
        AnimationClipPlayable playable = AnimationClipPlayable.Create(_teleportSmileGraph, teleportSmileAnimationClip);
        playable.SetDuration(teleportSmileAnimationClip.length);
        playable.SetApplyFootIK(false);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_teleportSmileGraph, "TeleportSmile", animator);
        output.SetSourcePlayable(playable);

        _teleportSmileGraph.Play();
        _teleportSmilePlayableActive = true;
        return true;
    }

    private void StopTeleportSmileAnimation()
    {
        if (!_teleportSmilePlayableActive)
            return;

        if (_teleportSmileGraph.IsValid())
            _teleportSmileGraph.Destroy();

        _teleportSmilePlayableActive = false;
    }

    private bool TryFindAmbushPoint(GameObject target, out Vector3 point)
    {
        point = transform.position;

        if (target == null)
            return false;

        Vector3 targetPosition = target.transform.position;
        Vector3 targetForward = ResolveTargetForward(target);
        Vector3 targetRight = Vector3.Cross(Vector3.up, targetForward).normalized;

        float bestScore = float.NegativeInfinity;
        Vector3 bestPoint = point;
        int attempts = Mathf.Max(1, candidateCount);
        int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit targetNavHit, navMeshSampleRadius, areaMask))
            return false;

        Vector3 pathTargetPosition = targetNavHit.position;

        for (int i = 0; i < attempts; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float angle = Random.Range(105f, 170f) * side;
            if (i % 5 == 0)
                angle = Random.Range(145f, 180f) * side;

            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * targetForward;
            if (i % 3 == 0)
                direction = (direction + targetRight * side * 0.35f).normalized;

            float distance = Random.Range(minAmbushDistance, Mathf.Max(minAmbushDistance + 0.1f, maxAmbushDistance));
            Vector3 rawCandidate = targetPosition + direction.normalized * distance;

            if (!NavMesh.SamplePosition(rawCandidate, out NavMeshHit hit, navMeshSampleRadius, areaMask))
                continue;

            Vector3 candidate = hit.position;
            Vector3 fromTarget = candidate - targetPosition;
            fromTarget.y = 0f;

            if (fromTarget.sqrMagnitude <= 0.0001f)
                continue;

            float forwardDot = Vector3.Dot(targetForward, fromTarget.normalized);
            if (forwardDot > maxForwardDot)
                continue;

            if (!HasReasonablePath(candidate, pathTargetPosition, areaMask))
                continue;

            if (!HasFrontClearance(candidate, targetPosition))
                continue;

            bool blocked = IsSightBlocked(targetPosition, candidate);
            // Best ambush: the victim cannot see it but a teammate can — that teammate gets one second to shout.
            bool teammateSees = blocked && TeammateCanSee(candidate, target);
            float score = (blocked ? 3f : 0f) + (teammateSees ? teammateVisibleBonus : 0f) - forwardDot + Random.value * 0.2f;
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = candidate;
            }
        }

        if (bestScore <= float.NegativeInfinity)
            return false;

        point = bestPoint;
        return true;
    }

    [SerializeField, Min(0f), Tooltip("Ambush score bonus when another living player can see the ambush spot.")]
    private float teammateVisibleBonus = 4f;

    private bool TeammateCanSee(Vector3 candidate, GameObject target)
    {
        Transform targetRoot = target != null ? target.transform.root : null;
        Vector3 spot = candidate + Vector3.up * 1f;
        int mask = sightBlockMask.value == 0 ? ~0 : sightBlockMask.value;
        int checks = 0;
        for (int i = 0; i < PlayerPawn.All.Count && checks < 4; i++)
        {
            PlayerPawn pawn = PlayerPawn.All[i];
            if (pawn == null || pawn.transform.root == targetRoot || !MonsterTargeting.IsAlive(pawn))
                continue;

            checks++;
            Vector3 eye = pawn.transform.position + Vector3.up * 1.6f;
            if ((eye - spot).sqrMagnitude > 30f * 30f)
                continue;

            if (!Physics.Linecast(eye, spot, mask, QueryTriggerInteraction.Ignore))
                return true;
        }

        return false;
    }

    private bool HasReasonablePath(Vector3 start, Vector3 targetPosition, int areaMask)
    {
        _path ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(start, targetPosition, areaMask, _path))
            return false;

        return _path.status == NavMeshPathStatus.PathComplete;
    }

    private bool HasFrontClearance(Vector3 candidate, Vector3 targetPosition)
    {
        Vector3 forward = Flatten(targetPosition - candidate);
        Vector3 center = candidate + Vector3.up * frontClearanceHeight + forward * frontClearanceDistance;
        int mask = sightBlockMask.value == 0 ? ~0 : sightBlockMask.value;
        return !Physics.CheckSphere(center, frontClearanceRadius, mask, QueryTriggerInteraction.Ignore);
    }

    private bool IsSightBlocked(Vector3 targetPosition, Vector3 candidate)
    {
        Vector3 origin = targetPosition + Vector3.up * 1.55f;
        Vector3 destination = candidate + Vector3.up * 1.1f;
        Vector3 direction = destination - origin;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
            return false;

        return Physics.Raycast(origin, direction.normalized, distance, sightBlockMask, QueryTriggerInteraction.Ignore);
    }

    private Vector3 ResolveTargetForward(GameObject target)
    {
        Camera camera = Camera.main;
        if (camera != null && target != null && camera.transform.IsChildOf(target.transform))
            return Flatten(camera.transform.forward);

        return Flatten(target != null ? target.transform.forward : Vector3.forward);
    }

    private bool TryResolveJumpPoint(out Vector3 point)
    {
        if (assignJumpPoint != null && _target != null)
            return assignJumpPoint.TryComputeJumpPoint(_target, out point);

        point = _target != null ? _target.transform.position + _target.transform.forward + Vector3.up : transform.position;
        int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSampleRadius, areaMask))
            return false;

        point = hit.position;
        return true;
    }

    private bool FaceTarget(bool instant = false)
    {
        if (_target == null)
            return false;

        Vector3 toTarget = _target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return true;

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        if (instant)
        {
            transform.rotation = targetRotation;
            return true;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            Mathf.Max(1f, bodyLookTurnSpeed) * Time.deltaTime);

        return Quaternion.Angle(transform.rotation, targetRotation) <= Mathf.Max(1f, attackFacingAngleTolerance);
    }

    private void ApplyHeadLook()
    {
        if (_target == null || headLookWeight <= 0f)
            return;

        if (headTransform == null)
            return;

        Vector3 toTarget = _target.transform.position + Vector3.up * 1.35f - headTransform.position;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return;

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        float angle = Quaternion.Angle(transform.rotation, desired);
        if (angle > maxHeadLookAngle)
            desired = Quaternion.RotateTowards(transform.rotation, desired, maxHeadLookAngle);

        Quaternion weighted = Quaternion.Slerp(headTransform.rotation, desired, Mathf.Clamp01(headLookWeight));
        headTransform.rotation = Quaternion.RotateTowards(
            headTransform.rotation,
            weighted,
            Mathf.Max(0f, headLookTurnSpeed) * Time.deltaTime);
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null || string.IsNullOrEmpty(locomotionSpeedParameter))
            return;

        if (doorAutoOpener != null && doorAutoOpener.IsMovingAcrossLink)
            return;

        if (doorAutoOpener != null && doorAutoOpener.IsWaitingForDoor)
        {
            SetAnimatorSpeedParameter(0f, true);
            return;
        }

        float speed = 0f;
        if (agent != null && agent.enabled)
        {
            bool activeOnNavMesh = agent.isOnNavMesh;
            bool expectsLocomotion =
                _state == SmilyState.Patrol
                || _state == SmilyState.Investigate
                || _state == SmilyState.Chase
                || _state == SmilyState.Flee;
            Vector3 velocity = activeOnNavMesh ? agent.velocity : Vector3.zero;
            if (velocity.sqrMagnitude <= 0.0001f
                && activeOnNavMesh
                && (agent.hasPath || agent.pathPending)
                && !agent.isStopped
                && expectsLocomotion)
            {
                velocity = agent.desiredVelocity;
            }

            speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed <= Mathf.Max(0f, animatorStopDeadZone))
                speed = 0f;
            else
                speed = Mathf.Clamp01(speed / Mathf.Max(0.1f, agent.speed));

            if (speed <= 0f && activeOnNavMesh && !agent.isStopped && expectsLocomotion)
            {
                bool hasMoveIntent = agent.hasPath || agent.pathPending;
                if (_state == SmilyState.Flee && fleeFromTarget != null && fleeFromTarget.IsFleeing)
                    hasMoveIntent = true;

                if (hasMoveIntent)
                    speed = 1f;
            }
        }

        if (_state == SmilyState.Idle
            || _state == SmilyState.LinkWarningSmile
            || _state == SmilyState.LinkSnap
            || _state == SmilyState.TeleportWindup
            || _state == SmilyState.TeleportRecovery
            || _state == SmilyState.AttackWindup
            || _state == SmilyState.AttackRecover)
        {
            speed = 0f;
        }

        SetAnimatorSpeedParameter(speed);
    }

    private void ApplyMovementSettings()
    {
        float moveSpeed = GetMoveSpeed();

        if (agent != null && agent.enabled)
            agent.speed = moveSpeed;

        SetAnimatorPlaybackSpeed(GetAnimatorPlaybackSpeed(moveSpeed));
    }

    private void SetAnimatorPlaybackSpeed(float playbackSpeed)
    {
        if (animator == null)
            return;

        playbackSpeed = Mathf.Max(0f, playbackSpeed);
        if (Mathf.Approximately(animator.speed, playbackSpeed))
            return;

        if (CanSynchronizeAnimator())
            networkAnimator.speed = playbackSpeed;
        else
            animator.speed = playbackSpeed;
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
            return;

        if (CanSynchronizeAnimator())
            networkAnimator.SetTrigger(triggerName);
        else
            animator.SetTrigger(triggerName);
    }

    private bool CanSynchronizeAnimator()
    {
        return networkAnimator != null && networkAnimator.isSpawned && networkAnimator.isServer;
    }

    private float GetMoveSpeed()
    {
        return _state switch
        {
            SmilyState.Chase => Mathf.Max(0.1f, chaseSpeed),
            SmilyState.Flee => Mathf.Max(0.1f, patrolSpeed * Mathf.Max(0.1f, fleeSpeedMultiplier)),
            _ => Mathf.Max(0.1f, patrolSpeed)
        };
    }

    private float GetAnimatorPlaybackSpeed(float moveSpeed)
    {
        if (!UsesLocomotionPlaybackScale())
            return 1f;

        return Mathf.Max(0.1f, moveSpeed / Mathf.Max(0.1f, patrolSpeed));
    }

    private bool UsesLocomotionPlaybackScale()
    {
        if (doorAutoOpener != null && doorAutoOpener.IsWaitingForDoor)
            return false;

        return _state == SmilyState.Patrol
            || _state == SmilyState.Investigate
            || _state == SmilyState.Chase
            || _state == SmilyState.Flee;
    }

    private void SetAnimatorSpeedParameter(float speed, bool immediate = false)
    {
        if (!HasAnimatorParameter(locomotionSpeedParameter, AnimatorControllerParameterType.Float))
            return;

        if (immediate)
        {
            animator.SetFloat(locomotionSpeedParameter, speed);
            return;
        }

        animator.SetFloat(locomotionSpeedParameter, speed, Mathf.Max(0f, locomotionDampTime), Time.deltaTime);
    }

    private void StopAgent()
    {
        if (agent == null || !agent.enabled)
            return;

        if (!agent.isOnNavMesh)
        {
            agent.updatePosition = true;
            return;
        }

        if (IsDoorTraversalBlockingBrain())
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            return;
        }

        agent.ResetPath();
        agent.velocity = Vector3.zero;
        agent.isStopped = true;
        agent.updatePosition = true;
    }

    private void ResumeAgent()
    {
        if (agent == null || !agent.enabled)
            return;

        agent.updatePosition = true;
        if (!agent.isOnNavMesh)
            return;

        agent.isStopped = false;
    }

    private void SpawnAttackPortalEffect()
    {
        SpawnEffect(attackPortalEffectPrefab, transform.position, transform.rotation, attackPortalEffectOffset, attackPortalEffectLifetime);
    }

    private void SpawnEffect(GameObject prefab, Vector3 basePosition, Quaternion rotation, Vector3 offset, float lifetime)
    {
        if (prefab == null)
            return;

        GameObject effect = Instantiate(prefab, basePosition + rotation * offset, rotation);
        float resolvedLifetime = Mathf.Max(0f, lifetime);
        if (resolvedLifetime > 0f)
            Destroy(effect, resolvedLifetime);
    }

    private void HideRenderersForLinkSnap()
    {
        if (!hideRenderersDuringLinkSnap || _snapHiddenRenderers != null)
            return;

        _snapHiddenRenderers = GetComponentsInChildren<Renderer>(true);
        _snapRendererEnabledStates = new bool[_snapHiddenRenderers.Length];

        for (int i = 0; i < _snapHiddenRenderers.Length; i++)
        {
            Renderer renderer = _snapHiddenRenderers[i];
            if (renderer == null)
                continue;

            _snapRendererEnabledStates[i] = renderer.enabled;
            renderer.enabled = false;
        }
    }

    private void RestoreRenderersAfterLinkSnap()
    {
        if (_snapHiddenRenderers == null)
            return;

        for (int i = 0; i < _snapHiddenRenderers.Length; i++)
        {
            Renderer renderer = _snapHiddenRenderers[i];
            if (renderer == null)
                continue;

            bool wasEnabled = _snapRendererEnabledStates != null
                && i < _snapRendererEnabledStates.Length
                && _snapRendererEnabledStates[i];

            renderer.enabled = wasEnabled;
        }

        _snapHiddenRenderers = null;
        _snapRendererEnabledStates = null;
    }

    private void EnterState(SmilyState next)
    {
        if (_state == next)
            return;

        SmilyState previous = _state;
        _state = next;
        ApplyMovementSettings();
        ApplyAudioForState(next);

        if (logStateChanges)
            Debug.Log($"[SmilyBrain] {previous} -> {next}", this);
    }

    private MonsterHealth _audioRelay;

    /// <summary>
    /// Audio is relayed through MonsterHealth (a spawned NetworkBehaviour) so every peer hears the
    /// Smily, not just the host that runs the brain. Falls back to local playback when unspawned.
    /// </summary>
    private void ApplyAudioForState(SmilyState state)
    {
        if (audioController == null)
            return;

        if (_audioRelay == null)
            _audioRelay = GetComponent<MonsterHealth>() ?? GetComponentInParent<MonsterHealth>();

        switch (state)
        {
            case SmilyState.Idle:
                RelayAudio(SmilyAudioEvent.IdleLoop);
                break;
            case SmilyState.Patrol:
            case SmilyState.Investigate:
                RelayAudio(SmilyAudioEvent.PatrolLoop);
                break;
            case SmilyState.AttackWindup:
                RelayAudio(SmilyAudioEvent.StopLoop);
                RelayAudio(SmilyAudioEvent.AttackWindup);
                break;
            case SmilyState.JumpAttack:
                RelayAudio(SmilyAudioEvent.StopLoop);
                RelayAudio(SmilyAudioEvent.AttackCommit);
                break;
            default:
                RelayAudio(SmilyAudioEvent.StopLoop);
                break;
        }
    }

    private void RelayAudio(SmilyAudioEvent audioEvent)
    {
        if (_audioRelay != null && _audioRelay.isSpawned)
            _audioRelay.RelaySmilyAudio(audioEvent);
        else
            MonsterHealth.ApplySmilyAudio(audioController, audioEvent);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        int hash = Animator.StringToHash(parameterName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.nameHash == hash && parameter.type == parameterType)
                return true;
        }

        return false;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        if (value.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return value.normalized;
    }

    private sealed class ColliderGraceScope : System.IDisposable
    {
        private readonly Collider[] _selfColliders;
        private readonly Collider[] _targetColliders;

        public ColliderGraceScope(SmilyBrain owner, GameObject target, float duration)
        {
            _selfColliders = null;
            _targetColliders = null;

            if (owner == null || target == null || duration <= 0f)
                return;

            _selfColliders = owner.GetComponentsInChildren<Collider>(true);
            Transform targetRoot = ResolveTargetRoot(target);
            _targetColliders = targetRoot != null
                ? targetRoot.GetComponentsInChildren<Collider>(true)
                : target.GetComponentsInChildren<Collider>(true);
            SetIgnored(true);
        }

        public void Dispose()
        {
            SetIgnored(false);
        }

        private void SetIgnored(bool ignored)
        {
            if (_selfColliders == null || _targetColliders == null)
                return;

            for (int i = 0; i < _selfColliders.Length; i++)
            {
                Collider self = _selfColliders[i];
                if (self == null)
                    continue;

                for (int j = 0; j < _targetColliders.Length; j++)
                {
                    Collider target = _targetColliders[j];
                    if (target == null || target == self)
                        continue;

                    Physics.IgnoreCollision(self, target, ignored);
                }
            }
        }

        private static Transform ResolveTargetRoot(GameObject target)
        {
            if (target == null)
                return null;

            PlayerDeath death = target.GetComponentInParent<PlayerDeath>();
            if (death != null)
                return death.transform;

            PlayerPawn pawn = target.GetComponentInParent<PlayerPawn>();
            if (pawn != null)
                return pawn.transform;

            PlayerVitals vitals = target.GetComponentInParent<PlayerVitals>();
            if (vitals != null)
                return vitals.transform;

            return target.transform.root != null ? target.transform.root : target.transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(_ambushPoint, 0.35f);
        Gizmos.DrawLine(transform.position, _ambushPoint);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_jumpPoint, 0.25f);

        if (_hasLastKnownTargetPosition)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_lastKnownTargetPosition, Mathf.Max(0.1f, investigationArrivalDistance));
            Gizmos.DrawLine(transform.position, _lastKnownTargetPosition);
        }
    }
}

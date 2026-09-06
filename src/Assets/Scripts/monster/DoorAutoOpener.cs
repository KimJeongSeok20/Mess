using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class DoorAutoOpener : MonoBehaviour
{
    [Header("Door Link")]
    [SerializeField] private string doorAreaName = "Door";
    [SerializeField] private bool treatAllLinksAsDoorWhenAreaMissing;
    [SerializeField] private MonsterDoorTraversalGroup traversalGroup;
    [SerializeField] private bool canOpenDoors = true;

    [Header("Jump Link")]
    [SerializeField] private bool useJumpAreaTraversal = true;
    [SerializeField] private string jumpAreaName = "MyJump";
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("Door Open")]
    [SerializeField] private float openDelaySeconds = 0.8f;
    [SerializeField] private float waitUntilOpenSeconds = 2f;
    [SerializeField] private float doorSearchRadius = 2f;
    [SerializeField] private LayerMask doorLayerMask = ~0;

    [Header("Continuous NavMesh Door")]
    [SerializeField, Tooltip("Detect a door portal on the current surface path before the agent reaches its physical collider.")]
    private bool useContinuousDoorDetection = true;
    [SerializeField, Min(0.5f)] private float continuousDoorLookAhead = 2.5f;
    [SerializeField, Min(0.1f)] private float continuousDoorProbeRadius = 0.55f;
    [SerializeField, Range(-1f, 1f)] private float continuousDoorMinimumForwardDot = 0.05f;
    [SerializeField, Min(0.02f)] private float continuousDoorProbeInterval = 0.1f;
    [SerializeField, Min(0f), Tooltip("Extra horizontal clearance used when checking whether the remaining NavMesh path crosses the physical doorway.")]
    private float continuousDoorPathPadding = 0.08f;
    [SerializeField, Min(0.05f), Tooltip("The path destination must continue at least this far beyond the door plane before the monster opens it.")]
    private float continuousDoorMinimumBeyondDistance = 0.25f;

    [Header("Traversal")]
    [SerializeField] private float traverseSpeedMultiplier = 1f;
    [SerializeField] private float linkMatchDistance = 3f;
    [SerializeField] private float doorClearanceRelaxSeconds = 2f;
    [SerializeField] private bool preserveCurrentYDuringManualLink = true;
    [SerializeField] private bool rotateAlongManualLink = true;
    [SerializeField] private float manualLinkTurnSpeedMultiplier = 1f;
    [SerializeField] private bool forceAnimatorSpeedOnManualLink = true;
    [SerializeField] private string animatorSpeedParameter = "SpeedMagnitude";
    [SerializeField] private bool syncAnimatorSpeedFromAgentVelocity;
    [SerializeField] private bool normalizeAnimatorSpeedToUnitRange = true;
    [SerializeField] private float animatorSpeedDampTime = 0.08f;
    [SerializeField] private float animatorStopDeadZone = 0.05f;
    [SerializeField] private float animatorArrivalBuffer = 0.05f;
    [SerializeField] private bool debugLog;
    [SerializeField] private bool debugWarpTrace;

    private NavMeshAgent _agent;
    private Animator _animator;
    private SmilyWallClearanceController _bodyClearance;
    private int _doorAreaIndex = -1;
    private int _jumpAreaIndex = -1;
    private bool _isTraversing;
    private bool _isMovingAcrossLink;
    private bool _isWaitingForDoor;
    private bool _manualTraversalBlocked;
    private bool _manualTraversalPaused;
    private bool _finishManualTraversalAtEndRequested;
    private bool _keepAgentStoppedAfterTraversal;
    private bool _currentLinkIsDoor;
    private float _doorClearanceRelaxUntil;
    private Coroutine _traverseRoutine;
    private Coroutine _continuousDoorRoutine;
    private MonsterDoorLinkBinding _continuousDoorBinding;
    private float _nextContinuousDoorProbeTime;
    private readonly Collider[] _continuousDoorHits = new Collider[24];
    private readonly Vector3[] _continuousDoorPathCorners = new Vector3[32];

    public bool IsTraversing => _isTraversing;
    public bool IsMovingAcrossLink => _isMovingAcrossLink;
    public bool IsWaitingForDoor => _isWaitingForDoor;
    public bool IsManualTraversalPaused => _manualTraversalPaused;
    public Vector3 CurrentTraversalEndPosition { get; private set; }
    public bool ShouldRelaxBodyClearanceForDoor => (_isTraversing && _currentLinkIsDoor) || Time.time < _doorClearanceRelaxUntil;

    public void ConfigureGroup(MonsterDoorTraversalGroup group)
    {
        ConfigureGroup(group, true);
    }

    public void ConfigureGroup(MonsterDoorTraversalGroup group, bool canOpen)
    {
        traversalGroup = group;
        canOpenDoors = canOpen;
    }

    public void ConfigureContinuousDoorDetection(
        bool enabled,
        float lookAhead,
        float probeRadius,
        float probeInterval,
        LayerMask layerMask)
    {
        useContinuousDoorDetection = enabled;
        continuousDoorLookAhead = Mathf.Max(0.5f, lookAhead);
        continuousDoorProbeRadius = Mathf.Max(0.1f, probeRadius);
        continuousDoorProbeInterval = Mathf.Max(0.02f, probeInterval);
        doorLayerMask = layerMask;
    }

    private void Awake()
    {
        CacheComponents();
        _agent.autoTraverseOffMeshLink = false;
        ResolveDoorArea();
        ResolveJumpArea();
    }

    private void Update()
    {
        UpdateAnimatorLocomotionSpeed();

        if (_isTraversing || _continuousDoorRoutine != null || _agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        _agent.autoTraverseOffMeshLink = false;

        if (!_agent.isOnOffMeshLink)
        {
            TryBeginContinuousDoorWait();
            return;
        }

        OffMeshLinkData linkData = _agent.currentOffMeshLinkData;
        if (!linkData.valid)
            return;

        bool linkResolved = TryResolveCurrentLink(linkData, out NavMeshLink navMeshLink, out int area);
        if (!linkResolved && debugLog)
        {
            Debug.LogWarning("[DoorAutoOpener] Could not resolve NavMeshLink component for current OffMeshLink. Traversing as a generic link to avoid a stuck agent.", this);
        }

        bool isJumpLink = IsJumpLink(area);
        bool isDoorLink = IsDoorLink(linkData, navMeshLink, area);
        _traverseRoutine = StartCoroutine(TraverseLinkRoutineGuarded(linkData, navMeshLink, isDoorLink, isJumpLink));
    }

    private void TryBeginContinuousDoorWait()
    {
        if (!useContinuousDoorDetection || Time.time < _nextContinuousDoorProbeTime)
            return;

        _nextContinuousDoorProbeTime = Time.time + continuousDoorProbeInterval;
        if (!_agent.hasPath || _agent.pathPending || _agent.isStopped)
            return;

        if (!TryFindContinuousDoorAhead(out MonsterDoorLinkBinding binding))
            return;

        _continuousDoorBinding = binding;
        _continuousDoorRoutine = StartCoroutine(HandleContinuousDoorRoutineGuarded(binding));
    }

    private bool TryFindContinuousDoorAhead(out MonsterDoorLinkBinding binding)
    {
        binding = null;
        int pathCornerCount = _agent.path.GetCornersNonAlloc(_continuousDoorPathCorners);
        if (pathCornerCount < 2)
            return false;

        Vector3 direction = _agent.desiredVelocity;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.01f)
        {
            direction = _agent.steeringTarget - transform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.01f)
            direction = transform.forward;
        direction.Normalize();

        float probeHeight = Mathf.Clamp(_agent.height * 0.45f, 0.35f, 1.2f);
        Vector3 start = transform.position + Vector3.up * probeHeight;
        Vector3 end = start + direction * continuousDoorLookAhead;
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            start,
            end,
            continuousDoorProbeRadius,
            _continuousDoorHits,
            doorLayerMask,
            QueryTriggerInteraction.Collide);

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _continuousDoorHits[i];
            _continuousDoorHits[i] = null;
            if (!MonsterDoorLinkBinding.TryResolve(hit, out MonsterDoorLinkBinding candidate) ||
                candidate == null || candidate.IsOpen)
            {
                continue;
            }

            if (!candidate.IsPassageOnPath(
                    _continuousDoorPathCorners,
                    pathCornerCount,
                    continuousDoorPathPadding,
                    continuousDoorLookAhead + continuousDoorProbeRadius,
                    continuousDoorMinimumBeyondDistance))
            {
                continue;
            }

            Vector3 closest = hit.ClosestPoint(start);
            Vector3 offset = closest - start;
            offset.y = 0f;
            float distance = offset.magnitude;
            float forwardDot = distance > 0.001f ? Vector3.Dot(direction, offset / distance) : 1f;
            if (forwardDot < continuousDoorMinimumForwardDot || distance >= bestDistance)
                continue;

            bestDistance = distance;
            binding = candidate;
        }

        return binding != null;
    }

    private IEnumerator HandleContinuousDoorRoutineGuarded(MonsterDoorLinkBinding binding)
    {
        bool hadPath = _agent != null && _agent.hasPath;
        bool wasStopped = _agent != null && _agent.isStopped;
        Vector3 destination = hadPath ? _agent.destination : transform.position;
        bool ownsClaim = false;

        try
        {
            if (binding == null || !binding.CanOpen || binding.IsOpen || !HasActiveAgentOnNavMesh())
                yield break;

            _isWaitingForDoor = true;
            _currentLinkIsDoor = true;
            SetDoorTraversalBinding(binding);
            _agent.isStopped = true;
            ForceIdleDuringDoorWait();

            ownsClaim = traversalGroup != null
                ? traversalGroup.TryClaim(binding, this, canOpenDoors)
                : canOpenDoors;
            if (ownsClaim)
                binding.Open();

            float timeout = waitUntilOpenSeconds + (ownsClaim ? 0f : openDelaySeconds + 0.5f);
            yield return WaitUntilDoorOpen(binding, timeout);
            if (!binding.IsOpen && debugLog)
                Debug.LogWarning($"[DoorAutoOpener] Continuous-path door did not open before timeout: {binding.name}", binding);
        }
        finally
        {
            if (traversalGroup != null && binding != null)
                traversalGroup.Release(binding, this);

            _doorClearanceRelaxUntil = Time.time + doorClearanceRelaxSeconds;
            _isWaitingForDoor = false;
            _currentLinkIsDoor = false;
            _continuousDoorBinding = null;
            _continuousDoorRoutine = null;
            SetDoorTraversalBinding(null);

            if (HasActiveAgentOnNavMesh())
            {
                _agent.isStopped = wasStopped;
                if (!wasStopped && hadPath)
                    _agent.SetDestination(destination);
            }
        }
    }

    private void CacheComponents()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _bodyClearance = GetComponent<SmilyWallClearanceController>();
    }

    public void SetManualTraversalPaused(bool paused)
    {
        _manualTraversalPaused = paused;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (paused)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
            else if (_isMovingAcrossLink)
            {
                _agent.isStopped = false;
            }
        }

        if (paused)
            ForceAnimatorSpeed(0f);
    }

    public void RequestManualTraversalCompleteAtEnd(bool keepAgentStopped)
    {
        if (!_isTraversing)
            return;

        _finishManualTraversalAtEndRequested = true;
        _keepAgentStoppedAfterTraversal = keepAgentStopped;
        SetManualTraversalPaused(false);
    }

    private IEnumerator TraverseLinkRoutineGuarded(OffMeshLinkData linkData, NavMeshLink navMeshLink, bool isDoorLink, bool isJumpLink)
    {
        try
        {
            yield return TraverseLinkRoutine(linkData, navMeshLink, isDoorLink, isJumpLink);
        }
        finally
        {
            _traverseRoutine = null;
            ResetTraversalState();
        }
    }

    private bool IsJumpLink(int currentLinkArea)
    {
        if (!useJumpAreaTraversal)
            return false;

        if (_jumpAreaIndex < 0)
            ResolveJumpArea();

        return _jumpAreaIndex >= 0 && currentLinkArea == _jumpAreaIndex;
    }

    private bool IsDoorLink(OffMeshLinkData linkData, NavMeshLink navMeshLink, int currentLinkArea)
    {
        if (_doorAreaIndex < 0)
            ResolveDoorArea();

        if (_doorAreaIndex >= 0 && currentLinkArea == _doorAreaIndex)
            return true;

        if (_doorAreaIndex < 0 && treatAllLinksAsDoorWhenAreaMissing)
            return true;

        return MonsterDoorLinkBinding.TryResolve(navMeshLink, GetLinkMidpoint(linkData), doorSearchRadius, doorLayerMask, out _);
    }

    private IEnumerator TraverseLinkRoutine(OffMeshLinkData linkData, NavMeshLink navMeshLink, bool isDoorLink, bool isJumpLink)
    {
        _isTraversing = true;
        _currentLinkIsDoor = isDoorLink;
        _manualTraversalPaused = false;
        _finishManualTraversalAtEndRequested = false;
        _keepAgentStoppedAfterTraversal = false;
        float traversalEnterTime = Time.time;
        bool hadPathBeforeTraverse = _agent.hasPath;
        Vector3 destinationBeforeTraverse = _agent.destination;

        string linkType = isJumpLink ? "Jump" : (isDoorLink ? "Door" : "Generic");
        LogWarpTrace($"Link enter type={linkType} pos={transform.position} linkStart={linkData.startPos} linkEnd={linkData.endPos}");

        _agent.isStopped = true;
        ForceAnimatorSpeed(0f);

        if (isDoorLink)
        {
            _doorClearanceRelaxUntil = Time.time + doorClearanceRelaxSeconds;
            bool doorReady = false;
            yield return HandleDoorBeforeTraversal(linkData, navMeshLink, hadPathBeforeTraverse, destinationBeforeTraverse, value => doorReady = value);
            if (!doorReady)
                yield break;
        }

        if (!HasActiveAgentOnNavMesh())
            yield break;

        Vector3 endPosition = GetTraversalEndPosition(linkData);
        CurrentTraversalEndPosition = endPosition;
        float speed = Mathf.Max(0.1f, _agent.speed * traverseSpeedMultiplier);
        bool updatePositionBeforeTraverse = _agent.updatePosition;
        _agent.isStopped = false;
        _agent.updatePosition = false;
        _manualTraversalBlocked = false;

        float movementStartTime = Time.time;
        Vector3 movementStartPosition = transform.position;
        LogWarpTrace($"Link move start type={linkType} from={movementStartPosition} to={endPosition} traverseSpeed={speed:F3}");

        _isMovingAcrossLink = true;
        if (isJumpLink)
            yield return TraverseJumpLink(endPosition, speed, !isDoorLink);
        else
            yield return TraverseLinearLink(endPosition, speed, !isDoorLink);

        if (_manualTraversalBlocked)
        {
            _agent.updatePosition = updatePositionBeforeTraverse;
            _isMovingAcrossLink = false;
            ForceAnimatorSpeed(0f);
            ExitDoorLinkAndRepath(hadPathBeforeTraverse, destinationBeforeTraverse);
            yield break;
        }

        if (!HasActiveAgentOnNavMesh())
            yield break;

        Vector3 finalLinkPosition = transform.position;
        SyncAgentBeforeCompleteOffMeshLink(finalLinkPosition);
        _agent.CompleteOffMeshLink();
        SyncAgentAfterCompleteOffMeshLink(finalLinkPosition);
        _agent.updatePosition = updatePositionBeforeTraverse;
        _agent.isStopped = false;

        if (isDoorLink)
            _doorClearanceRelaxUntil = Time.time + doorClearanceRelaxSeconds;

        if (_keepAgentStoppedAfterTraversal)
        {
            _agent.ResetPath();
            _agent.isStopped = true;
        }
        else if (hadPathBeforeTraverse && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.SetDestination(destinationBeforeTraverse);
        }

        ForceAnimatorSpeed(0f);
        _isMovingAcrossLink = false;

        float moveDuration = Time.time - movementStartTime;
        float totalDuration = Time.time - traversalEnterTime;
        float movedDistance = Vector3.Distance(movementStartPosition, transform.position);
        LogWarpTrace($"Link move done type={linkType} finalPos={transform.position} moved={movedDistance:F3} moveTime={moveDuration:F3}s totalTime={totalDuration:F3}s");

        if (debugLog && isDoorLink)
            Debug.Log("[DoorAutoOpener] Door link traversal completed.", this);
    }

    private IEnumerator HandleDoorBeforeTraversal(
        OffMeshLinkData linkData,
        NavMeshLink navMeshLink,
        bool hadPathBeforeTraverse,
        Vector3 destinationBeforeTraverse,
        System.Action<bool> setResult)
    {
        setResult(false);

        if (!MonsterDoorLinkBinding.TryResolve(navMeshLink, GetLinkMidpoint(linkData), doorSearchRadius, doorLayerMask, out MonsterDoorLinkBinding binding)
            || binding == null
            || !binding.CanOpen)
        {
            if (debugLog)
                Debug.LogWarning("[DoorAutoOpener] Door link has no resolvable door binding. Repathing instead of crossing closed door.", this);

            ExitDoorLinkAndRepath(hadPathBeforeTraverse, destinationBeforeTraverse);
            yield break;
        }

        SetDoorTraversalBinding(binding);

        if (!binding.IsOpen)
        {
            _isWaitingForDoor = true;
            ForceIdleDuringDoorWait();
            bool ownsClaim = false;
            bool mayOpen = canOpenDoors;

            if (traversalGroup != null)
                ownsClaim = traversalGroup.TryClaim(binding, this, mayOpen);
            else
                ownsClaim = mayOpen;

            if (ownsClaim)
            {
                binding.Open();
            }

            float timeout = waitUntilOpenSeconds + (ownsClaim ? 0f : openDelaySeconds + 0.5f);
            if (!binding.IsOpen)
                yield return WaitUntilDoorOpen(binding, timeout);

            traversalGroup?.Release(binding, this);
            _isWaitingForDoor = false;

            if (!binding.IsOpen)
            {
                if (debugLog)
                    Debug.LogWarning("[DoorAutoOpener] Door did not open before timeout. Repathing instead of crossing closed door.", this);

                ExitDoorLinkAndRepath(hadPathBeforeTraverse, destinationBeforeTraverse);
                yield break;
            }
        }

        setResult(true);
    }

    private IEnumerator WaitUntilDoorOpen(MonsterDoorLinkBinding binding, float timeout)
    {
        float elapsed = 0f;
        float maxWait = Mathf.Max(0f, timeout);

        while (binding != null && !binding.IsOpen && elapsed < maxWait)
        {
            ForceIdleDuringDoorWait();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void ForceIdleDuringDoorWait()
    {
        ForceAnimatorSpeed(0f);

        if (_animator != null)
            _animator.speed = 1f;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            _agent.velocity = Vector3.zero;
    }

    private IEnumerator TraverseLinearLink(Vector3 endPosition, float speed, bool useFullBodyClearance)
    {
        while ((transform.position - endPosition).sqrMagnitude > 0.0001f)
        {
            if (!HasActiveAgentOnNavMesh())
                yield break;

            if (_finishManualTraversalAtEndRequested)
            {
                break;
            }

            if (_manualTraversalPaused)
            {
                ForceAnimatorSpeed(0f);
                _agent.velocity = Vector3.zero;
                yield return null;
                continue;
            }

            if (rotateAlongManualLink)
                RotateAlongLink(endPosition);

            Vector3 currentPosition = transform.position;
            Vector3 nextPosition = Vector3.MoveTowards(currentPosition, endPosition, speed * Time.deltaTime);
            if (!CanAdvanceManualLink(nextPosition, endPosition, useFullBodyClearance))
            {
                _manualTraversalBlocked = true;
                yield break;
            }

            SetManualTraversalPosition(nextPosition, GetFrameVelocity(currentPosition, nextPosition));
            ForceAnimatorSpeed(speed);
            yield return null;
        }

        if (!CanAdvanceManualLink(endPosition, endPosition, useFullBodyClearance))
        {
            _manualTraversalBlocked = true;
            yield break;
        }

        SetManualTraversalPosition(endPosition, Vector3.zero);
    }

    private IEnumerator TraverseJumpLink(Vector3 endPosition, float speed, bool useFullBodyClearance)
    {
        Vector3 startPosition = transform.position;
        float horizontalDistance = Vector3.Distance(
            new Vector3(startPosition.x, 0f, startPosition.z),
            new Vector3(endPosition.x, 0f, endPosition.z));
        float duration = Mathf.Max(0.05f, horizontalDistance / Mathf.Max(0.1f, speed));

        float normalizedTime = 0f;
        while (normalizedTime < 1f)
        {
            if (!HasActiveAgentOnNavMesh())
                yield break;

            if (_finishManualTraversalAtEndRequested)
            {
                break;
            }

            if (_manualTraversalPaused)
            {
                ForceAnimatorSpeed(0f);
                _agent.velocity = Vector3.zero;
                yield return null;
                continue;
            }

            Vector3 position = Vector3.Lerp(startPosition, endPosition, normalizedTime);
            float yOffset = jumpHeight * 4f * (normalizedTime - normalizedTime * normalizedTime);
            position.y += yOffset;

            if (rotateAlongManualLink)
                RotateAlongLink(endPosition);

            if (!CanAdvanceManualLink(position, endPosition, useFullBodyClearance))
            {
                _manualTraversalBlocked = true;
                yield break;
            }

            SetManualTraversalPosition(position, GetFrameVelocity(transform.position, position));
            ForceAnimatorSpeed(speed);

            normalizedTime += Time.deltaTime / duration;
            yield return null;
        }

        if (!CanAdvanceManualLink(endPosition, endPosition, useFullBodyClearance))
        {
            _manualTraversalBlocked = true;
            yield break;
        }

        SetManualTraversalPosition(endPosition, Vector3.zero);
    }

    private bool CanAdvanceManualLink(Vector3 nextPosition, Vector3 endPosition, bool useFullBodyClearance)
    {
        if (_bodyClearance == null)
            _bodyClearance = GetComponent<SmilyWallClearanceController>();
        if (_bodyClearance == null)
            return true;

        Vector3 direction = endPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        bool useCoreClearance = _currentLinkIsDoor || !useFullBodyClearance;
        bool blocked = useCoreClearance
            ? _bodyClearance.HasCoreBlockerAt(nextPosition, direction)
            : _bodyClearance.HasBodyBlockerAt(nextPosition, direction);
        if (!blocked)
            return true;

        if (debugLog)
            Debug.LogWarning("[DoorAutoOpener] Body clearance blocked manual link traversal. Repathing instead of clipping through the link.", this);

        return false;
    }

    private Vector3 GetTraversalEndPosition(OffMeshLinkData linkData)
    {
        Vector3 endPosition = linkData.endPos + Vector3.up * _agent.baseOffset;
        if (preserveCurrentYDuringManualLink)
            endPosition.y = transform.position.y;
        return endPosition;
    }

    private static Vector3 GetLinkMidpoint(OffMeshLinkData linkData)
    {
        return (linkData.startPos + linkData.endPos) * 0.5f;
    }

    private bool HasActiveAgentOnNavMesh()
    {
        return _agent != null && _agent.enabled && _agent.isOnNavMesh;
    }

    private void ForceAnimatorSpeed(float value)
    {
        if (!forceAnimatorSpeedOnManualLink || _animator == null || string.IsNullOrEmpty(animatorSpeedParameter))
            return;

        _animator.SetFloat(animatorSpeedParameter, ConvertWorldSpeedToAnimatorValue(value));
    }

    private void UpdateAnimatorLocomotionSpeed()
    {
        if (_isTraversing || !syncAnimatorSpeedFromAgentVelocity || _agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        if (_animator == null || string.IsNullOrEmpty(animatorSpeedParameter))
            return;

        Vector3 velocity = _agent.velocity;
        float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float deadZone = Mathf.Max(0f, animatorStopDeadZone);
        bool shouldIdle = planarSpeed <= deadZone;

        if (!shouldIdle && _agent.hasPath && !_agent.pathPending)
        {
            float arrivalDistance = _agent.stoppingDistance + Mathf.Max(0f, animatorArrivalBuffer);
            if (_agent.remainingDistance <= arrivalDistance && _agent.desiredVelocity.sqrMagnitude <= deadZone * deadZone)
                shouldIdle = true;
        }

        float value = shouldIdle ? 0f : ConvertWorldSpeedToAnimatorValue(planarSpeed);
        _animator.SetFloat(animatorSpeedParameter, value, animatorSpeedDampTime, Time.deltaTime);
    }

    private float ConvertWorldSpeedToAnimatorValue(float worldSpeed)
    {
        if (!syncAnimatorSpeedFromAgentVelocity || !normalizeAnimatorSpeedToUnitRange)
            return worldSpeed;

        float referenceSpeed = Mathf.Max(0.1f, _agent != null ? _agent.speed : 1f);
        return Mathf.Clamp01(worldSpeed / referenceSpeed);
    }

    private Vector3 GetFrameVelocity(Vector3 from, Vector3 to)
    {
        return Time.deltaTime > 0f ? (to - from) / Time.deltaTime : Vector3.zero;
    }

    private void SetManualTraversalPosition(Vector3 position, Vector3 velocity)
    {
        transform.position = position;

        if (_agent == null || !_agent.enabled)
            return;

        _agent.nextPosition = position;
        _agent.velocity = velocity;
    }

    private void SyncAgentBeforeCompleteOffMeshLink(Vector3 finalPosition)
    {
        if (_agent == null || !_agent.enabled)
            return;

        transform.position = finalPosition;
        _agent.nextPosition = finalPosition;
        _agent.velocity = Vector3.zero;
    }

    private void SyncAgentAfterCompleteOffMeshLink(Vector3 finalPosition)
    {
        if (_agent == null || !_agent.enabled)
            return;

        transform.position = finalPosition;
        _agent.nextPosition = finalPosition;
        _agent.velocity = Vector3.zero;
    }

    private void RotateAlongLink(Vector3 endPosition)
    {
        Vector3 direction = endPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float angularSpeed = (_agent != null ? _agent.angularSpeed : 360f) * Mathf.Max(0f, manualLinkTurnSpeedMultiplier);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, angularSpeed * Time.deltaTime);
    }

    private void ExitDoorLinkAndRepath(bool hadPathBeforeTraverse, Vector3 destinationBeforeTraverse)
    {
        if (_agent == null || !_agent.enabled)
            return;

        if (_agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _agent.nextPosition = transform.position;
            _agent.updatePosition = true;
            _agent.isStopped = false;

            if (hadPathBeforeTraverse)
                _agent.SetDestination(destinationBeforeTraverse);
        }

        ResetTraversalState();
    }

    private void OnDisable()
    {
        if (_continuousDoorRoutine != null)
        {
            StopCoroutine(_continuousDoorRoutine);
            _continuousDoorRoutine = null;
        }

        if (_continuousDoorBinding != null)
            traversalGroup?.Release(_continuousDoorBinding, this);
        _continuousDoorBinding = null;

        if (_traverseRoutine != null)
        {
            StopCoroutine(_traverseRoutine);
            _traverseRoutine = null;
        }

        ResetTraversalState();
    }

    private void ResetTraversalState()
    {
        bool keepAgentStopped = _keepAgentStoppedAfterTraversal;

        _isTraversing = false;
        _isMovingAcrossLink = false;
        _isWaitingForDoor = false;
        _manualTraversalBlocked = false;
        _manualTraversalPaused = false;
        _finishManualTraversalAtEndRequested = false;
        _keepAgentStoppedAfterTraversal = false;
        _currentLinkIsDoor = false;
        CurrentTraversalEndPosition = transform.position;
        SetDoorTraversalBinding(null);

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.autoTraverseOffMeshLink = false;
            _agent.isStopped = keepAgentStopped;
            _agent.updatePosition = true;
        }

        ForceAnimatorSpeed(0f);
    }

    private void SetDoorTraversalBinding(MonsterDoorLinkBinding binding)
    {
        if (_bodyClearance == null)
            _bodyClearance = GetComponent<SmilyWallClearanceController>();

        if (_bodyClearance != null)
            _bodyClearance.SetDoorTraversalBinding(binding);
    }

    private void ResolveDoorArea()
    {
        _doorAreaIndex = NavMesh.GetAreaFromName(doorAreaName);

        if (_doorAreaIndex < 0 && debugLog)
            Debug.LogWarning($"[DoorAutoOpener] NavMesh area not found: {doorAreaName}", this);
    }

    private void ResolveJumpArea()
    {
        _jumpAreaIndex = NavMesh.GetAreaFromName(jumpAreaName);

        if (_jumpAreaIndex < 0 && debugLog && useJumpAreaTraversal)
            Debug.LogWarning($"[DoorAutoOpener] Jump NavMesh area not found: {jumpAreaName}", this);
    }

    private bool TryResolveCurrentLink(OffMeshLinkData linkData, out NavMeshLink link, out int area)
    {
        link = null;
        area = -1;

        NavMeshLink[] links = FindObjectsByType<NavMeshLink>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (links == null || links.Length == 0)
            return false;

        float bestScore = float.PositiveInfinity;
        NavMeshLink bestLink = null;

        for (int i = 0; i < links.Length; i++)
        {
            NavMeshLink candidate = links[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            Vector3 linkStart = candidate.transform.TransformPoint(candidate.startPoint);
            Vector3 linkEnd = candidate.transform.TransformPoint(candidate.endPoint);

            float scoreForward = Vector3.Distance(linkStart, linkData.startPos) + Vector3.Distance(linkEnd, linkData.endPos);
            float scoreReverse = Vector3.Distance(linkStart, linkData.endPos) + Vector3.Distance(linkEnd, linkData.startPos);
            float score = Mathf.Min(scoreForward, scoreReverse);

            if (candidate.agentTypeID == _agent.agentTypeID)
                score *= 0.5f;

            if (score < bestScore)
            {
                bestScore = score;
                bestLink = candidate;
            }
        }

        if (bestLink == null || bestScore > Mathf.Max(0.1f, linkMatchDistance))
            return false;

        link = bestLink;
        area = bestLink.area;
        return true;
    }

    private void LogWarpTrace(string phase)
    {
        if (!debugWarpTrace || _agent == null)
            return;

        Debug.Log($"[DoorWarp][f:{Time.frameCount}] {phase} pos={transform.position} next={_agent.nextPosition} vel={_agent.velocity} isStopped={_agent.isStopped} onLink={_agent.isOnOffMeshLink} hasPath={_agent.hasPath} pathStatus={_agent.pathStatus}", this);
    }
}

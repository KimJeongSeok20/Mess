using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class SmilyWallClearanceController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private JumpToPoint jumpToPoint;
    [SerializeField] private DoorAutoOpener doorAutoOpener;

    [Header("Body Probes")]
    [SerializeField] private LayerMask blockerMask = ~0;
    [SerializeField] private float frontDistance = 0.65f;
    [SerializeField] private float probeRadius = 0.34f;
    [SerializeField] private float probeHeight = 0.55f;
    [SerializeField] private float frontSideOffset = 0.36f;
    [SerializeField] private float bodyForwardOffset = 0.22f;
    [SerializeField] private float bodySideOffset = 0.44f;
    [SerializeField] private float minMoveSpeed = 0.08f;
    [SerializeField] private float stopCooldown = 0.2f;
    [SerializeField] private bool drawDebugProbes = true;

    private readonly Collider[] _overlapBuffer = new Collider[16];
    private readonly Vector3[] _debugProbeCenters = new Vector3[5];
    private float _nextStopTime;
    private bool _stoppedForClearance;
    private int _debugProbeCount;
    private bool _debugLastBlocked;
    private MonsterDoorLinkBinding _doorTraversalBinding;

    private void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        if (_stoppedForClearance && Time.time >= _nextStopTime)
        {
            agent.isStopped = false;
            _stoppedForClearance = false;
        }

        if (jumpToPoint != null && jumpToPoint.IsJumping)
            return;

        if (agent.isOnOffMeshLink)
            return;

        if (doorAutoOpener != null && doorAutoOpener.IsTraversing)
            return;

        if (Time.time < _nextStopTime)
            return;

        if (!agent.hasPath || agent.velocity.sqrMagnitude < minMoveSpeed * minMoveSpeed)
            return;

        Vector3 moveDirection = agent.desiredVelocity.sqrMagnitude > 0.0001f
            ? agent.desiredVelocity
            : agent.steeringTarget - transform.position;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude <= 0.0001f)
            return;

        moveDirection.Normalize();
        bool includeSideProbes = doorAutoOpener == null || !doorAutoOpener.ShouldRelaxBodyClearanceForDoor;
        if (!HasNavigationBlocker(transform.position, moveDirection, includeSideProbes))
            return;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        _stoppedForClearance = true;
        _nextStopTime = Time.time + stopCooldown;
    }

    public bool HasBodyBlockerAt(Vector3 rootPosition, Vector3 moveDirection)
    {
        return HasBodyBlocker(rootPosition, moveDirection, true);
    }

    public bool HasCoreBlockerAt(Vector3 rootPosition, Vector3 moveDirection)
    {
        return HasBodyBlocker(rootPosition, moveDirection, false);
    }

    public void SetDoorTraversalBinding(MonsterDoorLinkBinding binding)
    {
        _doorTraversalBinding = binding;
    }

    private bool HasNavigationBlocker(Vector3 rootPosition, Vector3 moveDirection, bool includeSideProbes)
    {
        return HasBodyBlocker(rootPosition, moveDirection, includeSideProbes, true);
    }

    private bool HasBodyBlocker(Vector3 rootPosition, Vector3 moveDirection, bool includeSideProbes)
    {
        return HasBodyBlocker(rootPosition, moveDirection, includeSideProbes, false);
    }

    private bool HasBodyBlocker(Vector3 rootPosition, Vector3 moveDirection, bool includeSideProbes, bool navigationMode)
    {
        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude <= 0.0001f)
            moveDirection = transform.forward;

        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return false;

        moveDirection.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;
        Vector3 basePosition = rootPosition + Vector3.up * probeHeight;

        _debugProbeCount = 0;
        _debugLastBlocked = false;

        bool frontBlocked = ProbeBlocked(basePosition + moveDirection * frontDistance);
        if (!includeSideProbes)
            return frontBlocked;

        bool frontRightBlocked = ProbeBlocked(basePosition + moveDirection * frontDistance + right * frontSideOffset);
        bool frontLeftBlocked = ProbeBlocked(basePosition + moveDirection * frontDistance - right * frontSideOffset);
        bool bodyRightBlocked = ProbeBlocked(basePosition + moveDirection * bodyForwardOffset + right * bodySideOffset);
        bool bodyLeftBlocked = ProbeBlocked(basePosition + moveDirection * bodyForwardOffset - right * bodySideOffset);

        if (!navigationMode)
            return frontBlocked || frontRightBlocked || frontLeftBlocked || bodyRightBlocked || bodyLeftBlocked;

        if (frontBlocked && frontRightBlocked && frontLeftBlocked)
            return true;

        return bodyRightBlocked && bodyLeftBlocked;
    }

    private bool ProbeBlocked(Vector3 center)
    {
        if (_debugProbeCount < _debugProbeCenters.Length)
            _debugProbeCenters[_debugProbeCount++] = center;

        int mask = blockerMask.value == 0 ? ~0 : blockerMask.value;
        int count = Physics.OverlapSphereNonAlloc(center, probeRadius, _overlapBuffer, mask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _overlapBuffer[i];
            if (hit == null)
                continue;

            Transform hitTransform = hit.transform;
            if (hitTransform.root == transform.root)
                continue;

            if (IsIgnoredBlocker(hit))
                continue;

            _debugLastBlocked = true;
            return true;
        }

        return false;
    }

    private bool IsIgnoredBlocker(Collider hit)
    {
        if (hit == null)
            return true;

        if (hit.isTrigger)
            return true;

        if (hit.GetComponentInParent<PlayerPawn>() != null)
            return true;

        if (_doorTraversalBinding != null && _doorTraversalBinding.IsOpenPassageCollider(hit))
            return true;

        if (hit.GetComponent<MonsterDoorLinkBinding>() != null)
            return true;

        if (hit.GetComponent<DoorAutoOpener>() != null)
            return true;

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugProbes)
            return;

        Vector3 direction = Application.isPlaying && agent != null && agent.desiredVelocity.sqrMagnitude > 0.0001f
            ? agent.desiredVelocity
            : transform.forward;

        if (!Application.isPlaying)
            HasBodyBlocker(transform.position, direction, true);

        Gizmos.color = _debugLastBlocked ? Color.red : Color.cyan;
        for (int i = 0; i < _debugProbeCount; i++)
            Gizmos.DrawWireSphere(_debugProbeCenters[i], probeRadius);
    }
}

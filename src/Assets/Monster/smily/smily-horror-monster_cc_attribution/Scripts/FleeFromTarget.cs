using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class FleeFromTarget : MonoBehaviour
{
    public NavMeshAgent agent;

    [Header("Flee Settings")]
    public float fleeRadius = 20f;
    public int sampleCount = 30;
    public float minDistFromTarget = 5f;
    public float fleeDuration = 3.5f;
    [SerializeField] private float fallbackFleeDistance = 6f;
    [SerializeField] private float navMeshSampleRadius = 3f;
    [SerializeField] private float minFleeProgressDistance = 1.5f;
    [SerializeField, Range(-1f, 1f)] private float minAwayDirectionDot = 0.15f;

    [Header("Door Detection")]
    public float doorCheckDistance = 1.5f;
    public float doorCheckRadius = 0.35f;
    public LayerMask doorMask;
    public float doorOpenDelay = 0.5f;

    [Header("Re-Aggro Cooldown")]
    [SerializeField] private float reAggroCooldown = 5f;

    private Transform _target;
    private bool _isFleeing;
    private bool _isOpeningDoor;
    private float _timer;
    private float _fleeEndsAtRealtime;
    private float _lastFleeEndTime = float.NegativeInfinity;
    private Vector3 _lastFleePoint;

    public bool IsFleeing => _isFleeing || _isOpeningDoor;
    public bool IsInReAggroCooldown => !float.IsNegativeInfinity(_lastFleeEndTime)
        && Time.time - _lastFleeEndTime < reAggroCooldown;
    public Vector3 LastFleePoint => _lastFleePoint;

    private void Awake()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
    }

    private void OnValidate()
    {
        if (fleeDuration < 0.1f)
            fleeDuration = 0.1f;

        if (reAggroCooldown < 0f)
            reAggroCooldown = 0f;

        if (minFleeProgressDistance < 0f)
            minFleeProgressDistance = 0f;
    }

    public void StartFlee(Transform target)
    {
        if (agent == null || target == null)
            return;

        _target = target;
        bool activeOnNavMesh = HasActiveAgentOnNavMesh();

        if (!activeOnNavMesh || !TryGetBestFleePoint(out Vector3 fleePoint))
            TryGetFallbackFleePoint(target, out fleePoint);

        _lastFleePoint = fleePoint;
        bool destinationQueued = false;
        if (activeOnNavMesh)
        {
            agent.isStopped = false;
            destinationQueued = agent.SetDestination(fleePoint);
        }

        _isFleeing = true;
        _isOpeningDoor = false;
        _timer = 0f;
        _fleeEndsAtRealtime = Time.realtimeSinceStartup + Mathf.Max(0.1f, fleeDuration);

        if (!destinationQueued)
            Debug.LogWarning("[FleeFromTarget] Flee started without a valid NavMesh destination. Locomotion hold will cover the visual state.", this);
    }

    private void Update()
    {
        if (!_isFleeing)
            return;

        _timer = Mathf.Max(0f, Time.realtimeSinceStartup - (_fleeEndsAtRealtime - Mathf.Max(0.1f, fleeDuration)));
        if (Time.realtimeSinceStartup >= _fleeEndsAtRealtime && !_isOpeningDoor)
        {
            _isFleeing = false;
            _lastFleeEndTime = Time.time;
            return;
        }

        if (!_isOpeningDoor)
            CheckFrontDoor();
    }

    private bool TryGetBestFleePoint(out Vector3 best)
    {
        best = transform.position;
        if (!HasActiveAgentOnNavMesh() || _target == null)
            return false;

        NavMeshPath path = new NavMeshPath();
        float bestScore = float.NegativeInfinity;
        bool found = false;
        Vector3 targetPos = _target.position;
        Vector3 currentPosition = transform.position;
        Vector3 awayDirection = Flatten(currentPosition - targetPos);
        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = Flatten(transform.forward);

        if (awayDirection.sqrMagnitude <= 0.0001f)
            awayDirection = Vector3.forward;

        awayDirection.Normalize();
        float currentDistanceFromTarget = HorizontalDistance(currentPosition, targetPos);
        float requiredDistanceFromTarget = Mathf.Max(minDistFromTarget, currentDistanceFromTarget + minFleeProgressDistance);

        for (int i = 0; i < Mathf.Max(1, sampleCount); i++)
        {
            Vector3 random = BuildFleeCandidate(currentPosition, awayDirection, i);

            if (!NavMesh.SamplePosition(random, out NavMeshHit hit, Mathf.Max(0.1f, navMeshSampleRadius), agent.areaMask))
                continue;

            Vector3 candidate = hit.position;
            Vector3 firstMove = Flatten(candidate - currentPosition);
            if (firstMove.sqrMagnitude <= 0.0001f)
                continue;

            float candidateAwayDot = Vector3.Dot(firstMove.normalized, awayDirection);
            if (candidateAwayDot < minAwayDirectionDot)
                continue;

            if (!agent.CalculatePath(candidate, path))
                continue;

            if (path.status != NavMeshPathStatus.PathComplete)
                continue;

            if (!PathStartsAwayFromTarget(path, awayDirection, currentPosition))
                continue;

            float distToTarget = HorizontalDistance(candidate, targetPos);
            if (distToTarget < requiredDistanceFromTarget)
                continue;

            float pathLength = GetPathLength(path);
            float score = (distToTarget - currentDistanceFromTarget) * 4f + candidateAwayDot * 3f - pathLength * 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetFallbackFleePoint(Transform target, out Vector3 fleePoint)
    {
        fleePoint = transform.position;

        Vector3 direction = target != null ? transform.position - target.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        direction.Normalize();
        float distance = Mathf.Max(1f, fallbackFleeDistance);
        float sampleRadius = Mathf.Max(0.1f, navMeshSampleRadius);

        for (int i = 0; i < 8; i++)
        {
            float angle = i == 0 ? 0f : (i % 2 == 0 ? 35f : -35f) * ((i + 1) / 2);
            Vector3 rotated = Quaternion.AngleAxis(angle, Vector3.up) * direction;
            Vector3 candidate = transform.position + rotated * distance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, agent != null ? agent.areaMask : NavMesh.AllAreas))
                continue;

            fleePoint = hit.position;
            return true;
        }

        fleePoint = transform.position + direction * distance;
        return false;
    }

    private Vector3 BuildFleeCandidate(Vector3 currentPosition, Vector3 awayDirection, int index)
    {
        float distance = Random.Range(Mathf.Max(1f, minDistFromTarget), Mathf.Max(minDistFromTarget + 0.1f, fleeRadius));
        if (index % 3 != 0)
        {
            float angle = Random.Range(-80f, 80f);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * awayDirection;
            return currentPosition + direction.normalized * distance;
        }

        Vector3 random = currentPosition + Random.insideUnitSphere * Mathf.Max(1f, fleeRadius);
        random.y = currentPosition.y;
        return random;
    }

    private bool PathStartsAwayFromTarget(NavMeshPath path, Vector3 awayDirection, Vector3 currentPosition)
    {
        if (path == null || path.corners == null || path.corners.Length == 0)
            return false;

        Vector3 firstCorner = path.corners.Length > 1 ? path.corners[1] : path.corners[0];
        Vector3 firstStep = Flatten(firstCorner - currentPosition);
        if (firstStep.sqrMagnitude <= 0.0001f && path.corners.Length > 0)
            firstStep = Flatten(path.corners[path.corners.Length - 1] - currentPosition);

        return firstStep.sqrMagnitude > 0.0001f
            && Vector3.Dot(firstStep.normalized, awayDirection) >= minAwayDirectionDot;
    }

    private static float GetPathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return 0f;

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return length;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Flatten(a - b).magnitude;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private void CheckFrontDoor()
    {
        if (!HasActiveAgentOnNavMesh() || !agent.hasPath || agent.desiredVelocity.sqrMagnitude < 0.001f)
            return;

        Vector3 dir = agent.desiredVelocity.normalized;

        if (!Physics.SphereCast(
                transform.position + Vector3.up * 0.5f,
                doorCheckRadius,
                dir,
                out RaycastHit hit,
                doorCheckDistance,
                doorMask))
            return;

        DoorAnimatorOpener doorOpener = hit.collider.GetComponentInParent<DoorAnimatorOpener>();
        if (doorOpener == null)
            return;

        if (!IsDoorOnMyPath(doorOpener.transform.position))
            return;

        if (doorOpener.IsOpen)
            return;

        StartCoroutine(OpenDoorRoutine(doorOpener));
    }

    private bool IsDoorOnMyPath(Vector3 doorPos)
    {
        if (!HasActiveAgentOnNavMesh() || !agent.hasPath)
            return false;

        Vector3 moveDir = agent.desiredVelocity.normalized;
        Vector3 toDoor = (doorPos - transform.position).normalized;

        float dot = Vector3.Dot(moveDir, toDoor);
        if (dot < 0.6f)
            return false;

        Vector3 nextCorner = GetNextPathCorner();
        float distToDoor = Vector3.Distance(transform.position, doorPos);
        float distToNext = Vector3.Distance(transform.position, nextCorner);

        return distToDoor < distToNext + 0.5f;
    }

    private Vector3 GetNextPathCorner()
    {
        if (!HasActiveAgentOnNavMesh())
            return transform.position;

        if (agent.path.corners.Length > 1)
            return agent.path.corners[1];

        return agent.destination;
    }

    private IEnumerator OpenDoorRoutine(DoorAnimatorOpener door)
    {
        _isOpeningDoor = true;
        if (HasActiveAgentOnNavMesh())
            agent.isStopped = true;

        yield return new WaitForSeconds(doorOpenDelay);

        if (door != null)
            door.OpenDoor();

        if (HasActiveAgentOnNavMesh())
            agent.isStopped = false;
        _isOpeningDoor = false;
    }

    private bool HasActiveAgentOnNavMesh()
    {
        return agent != null && agent.enabled && agent.isOnNavMesh;
    }
}

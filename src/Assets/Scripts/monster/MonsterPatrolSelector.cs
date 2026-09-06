using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class MonsterPatrolSelector : MonoBehaviour
{
    [Header("Patrol Selection")]
    [SerializeField] private float searchRadius = 20f;
    [SerializeField] private int avoidRecentCount = 2;      // 0~2 추천
    [SerializeField] private int pathCheckTries = 6;        // 경로검사 시도 횟수
    [SerializeField] private bool requirePathComplete = true;
    [SerializeField] private bool usePatrolPoints = true;
    [SerializeField] private float minPatrolDistance = 2f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private float minEdgeClearance = 0.35f;
    [Header("Path Quality")]
    [SerializeField] private float maxPathLength = 40f;
    [SerializeField] private float maxPathDistanceRatio = 2.5f;
    [SerializeField] private float maxVerticalDelta = 2f;
    [Header("Stuck Detection")]
    [SerializeField] private float stuckCheckInterval = 0.5f;
    [SerializeField] private float stuckTimeout = 2f;
    [SerializeField] private float stuckMinMoveDistance = 0.2f;
    [SerializeField] private float stuckArrivalThreshold = 0.6f;
    [Header("Warp Recovery")]
    [SerializeField] private float warpMaxDistance = 3f;
    [SerializeField] private bool logWarp;

    private NavMeshAgent _agent;
    private readonly List<Transform> _candidates = new(128);

    private Transform _last1;
    private Transform _last2;
    private Transform _fallbackPoint;
    private bool _patrolTracking;
    private float _nextStuckCheckTime;
    private float _stuckTimer;
    private Vector3 _lastPosition;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        var fallbackGO = new GameObject("NavMeshFallbackPoint");
        fallbackGO.hideFlags = HideFlags.HideInHierarchy;
        _fallbackPoint = fallbackGO.transform;
        _fallbackPoint.position = transform.position;
    }

    private void OnDestroy()
    {
        if (_fallbackPoint != null)
            Destroy(_fallbackPoint.gameObject);
    }

    // ✅ (추가) PatrolPoint "오브젝트(Transform)"를 반환
    public bool TryPickNextPoint(out Transform point)
    {
        point = null;

        if (_agent == null)
            return false;

        if (!_agent.enabled)
            return false;

        if (!_agent.isOnNavMesh)
        {
            TryWarpToNavMesh(transform.position, "pick-start");
            if (!_agent.isOnNavMesh)
                return false;
        }

        if (!usePatrolPoints)
            return TryPickFallbackAndTrack(out point);

        var all = DungeonPointRegistry.PatrolPoints;
        if (all == null || all.Count == 0)
        {
            // Fallback: Use NavMesh random patrol when PatrolPoints empty
            return TryPickFallbackAndTrack(out point);
        }

        float r2 = searchRadius * searchRadius;
        _candidates.Clear();

        // 1) 반경 내 후보
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            if (IsAvoid(t)) continue;

            var d = t.position - transform.position;
            if (d.sqrMagnitude <= r2)
                _candidates.Add(t);
        }

        // 2) 반경 내 없으면 전체 백업(avoid 적용)
        if (_candidates.Count == 0)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (IsAvoid(t)) continue;
                _candidates.Add(t);
            }
        }

        // 3) 그래도 없으면 avoid 완화
        if (_candidates.Count == 0)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                _candidates.Add(t);
            }
        }

        if (_candidates.Count == 0)
            return TryPickFallbackAndTrack(out point);

        // 4) 랜덤 선택 + (선택) 경로 유효성 검사
        int tries = Mathf.Min(pathCheckTries, _candidates.Count);
        for (int k = 0; k < tries; k++)
        {
            int idx = Random.Range(0, _candidates.Count);
            var pick = _candidates[idx];
            _candidates.RemoveAt(idx);

            if (pick == null) continue;

            if (!TryGetReachablePoint(pick.position, out var navPoint))
                continue;

            _fallbackPoint.position = navPoint;
            PushHistory(pick);
            return TryPickWithTracking(out point);
        }

        return TryPickFallbackAndTrack(out point);
    }

    private bool IsAvoid(Transform t)
    {
        if (avoidRecentCount <= 0) return false;
        if (avoidRecentCount >= 1 && t == _last1) return true;
        if (avoidRecentCount >= 2 && t == _last2) return true;
        return false;
    }

    private void PushHistory(Transform t)
    {
        _last2 = _last1;
        _last1 = t;
    }

    private bool TryGetNavMeshFallbackPoint(out Transform point)
    {
        point = null;

        if (_agent == null || _fallbackPoint == null)
            return false;

        // Loop up to pathCheckTries attempts to find valid NavMesh point
        for (int k = 0; k < pathCheckTries; k++)
        {
            // Generate random offset in horizontal plane
            Vector3 offset = Random.insideUnitSphere * searchRadius;
            offset.y = 0f;

            Vector3 candidate = transform.position + offset;

            if (!TryGetReachablePoint(candidate, out var navPoint))
                continue;

            _fallbackPoint.position = navPoint;
            PushHistory(_fallbackPoint);
            point = _fallbackPoint;
            return true;
        }

        // All attempts failed
        return false;
    }

    private bool TryPickWithTracking(out Transform point)
    {
        point = _fallbackPoint;
        _patrolTracking = true;
        _stuckTimer = 0f;
        _lastPosition = transform.position;
        _nextStuckCheckTime = Time.time + stuckCheckInterval;
        return point != null;
    }

    private bool TryPickFallbackAndTrack(out Transform point)
    {
        if (!TryGetNavMeshFallbackPoint(out point))
            return false;

        return TryPickWithTracking(out point);
    }

    private void Update()
    {
        if (!_patrolTracking || _agent == null || _fallbackPoint == null)
            return;

        if (!_agent.enabled || !_agent.isOnNavMesh)
        {
            if (_agent != null && _agent.enabled)
                TryWarpToNavMesh(transform.position, "off-navmesh");
            return;
        }

        if (_agent.pathPending)
            return;

        float destinationDelta = Vector3.Distance(_agent.destination, _fallbackPoint.position);
        if (destinationDelta > 0.2f)
        {
            _patrolTracking = false;
            return;
        }

        float remaining = _agent.remainingDistance;
        float arrivalThreshold = Mathf.Max(_agent.stoppingDistance, stuckArrivalThreshold);
        float targetDistance = Vector3.Distance(transform.position, _fallbackPoint.position);
        if (targetDistance <= arrivalThreshold)
        {
            _stuckTimer = 0f;
            _lastPosition = transform.position;
            return;
        }

        if (Time.time < _nextStuckCheckTime)
            return;

        bool pathInvalid = !_agent.hasPath || _agent.pathStatus != NavMeshPathStatus.PathComplete;
        bool distanceMismatch = remaining <= arrivalThreshold && targetDistance > arrivalThreshold;

        float minMoveSqr = stuckMinMoveDistance * stuckMinMoveDistance;
        float movedSqr = (transform.position - _lastPosition).sqrMagnitude;
        bool lowMotion = movedSqr < minMoveSqr
                         && _agent.velocity.sqrMagnitude < 0.01f
                         && _agent.desiredVelocity.sqrMagnitude < 0.01f;

        if (pathInvalid || distanceMismatch || lowMotion)
            _stuckTimer += stuckCheckInterval;
        else
            _stuckTimer = 0f;

        _lastPosition = transform.position;
        _nextStuckCheckTime = Time.time + stuckCheckInterval;

        if (_stuckTimer >= stuckTimeout)
        {
            if (TryGetNavMeshFallbackPoint(out var point))
            {
                _agent.ResetPath();
                _agent.isStopped = false;
                _agent.SetDestination(point.position);
            }
            _stuckTimer = 0f;
        }
    }

    private bool TryWarpToNavMesh(Vector3 origin, string reason)
    {
        if (_agent == null || !_agent.enabled)
            return false;

        float sampleRadius = Mathf.Max(navMeshSampleRadius, _agent.radius * 2f);
        if (!NavMesh.SamplePosition(origin, out NavMeshHit snap, sampleRadius, _agent.areaMask))
        {
            if (logWarp)
                Debug.LogWarning($"[MonsterPatrolSelector] Warp failed (no navmesh) reason={reason}", this);
            return false;
        }

        if (maxVerticalDelta > 0f && Mathf.Abs(snap.position.y - transform.position.y) > maxVerticalDelta)
            return false;

        if (warpMaxDistance > 0f && Vector3.Distance(transform.position, snap.position) > warpMaxDistance)
            return false;

        bool warped = _agent.Warp(snap.position);
        if (warped)
        {
            _agent.isStopped = false;
            _stuckTimer = 0f;
            _lastPosition = transform.position;
            _nextStuckCheckTime = Time.time + stuckCheckInterval;
        }

        if (logWarp)
            Debug.Log($"[MonsterPatrolSelector] Warp {(warped ? "OK" : "FAILED")} reason={reason} from={transform.position} to={snap.position}", this);

        return warped;
    }

    [ContextMenu("Debug/Warp To Nearest NavMesh")]
    private void DebugWarpToNearestNavMesh()
    {
        if (_agent == null)
            _agent = GetComponent<NavMeshAgent>();

        TryWarpToNavMesh(transform.position, "debug");
    }

    [ContextMenu("Debug/Warp To Random NavMesh Point")]
    private void DebugWarpToRandomNavMeshPoint()
    {
        if (_agent == null)
            _agent = GetComponent<NavMeshAgent>();

        if (TryGetNavMeshFallbackPoint(out var point))
            TryWarpToNavMesh(point.position, "debug-random");
    }

    private bool TryGetReachablePoint(Vector3 desired, out Vector3 point)
    {
        point = default;
        if (_agent == null || !_agent.enabled)
            return false;

        if (!_agent.isOnNavMesh)
        {
            if (!TryWarpToNavMesh(transform.position, "path-check"))
                return false;
            if (!_agent.isOnNavMesh)
                return false;
        }

        float sampleRadius = Mathf.Max(navMeshSampleRadius, _agent.radius * 2f);
        if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, sampleRadius, _agent.areaMask))
            return false;

        if (minPatrolDistance > 0f)
        {
            float minDist = minPatrolDistance * minPatrolDistance;
            if ((hit.position - transform.position).sqrMagnitude < minDist)
                return false;
        }

        if (minEdgeClearance > 0f)
        {
            if (NavMesh.FindClosestEdge(hit.position, out NavMeshHit edge, _agent.areaMask))
            {
                if (edge.distance < minEdgeClearance)
                    return false;
            }
        }

        if (maxVerticalDelta > 0f)
        {
            if (Mathf.Abs(hit.position.y - transform.position.y) > maxVerticalDelta)
                return false;
        }

        bool needsPathCheck = requirePathComplete || maxPathLength > 0f || maxPathDistanceRatio > 0f;
        if (needsPathCheck)
        {
            var path = new NavMeshPath();
            if (!_agent.CalculatePath(hit.position, path))
                return false;
            if (path.status != NavMeshPathStatus.PathComplete)
                return false;

            float pathLength = CalculatePathLength(path);
            if (maxPathLength > 0f && pathLength > maxPathLength)
                return false;

            float directDistance = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(hit.position.x, 0f, hit.position.z));
            if (maxPathDistanceRatio > 0f && directDistance > 0.1f && pathLength > directDistance * maxPathDistanceRatio)
                return false;
        }

        point = hit.position;
        return true;
    }

    private static float CalculatePathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return 0f;

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return length;
    }
}

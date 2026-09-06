using UnityEngine;
using UnityEngine.AI;

public class JumpToPoint : MonoBehaviour
{
[Header("Default Jump Settings")]
    [SerializeField] private float defaultDuration = 0.4f;
    [SerializeField] private float defaultHeight = 1.5f;
    [SerializeField] private bool showDebug = false;

    [SerializeField] private float landingNavMeshSampleRadius = 0.5f;
    [SerializeField] private bool preserveStartHeightAboveNavMesh = true;
    [SerializeField] private float landingYOffset;
    [SerializeField] private float turnSpeed = 720f;
    [SerializeField] private float sameTargetAttackCooldown = 3f;

    public bool IsJumping { get; private set; }

    private Vector3 _startPos;
    private Vector3 _targetPos;
    private float _duration;
    private float _height;
    private float _elapsed;

    private NavMeshAgent _agent;
    private SmilyAttackState _attackState;
    private bool _wasStoppedBeforeJump;
    private bool _hadAgentBeforeJump;
    private bool _hasLookTarget;
    private Vector3 _lookTarget;
    private GameObject _lastAttackTarget;
    private float _lastAttackStartedAt = -999f;
    private bool _reportedMissingAttackState;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _attackState = GetComponent<SmilyAttackState>();
        if (_attackState == null)
            ReportMissingAttackState();
    }

    public void StartJump(Vector3 targetPoint, float duration, float height)
    {
        StartJump(targetPoint, duration, height, targetPoint);
    }

    public void StartJump(Vector3 targetPoint, float duration, float height, Vector3 lookTarget)
    {
        TryStartJump(targetPoint, duration, height, lookTarget, null);
    }

    public bool TryStartJump(Vector3 targetPoint, float duration, float height, Vector3 lookTarget, GameObject attackTarget)
    {
        return TryStartJump(targetPoint, duration, height, lookTarget, attackTarget, false);
    }

    public bool TryStartJump(Vector3 targetPoint, float duration, float height, Vector3 lookTarget, GameObject attackTarget, bool ignoreSameTargetCooldown)
    {
        if (!CanStartAttack(attackTarget, ignoreSameTargetCooldown))
            return false;

        if (_attackState == null)
        {
            _attackState = GetComponent<SmilyAttackState>();
            if (_attackState == null)
            {
                ReportMissingAttackState();
                return false;
            }
        }

        BeginJump(targetPoint, duration, height, lookTarget, attackTarget);
        return true;
    }

    public bool CanStartAttack(GameObject attackTarget)
    {
        return CanStartAttack(attackTarget, false);
    }

    public bool CanStartAttack(GameObject attackTarget, bool ignoreSameTargetCooldown)
    {
        if (IsJumping)
            return false;

        if (ignoreSameTargetCooldown)
            return true;

        if (attackTarget == null || attackTarget != _lastAttackTarget)
            return true;

        return Time.time - _lastAttackStartedAt >= sameTargetAttackCooldown;
    }

    private void BeginJump(Vector3 targetPoint, float duration, float height, Vector3 lookTarget, GameObject attackTarget)
    {
        _startPos = transform.position;
        _targetPos = ResolveLandingPosition(targetPoint, _startPos);
        _lookTarget = lookTarget;
        _hasLookTarget = true;
        _lastAttackTarget = attackTarget;
        _lastAttackStartedAt = Time.time;

        _duration = duration > 0f ? duration : defaultDuration;
        _height = Mathf.Abs(height) > 0f ? height : defaultHeight;

        _elapsed = 0f;
        IsJumping = true;
        FaceLookTarget(true);

        if (_agent != null)
        {
            _hadAgentBeforeJump = _agent.enabled;
            bool activeOnNavMesh = _agent.enabled && _agent.isOnNavMesh;
            _wasStoppedBeforeJump = activeOnNavMesh && _agent.isStopped;
            _agent.updatePosition = false;

            if (activeOnNavMesh)
                _agent.isStopped = true;
        }
    }

    private void Update()
    {
        if (!IsJumping)
            return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);

        FaceLookTarget(false);

        Vector3 pos = Vector3.Lerp(_startPos, _targetPos, t);
        pos.y += Mathf.Sin(Mathf.PI * t) * _height;
        transform.position = pos;

        if (t < 1f)
            return;

        Vector3 finalPos = ResolveLandingPosition(_targetPos, _startPos);
        transform.position = finalPos;

        if (_agent != null)
        {
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                _agent.Warp(finalPos);
                _agent.nextPosition = finalPos;
            }

            _agent.updatePosition = true;

            if (_agent.enabled && _agent.isOnNavMesh)
                _agent.isStopped = _wasStoppedBeforeJump;
        }

        IsJumping = false;
        _hasLookTarget = false;
        _attackState?.EndAttackWindow("JumpComplete");
    }

    private void OnDisable()
    {
        _attackState?.EndAttackWindow("JumpToPointDisabled");
    }

    public void CancelJump()
    {
        IsJumping = false;
        _hasLookTarget = false;
        _attackState?.EndAttackWindow("JumpCanceled");

        if (_agent != null && _agent.enabled)
        {
            _agent.updatePosition = true;

            if (_hadAgentBeforeJump && _agent.isOnNavMesh)
                _agent.isStopped = _wasStoppedBeforeJump;
        }
    }

    private void FaceLookTarget(bool instant)
    {
        if (!_hasLookTarget)
            return;

        Vector3 toTarget = _lookTarget - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = instant
            ? targetRotation
            : Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    private Vector3 ResolveLandingPosition(Vector3 targetPosition, Vector3 startPosition)
    {
        int areaMask = _agent != null ? _agent.areaMask : NavMesh.AllAreas;
        float sampleRadius = Mathf.Max(0.05f, landingNavMeshSampleRadius);
        Vector3 landingPosition = NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, sampleRadius, areaMask)
            ? hit.position
            : targetPosition;

        landingPosition.y += ResolveRootHeightAboveNavMesh(startPosition);
        return landingPosition;
    }

    private float ResolveRootHeightAboveNavMesh(Vector3 rootPosition)
    {
        float rootHeight = 0f;
        if (preserveStartHeightAboveNavMesh && TryResolveRootHeightAboveNavMesh(rootPosition, out rootHeight) && rootHeight > 0.001f)
            return rootHeight + landingYOffset;

        return EstimateAgentRootHeightAboveNavMesh() + landingYOffset;
    }

    private bool TryResolveRootHeightAboveNavMesh(Vector3 rootPosition, out float height)
    {
        int areaMask = _agent != null ? _agent.areaMask : NavMesh.AllAreas;
        float sampleRadius = Mathf.Max(landingNavMeshSampleRadius, EstimateAgentRootHeightAboveNavMesh() + 0.25f);
        if (NavMesh.SamplePosition(rootPosition, out NavMeshHit hit, sampleRadius, areaMask))
        {
            height = rootPosition.y - hit.position.y;
            return true;
        }

        height = 0f;
        return false;
    }

    private float EstimateAgentRootHeightAboveNavMesh()
    {
        if (_agent == null)
            return 0f;

        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        return Mathf.Max(0f, _agent.baseOffset) * scaleY;
    }

    private void ReportMissingAttackState()
    {
        if (_reportedMissingAttackState)
            return;

        _reportedMissingAttackState = true;
        Debug.LogError("[JumpToPoint] Missing SmilyAttackState. Fix the Smily prefab/setup; runtime auto-attach is disabled.", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebug || !IsJumping)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(_targetPos, 0.25f);
    }
}

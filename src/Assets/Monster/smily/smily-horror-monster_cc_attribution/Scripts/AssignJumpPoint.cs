using UnityEngine;
using UnityEngine.AI;

public class AssignJumpPoint : MonoBehaviour
{
    [Header("Jump Point Settings")]
    [SerializeField] private float forwardOffset = 2f;
    [SerializeField] private float heightOffset = 1f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private float maxVerticalDelta = 2.5f;
    [SerializeField] private bool requireReachableLanding = true;
    [SerializeField] private bool showDebugGizmos = true;

    public Vector3 LastJumpPoint { get; private set; }

    public Vector3 ComputeJumpPoint(GameObject target)
    {
        if (TryComputeJumpPoint(target, out Vector3 jumpPoint))
            return jumpPoint;

        LastJumpPoint = transform.position;
        return LastJumpPoint;
    }

    public bool TryComputeJumpPoint(GameObject target, out Vector3 jumpPoint)
    {
        if (target == null)
        {
            jumpPoint = transform.position;
            LastJumpPoint = jumpPoint;
            return false;
        }

        Transform targetTransform = target.transform;
        Vector3 fallbackJumpPoint = targetTransform.position + targetTransform.forward * forwardOffset;
        Vector3 rawJumpPoint = fallbackJumpPoint + Vector3.up * heightOffset;

        jumpPoint = fallbackJumpPoint;

        if (NavMesh.SamplePosition(rawJumpPoint, out NavMeshHit hit, navMeshSampleRadius, ResolveAreaMask()))
        {
            float verticalDelta = Mathf.Abs(hit.position.y - transform.position.y);
            if (verticalDelta <= Mathf.Max(0f, maxVerticalDelta) && HasReachableLanding(hit.position))
            {
                jumpPoint = hit.position;
                LastJumpPoint = jumpPoint;
                return true;
            }

            if (requireReachableLanding)
                return false;
        }
        else if (requireReachableLanding)
        {
            return false;
        }

        LastJumpPoint = jumpPoint;
        return true;
    }

    private bool HasReachableLanding(Vector3 landing)
    {
        if (!requireReachableLanding)
            return true;

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, landing, ResolveAreaMask(), path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    private int ResolveAreaMask()
    {
        var agent = GetComponent<NavMeshAgent>();
        return agent != null ? agent.areaMask : NavMesh.AllAreas;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(LastJumpPoint, 0.25f);
    }
}

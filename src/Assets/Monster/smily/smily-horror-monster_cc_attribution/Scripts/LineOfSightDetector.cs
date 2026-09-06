using UnityEngine;

public class LineOfSightDetector : MonoBehaviour
{
    [Header("Vision Origin")]
    [SerializeField]
    private Transform m_visionOrigin;
    [SerializeField]
    private Vector3 m_localVisionOffset = new Vector3(0f, 0.8f, 0.45f);

    [Header("Detection Settings")]
    [SerializeField]
    private LayerMask m_playerLayerMask;
    [SerializeField]
    private LayerMask m_visibilityMask = ~0;
    [SerializeField]
    private float m_detectionRange = 10.0f;
    [SerializeField]
    private float m_detectionHeight = 3f;
    [SerializeField, Range(0f, 180f)]
    private float m_halfViewAngle = 60f;
    [SerializeField] private float m_closeRangeAlwaysVisible = 1.75f;

    [SerializeField] private bool showDebugVisuals = true;

    private readonly RaycastHit[] _raycastHits = new RaycastHit[12];

    public Vector3 SightOrigin => ResolveSightOrigin();
    public Vector3 SightForward => ResolveSightForward();

    public GameObject PerformDetection(GameObject potentialTarget)
    {
        if (potentialTarget == null)
            return null;

        GameObject targetRoot = ResolveTargetRoot(potentialTarget);
        if (targetRoot == null)
            return null;

        Vector3 targetPos = ResolveTargetAimPosition(targetRoot);

        if (!IsWithinFieldOfView(targetPos))
        {
            if (showDebugVisuals && this.enabled)
                Debug.DrawLine(ResolveSightOrigin(), targetPos, Color.yellow);

            return null;
        }
        
        Vector3[] origins =
        {
            ResolveSightOrigin(),
            ResolveSightOrigin() - Vector3.up * 0.25f,
            ResolveSightOrigin() + Vector3.up * 0.25f
        };

        for (int i = 0; i < origins.Length; i++)
        {
            Vector3 origin = origins[i];
            Vector3 direction = targetPos - origin;
            float distance = direction.magnitude;

            if (distance > m_detectionRange * DungeonDarknessSense.DetectionMultiplierAt(targetPos))
                continue;

            if (TryRaycastFirstNonSelf(origin, direction.normalized, distance, out RaycastHit hit))
            {
                GameObject hitTarget = ResolveTargetRoot(hit.collider != null ? hit.collider.gameObject : null);
                if (hitTarget == targetRoot)
                {
                    if (showDebugVisuals && this.enabled)
                        Debug.DrawLine(origin, targetPos, Color.green);

                    return targetRoot;
                }

                if (showDebugVisuals && this.enabled)
                    Debug.DrawLine(origin, hit.point, Color.red);
            }
            else if (showDebugVisuals && this.enabled)
            {
                Debug.DrawLine(origin, origin + direction.normalized * distance, new Color(1f, 0.4f, 0f));
            }
        }

        // All rays failed
        return null;
    }

    private GameObject ResolveTargetRoot(GameObject target)
    {
        if (target == null)
            return null;

        PlayerPawn playerPawn = target.GetComponentInParent<PlayerPawn>();
        if (playerPawn != null)
            return playerPawn.gameObject;

        PlayerVitals playerVitals = target.GetComponentInParent<PlayerVitals>();
        if (playerVitals != null)
            return playerVitals.gameObject;

        PlayerDeath playerDeath = target.GetComponentInParent<PlayerDeath>();
        if (playerDeath != null)
            return playerDeath.gameObject;

        return target;
    }

    private Vector3 ResolveSightOrigin()
    {
        if (m_visionOrigin != null)
            return m_visionOrigin.position;

        return transform.TransformPoint(m_localVisionOffset + Vector3.up * m_detectionHeight);
    }

    private Vector3 ResolveSightForward()
    {
        Vector3 forward = m_visionOrigin != null ? m_visionOrigin.forward : transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f)
            forward = transform.parent != null ? transform.parent.forward : Vector3.forward;

        forward.y = 0f;
        return forward.sqrMagnitude <= 0.0001f ? Vector3.forward : forward.normalized;
    }

    private Vector3 ResolveTargetAimPosition(GameObject targetRoot)
    {
        if (targetRoot == null)
            return transform.position;

        Collider[] colliders = targetRoot.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
            return bounds.center;

        return targetRoot.transform.position + Vector3.up;
    }

    private bool TryRaycastFirstNonSelf(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            _raycastHits,
            distance,
            GetVisibilityMask(),
            QueryTriggerInteraction.Ignore
        );

        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _raycastHits[i];
            if (candidate.collider == null)
                continue;

            Transform hitTransform = candidate.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform) || hitTransform.root == transform.root)
                continue;

            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            hit = _raycastHits[bestIndex];
            return true;
        }

        hit = default;
        return false;
    }

    private int GetVisibilityMask()
    {
        int mask = m_visibilityMask.value;
        if (mask == 0)
            mask = ~0;

        if (m_playerLayerMask.value != 0)
            mask |= m_playerLayerMask.value;

        return mask;
    }

    private bool IsWithinFieldOfView(Vector3 targetPosition)
    {
        Vector3 origin = ResolveSightOrigin();
        Vector3 toTarget = targetPosition - origin;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude <= 0.0001f)
            return true;

        if (toTarget.sqrMagnitude <= m_closeRangeAlwaysVisible * m_closeRangeAlwaysVisible)
            return true;

        Vector3 forward = ResolveSightForward();
        toTarget.Normalize();

        return Vector3.Angle(forward, toTarget) <= m_halfViewAngle;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugVisuals)
            return;

        Vector3 origin = ResolveSightOrigin();
        Vector3 forward = ResolveSightForward();

        Quaternion leftRotation = Quaternion.AngleAxis(-m_halfViewAngle, Vector3.up);
        Quaternion rightRotation = Quaternion.AngleAxis(m_halfViewAngle, Vector3.up);
        Vector3 left = leftRotation * forward * m_detectionRange;
        Vector3 right = rightRotation * forward * m_detectionRange;

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(origin, 0.3f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, forward * m_detectionRange);
        Gizmos.DrawRay(origin, left);
        Gizmos.DrawRay(origin, right);
    }
}

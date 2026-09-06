using UnityEngine;

public class RangeDetector : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private float detectionRadius = 10f;
    [SerializeField] private bool showDebugVisuals = true;

    public GameObject DetectedTarget { get; private set; }

    public GameObject UpdateDetector() // 가장 가까운 살아있는 PlayerPawn 타겟 찾기
    {
        return UpdateDetector(null);
    }

    public GameObject UpdateDetector(System.Predicate<GameObject> targetFilter)
    {
        float closestSqrDist = float.MaxValue;
        PlayerPawn closest = null;

        for (int i = 0; i < PlayerPawn.All.Count; i++)
        {
            PlayerPawn candidate = PlayerPawn.All[i];
            if (!IsAlive(candidate))
                continue;

            // Dark rooms let monsters notice players from farther away, lit rooms from closer,
            // and a player with no teammate nearby (straggler) is noticed from farther still.
            float radius = detectionRadius * DungeonDarknessSense.DetectionMultiplierAt(candidate.transform.position);
            float scoredDistance = MonsterTargeting.ScoredDistance(transform.position, candidate.transform);
            float sqrDist = scoredDistance * scoredDistance;
            if (scoredDistance > radius)
                continue;

            if (targetFilter != null && !targetFilter(candidate.gameObject))
                continue;

            if (sqrDist < closestSqrDist)
            {
                closestSqrDist = sqrDist;
                closest = candidate;
            }
        }

        DetectedTarget = closest != null ? closest.gameObject : null;
        return DetectedTarget;
    }

    private static bool IsAlive(PlayerPawn candidate) => MonsterTargeting.IsAlive(candidate);

    private void OnDrawGizmos()
    {
        if (!showDebugVisuals || !enabled) return;

        Gizmos.color = DetectedTarget ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}

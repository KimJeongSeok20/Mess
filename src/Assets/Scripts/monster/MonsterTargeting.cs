using UnityEngine;

/// <summary>
/// Shared target-scoring rules for monster detectors. A player with no living teammate nearby is a
/// straggler and is noticed from farther away, so groups that split up pay for it.
/// </summary>
public static class MonsterTargeting
{
    /// <summary>A player with no living teammate within this distance counts as isolated.</summary>
    public const float IsolationRadius = 8f;

    /// <summary>Detection-distance bonus (metres) applied to isolated players.</summary>
    public const float IsolationBonus = 5f;

    public static bool IsAlive(PlayerPawn candidate)
    {
        if (candidate == null || !candidate.isActiveAndEnabled)
            return false;

        PlayerVitals vitals = candidate.GetComponent<PlayerVitals>();
        return vitals == null || !vitals.IsDead;
    }

    /// <summary>True when no other living player is within <see cref="IsolationRadius"/> of the candidate.</summary>
    public static bool IsIsolated(Transform candidate)
    {
        if (candidate == null)
            return false;

        float radiusSqr = IsolationRadius * IsolationRadius;
        Vector3 position = candidate.position;
        for (int i = 0; i < PlayerPawn.All.Count; i++)
        {
            PlayerPawn other = PlayerPawn.All[i];
            if (other == null || other.transform == candidate || !IsAlive(other))
                continue;

            if ((other.transform.position - position).sqrMagnitude <= radiusSqr)
                return false;
        }

        return true;
    }

    /// <summary>Distance bonus for the candidate: <see cref="IsolationBonus"/> when isolated, otherwise 0.</summary>
    public static float IsolationBonusFor(Transform candidate)
    {
        return IsIsolated(candidate) ? IsolationBonus : 0f;
    }

    /// <summary>
    /// Effective distance used for ranking and range checks: the real distance minus the isolation
    /// bonus, never below zero.
    /// </summary>
    public static float ScoredDistance(Vector3 from, Transform candidate)
    {
        float distance = Vector3.Distance(from, candidate.position);
        return Mathf.Max(0f, distance - IsolationBonusFor(candidate) - LootScent.BonusFor(candidate));
    }
}

using UnityEngine;

/// <summary>
/// "Loot scent": the more value a player carries, the farther monsters notice them. The owner
/// reports its inventory value (server clamps it); detectors ask for the distance bonus here.
/// </summary>
public static class LootScent
{
    /// <summary>Credits of carried loot per metre of extra detection distance.</summary>
    public const float CreditsPerMetre = 500f;

    /// <summary>Maximum extra detection distance from loot scent (metres).</summary>
    public const float MaxBonusMetres = 6f;

    /// <summary>Highest value a client may report (server clamp).</summary>
    public const int MaxReportedValue = 50000;

    private static float s_nextReportAt;
    private static int s_lastReported = -1;

    public static float BonusMetres(int carriedValue)
    {
        if (carriedValue <= 0)
            return 0f;

        return Mathf.Min(MaxBonusMetres, carriedValue / CreditsPerMetre);
    }

    public static float BonusFor(Transform candidate)
    {
        if (candidate == null)
            return 0f;

        PlayerPawn pawn = candidate.GetComponent<PlayerPawn>() ?? candidate.GetComponentInParent<PlayerPawn>();
        return pawn != null ? BonusMetres(pawn.CarriedValue) : 0f;
    }

    /// <summary>Owner side: push the local inventory value to the server (throttled, only on change).</summary>
    public static void ReportLocal(InventoryManager inventory)
    {
        if (inventory == null)
            return;

        int value = Mathf.Clamp(inventory.GetCarriedValue(), 0, MaxReportedValue);
        if (value == s_lastReported && Time.unscaledTime < s_nextReportAt)
            return;

        var local = Demo.Scripts.Runtime.Character.FPSMovement.LocalPlayerPosition;
        if (local == null)
            return;

        PlayerPawn pawn = local.transform.root.GetComponent<PlayerPawn>() ?? local.GetComponentInParent<PlayerPawn>();
        if (pawn == null || !pawn.isSpawned || !pawn.isOwner)
            return;

        s_lastReported = value;
        s_nextReportAt = Time.unscaledTime + 0.5f;
        pawn.ReportCarriedValue(value);
    }
}

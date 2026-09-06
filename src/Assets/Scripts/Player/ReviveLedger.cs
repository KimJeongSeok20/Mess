using UnityEngine;

/// <summary>
/// Server-side count of paid revives today. Each revive costs more than the last so a team that
/// keeps dying bleeds money toward the tax deadline; the counter resets with the day.
/// </summary>
public static class ReviveLedger
{
    public const int BaseCost = 400;
    public const float GrowthPerRevive = 1.5f;

    public static int RevivesToday { get; private set; }

    public static int CostFor(int revivesAlreadyUsed)
    {
        double cost = BaseCost * System.Math.Pow(GrowthPerRevive, System.Math.Max(0, revivesAlreadyUsed));
        return cost >= int.MaxValue ? int.MaxValue : (int)System.Math.Round(cost);
    }

    public static int CurrentCost => CostFor(RevivesToday);

    public static void RegisterRevive()
    {
        RevivesToday++;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        TimeManager.OnDayReset -= Reset;
        TimeManager.OnDayReset += Reset;
        TimeManager.OnRunRestarted -= ResetForRestart;
        TimeManager.OnRunRestarted += ResetForRestart;
        RevivesToday = 0;
    }

    private static void Reset() => RevivesToday = 0;
    private static void ResetForRestart(string _) => RevivesToday = 0;
}

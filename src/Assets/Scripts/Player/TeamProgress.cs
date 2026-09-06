using UnityEngine;

/// <summary>
/// Local mirror of team-wide progression that feeds the SkillWeb:
///   - Skill points: earned by processing monster trophies at the corpse station (per player).
///   - Civic rank: raised for the whole team every time the tax is paid; gates the "special"
///     skill nodes (battery cells, forge safety nets, team insurance) via SkillWeb.playerLevel.
/// The SkillWeb runtime resets its statics when it initializes, so the values are re-applied
/// by PlayerPerks every refresh instead of being written once.
/// </summary>
public static class TeamProgress
{
    public const int BaseCivicRank = 1;

    public static int CivicRank { get; private set; } = BaseCivicRank;
    public static int SkillPointsEarned { get; private set; }
    private static int _skillPointsApplied;
    private static bool _initialSkillPointStateEstablished;

    public static event System.Action Changed;

    public static void EnsureInitialSkillPoints(int startingPoints)
    {
        if (_initialSkillPointStateEstablished)
            return;

        _initialSkillPointStateEstablished = true;
        // Earned or restored balances are authoritative; a new test session alone gets its seed.
        if (SkillPointsEarned > 0 || Esper.SkillWeb.SkillWeb.skillPoints > 0)
            return;

        Esper.SkillWeb.SkillWeb.skillPoints = Mathf.Max(0, startingPoints);
        Changed?.Invoke();
    }

    public static void AddSkillPoints(int amount, string reason)
    {
        if (amount <= 0)
            return;

        SkillPointsEarned += amount;
        Debug.Log($"[TeamProgress] +{amount} skill point(s) ({reason}); earned total {SkillPointsEarned}");
        Changed?.Invoke();
    }

    public static void SetCivicRank(int rank)
    {
        rank = Mathf.Max(BaseCivicRank, rank);
        if (rank == CivicRank)
            return;

        CivicRank = rank;
        Debug.Log($"[TeamProgress] Civic rank is now {CivicRank}");
        Changed?.Invoke();
    }

    /// <summary>
    /// Push pending points and the civic rank into the SkillWeb statics. Safe to call often:
    /// only the not-yet-applied delta is added, and the rank is mirrored as the player level.
    /// </summary>
    public static void ApplyToSkillWeb()
    {
        int pending = SkillPointsEarned - _skillPointsApplied;
        if (pending > 0)
        {
            Esper.SkillWeb.SkillWeb.skillPoints += pending;
            _skillPointsApplied = SkillPointsEarned;
        }

        // Mirror the rank exactly: overdue tax can lower it, which blocks buying higher-rank
        // nodes until the team recovers (nodes already bought are kept by the SkillWeb).
        if (Esper.SkillWeb.SkillWeb.playerLevel != CivicRank)
            Esper.SkillWeb.SkillWeb.playerLevel = CivicRank;
    }

    /// <summary>Run restart: everything back to zero.</summary>
    public static void ResetAll()
    {
        var view = Esper.SkillWeb.UI.UGUI.WebViewUGUI.Active;
        if (view != null && view.web != null) view.ResetAllSkills();
        Esper.SkillWeb.SkillWeb.skillPoints = 0;
        Esper.SkillWeb.SkillWeb.playerLevel = BaseCivicRank;
        CivicRank = BaseCivicRank;
        SkillPointsEarned = 0;
        _skillPointsApplied = 0;
        _initialSkillPointStateEstablished = false;
        Changed?.Invoke();
    }

    public static void RestoreCheckpoint(int civicRank, int earned, int available)
    {
        _initialSkillPointStateEstablished = true;
        CivicRank = Mathf.Max(BaseCivicRank, civicRank);
        SkillPointsEarned = Mathf.Max(0, earned);
        _skillPointsApplied = SkillPointsEarned;
        Esper.SkillWeb.SkillWeb.skillPoints = Mathf.Max(0, available);
        Esper.SkillWeb.SkillWeb.playerLevel = CivicRank;
        Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        TimeManager.OnRunRestarted -= OnRunRestarted;
        TimeManager.OnRunRestarted += OnRunRestarted;
    }

    private static void OnRunRestarted(string _) => ResetAll();
}


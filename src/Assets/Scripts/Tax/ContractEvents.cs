using System;

/// <summary>What a daily company contract asks the team to do.</summary>
public enum ContractGoal
{
    SmilyTrophies,
    ClownTrophies,
    OctopusTrophies,
    SaleCredits,
    BatteryRecharges,
}

/// <summary>
/// Server-side progress feed for <see cref="DailyContractBoard"/>. Gameplay systems that already run
/// on the server (corpse processor, currency, dungeon battery) report here; nothing on clients can
/// forge progress because the reports are only raised from server code paths.
/// </summary>
public static class ContractEvents
{
    public static event Action<ContractGoal, int> Reported;

    public static void Report(ContractGoal goal, int amount)
    {
        if (amount <= 0)
            return;

        Reported?.Invoke(goal, amount);
    }

    /// <summary>Maps a processed trophy item name to a contract goal, or null when it is not a trophy.</summary>
    public static ContractGoal? GoalForTrophy(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return null;

        string lower = itemName.ToLowerInvariant();
        if (lower.Contains("smily"))
            return ContractGoal.SmilyTrophies;
        if (lower.Contains("clown"))
            return ContractGoal.ClownTrophies;
        if (lower.Contains("octopus"))
            return ContractGoal.OctopusTrophies;

        return null;
    }
}

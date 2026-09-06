using System;

/// <summary>
/// The five visual states available on the tax-machine's dedicated upper monitor.
/// The authoritative machine selects one of these states on the server and synchronizes it.
/// </summary>
public enum TaxMachineScreenState
{
    Days2 = 0,
    Days1 = 1,
    // Keep Frown and Smile on their original serialized values. Pay was added later.
    Pay = 4,
    Frown = 2,
    Smile = 3,
}

/// <summary>
/// Deterministic, session-agnostic tax schedule calculations.
/// A cycle first becomes due on the final day of that cycle: day 3, 6, 9, and so on.
/// </summary>
public static class TaxSchedule
{
    public const int DefaultCycleDays = 3;
    /// <summary>
    /// Base tax for cycle one. Calibrated against the default loot budget (10000/day, ~20 items of
    /// 180-888) so a team that loots casually can pay, and a team that stops looting cannot.
    /// </summary>
    public const int DefaultTaxPerCycle = 2500;
    /// <summary>
    /// Each later cycle costs (1 + growth) times the previous one: 2500, 3250, 4225, 5493, ...
    /// </summary>
    public const float DefaultGrowthPerCycle = 0.3f;
    private const int MaxCyclesPerCostCalculation = 10000;

    /// <summary>
    /// Tax owed for one specific one-based cycle. Cycle one costs <paramref name="baseTax"/>;
    /// every later cycle grows geometrically by <paramref name="growthPerCycle"/>.
    /// Saturates to int.MaxValue instead of overflowing.
    /// </summary>
    public static int TaxForCycle(int cycleIndex, int baseTax, float growthPerCycle)
    {
        if (cycleIndex <= 0 || baseTax <= 0)
            return 0;

        double growth = Math.Max(0.0, growthPerCycle);
        double amount = baseTax * Math.Pow(1.0 + growth, cycleIndex - 1);
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount >= int.MaxValue)
            return int.MaxValue;

        return (int)Math.Round(amount);
    }

    /// <summary>
    /// Sum of the escalating tax for cycles (<paramref name="fromCycleExclusive"/>, <paramref name="toCycleInclusive"/>].
    /// Returns false when the exact total does not fit an int; <paramref name="cost"/> is then int.MaxValue.
    /// </summary>
    public static bool TryCalculateCostForCycleRange(
        int fromCycleExclusive,
        int toCycleInclusive,
        int baseTax,
        float growthPerCycle,
        out int cost)
    {
        cost = 0;

        if (baseTax <= 0 || toCycleInclusive <= fromCycleExclusive)
            return true;

        int start = Math.Max(1, fromCycleExclusive + 1);
        if ((long)toCycleInclusive - start >= MaxCyclesPerCostCalculation)
        {
            cost = int.MaxValue;
            return false;
        }

        long total = 0;
        for (int cycle = start; cycle <= toCycleInclusive; cycle++)
        {
            int cycleTax = TaxForCycle(cycle, baseTax, growthPerCycle);
            if (cycleTax == int.MaxValue)
            {
                cost = int.MaxValue;
                return false;
            }

            total += cycleTax;
            if (total > int.MaxValue)
            {
                cost = int.MaxValue;
                return false;
            }
        }

        cost = (int)total;
        return true;
    }

    /// <summary>
    /// Escalating cost of every currently unpaid cycle.
    /// </summary>
    public static bool TryCalculateOutstandingCost(
        int dueThroughCycle,
        int paidThroughCycle,
        int baseTax,
        float growthPerCycle,
        out int cost)
    {
        return TryCalculateCostForCycleRange(
            Math.Max(0, paidThroughCycle),
            dueThroughCycle,
            baseTax,
            growthPerCycle,
            out cost);
    }

    /// <summary>
    /// Days left until the next due day. Zero on a due day itself.
    /// </summary>
    public static int DaysUntilNextDue(int currentDay, int daysPerCycle = DefaultCycleDays)
    {
        if (daysPerCycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerCycle), "Tax cycles must be at least one day long.");

        if (currentDay <= 0)
            return daysPerCycle - 1;

        int nextDueDay = ((currentDay - 1) / daysPerCycle + 1) * daysPerCycle;
        return nextDueDay - currentDay;
    }

    /// <summary>
    /// True when the oldest unpaid cycle's due day plus <paramref name="graceDays"/> is already behind
    /// <paramref name="currentDay"/>. Cycle one is due on day three: with zero grace, an unpaid team is
    /// evicted at the start of day four; with one grace day, at the start of day five.
    /// </summary>
    public static bool ShouldEvict(
        int currentDay,
        int dueThroughCycle,
        int paidThroughCycle,
        int daysPerCycle = DefaultCycleDays,
        int graceDays = 0)
    {
        if (daysPerCycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerCycle), "Tax cycles must be at least one day long.");

        if (OutstandingCycles(dueThroughCycle, paidThroughCycle) == 0)
            return false;

        long oldestUnpaidCycle = Math.Max(1, (long)paidThroughCycle + 1);
        long dueDay = oldestUnpaidCycle * daysPerCycle;
        return currentDay > dueDay + Math.Max(0, graceDays);
    }

    /// <summary>
    /// Returns the highest tax cycle that is due at the supplied one-based in-game day.
    /// Days one and two have no due tax; day three makes cycle one due.
    /// </summary>
    public static int DueThroughCycleForDay(int currentDay, int daysPerCycle = DefaultCycleDays)
    {
        if (daysPerCycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerCycle), "Tax cycles must be at least one day long.");

        if (currentDay <= 0)
            return 0;

        return currentDay / daysPerCycle;
    }

    /// <summary>
    /// Resolves the one-based day within a tax cycle. Non-positive session days use the
    /// first pre-due display state so an uninitialized session never presents false debt.
    /// </summary>
    public static int CycleDayForDay(int currentDay, int daysPerCycle = DefaultCycleDays)
    {
        if (daysPerCycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerCycle), "Tax cycles must be at least one day long.");

        if (currentDay <= 0)
            return 1;

        return ((currentDay - 1) % daysPerCycle) + 1;
    }

    /// <summary>
    /// Maps server-known tax markers and the server's current day to the monitor state.
    /// The due day shows PAY for the newly due cycle. Debt carried beyond that due day shows
    /// the frown, while paying the due cycle on time shows the smile.
    /// </summary>
    public static TaxMachineScreenState ResolveScreenState(
        int currentDay,
        int dueThroughCycle,
        int paidThroughCycle,
        int daysPerCycle = DefaultCycleDays)
    {
        if (daysPerCycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerCycle), "Tax cycles must be at least one day long.");

        int cycleDay = CycleDayForDay(currentDay, daysPerCycle);
        int outstandingCycles = OutstandingCycles(dueThroughCycle, paidThroughCycle);

        if (cycleDay == daysPerCycle)
        {
            if (outstandingCycles == 0 && dueThroughCycle > 0)
                return TaxMachineScreenState.Smile;

            return outstandingCycles == 1
                ? TaxMachineScreenState.Pay
                : TaxMachineScreenState.Frown;
        }

        if (outstandingCycles > 0)
            return TaxMachineScreenState.Frown;

        if (cycleDay == 1)
            return TaxMachineScreenState.Days2;

        if (cycleDay == 2)
            return TaxMachineScreenState.Days1;

        // Unsupported intermediate days in a non-default schedule fail visibly instead of
        // presenting a misleading countdown.
        return TaxMachineScreenState.Frown;
    }

    /// <summary>
    /// Returns the number of unpaid cycles represented by two inclusive cycle markers.
    /// The result is clamped instead of allowing invalid/corrupt values to overflow.
    /// </summary>
    public static int OutstandingCycles(int dueThroughCycle, int paidThroughCycle)
    {
        if (dueThroughCycle <= paidThroughCycle)
            return 0;

        long outstanding = (long)dueThroughCycle - paidThroughCycle;
        return outstanding > int.MaxValue ? int.MaxValue : (int)outstanding;
    }

    /// <summary>
    /// Calculates a representable cost. The returned false value means the exact cost
    /// exceeded the integer currency range; <paramref name="cost"/> is saturated to int.MaxValue.
    /// </summary>
    public static bool TryCalculateCostForCycles(int cycleCount, int taxPerCycle, out int cost)
    {
        cost = 0;

        if (cycleCount <= 0 || taxPerCycle <= 0)
            return true;

        long total = (long)cycleCount * taxPerCycle;
        if (total > int.MaxValue)
        {
            cost = int.MaxValue;
            return false;
        }

        cost = (int)total;
        return true;
    }

    /// <summary>
    /// Returns an overflow-safe displayed cost. Call <see cref="TryCalculateCostForCycles"/>
    /// when the caller must know whether the total was saturated.
    /// </summary>
    public static int CalculateCostForCycles(int cycleCount, int taxPerCycle)
    {
        TryCalculateCostForCycles(cycleCount, taxPerCycle, out int cost);
        return cost;
    }

    public static bool TryCalculateOutstandingCost(
        int dueThroughCycle,
        int paidThroughCycle,
        int taxPerCycle,
        out int cost)
    {
        return TryCalculateCostForCycles(
            OutstandingCycles(dueThroughCycle, paidThroughCycle),
            taxPerCycle,
            out cost);
    }

    /// <summary>
    /// The lower amount display previews one upcoming cycle during the countdown, shows every
    /// accrued unpaid cycle once debt exists, and clears to zero after successful payment.
    /// </summary>
    public static int DisplayedPaymentCycles(int outstandingCycles, TaxMachineScreenState screenState)
    {
        if (outstandingCycles > 0)
            return outstandingCycles;

        return screenState == TaxMachineScreenState.Days2
            || screenState == TaxMachineScreenState.Days1
            || screenState == TaxMachineScreenState.Pay
            ? 1
            : 0;
    }
}

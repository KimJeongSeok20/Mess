using NUnit.Framework;

public sealed class TaxScheduleTests
{
    [TestCase(1, 0)]
    [TestCase(2, 0)]
    [TestCase(3, 1)]
    [TestCase(6, 2)]
    public void DueThroughCycleStartsOnDayThreeAndRepeatsEveryThreeDays(int day, int expectedDueCycle)
    {
        Assert.AreEqual(expectedDueCycle, TaxSchedule.DueThroughCycleForDay(day));
    }

    [Test]
    public void OutstandingCyclesAccrueUntilTheTeamPaysThroughTheLatestDueCycle()
    {
        Assert.AreEqual(3, TaxSchedule.OutstandingCycles(dueThroughCycle: 4, paidThroughCycle: 1));
        Assert.AreEqual(0, TaxSchedule.OutstandingCycles(dueThroughCycle: 4, paidThroughCycle: 4));
        Assert.AreEqual(0, TaxSchedule.OutstandingCycles(dueThroughCycle: 2, paidThroughCycle: 4));
    }

    [Test]
    public void OutstandingCostUsesEveryUnpaidCycle()
    {
        bool representable = TaxSchedule.TryCalculateOutstandingCost(
            dueThroughCycle: 4,
            paidThroughCycle: 1,
            taxPerCycle: TaxSchedule.DefaultTaxPerCycle,
            out int cost);

        Assert.IsTrue(representable);
        Assert.AreEqual(3 * TaxSchedule.DefaultTaxPerCycle, cost);
    }

    [TestCase(1, 1000, 0.3f, 1000)]
    [TestCase(2, 1000, 0.3f, 1300)]
    [TestCase(3, 1000, 0.3f, 1690)]
    [TestCase(5, 1000, 0f, 1000)]
    [TestCase(0, 1000, 0.3f, 0)]
    public void TaxEscalatesGeometricallyPerCycle(int cycle, int baseTax, float growth, int expected)
    {
        Assert.AreEqual(expected, TaxSchedule.TaxForCycle(cycle, baseTax, growth));
    }

    [Test]
    public void EscalatingOutstandingCostSumsOnlyUnpaidCycles()
    {
        bool representable = TaxSchedule.TryCalculateOutstandingCost(
            dueThroughCycle: 3,
            paidThroughCycle: 1,
            baseTax: 1000,
            growthPerCycle: 0.3f,
            out int cost);

        Assert.IsTrue(representable);
        Assert.AreEqual(1300 + 1690, cost);

        Assert.IsTrue(TaxSchedule.TryCalculateOutstandingCost(2, 2, 1000, 0.3f, out int nothingOwed));
        Assert.AreEqual(0, nothingOwed);
    }

    [Test]
    public void EscalatingCostSaturatesInsteadOfOverflowing()
    {
        bool representable = TaxSchedule.TryCalculateOutstandingCost(
            dueThroughCycle: int.MaxValue,
            paidThroughCycle: 0,
            baseTax: 1000,
            growthPerCycle: 0.3f,
            out int cost);

        Assert.IsFalse(representable);
        Assert.AreEqual(int.MaxValue, cost);
        Assert.AreEqual(int.MaxValue, TaxSchedule.TaxForCycle(5000, 1000, 0.3f));
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    [TestCase(3, 0)]
    [TestCase(4, 2)]
    [TestCase(6, 0)]
    public void DaysUntilNextDueCountsDownToTheDueDay(int day, int expected)
    {
        Assert.AreEqual(expected, TaxSchedule.DaysUntilNextDue(day));
    }

    [TestCase(3, 1, 0, 0, false)]
    [TestCase(4, 1, 0, 0, true)]
    [TestCase(4, 1, 1, 0, false)]
    [TestCase(4, 1, 0, 1, false)]
    [TestCase(5, 1, 0, 1, true)]
    [TestCase(7, 2, 1, 0, true)]
    [TestCase(7, 2, 2, 0, false)]
    [TestCase(1, 0, 0, 0, false)]
    public void EvictionTriggersOnceTheOldestUnpaidCycleIsPastItsDueDayPlusGrace(
        int currentDay,
        int dueThroughCycle,
        int paidThroughCycle,
        int graceDays,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            TaxSchedule.ShouldEvict(currentDay, dueThroughCycle, paidThroughCycle, TaxSchedule.DefaultCycleDays, graceDays));
    }

    [Test]
    public void CostCalculationSaturatesInsteadOfOverflowing()
    {
        bool representable = TaxSchedule.TryCalculateCostForCycles(int.MaxValue, TaxSchedule.DefaultTaxPerCycle, out int cost);

        Assert.IsFalse(representable);
        Assert.AreEqual(int.MaxValue, cost);
        Assert.AreEqual(int.MaxValue, TaxSchedule.CalculateCostForCycles(int.MaxValue, TaxSchedule.DefaultTaxPerCycle));
    }

    [TestCase(1, 0, 0, TaxMachineScreenState.Days2)]
    [TestCase(2, 0, 0, TaxMachineScreenState.Days1)]
    [TestCase(3, 1, 0, TaxMachineScreenState.Pay)]
    [TestCase(3, 1, 1, TaxMachineScreenState.Smile)]
    [TestCase(4, 1, 0, TaxMachineScreenState.Frown)]
    [TestCase(4, 1, 1, TaxMachineScreenState.Days2)]
    [TestCase(5, 1, 1, TaxMachineScreenState.Days1)]
    [TestCase(6, 2, 1, TaxMachineScreenState.Pay)]
    [TestCase(6, 2, 0, TaxMachineScreenState.Frown)]
    [TestCase(6, 2, 2, TaxMachineScreenState.Smile)]
    public void ScreenStateUsesTheServerScheduleAndAccruedDebt(
        int currentDay,
        int dueThroughCycle,
        int paidThroughCycle,
        TaxMachineScreenState expected)
    {
        Assert.AreEqual(
            expected,
            TaxSchedule.ResolveScreenState(currentDay, dueThroughCycle, paidThroughCycle));
    }

    [Test]
    public void CycleRolloverReturnsToTheTwoDaysRemainingStateAfterDuePayment()
    {
        Assert.AreEqual(1, TaxSchedule.CycleDayForDay(4));
        Assert.AreEqual(
            TaxMachineScreenState.Days2,
            TaxSchedule.ResolveScreenState(currentDay: 4, dueThroughCycle: 1, paidThroughCycle: 1));
    }

    [TestCase(TaxMachineScreenState.Days2, 0f, TaxMachineScreenState.Days2)]
    [TestCase(TaxMachineScreenState.Days2, 1.99f, TaxMachineScreenState.Days2)]
    [TestCase(TaxMachineScreenState.Days2, 2f, TaxMachineScreenState.Frown)]
    [TestCase(TaxMachineScreenState.Days2, 4f, TaxMachineScreenState.Days2)]
    [TestCase(TaxMachineScreenState.Days1, 2f, TaxMachineScreenState.Frown)]
    [TestCase(TaxMachineScreenState.Pay, 2f, TaxMachineScreenState.Frown)]
    [TestCase(TaxMachineScreenState.Smile, 20f, TaxMachineScreenState.Smile)]
    [TestCase(TaxMachineScreenState.Frown, 20f, TaxMachineScreenState.Frown)]
    public void AlternatingStatesUseTwoSecondPhasesAndHoldTerminalFaces(
        TaxMachineScreenState selectedState,
        float elapsedSeconds,
        TaxMachineScreenState expected)
    {
        Assert.AreEqual(
            expected,
            TaxMachineScreenController.ResolveAlternatingState(selectedState, elapsedSeconds, intervalSeconds: 2f));
    }

    [TestCase(0, TaxMachineScreenState.Days2, 1)]
    [TestCase(0, TaxMachineScreenState.Days1, 1)]
    [TestCase(0, TaxMachineScreenState.Pay, 1)]
    [TestCase(0, TaxMachineScreenState.Smile, 0)]
    [TestCase(0, TaxMachineScreenState.Frown, 0)]
    [TestCase(2, TaxMachineScreenState.Frown, 2)]
    public void AmountDisplayShowsUpcomingAccruedAndClearedCycleCounts(
        int outstandingCycles,
        TaxMachineScreenState screenState,
        int expectedDisplayedCycles)
    {
        Assert.AreEqual(
            expectedDisplayedCycles,
            TaxSchedule.DisplayedPaymentCycles(outstandingCycles, screenState));
    }
}

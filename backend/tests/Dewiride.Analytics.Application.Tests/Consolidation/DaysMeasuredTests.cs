using Dewiride.Analytics.Application.Consolidation;

namespace Dewiride.Analytics.Application.Tests.Consolidation;

/// <summary>
/// Covers how many of a month's days a website was measured on, which is what an average per day
/// divides by.
/// </summary>
public sealed class DaysMeasuredTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    [Fact]
    public void A_Finished_Month_Counts_Every_Day_It_Has()
    {
        DaysMeasured.In(new DateOnly(2026, 1, 1), null, Today).Should().Be(31);
        DaysMeasured.In(new DateOnly(2026, 4, 1), null, Today).Should().Be(30);
    }

    /// <summary>A website measured from the nineteenth is not averaged over the days before it.</summary>
    [Fact]
    public void The_First_Month_Counts_From_The_First_Recorded_Day()
    {
        DaysMeasured.In(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 19), Today).Should().Be(13);
    }

    [Fact]
    public void A_First_Day_Before_The_Month_Leaves_It_Whole()
    {
        DaysMeasured.In(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 19), Today).Should().Be(30);
    }

    /// <summary>The day it is now is a day measured, however little of it has gone.</summary>
    [Fact]
    public void The_Month_Running_Now_Counts_The_Days_So_Far_Today_Included()
    {
        DaysMeasured.In(new DateOnly(2026, 10, 1), null, Today).Should().Be(30);
        DaysMeasured.In(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 19), Today).Should().Be(12);
    }

    [Fact]
    public void February_Has_The_Days_Its_Year_Gives_It()
    {
        DaysMeasured.In(new DateOnly(2027, 2, 1), null, new DateOnly(2027, 6, 1)).Should().Be(28);
        DaysMeasured.In(new DateOnly(2028, 2, 1), null, new DateOnly(2028, 6, 1)).Should().Be(29);
    }

    [Fact]
    public void A_Month_Nothing_Was_Measured_In_Counts_No_Days()
    {
        DaysMeasured.In(new DateOnly(2026, 11, 1), null, Today).Should().Be(0);
        DaysMeasured.In(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 5), Today).Should().Be(0);
    }
}

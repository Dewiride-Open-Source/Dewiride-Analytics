using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers which day of a month, or which hour, is its busiest.
/// </summary>
/// <remarks>
/// A kept month's busiest day and busiest hour outlive the reports that could correct them, and the
/// store hands a month's days and hours back in no order it promises — so the answer has to be the
/// same whatever order they come in.
/// </remarks>
public sealed class PeakTests
{
    private static readonly DateOnly Fourth = new(2026, 9, 4);
    private static readonly DateOnly Ninth = new(2026, 9, 9);

    [Fact]
    public void The_Earliest_Of_Two_Equally_Busy_Days_Is_The_Busiest_Whatever_Order_They_Come_In()
    {
        var laterFirst = new Peak<DateOnly>();
        laterFirst.Offer(Ninth, 3);
        laterFirst.Offer(Fourth, 3);

        var earlierFirst = new Peak<DateOnly>();
        earlierFirst.Offer(Fourth, 3);
        earlierFirst.Offer(Ninth, 3);

        laterFirst.When.Should().Be(Fourth);
        laterFirst.PageViews.Should().Be(3);
        earlierFirst.When.Should().Be(Fourth);
        earlierFirst.PageViews.Should().Be(3);
    }

    /// <summary>
    /// Hours are decided by the rule days are: of two as busy, the one whose first minute came first on
    /// the clock — here an evening hour of the fourth against a morning hour of the ninth.
    /// </summary>
    [Fact]
    public void The_Earliest_Of_Two_Equally_Busy_Hours_Is_The_Busiest_Whatever_Order_They_Come_In()
    {
        var evening = Fourth.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Unspecified);
        var morning = Ninth.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Unspecified);

        var laterFirst = new Peak<DateTime>();
        laterFirst.Offer(morning, 5);
        laterFirst.Offer(evening, 5);

        var earlierFirst = new Peak<DateTime>();
        earlierFirst.Offer(evening, 5);
        earlierFirst.Offer(morning, 5);

        laterFirst.When.Should().Be(evening);
        laterFirst.PageViews.Should().Be(5);
        earlierFirst.When.Should().Be(evening);
        earlierFirst.PageViews.Should().Be(5);
    }

    [Fact]
    public void A_Busier_Later_Day_Is_The_Busiest()
    {
        var busiest = new Peak<DateOnly>();
        busiest.Offer(Fourth, 3);
        busiest.Offer(Ninth, 4);

        busiest.When.Should().Be(Ninth);
        busiest.PageViews.Should().Be(4);
    }

    /// <summary>
    /// The hour the clocks go back through is offered twice, once on each side of the change, and is
    /// the busier of the two — never the two added together.
    /// </summary>
    [Fact]
    public void The_Same_Hour_Offered_Twice_Is_The_Busier_Of_The_Two_And_Never_Their_Sum()
    {
        var oneInTheMorning = new DateTime(2025, 10, 26, 1, 0, 0, DateTimeKind.Unspecified);

        var busiest = new Peak<DateTime>();
        busiest.Offer(oneInTheMorning, 2);
        busiest.Offer(oneInTheMorning, 3);

        busiest.When.Should().Be(oneInTheMorning);
        busiest.PageViews.Should().Be(3);
    }

    /// <summary>A day nothing was delivered on is never the busiest, so a month with nothing has none.</summary>
    [Fact]
    public void No_Day_Is_The_Busiest_Where_Nothing_Was_Delivered()
    {
        var busiest = new Peak<DateOnly>();
        busiest.Offer(Ninth, 0);
        busiest.Offer(Fourth, 0);

        busiest.When.Should().BeNull();
        busiest.PageViews.Should().Be(0);
    }
}

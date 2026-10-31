using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Application.Tests.Analytics;

/// <summary>
/// Covers which day of a month is its busiest.
/// </summary>
/// <remarks>
/// A kept month's busiest day outlives the reports that could correct it, and the store hands a
/// month's days back in no order it promises — so the answer has to be the same whatever order the
/// days come in.
/// </remarks>
public sealed class BusiestDayTests
{
    private static readonly DateOnly Fourth = new(2026, 9, 4);
    private static readonly DateOnly Ninth = new(2026, 9, 9);

    [Fact]
    public void The_Earliest_Of_Two_Equally_Busy_Days_Is_The_Busiest_Whatever_Order_They_Come_In()
    {
        var laterFirst = new BusiestDay();
        laterFirst.Offer(Ninth, 3);
        laterFirst.Offer(Fourth, 3);

        var earlierFirst = new BusiestDay();
        earlierFirst.Offer(Fourth, 3);
        earlierFirst.Offer(Ninth, 3);

        laterFirst.Day.Should().Be(Fourth);
        laterFirst.PageViews.Should().Be(3);
        earlierFirst.Day.Should().Be(Fourth);
        earlierFirst.PageViews.Should().Be(3);
    }

    [Fact]
    public void A_Busier_Later_Day_Is_The_Busiest()
    {
        var busiest = new BusiestDay();
        busiest.Offer(Fourth, 3);
        busiest.Offer(Ninth, 4);

        busiest.Day.Should().Be(Ninth);
        busiest.PageViews.Should().Be(4);
    }

    /// <summary>A day nothing was delivered on is never the busiest, so a month with nothing has none.</summary>
    [Fact]
    public void No_Day_Is_The_Busiest_Where_Nothing_Was_Delivered()
    {
        var busiest = new BusiestDay();
        busiest.Offer(Ninth, 0);
        busiest.Offer(Fourth, 0);

        busiest.Day.Should().BeNull();
        busiest.PageViews.Should().Be(0);
    }
}

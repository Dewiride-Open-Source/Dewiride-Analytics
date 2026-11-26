using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Tests.Consolidation;

/// <summary>
/// Covers which months of a website's busiest pages a run counts.
/// </summary>
/// <remarks>
/// A month's pages are kept on a row of their own and outlive their reports as a month's totals do, so
/// they are counted again on the same terms, by a version of the arithmetic of their own.
/// </remarks>
public sealed class PageMonthsToCountTests
{
    private const string Zone = "Etc/UTC";

    /// <summary>A moment in the middle of March, with January and February finished.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Rules other than the ones compiled into the build, so these cases do not move with them.</summary>
    private static readonly RulesetVersion Rules = new(4, 2);

    /// <summary>Judging caught up to within a visit's timeout of the present, under <see cref="Rules"/>.</summary>
    private static readonly SiteJudging CaughtUp = new(Now.AddMinutes(-30), Now.AddMinutes(-30), Rules);

    /// <summary>
    /// A month's pages are counted whether or not its totals were kept: the two are rows of their own.
    /// </summary>
    [Fact]
    public void A_Month_Whose_Pages_Were_Never_Kept_Is_Counted()
    {
        Chosen([Pages(Month(2))]).Should().Equal(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void A_Month_Whose_Pages_Were_Kept_Is_Left_Alone()
    {
        Chosen([Pages(Month(1)), Pages(Month(2))]).Should().BeEmpty();
    }

    [Fact]
    public void A_Months_Pages_Are_Counted_Again_In_A_New_Time_Zone()
    {
        var january = Month(1);
        var elsewhere = Pages(january) with { Described = Whole(january) with { TimeZoneId = "Asia/Kolkata" } };

        Chosen([elsewhere, Pages(Month(2))]).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// Pages counted before judging had finished with the month hold fewer people's views than they will, so
    /// judging moving on through it counts them again, on the terms a month's totals are.
    /// </summary>
    [Fact]
    public void A_Months_Pages_Are_Counted_Again_Once_Judging_Has_Moved_On()
    {
        var reached = new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero);
        var january = Month(1);
        var waiting = Pages(january) with { Described = Whole(january) with { JudgedTo = reached, RulesetJudgedTo = reached } };

        Chosen([waiting, Pages(Month(2))]).Should().Equal(new DateOnly(2026, 1, 1));
        Chosen([waiting, Pages(Month(2))], new SiteJudging(reached, reached, Rules)).Should().BeEmpty();
    }

    [Fact]
    public void A_Months_Pages_Are_Counted_Again_Once_Newer_Rules_Have_Judged_All_Of_It()
    {
        var january = Month(1);
        var walked = new SiteJudging(CaughtUp.Settled, january.Window.To + TimeSpan.FromHours(6), new RulesetVersion(5, 0));

        Chosen([Pages(january), Pages(Month(2))], walked).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// Pages counted by an older version of their own arithmetic are counted again by this one while the
    /// month's reports remain.
    /// </summary>
    [Fact]
    public void A_Months_Pages_Are_Counted_Again_Once_The_Pages_Arithmetic_Is_Newer()
    {
        var january = Month(1);
        var older = Pages(january) with { Described = Whole(january) with { CountingRules = CountingRules.Pages - 1 } };

        Chosen([older, Pages(Month(2))]).Should().Equal(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void The_Pages_Of_The_Month_Running_Now_Are_Never_Counted()
    {
        Choose(new MonthSurvey(null, [Month(3)]), [], Now).Should().BeEmpty();
    }

    /// <summary>
    /// Once the oldest report a month is counted from could be gone, a count would come back short without
    /// saying so: the month keeps whatever was kept of its pages, or none.
    /// </summary>
    [Fact]
    public void The_Pages_Of_A_Month_Whose_Reports_May_Have_Begun_To_Expire_Are_Never_Counted()
    {
        var january = Month(1);
        var survey = new MonthSurvey(null, [january]);
        var expiring = Retention.CountableUntil(january.Window.From);

        Choose(survey, [], expiring - TimeSpan.FromTicks(1)).Should().Equal(new DateOnly(2026, 1, 1));
        Choose(survey, [], expiring).Should().BeEmpty();
    }

    [Fact]
    public void A_Run_Counts_The_Pages_Of_No_More_Months_Than_Its_Allowance_The_Oldest_First()
    {
        var survey = new MonthSurvey(
            null,
            [.. Enumerable.Range(0, 5).Select(step => Uncounted(new DateOnly(2025, 9, 1).AddMonths(step))), Month(3)]);

        PageMonthsToCount.Choose(survey, [], CaughtUp, Zone, Now, most: 3)
            .Select(month => month.Month)
            .Should().Equal(new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 1), new DateOnly(2025, 11, 1));
    }

    /// <summary>What a run chooses from January, February and the running March.</summary>
    private static DateOnly[] Chosen(ImmutableArray<KeptPageMonth> kept, SiteJudging? judging = null) =>
        [
            .. PageMonthsToCount.Choose(
                    new MonthSurvey(null, [Month(1), Month(2), Month(3)]),
                    kept,
                    judging ?? CaughtUp,
                    Zone,
                    Now,
                    most: 4)
                .Select(month => month.Month),
        ];

    private static DateOnly[] Choose(MonthSurvey survey, ImmutableArray<KeptPageMonth> kept, DateTimeOffset now) =>
        [.. PageMonthsToCount.Choose(survey, kept, CaughtUp, Zone, now, most: 4).Select(month => month.Month)];

    /// <summary>
    /// A month of 2026 whose totals were counted whole, which says nothing about its pages.
    /// </summary>
    private static SurveyedMonth Month(int month)
    {
        var surveyed = Uncounted(new DateOnly(2026, month, 1));

        return surveyed with { Kept = Whole(surveyed) };
    }

    private static SurveyedMonth Uncounted(DateOnly month) =>
        new(month, new TimeRange(Midnight(month), Midnight(month.AddMonths(1))), null);

    /// <summary>A month's pages kept whole, an hour after the month ended, under the pages' arithmetic.</summary>
    private static KeptPageMonth Pages(SurveyedMonth month) =>
        new(month.Month, Whole(month) with { CountingRules = CountingRules.Pages });

    /// <summary>What a month counted whole, once judging had finished with it, says of itself.</summary>
    private static KeptMonth Whole(SurveyedMonth month) =>
        new(
            Zone,
            null,
            Rules,
            month.Window.To,
            CountingRules.Current,
            month.Window.To,
            month.Window.To,
            month.Window.To.AddHours(1));

    private static DateTimeOffset Midnight(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Tests.Consolidation;

/// <summary>
/// Covers which of a website's months a run counts.
/// </summary>
/// <remarks>
/// A kept month outlives the reports it was counted from, so whatever it says when its reports go
/// is what it says for ever. Everything here is about counting a month again exactly when what was
/// kept no longer describes it — and never once it can no longer be counted in full.
/// </remarks>
public sealed class MonthsToCountTests
{
    private const string Zone = "Etc/UTC";

    /// <summary>A moment in the middle of March, with January and February finished.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Rules other than the ones compiled into the build, so these cases do not move with them.</summary>
    private static readonly RulesetVersion Rules = new(4, 2);

    /// <summary>Rules newer than <see cref="Rules"/>.</summary>
    private static readonly RulesetVersion NewerRules = new(5, 0);

    /// <summary>Judging caught up to within a visit's timeout of the present, under <see cref="Rules"/>.</summary>
    private static readonly SiteJudging CaughtUp = new(Now.AddMinutes(-30), Now.AddMinutes(-30), Rules);

    [Fact]
    public void A_Month_Nobody_Has_Counted_Is_Counted()
    {
        var survey = Survey(Month(1, kept: false), Month(2), Running());

        Chosen(survey).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>A count that stopped part of the way through a month says less than the month held.</summary>
    [Fact]
    public void A_Month_Counted_Only_Part_Of_The_Way_Is_Counted_Again()
    {
        var january = Month(1);
        var partly = january with { Kept = Whole(january) with { CountedTo = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero) } };

        Chosen(Survey(partly, Month(2), Running())).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// A month counted before judging had finished with it holds fewer people than it will once
    /// judging has, so judging moving on through it is a reason to count it again.
    /// </summary>
    [Fact]
    public void A_Month_Whose_Judging_Has_Moved_On_Is_Counted_Again()
    {
        var reached = new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero);
        var january = Month(1);
        var waiting = Survey(
            january with { Kept = Whole(january) with { JudgedTo = reached, RulesetJudgedTo = reached } },
            Month(2),
            Running());

        Chosen(waiting).Should().Equal(new DateOnly(2026, 1, 1));
        Chosen(waiting, new SiteJudging(reached, reached, Rules))
            .Should().BeEmpty("judging has not moved past where it was when the month was counted");
    }

    /// <summary>
    /// Judging working through a backlog moves on every run, and a month it has not reached yet is
    /// unchanged by that however far it moves elsewhere. Counting such a month again on every run
    /// would hold up the month that just finished behind months nothing has happened to. What
    /// decides a month's people begins a whole visit before the month, so judging reaching that far
    /// is the first thing that changes it.
    /// </summary>
    [Fact]
    public void A_Month_Judging_Has_Not_Reached_Is_Left_Until_It_Has()
    {
        var stuck = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var january = Month(1);
        var february = Month(2);
        var behind = Survey(
            january with { Kept = Whole(january) with { JudgedTo = stuck, RulesetJudgedTo = stuck } },
            february with { Kept = Whole(february) with { JudgedTo = stuck, RulesetJudgedTo = stuck } },
            Running());

        var withinJanuary = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        var intoFebruarysPeople = february.Window.From - VisitorKeys.LongestVisit + TimeSpan.FromHours(1);

        Chosen(behind, new SiteJudging(withinJanuary, withinJanuary, Rules))
            .Should().Equal(new DateOnly(2026, 1, 1));
        Chosen(behind, new SiteJudging(intoFebruarysPeople, intoFebruarysPeople, Rules))
            .Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
    }

    /// <summary>
    /// Improving the rules judges a website's whole history again. Once the rules now running have
    /// passed the end of a month kept under older ones, its verdicts are the new rules' and it is
    /// counted once more.
    /// </summary>
    [Fact]
    public void A_Month_Is_Counted_Again_Once_Newer_Rules_Have_Judged_All_Of_It()
    {
        var january = Month(1);
        var older = Survey(january, Month(2), Running());
        var walked = new SiteJudging(CaughtUp.Settled, january.Window.To + TimeSpan.FromHours(6), NewerRules);

        Chosen(older, walked).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// Counted while the walk is still inside it, a month would mix the two rules' verdicts and be
    /// counted again on every run of the walk.
    /// </summary>
    [Fact]
    public void A_Month_Newer_Rules_Are_Still_Walking_Is_Left_As_It_Was()
    {
        var january = Month(1);
        var walking = new SiteJudging(
            CaughtUp.Settled,
            january.Window.To - TimeSpan.FromDays(5),
            NewerRules);

        Chosen(Survey(january, Month(2), Running()), walking).Should().BeEmpty();
    }

    /// <summary>
    /// An engine rolled back to an earlier release runs older rules than the ones a newer engine
    /// counted with. It reads the same newest verdicts, so counting again would gain nothing, and
    /// it must not undo a newer engine's work.
    /// </summary>
    [Fact]
    public void An_Older_Engine_Never_Recounts_A_Newer_Ones_Month()
    {
        var january = Month(1);
        var newer = january with { Kept = Whole(january) with { Ruleset = NewerRules } };

        Chosen(Survey(newer, Month(2), Running()), CaughtUp).Should().BeEmpty();
    }

    [Fact]
    public void A_Month_Is_Counted_Again_In_A_New_Time_Zone()
    {
        var january = Month(1);
        var elsewhere = january with { Kept = Whole(january) with { TimeZoneId = "Asia/Kolkata" } };

        Chosen(Survey(elsewhere, Month(2), Running())).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// A website's zone changing counts again only the months whose reports are all still held. A
    /// month that froze keeps the zone it was counted in, so at the line between the two the old
    /// zone's month and the new zone's month can overlap or leave a gap.
    /// </summary>
    [Fact]
    public void A_Month_That_Froze_Keeps_The_Zone_It_Was_Counted_In_And_The_Next_Is_Counted_In_The_New_One()
    {
        var march = Uncounted(new DateOnly(2025, 3, 1));
        var april = Uncounted(new DateOnly(2025, 4, 1));
        var survey = Survey(march with { Kept = Whole(march) }, april with { Kept = Whole(april) }, Running());

        MonthsToCount.Choose(survey, CaughtUp, "Asia/Kolkata", Now, most: 4)
            .Select(month => month.Month)
            .Should().Equal(new DateOnly(2025, 4, 1));
    }

    [Fact]
    public void A_Month_Counted_By_Older_Arithmetic_Is_Counted_Again()
    {
        var january = Month(1);
        var older = january with { Kept = Whole(january) with { CountingRules = CountingRules.Current - 1 } };

        Chosen(Survey(older, Month(2), Running())).Should().Equal(new DateOnly(2026, 1, 1));
    }

    /// <summary>
    /// Once the oldest report a month is counted from could be gone, a count would come back short
    /// without saying so. Up to that instant the month is counted; from it, never again.
    /// </summary>
    [Fact]
    public void A_Month_Whose_Reports_May_Have_Begun_To_Expire_Is_Never_Counted()
    {
        var january = Month(1, kept: false);
        var expiring = Retention.CountableUntil(january.Window.From);
        var survey = Survey(january);

        MonthsToCount.Choose(survey, CaughtUp, Zone, expiring - TimeSpan.FromTicks(1), most: 4)
            .Select(month => month.Month)
            .Should().Equal(new DateOnly(2026, 1, 1));
        MonthsToCount.Choose(survey, CaughtUp, Zone, expiring, most: 4).Should().BeEmpty();
    }

    /// <summary>
    /// The month running now is answered from its reports whenever somebody looks, so keeping it
    /// would only add a second, staler copy of the same figure.
    /// </summary>
    [Fact]
    public void The_Month_Running_Now_Is_Never_Counted()
    {
        Chosen(Survey(Month(1), Month(2), Running(kept: false))).Should().BeEmpty();
    }

    [Fact]
    public void The_Oldest_Months_Are_Counted_First()
    {
        var survey = Survey(Month(1, kept: false), Month(2, kept: false), Running());

        MonthsToCount.Choose(survey, CaughtUp, Zone, Now, most: 1)
            .Select(month => month.Month)
            .Should().Equal(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void A_Run_Counts_No_More_Than_Its_Allowance()
    {
        var survey = new MonthSurvey(
            null,
            [
                .. Enumerable.Range(0, 5).Select(step => Uncounted(new DateOnly(2025, 9, 1).AddMonths(step))),
                Running(kept: false),
            ]);

        MonthsToCount.Choose(survey, CaughtUp, Zone, Now, most: 3)
            .Select(month => month.Month)
            .Should().Equal(new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 1), new DateOnly(2025, 11, 1));
    }

    [Fact]
    public void A_Month_Counted_Whole_Under_The_Rules_Running_Now_Is_Left_Alone()
    {
        Chosen(Survey(Month(1), Month(2), Running())).Should().BeEmpty();
    }

    private static DateOnly[] Chosen(MonthSurvey survey, SiteJudging? judging = null) =>
        [.. MonthsToCount.Choose(survey, judging ?? CaughtUp, Zone, Now, most: 4).Select(month => month.Month)];

    private static MonthSurvey Survey(params SurveyedMonth[] months) => new(null, [.. months]);

    /// <summary>A month of 2026, counted whole under <see cref="Rules"/> unless said otherwise.</summary>
    private static SurveyedMonth Month(int month, bool kept = true)
    {
        var surveyed = Uncounted(new DateOnly(2026, month, 1));

        return kept ? surveyed with { Kept = Whole(surveyed) } : surveyed;
    }

    /// <summary>March, which is running at <see cref="Now"/>.</summary>
    private static SurveyedMonth Running(bool kept = true) => Month(3, kept);

    private static SurveyedMonth Uncounted(DateOnly month) =>
        new(month, new TimeRange(Midnight(month), Midnight(month.AddMonths(1))), null);

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

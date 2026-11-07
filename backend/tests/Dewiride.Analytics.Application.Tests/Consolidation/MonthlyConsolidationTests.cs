using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Dewiride.Analytics.Application.Tests.Consolidation;

/// <summary>
/// Covers keeping a website's finished months: what is counted, what is written, and what is
/// never done.
/// </summary>
/// <remarks>
/// A kept month is the only record of its month once its reports go, so the cases here are the
/// ones that would leave a wrong record behind for ever: a month kept half-counted, a website's
/// months kept after the website was removed, and anything at all deleted along the way.
/// </remarks>
public sealed class MonthlyConsolidationTests
{
    private const string Zone = "Etc/UTC";

    private static readonly Guid SiteId = Guid.Parse("0197c0de-0000-7000-8000-000000000001");

    /// <summary>A moment in the middle of March, with January and February finished.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A website added two years before <see cref="Now"/>. Where it is not known when months began to
    /// be kept, they are taken to begin now, so its oldest reports may have gone uncounted and its
    /// oldest report still held is its first day only where an earlier count kept one.
    /// </summary>
    private static readonly SiteRegistration Site = new(SiteId, "example.com", Now.AddYears(-2), Zone);

    /// <summary>
    /// The same website added three months before <see cref="Now"/>, so nothing it ever sent can
    /// have gone yet and its oldest report is its first day.
    /// </summary>
    private static readonly SiteRegistration YoungSite = Site with { AddedAt = Now.AddMonths(-3) };

    /// <summary>An installation that has kept months since before <see cref="Site"/> was added.</summary>
    private static readonly DateTimeOffset KeptAllAlong = Now.AddYears(-3);

    /// <summary>
    /// An installation that began keeping months two months before <see cref="Now"/>, long after the
    /// month <see cref="Site"/> was added in could last be counted.
    /// </summary>
    private static readonly DateTimeOffset KeptLately = Now.AddMonths(-2);

    private static readonly MonthFigures Counted = new(
        PageViews: 120,
        Visitors: 40,
        BestDay: new DateOnly(2026, 1, 12),
        BestDayPageViews: 9,
        ByWeekday: [20, 20, 20, 20, 20, 10, 10],
        ByHour: [.. Enumerable.Repeat(5L, MonthFigures.Hours)]);

    private static readonly ImmutableArray<VerdictTally> Verdicts =
    [
        new(TrafficCategory.LikelyHuman, 30, 100),
        new(TrafficCategory.KnownSearchCrawler, 5, 20),
    ];

    private readonly ISiteRoster _roster = Substitute.For<ISiteRoster>();
    private readonly IMonthConsolidation _months = Substitute.For<IMonthConsolidation>();
    private readonly IPostCatalogue _catalogue = Substitute.For<IPostCatalogue>();
    private readonly IClassificationProgressStore _progress = Substitute.For<IClassificationProgressStore>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeLogger<MonthlyConsolidation> _logger = new();

    public MonthlyConsolidationTests()
    {
        _roster.WhileItExistsAsync(SiteId, Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => DoneAsync(call.Arg<Func<CancellationToken, Task>>(), call.Arg<CancellationToken>()));
        _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>())
            .Returns(Now.AddHours(-1));
        _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>())
            .Returns(Now.AddHours(-1));
        _months.CountAsync(Arg.Any<MonthToCount>(), Arg.Any<Population>(), Arg.Any<CancellationToken>())
            .Returns(Counted);
        _months.CountVerdictsAsync(Arg.Any<MonthToCount>(), Arg.Any<CancellationToken>())
            .Returns(Verdicts);
        _months.SurveyPostsAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<KeptPostMonth>.Empty);
        _months.CountPostsAsync(Arg.Any<MonthToCount>(), Arg.Any<Population>(), Arg.Any<ImmutableArray<string>>(), Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<PathViews>.Empty);
        _catalogue.CountedPathsAsync(SiteId, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<string>.Empty);
        Surveying(new MonthSurvey(new DateOnly(2026, 1, 19), [Month(1), Month(2), Month(3)]));
    }

    [Fact]
    public async Task A_Month_That_Fails_To_Count_Is_Not_Kept_And_Does_Not_Stop_The_Others()
    {
        _months.CountAsync(Arg.Is<MonthToCount>(month => month.Month.Month == 1), Population.People, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The store went away."));

        await CatchUpAsync();

        Kept().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 2, 1));
        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Id.Id == 5203 && record.Level == LogLevel.Warning);
    }

    /// <summary>
    /// A statement the client gave up on is cancelled by its own deadline, not by the run. It is a
    /// month that failed to count like any other — the run goes on and keeps the months after it —
    /// rather than a reason to stop the whole engine.
    /// </summary>
    [Fact]
    public async Task A_Month_Whose_Count_Times_Out_Does_Not_Stop_The_Others()
    {
        _months.CountAsync(Arg.Is<MonthToCount>(month => month.Month.Month == 1), Population.People, Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The client stopped waiting.", new TimeoutException()));

        await CatchUpAsync();

        Kept().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 2, 1));
        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Id.Id == 5203 && record.Level == LogLevel.Warning);
    }

    /// <summary>Shutting down stops a run part-way and writes nothing it had not finished.</summary>
    [Fact]
    public async Task Shutting_Down_Stops_The_Count_And_Keeps_Nothing()
    {
        using var stopping = new CancellationTokenSource();
        _months.CountAsync(Arg.Is<MonthToCount>(month => month.Month.Month == 1), Population.People, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await stopping.CancelAsync();

                return await Task.FromCanceled<MonthFigures>(stopping.Token);
            });

        var act = () => CatchUpAsync(Site, stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _months.DidNotReceive().KeepAsync(Arg.Any<IReadOnlyCollection<ConsolidatedMonth>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>One write per website per run, so a run that returns has kept everything it counted.</summary>
    [Fact]
    public async Task Counted_Months_Are_Kept_In_One_Write()
    {
        await CatchUpAsync();

        await _months.Received(1).KeepAsync(Arg.Any<IReadOnlyCollection<ConsolidatedMonth>>(), Arg.Any<CancellationToken>());
        Kept().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
    }

    /// <summary>
    /// Removing a website deletes what was kept of it, and a count that was under way at the time
    /// would otherwise put its months back for nothing to ever read. The website here is still there
    /// when the run begins and goes once the last month has been counted, so only a question asked
    /// at the moment of writing can see that it went.
    /// </summary>
    [Fact]
    public async Task A_Count_Is_Not_Kept_For_A_Website_Removed_While_It_Ran()
    {
        var removed = false;
        _months.CountVerdictsAsync(Arg.Is<MonthToCount>(month => month.Month.Month == 2), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                removed = true;

                return Verdicts;
            });
        _roster.WhileItExistsAsync(SiteId, Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => removed
                ? Task.FromResult(false)
                : DoneAsync(call.Arg<Func<CancellationToken, Task>>(), call.Arg<CancellationToken>()));

        await CatchUpAsync();

        await _months.Received().CountVerdictsAsync(
            Arg.Is<MonthToCount>(month => month.Month.Month == 2),
            Arg.Any<CancellationToken>());
        await _months.DidNotReceive().KeepAsync(Arg.Any<IReadOnlyCollection<ConsolidatedMonth>>(), Arg.Any<CancellationToken>());
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Id.Id == 5206);
    }

    /// <summary>
    /// A count that looked for what to delete by what the control plane no longer lists would
    /// delete every website's history on an engine pointed at an empty or restored database. So
    /// counting is given nothing that deletes: no purge, and a store that can only be read and
    /// written to.
    /// </summary>
    [Fact]
    public void Nothing_Is_Ever_Deleted_By_Counting()
    {
        var dependencies = typeof(MonthlyConsolidation).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        dependencies.Should().NotContain(typeof(ITelemetryPurge));
        typeof(IMonthConsolidation).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(
                "SurveyAsync",
                "CountAsync",
                "CountVerdictsAsync",
                "KeepAsync",
                "SurveyPostsAsync",
                "CountPostsAsync",
                "KeepPostsAsync");
    }

    /// <summary>
    /// A month's posts are kept on a row of their own: every path a page was delivered at, with the
    /// people's pages beside everybody's — nought where none went to a person — and which list of posts
    /// was counted, under the posts' own version of the arithmetic.
    /// </summary>
    [Fact]
    public async Task A_Months_Posts_Are_Kept_With_The_Peoples_Pages_Beside_Everybodys()
    {
        ImmutableArray<string> paths = ["/a/", "/b/", "/c/"];
        ListingPosts(paths);
        _months.CountPostsAsync(Arg.Any<MonthToCount>(), Population.Everybody, paths, Arg.Any<CancellationToken>())
            .Returns([new PathViews("/a/", 5), new PathViews("/b/", 3)]);
        _months.CountPostsAsync(Arg.Any<MonthToCount>(), Population.People, paths, Arg.Any<CancellationToken>())
            .Returns([new PathViews("/b/", 2)]);

        await CatchUpAsync();

        await _months.Received(1).KeepPostsAsync(Arg.Any<IReadOnlyCollection<ConsolidatedPostMonth>>(), Arg.Any<CancellationToken>());

        var kept = KeptPosts();

        kept.Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        kept[0].Views.Should().Equal(new PostMonthViews("/a/", 5, 0), new PostMonthViews("/b/", 3, 2));
        kept[0].Fingerprint.Should().Be(PostFingerprint.Of(paths));
        kept[0].CountingRules.Should().Be(CountingRules.Posts);
        kept[0].CountedTo.Should().Be(Midnight(2026, 2));
        kept[0].JudgedTo.Should().Be(Midnight(2026, 2));
        kept[0].CountedAt.Should().Be(Now);
    }

    /// <summary>
    /// A month whose posts fail to count is left as it was and tried again, and stops neither the posts
    /// of the months after it nor any month's totals.
    /// </summary>
    [Fact]
    public async Task A_Months_Posts_That_Fail_To_Count_Stop_Neither_The_Other_Months_Posts_Nor_Their_Totals()
    {
        ListingPosts(["/a/"]);
        _months.CountPostsAsync(
                Arg.Is<MonthToCount>(month => month.Month.Month == 1),
                Population.Everybody,
                Arg.Any<ImmutableArray<string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The store went away."));

        await CatchUpAsync();

        Kept().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        KeptPosts().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 2, 1));
        _logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Id.Id == 5208 && record.Level == LogLevel.Warning);
    }

    /// <summary>
    /// The months' totals are kept before the posts are counted, so a pass over posts that cannot even
    /// read what was kept of them leaves the totals kept.
    /// </summary>
    [Fact]
    public async Task A_Pass_Over_Posts_That_Fails_Leaves_The_Months_Totals_Kept()
    {
        ListingPosts(["/a/"]);
        _months.SurveyPostsAsync(SiteId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The store went away."));

        Func<Task> act = CatchUpAsync;

        await act.Should().ThrowAsync<InvalidOperationException>();
        Kept().Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        await _months.DidNotReceive().KeepPostsAsync(Arg.Any<IReadOnlyCollection<ConsolidatedPostMonth>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A website that lists no posts has nothing to count, so nothing about its posts is read at all —
    /// which is most websites, on every run.
    /// </summary>
    [Fact]
    public async Task A_Website_That_Lists_No_Posts_Has_None_Counted()
    {
        await CatchUpAsync();

        await _months.DidNotReceive().SurveyPostsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _months.DidNotReceive().CountPostsAsync(
            Arg.Any<MonthToCount>(),
            Arg.Any<Population>(),
            Arg.Any<ImmutableArray<string>>(),
            Arg.Any<CancellationToken>());
        await _months.DidNotReceive().KeepPostsAsync(Arg.Any<IReadOnlyCollection<ConsolidatedPostMonth>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The website's first day is what an average per day starts counting from, so it is kept with
    /// the month it falls in and with no other.
    /// </summary>
    [Fact]
    public async Task A_First_Day_Is_Kept_Only_On_The_Month_That_Holds_It()
    {
        await CatchUpAsync(YoungSite, TestContext.Current.CancellationToken);

        var kept = Kept();

        kept[0].FirstDay.Should().Be(new DateOnly(2026, 1, 19));
        kept[0].DaysMeasured.Should().Be(13);
        kept[1].FirstDay.Should().BeNull();
        kept[1].DaysMeasured.Should().Be(28);
    }

    /// <summary>
    /// The oldest report still held is the website's first only while nothing older was kept. A
    /// website whose earlier months were kept before their reports went began before it.
    /// </summary>
    [Fact]
    public async Task A_First_Day_Is_Not_Taken_From_A_Month_After_One_Already_Kept()
    {
        var december = new SurveyedMonth(
            new DateOnly(2025, 12, 1),
            Window(new DateOnly(2025, 12, 1)),
            new KeptMonth(Zone, new DateOnly(2025, 12, 3), RulesetVersion.Current, Midnight(2026, 1), CountingRules.Current, Midnight(2026, 1), Midnight(2026, 1), Midnight(2026, 1)));
        Surveying(new MonthSurvey(new DateOnly(2026, 1, 19), [december, Month(1), Month(2), Month(3)]));

        await CatchUpAsync();

        Kept().Should().OnlyContain(month => month.FirstDay == null);
    }

    /// <summary>
    /// A website added long before this installation began keeping months may have sent reports that
    /// went before any month of it was kept. With nothing kept that says when it was first measured,
    /// it keeps no first day at all: the oldest report still held then only says how far back the
    /// store reaches, and a first day kept from it could be wrong for ever.
    /// </summary>
    [Fact]
    public async Task No_First_Day_Is_Kept_Where_Months_Began_To_Be_Kept_After_The_Website_Could_Lose_Some()
    {
        Surveying(new MonthSurvey(new DateOnly(2026, 1, 19), [Month(1), Month(2), Month(3)]) { KeepingSince = KeptLately });

        await CatchUpAsync();

        var kept = Kept();

        kept[0].FirstDay.Should().BeNull();
        kept[0].DaysMeasured.Should().Be(31);
    }

    /// <summary>
    /// A website first visited long after it was added, on an installation that has kept months all
    /// along, cannot have sent anything that went uncounted: every month it sent anything in would
    /// have been kept. Its oldest report is its first day, however long ago it was added.
    /// </summary>
    [Fact]
    public async Task A_First_Day_Is_Kept_For_A_Website_First_Visited_Long_After_It_Was_Added_Where_Months_Were_Kept_All_Along()
    {
        Surveying(new MonthSurvey(new DateOnly(2026, 1, 19), [Month(1), Month(2), Month(3)]) { KeepingSince = KeptAllAlong });

        await CatchUpAsync();

        var kept = Kept();

        kept[0].FirstDay.Should().Be(new DateOnly(2026, 1, 19));
        kept[0].DaysMeasured.Should().Be(13);
        kept[1].FirstDay.Should().BeNull();
        kept[1].DaysMeasured.Should().Be(28);
    }

    /// <summary>
    /// A month kept as measured on none of its days, after a change of zone left it in front of the
    /// website's first report, stays that way when it is counted again later — under newer counting
    /// rules, here — however long ago the website was added.
    /// </summary>
    [Fact]
    public async Task A_Month_Kept_Before_The_First_Day_Stays_Measured_On_None_Of_Its_Days_When_Counted_Again()
    {
        var january = new SurveyedMonth(
            new DateOnly(2026, 1, 1),
            Window(new DateOnly(2026, 1, 1)),
            new KeptMonth(Zone, null, RulesetVersion.Current, Midnight(2026, 2), CountingRules.Current - 1, Midnight(2026, 2), Midnight(2026, 2), Midnight(2026, 2)));
        Surveying(new MonthSurvey(new DateOnly(2026, 2, 1), [january, Month(2), Month(3)]) { KeepingSince = KeptAllAlong });

        await CatchUpAsync();

        var kept = Kept();

        kept.Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        kept[0].FirstDay.Should().BeNull();
        kept[0].DaysMeasured.Should().Be(0);
        kept[1].FirstDay.Should().Be(new DateOnly(2026, 2, 1));
        kept[1].DaysMeasured.Should().Be(28);
    }

    /// <summary>
    /// A first day kept with an earlier count is carried, and while its report is still held it is
    /// read again on the calendar the website keeps now: after a change of zone the first report
    /// falls on the first of February, so January — kept on the old calendar — was measured on none
    /// of its days, whatever the website's age.
    /// </summary>
    [Fact]
    public async Task A_First_Day_Kept_Before_A_Change_Of_Zone_Moves_To_The_Day_It_Falls_On_Now()
    {
        var january = new SurveyedMonth(
            new DateOnly(2026, 1, 1),
            Window(new DateOnly(2026, 1, 1)),
            new KeptMonth("America/Los_Angeles", new DateOnly(2026, 1, 31), RulesetVersion.Current, Midnight(2026, 2), CountingRules.Current, Midnight(2026, 2), Midnight(2026, 2), Midnight(2026, 2)));
        Surveying(new MonthSurvey(new DateOnly(2026, 2, 1), [january, Month(2), Month(3)]));

        await CatchUpAsync();

        var kept = Kept();

        kept[0].Month.Month.Should().Be(new DateOnly(2026, 1, 1));
        kept[0].FirstDay.Should().BeNull();
        kept[0].DaysMeasured.Should().Be(0);
        kept[1].FirstDay.Should().Be(new DateOnly(2026, 2, 1));
        kept[1].DaysMeasured.Should().Be(28);
    }

    /// <summary>
    /// A change to a zone behind the old one can put the first report in the month before the one
    /// that kept the first day. Where that month can no longer be counted the day could be kept
    /// nowhere on the new calendar, so the month that held it keeps it when it is counted again,
    /// rather than losing the website's first day for good.
    /// </summary>
    [Fact]
    public async Task A_Kept_First_Day_Stays_Where_The_Month_It_Would_Move_Into_Can_No_Longer_Be_Counted()
    {
        var march = new SurveyedMonth(new DateOnly(2025, 3, 1), Window(new DateOnly(2025, 3, 1)), null);
        var april = new SurveyedMonth(
            new DateOnly(2025, 4, 1),
            Window(new DateOnly(2025, 4, 1)),
            new KeptMonth("Asia/Kolkata", new DateOnly(2025, 4, 1), RulesetVersion.Current, Midnight(2025, 5), CountingRules.Current, Midnight(2025, 5), Midnight(2025, 5), Midnight(2025, 5)));
        Surveying(new MonthSurvey(new DateOnly(2025, 3, 31), [march, april, Month(3)]) { KeepingSince = KeptAllAlong });

        await CatchUpAsync();

        var kept = Kept().Should().ContainSingle().Subject;

        kept.Month.Month.Should().Be(new DateOnly(2025, 4, 1));
        kept.FirstDay.Should().Be(new DateOnly(2025, 4, 1));
        kept.DaysMeasured.Should().Be(30);
    }

    /// <summary>
    /// A website changed to a zone ahead of the one its first month was kept in can find its first
    /// report in the next month: the first day keeps its own month, and the month kept before it
    /// was measured on none of its days. Nothing the website sent can have gone yet, so its oldest
    /// report is its first day whichever month of the calendar now holds it.
    /// </summary>
    [Fact]
    public async Task A_Month_A_Change_Of_Zone_Leaves_Before_The_First_Day_Is_Measured_On_None_Of_Its_Days()
    {
        var january = new SurveyedMonth(
            new DateOnly(2026, 1, 1),
            Window(new DateOnly(2026, 1, 1)),
            new KeptMonth("America/Los_Angeles", new DateOnly(2026, 1, 31), RulesetVersion.Current, Midnight(2026, 2), CountingRules.Current, Midnight(2026, 2), Midnight(2026, 2), Midnight(2026, 2)));
        Surveying(new MonthSurvey(new DateOnly(2026, 2, 1), [january, Month(2), Month(3)]));

        await CatchUpAsync(YoungSite, TestContext.Current.CancellationToken);

        var kept = Kept();

        kept.Select(month => month.Month.Month).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        kept[0].FirstDay.Should().BeNull();
        kept[0].DaysMeasured.Should().Be(0);
        kept[1].FirstDay.Should().Be(new DateOnly(2026, 2, 1));
        kept[1].DaysMeasured.Should().Be(28);
    }

    /// <summary>
    /// Verdicts are stored before judging's bookmark moves past them, so a bookmark read before the
    /// months can only understate what the count saw, which counts a month again later; read after,
    /// it could claim a month whole whose verdicts the count never saw.
    /// </summary>
    [Fact]
    public async Task Judging_Is_Read_Before_The_Months_It_Qualifies()
    {
        await CatchUpAsync();

        Received.InOrder(() =>
        {
            _ = _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>());
            _ = _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>());
            _ = _months.SurveyAsync(SiteId, Zone, Now, Arg.Any<CancellationToken>());
            _ = _months.CountAsync(
                Arg.Is<MonthToCount>(month => month.Month.Month == 1),
                Population.Everybody,
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_Kept_Month_Says_How_It_Was_Counted()
    {
        await CatchUpAsync();

        var january = Kept()[0];

        january.Month.Should().Be(new MonthToCount(SiteId, Zone, new DateOnly(2026, 1, 1), Window(new DateOnly(2026, 1, 1))));
        january.Everybody.Should().Be(Counted);
        january.People.Should().Be(Counted);
        january.Verdicts.Should().Equal(Verdicts);
        january.Ruleset.Should().Be(RulesetVersion.Current);
        january.CountingRules.Should().Be(CountingRules.Current);
        january.CountedTo.Should().Be(Midnight(2026, 2));
        january.CountedAt.Should().Be(Now);
    }

    /// <summary>
    /// How far judging had reached is kept no further than the month itself, so a month counted
    /// long after judging finished with it reads as whole rather than as reaching into the next.
    /// </summary>
    [Fact]
    public async Task How_Far_Judging_Had_Reached_Is_Kept_No_Further_Than_The_Month()
    {
        await CatchUpAsync();

        var january = Kept()[0];

        january.JudgedTo.Should().Be(Midnight(2026, 2));
        january.RulesetJudgedTo.Should().Be(Midnight(2026, 2));
    }

    /// <summary>
    /// Where judging stopped part of the way through a month, that is where its people stop being
    /// complete — and the settled point is no later than a visit's timeout before the present.
    /// </summary>
    [Fact]
    public async Task How_Far_Judging_Had_Reached_Is_Kept_Where_It_Stopped_Inside_The_Month()
    {
        var stopped = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);
        _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);

        await CatchUpAsync();

        var january = Kept()[0];

        january.JudgedTo.Should().Be(stopped);
        january.RulesetJudgedTo.Should().Be(stopped);
    }

    /// <summary>
    /// The store keeps every instant to the millisecond, so how far judging had reached is kept to
    /// the millisecond too, rounded down — never claiming judging reached a fraction further than it
    /// did.
    /// </summary>
    [Fact]
    public async Task How_Far_Judging_Had_Reached_Is_Kept_To_The_Whole_Millisecond()
    {
        var stopped = Stalled();
        _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);
        _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);

        await CatchUpAsync();

        var january = Kept()[0];

        january.JudgedTo.Should().Be(WholeMilliseconds(stopped));
        january.RulesetJudgedTo.Should().Be(WholeMilliseconds(stopped));
        (january.JudgedTo.UtcTicks % TimeSpan.TicksPerMillisecond).Should().Be(0);
    }

    /// <summary>
    /// Judging stalled part-way through a millisecond of a month — switched off, failing, or a newer
    /// ruleset's walk still under way — leaves the month as the store kept it: the instant it reads
    /// back is the instant judging is at, so nothing has moved and the month is not counted again on
    /// every run until judging does.
    /// </summary>
    [Fact]
    public async Task A_Month_Kept_Where_Judging_Stalled_Is_Not_Counted_Again_While_It_Stays_There()
    {
        var stopped = Stalled();
        var held = WholeMilliseconds(stopped);
        _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);
        _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns(stopped);
        var january = Month(1);
        var keptWhereItStalled = january with
        {
            Kept = new KeptMonth(Zone, new DateOnly(2026, 1, 19), RulesetVersion.Current, held, CountingRules.Current, january.Window.To, held, Midnight(2026, 2)),
        };
        Surveying(new MonthSurvey(new DateOnly(2026, 1, 19), [keptWhereItStalled, Month(3)]));

        await CatchUpAsync();

        await _months.DidNotReceive().CountAsync(Arg.Any<MonthToCount>(), Arg.Any<Population>(), Arg.Any<CancellationToken>());
        await _months.DidNotReceive().KeepAsync(Arg.Any<IReadOnlyCollection<ConsolidatedMonth>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A website judging has never started on has nothing judged in any of its months.</summary>
    [Fact]
    public async Task A_Month_Judging_Never_Started_On_Is_Kept_As_Judged_To_Its_First_Instant()
    {
        _progress.CaughtUpToAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _progress.JudgedThroughAsync(SiteId, RulesetVersion.Current, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);

        await CatchUpAsync();

        var january = Kept()[0];

        january.JudgedTo.Should().Be(Midnight(2026, 1));
        january.RulesetJudgedTo.Should().Be(Midnight(2026, 1));
    }

    [Fact]
    public async Task A_Website_With_Nothing_To_Count_Is_Neither_Counted_Nor_Written()
    {
        Surveying(new MonthSurvey(null, []));

        await CatchUpAsync();

        await _months.DidNotReceive().CountAsync(Arg.Any<MonthToCount>(), Arg.Any<Population>(), Arg.Any<CancellationToken>());
        await _roster.DidNotReceive().WhileItExistsAsync(
            Arg.Any<Guid>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
        await _months.DidNotReceive().KeepAsync(Arg.Any<IReadOnlyCollection<ConsolidatedMonth>>(), Arg.Any<CancellationToken>());
    }

    private Task CatchUpAsync() => CatchUpAsync(Site, CancellationToken.None);

    private Task CatchUpAsync(SiteRegistration site, CancellationToken cancellationToken) =>
        new MonthlyConsolidation(
                _roster,
                _months,
                _catalogue,
                _progress,
                Options.Create(new ConsolidationOptions()),
                Options.Create(new ClassificationOptions()),
                _clock,
                _logger)
            .CatchUpAsync(site, cancellationToken);

    private void Surveying(MonthSurvey survey) =>
        _months.SurveyAsync(SiteId, Zone, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(survey);

    /// <summary>What the website's roster does while the website exists: the work, and then yes.</summary>
    private static async Task<bool> DoneAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await work(cancellationToken);

        return true;
    }

    /// <summary>What the one write held.</summary>
    private ConsolidatedMonth[] Kept() =>
    [
        .. _months.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMonthConsolidation.KeepAsync))
            .Select(call => (IReadOnlyCollection<ConsolidatedMonth>)call.GetArguments()[0]!)
            .Single(),
    ];

    /// <summary>What the one write of posts held.</summary>
    private ConsolidatedPostMonth[] KeptPosts() =>
    [
        .. _months.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMonthConsolidation.KeepPostsAsync))
            .Select(call => (IReadOnlyCollection<ConsolidatedPostMonth>)call.GetArguments()[0]!)
            .Single(),
    ];

    /// <summary>Has the website list posts at these paths.</summary>
    private void ListingPosts(ImmutableArray<string> paths) =>
        _catalogue.CountedPathsAsync(SiteId, Arg.Any<CancellationToken>()).Returns(paths);

    /// <summary>
    /// An instant inside January that judging stopped at, with the part of a millisecond a bookmark
    /// keeps and the telemetry store does not.
    /// </summary>
    private static DateTimeOffset Stalled() =>
        new DateTimeOffset(2026, 1, 20, 23, 40, 12, 345, TimeSpan.Zero).AddTicks(6780);

    private static DateTimeOffset WholeMilliseconds(DateTimeOffset instant) =>
        new(instant.UtcTicks - (instant.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    /// <summary>A month of 2026 nothing has been kept of.</summary>
    private static SurveyedMonth Month(int month)
    {
        var first = new DateOnly(2026, month, 1);

        return new SurveyedMonth(first, Window(first), null);
    }

    private static TimeRange Window(DateOnly month) =>
        new(Midnight(month.Year, month.Month), Midnight(month.AddMonths(1).Year, month.AddMonths(1).Month));

    private static DateTimeOffset Midnight(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);
}

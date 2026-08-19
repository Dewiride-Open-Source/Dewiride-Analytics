using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that how a page was read is counted once per reading, and never invented.
/// </summary>
/// <remarks>
/// A reading is one visitor on one page. The browser reports its progress repeatedly while the
/// page is open and every report carries a running total, so a real store is the only place the
/// two properties that matter can be shown to hold at once: that a reading is worth its largest
/// report rather than the sum of them, and that a reading nobody was watching stays countable
/// while staying out of every figure taken over the ones that were.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteEngagementTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The property only a real store proves. Progress reports are cumulative, so summing them
    /// would multiply one reading by however many times it announced itself.
    /// </summary>
    [Fact]
    public async Task A_Reading_Is_Worth_Its_Largest_Report_Rather_Than_All_Of_Them()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Opened(siteId, Midnight.AddHours(1), "visitor-a"),
            Progressed(siteId, Midnight.AddHours(1).AddSeconds(15), "visitor-a", 15000, 20),
            Progressed(siteId, Midnight.AddHours(1).AddSeconds(45), "visitor-a", 45000, 55),
            Left(siteId, Midnight.AddHours(1).AddSeconds(60), "visitor-a", 60000, 80));

        var reading = await ReadingOf(siteId);

        reading.TotalReadings.Should().Be(1);
        reading.MeasuredReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(60000);
        reading.Reach.Whole.Should().Be(1);
    }

    /// <summary>
    /// A report that never arrives has to cost nothing. A phone that dismisses the tab sends no
    /// closing report at all, and the reading is still worth whatever the last progress report
    /// said it was.
    /// </summary>
    [Fact]
    public async Task A_Reading_That_Never_Announced_Its_End_Is_Worth_What_It_Last_Said()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Opened(siteId, Midnight.AddHours(1), "visitor-a"),
            Progressed(siteId, Midnight.AddHours(1).AddSeconds(15), "visitor-a", 15000, 30));

        var reading = await ReadingOf(siteId);

        reading.MeasuredReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(15000);
        reading.Reach.Quarter.Should().Be(1);
    }

    /// <summary>
    /// The distinction the whole product rests on. A visit that only a server saw is a visit
    /// nobody was watching read, which is a different fact from a visit where nobody stayed.
    /// </summary>
    [Fact]
    public async Task A_Visit_Only_A_Server_Saw_Is_Unmeasured_Rather_Than_Unengaged()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Opened(siteId, Midnight.AddHours(1), "visitor-a"),
            Left(siteId, Midnight.AddHours(1).AddSeconds(30), "visitor-a", 30000, 60),
            Opened(siteId, Midnight.AddHours(2), "crawler-b", surface: IngestSurface.NextJsMiddleware),
            Opened(siteId, Midnight.AddHours(3), "crawler-c", surface: IngestSurface.NextJsMiddleware));

        var reading = await ReadingOf(siteId);

        reading.TotalReadings.Should().Be(3);
        reading.MeasuredReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(30000);
        reading.Reach.Top.Should().Be(0);
    }

    /// <summary>
    /// The bands are what a bar is drawn from, so they have to account for every measured reading
    /// exactly once: no gap, no overlap, and nothing unmeasured smuggled into the first one.
    /// </summary>
    [Fact]
    public async Task The_Depth_Bands_Account_For_Every_Measured_Reading_Exactly_Once()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 1000, 0, path: "/a"),
            Left(siteId, Midnight.AddHours(1), "visitor-b", 1000, 24, path: "/b"),
            Left(siteId, Midnight.AddHours(1), "visitor-c", 1000, 25, path: "/c"),
            Left(siteId, Midnight.AddHours(1), "visitor-d", 1000, 49, path: "/d"),
            Left(siteId, Midnight.AddHours(1), "visitor-e", 1000, 50, path: "/e"),
            Left(siteId, Midnight.AddHours(1), "visitor-f", 1000, 74, path: "/f"),
            Left(siteId, Midnight.AddHours(1), "visitor-g", 1000, 75, path: "/g"),
            Left(siteId, Midnight.AddHours(1), "visitor-h", 1000, 100, path: "/h"),
            Opened(siteId, Midnight.AddHours(2), "crawler-i", surface: IngestSurface.NextJsMiddleware));

        var reading = await ReadingOf(siteId);

        reading.MeasuredReadings.Should().Be(8);
        reading.Reach.Top.Should().Be(2);
        reading.Reach.Quarter.Should().Be(2);
        reading.Reach.Half.Should().Be(2);
        reading.Reach.Whole.Should().Be(2);
    }

    /// <summary>
    /// Presence of interaction is a signal; what was typed is not, and is never collected. Both
    /// kinds count the same, and a reading that had neither is not counted as having had one.
    /// </summary>
    [Fact]
    public async Task A_Pointer_And_A_Key_Both_Count_As_Having_Done_Something()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 1000, 10, path: "/a", pointer: true),
            Left(siteId, Midnight.AddHours(1), "visitor-b", 1000, 10, path: "/b", keyboard: true),
            Left(siteId, Midnight.AddHours(1), "visitor-c", 1000, 10, path: "/c"));

        var reading = await ReadingOf(siteId);

        reading.MeasuredReadings.Should().Be(3);
        reading.InteractedReadings.Should().Be(2);
    }

    /// <summary>
    /// The middle reading rather than the mean one. One page left open through a lunch break
    /// would otherwise decide what a typical reader is said to have done.
    /// </summary>
    [Fact]
    public async Task Attention_Is_The_Middle_Reading_Rather_Than_The_Average()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 4000, 10, path: "/a"),
            Left(siteId, Midnight.AddHours(1), "visitor-b", 5000, 10, path: "/b"),
            Left(siteId, Midnight.AddHours(1), "visitor-c", 3600000, 10, path: "/c"));

        var reading = await ReadingOf(siteId);

        reading.MedianEngagedMs.Should().Be(5000);
    }

    /// <summary>
    /// Both halves of the measurement watch the same delivery, and one of them cannot see any of
    /// this. A reader watched by both is one reading, worth what the half that could see it saw.
    /// </summary>
    [Fact]
    public async Task A_Reading_Both_Halves_Saw_Is_One_Reading()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Opened(siteId, Midnight.AddHours(1), "browser-key", correlationId: "abc"),
            Left(siteId, Midnight.AddHours(1).AddSeconds(20), "browser-key", 20000, 40),
            Opened(
                siteId,
                Midnight.AddHours(1),
                "server-key",
                surface: IngestSurface.NextJsMiddleware,
                correlationId: "abc"));

        var reading = await ReadingOf(siteId);

        reading.TotalReadings.Should().Be(1);
        reading.MeasuredReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(20000);
    }

    [Fact]
    public async Task Pages_Are_Ranked_By_The_Attention_They_Held()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 5000, 30, path: "/quick"),
            Left(siteId, Midnight.AddHours(1), "visitor-b", 90000, 30, path: "/long"),
            Left(siteId, Midnight.AddHours(1), "visitor-c", 40000, 30, path: "/middling"));

        var pages = await PageOfReadings(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/long", "/middling", "/quick");
        pages.LongestMedianEngagedMs.Should().Be(90000);
        pages.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task Pages_Are_Ranked_By_How_Far_Down_Readers_Got_When_Asked_To_Be()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 90000, 10, path: "/skimmed"),
            Left(siteId, Midnight.AddHours(1), "visitor-b", 5000, 95, path: "/finished"));

        var pages = await PageOfReadings(siteId, EngagementRanking.Depth);

        pages.Pages.Select(page => page.Path).Should().Equal("/finished", "/skimmed");
        pages.Pages[0].MedianScrollDepthPercent.Should().Be(95);
    }

    /// <summary>
    /// A page seen solely by a reporter on the website's own server has nothing to say about how
    /// it was read, and a row of noughts beside it would say something quite different.
    /// </summary>
    [Fact]
    public async Task A_Page_Nothing_Could_Be_Measured_On_Is_Left_Off_The_List()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 5000, 30, path: "/read"),
            Opened(
                siteId,
                Midnight.AddHours(2),
                "crawler-b",
                path: "/crawled",
                surface: IngestSurface.NextJsMiddleware));

        var pages = await PageOfReadings(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/read");
        pages.TotalPages.Should().Be(1);
    }

    /// <summary>
    /// The ordering is total, so successive slices neither repeat a page nor skip one even where
    /// several pages held identical attention.
    /// </summary>
    [Fact]
    public async Task Slices_Of_The_List_Neither_Repeat_A_Page_Nor_Skip_One()
    {
        var siteId = Guid.NewGuid();
        var events = Enumerable.Range(0, 9)
            .Select(rank => Left(
                siteId,
                Midnight.AddHours(1),
                $"visitor-{rank}",
                5000,
                30,
                path: $"/page-{rank}"))
            .ToArray();

        await WriteAsync(events);

        var first = await PageOfReadings(siteId, limit: 4);
        var second = await PageOfReadings(siteId, limit: 4, offset: 4);
        var third = await PageOfReadings(siteId, limit: 4, offset: 8);

        var walked = first.Pages.Concat(second.Pages).Concat(third.Pages)
            .Select(page => page.Path)
            .ToArray();

        walked.Should().HaveCount(9).And.OnlyHaveUniqueItems();
        first.TotalPages.Should().Be(9);
        third.TotalPages.Should().Be(9);
    }

    [Fact]
    public async Task Another_Website_Is_No_Part_Of_This_One()
    {
        var siteId = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 5000, 30),
            Left(elsewhere, Midnight.AddHours(1), "visitor-b", 90000, 90));

        var reading = await ReadingOf(siteId);

        reading.TotalReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(5000);
    }

    [Fact]
    public async Task Activity_Outside_The_Window_Is_No_Part_Of_It()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            Left(siteId, Midnight.AddHours(1), "visitor-a", 5000, 30),
            Left(siteId, Midnight.AddDays(2), "visitor-b", 90000, 90, path: "/later"));

        var reading = await ReadingOf(siteId);

        reading.TotalReadings.Should().Be(1);
        reading.MedianEngagedMs.Should().Be(5000);
    }

    [Fact]
    public async Task A_Window_With_No_Traffic_Answers_With_Noughts_Rather_Than_Nothing()
    {
        var siteId = Guid.NewGuid();

        var reading = await ReadingOf(siteId);
        var pages = await PageOfReadings(siteId);

        reading.TotalReadings.Should().Be(0);
        reading.MeasuredReadings.Should().Be(0);
        reading.MedianEngagedMs.Should().Be(0);
        reading.Reach.Should().Be(default(ScrollReach));
        pages.Pages.Should().BeEmpty();
        pages.TotalPages.Should().Be(0);
        pages.LongestMedianEngagedMs.Should().Be(0);
    }

    /// <summary>
    /// A path is written by whoever asked for it, and it is grouped on and read back rather than
    /// built into the statement, which is the whole of what a hostile one can do here.
    /// </summary>
    [Fact]
    public async Task A_Page_Named_To_Break_The_Statement_Is_Read_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "/'); DROP TABLE events; --";

        await WriteAsync(Left(siteId, Midnight.AddHours(1), "visitor-a", 5000, 30, path: hostile));

        var pages = await PageOfReadings(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal(hostile);
    }

    private Task<SiteEngagement> ReadingOf(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteEngagementAsync(
            Scope(siteId),
            new SiteEngagementQuery(new TimeRange(Midnight, Midnight.AddDays(1))),
            Cancellation.Token);

    private Task<SitePageEngagement> PageOfReadings(
        Guid siteId,
        EngagementRanking ranking = EngagementRanking.Attention,
        int limit = 10,
        int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSitePageEngagementAsync(
            Scope(siteId),
            new SitePageEngagementQuery(
                new TimeRange(Midnight, Midnight.AddDays(1)),
                ranking,
                limit,
                offset),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    /// <summary>A page being delivered, which measures nothing yet.</summary>
    private static RawEvent Opened(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path = "/",
        IngestSurface surface = IngestSurface.BrowserTracker,
        string? correlationId = null) =>
        Reported(siteId, at, visitorKey, path, EventKind.PageView, surface, correlationId);

    /// <summary>A page saying how the reading is going, part of the way through.</summary>
    private static RawEvent Progressed(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        int engagedMs,
        byte depth,
        string path = "/") =>
        Reported(siteId, at, visitorKey, path, EventKind.Engagement) with
        {
            EngagedMs = engagedMs,
            ScrollDepthPercent = depth,
            HadPointerInteraction = false,
            HadKeyboardInteraction = false,
        };

    /// <summary>A page saying what the reading came to, as the reader leaves it.</summary>
    private static RawEvent Left(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        int engagedMs,
        byte depth,
        string path = "/",
        bool pointer = false,
        bool keyboard = false) =>
        Reported(siteId, at, visitorKey, path, EventKind.Exit) with
        {
            EngagedMs = engagedMs,
            ScrollDepthPercent = depth,
            HadPointerInteraction = pointer,
            HadKeyboardInteraction = keyboard,
        };

    private static RawEvent Reported(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        EventKind kind,
        IngestSurface surface = IngestSurface.BrowserTracker,
        string? correlationId = null) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = kind,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            CorrelationId = correlationId,
        };
}

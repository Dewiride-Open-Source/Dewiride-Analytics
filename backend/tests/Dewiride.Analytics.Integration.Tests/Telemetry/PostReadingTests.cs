using System.Collections.Immutable;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Posts;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that one post is read where Insights reads it and as its row on the list of pages: counted at its
/// paths once both halves of the measurement are reconciled, at exactly the paths it is listed at however
/// they are spelled, and from the newest count of each month kept.
/// </summary>
/// <remarks>
/// The two questions about one post are the only ones that send a post's path to the store, bound to the
/// statement. What they send is matched exactly as written, so a path a website's sitemap spells in an
/// unusual way is still found, and a path one character away is not.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PostReadingTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = StillHeld.Midnight;

    private ITelemetryQueries Queries => stack.Services.GetRequiredService<ITelemetryQueries>();

    /// <summary>
    /// Paths a website's sitemap may give a post: as long as any a post may be listed at, and holding what a
    /// string literal, an escape or a placeholder is written with.
    /// </summary>
    public static TheoryData<string> AwkwardPaths =>
    [
        $"/{new string('a', Site.MaxSitemapAddressLength - 1)}",
        "/posts/it's-here/",
        "/posts/\"quoted\"/",
        "/posts/back\\slash/",
        "/posts/100%25/",
        "/posts/one+two/",
        "/{post_paths:Array(String)}/",
    ];

    /// <summary>
    /// A server's report of a post is told whose it was by the browser that echoed another page of the same
    /// visit, as the list of pages tells it, so one post read once is one view, as its row there says. Kept
    /// to the post's paths before both halves were reconciled, the server's report could not be told whose it
    /// was, and the post would have two readers.
    /// </summary>
    [Fact]
    public async Task One_Posts_Views_Fold_A_Servers_Report_Into_The_Visitor_A_Browser_Named_On_Another_Page()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(1);

        await WriteAsync(
            FromServer(siteId, at, "as-the-server-saw-them", "/landing/", "delivery-1"),
            FromBrowser(siteId, at, "as-the-browser-saw-them", "/landing/", "delivery-1"),
            FromServer(siteId, at.AddMinutes(1), "as-the-server-saw-them", "/posts/a/"),
            FromBrowser(siteId, at.AddMinutes(1), "as-the-browser-saw-them", "/posts/a/"));

        var series = await SeriesAsync(siteId, ["/posts/a/"]);
        var pages = await Queries.GetSitePagesAsync(Scope(siteId), new SitePagesQuery(Day(), 10), Cancellation.Token);

        series.Should().ContainSingle().Which.Value.Should().Be(1);
        pages.Pages.Should().ContainSingle(page => page.Path == "/posts/a/").Which.PageViews.Should().Be(1);
    }

    /// <summary>
    /// Over a stretch that is one bucket, a post's series is its rows on the list of pages for the same
    /// stretch — its own path and where it landed, added up — for everybody and for the people alike.
    /// </summary>
    /// <param name="population">Who the pages were delivered to.</param>
    [Theory]
    [InlineData(Population.Everybody)]
    [InlineData(Population.People)]
    public async Task One_Posts_Series_Over_One_Bucket_Is_Its_Rows_On_The_List_Of_Pages(Population population)
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(2);
        var reader = $"reader-{siteId:n}";
        var scraper = $"scraper-{siteId:n}";

        await WriteAsync(
            FromBrowser(siteId, at, reader, "/posts/old/"),
            FromBrowser(siteId, at.AddSeconds(1), reader, "/posts/new/"),
            FromBrowser(siteId, at.AddSeconds(2), reader, "/elsewhere/"),
            FromServer(siteId, at.AddSeconds(10), scraper, "/posts/new/"),
            FromServer(siteId, at.AddSeconds(11), scraper, "/posts/new/"),
            FromServer(siteId, at.AddSeconds(12), scraper, "/posts/old/"));
        await KeptRecord.JudgedAsync(stack, siteId, reader, at, at.AddSeconds(2), TrafficCategory.LikelyHuman);
        await KeptRecord.JudgedAsync(stack, siteId, scraper, at.AddSeconds(10), at.AddSeconds(12), TrafficCategory.ContentScraper);

        var series = await SeriesAsync(siteId, ["/posts/old/", "/posts/new/"], population);
        var pages = await Queries.GetSitePagesAsync(
            Scope(siteId),
            new SitePagesQuery(Day(), 10) { Population = population },
            Cancellation.Token);
        var rows = pages.Pages.Where(page => page.Path is "/posts/old/" or "/posts/new/").Sum(page => page.PageViews);

        rows.Should().Be(population == Population.People ? 2 : 5);
        series.Should().ContainSingle().Which.Value.Should().Be(rows);
    }

    /// <summary>
    /// A post is counted at its own path and where it landed, and at no path that merely looks like either:
    /// another case, no trailing slash, the same letter escaped, or a path beneath it.
    /// </summary>
    [Fact]
    public async Task One_Posts_Series_Counts_Its_Own_Path_And_Where_It_Landed_And_No_Path_Like_Them()
    {
        var siteId = Guid.NewGuid();
        var at = Midnight.AddHours(3);
        string[] lookalikes = ["/Posts/a/", "/posts/a", "/posts/%61/", "/posts/a/more/", "/posts/b/a/"];

        await WriteAsync(
        [
            FromBrowser(siteId, at, "reader", "/posts/a/"),
            FromBrowser(siteId, at.AddSeconds(1), "reader", "/posts/b/"),
            .. lookalikes.Select((path, index) => FromBrowser(siteId, at.AddSeconds(2 + index), "reader", path)),
        ]);

        var series = await SeriesAsync(siteId, ["/posts/a/", "/posts/b/"]);

        series.Should().ContainSingle().Which.Value.Should().Be(2);
    }

    /// <summary>
    /// However a post's path is spelled, it is bound to the statement and matched exactly as written, in the
    /// reports and in the months kept alike; a path one character different finds nothing in either.
    /// </summary>
    /// <param name="path">The post's path.</param>
    [Theory]
    [MemberData(nameof(AwkwardPaths))]
    public async Task A_Long_Awkward_Or_Hostile_Path_Is_Bound_And_Matched_Exactly_As_Written(string path)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var calendar = new ZoneCalendar(site.TimeZoneId);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(Midnight)).AddMonths(-1);
        var other = $"{path[..^1]}{(path[^1] == 'x' ? 'y' : 'x')}";

        await WriteAsync(FromBrowser(site.Id, Midnight.AddHours(4), "reader", path));
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, month, PostFingerprint.Of([path]), Midnight, new PostMonthViews(path, 3, 1)));

        var found = await SeriesAsync(site.Id, [path]);
        var missed = await SeriesAsync(site.Id, [other]);
        var kept = await KeptAsync(site.Id, [path]);
        var keptMissed = await KeptAsync(site.Id, [other]);

        found.Should().ContainSingle().Which.Value.Should().Be(1);
        missed.Should().ContainSingle().Which.Value.Should().Be(0);
        kept.Should().ContainSingle().Which.PageViews.Should().Be(3);
        kept[0].Named.Should().BeTrue();
        keptMissed.Should().ContainSingle().Which.PageViews.Should().Be(0);
        keptMissed[0].Named.Should().BeFalse();
    }

    /// <summary>
    /// A month counted twice is read as its newest count, and the post's paths — its own and where it landed —
    /// are picked out of it and added up, from everybody's figures or the people's.
    /// </summary>
    /// <param name="population">Who the pages were delivered to.</param>
    [Theory]
    [InlineData(Population.Everybody)]
    [InlineData(Population.People)]
    public async Task One_Posts_Kept_Months_Are_Each_Months_Newest_Count_Of_Its_Paths(Population population)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var calendar = new ZoneCalendar(site.TimeZoneId);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(Midnight)).AddMonths(-2);
        var counted = calendar.MidnightOn(month.AddMonths(1)).AddDays(1);
        var recounted = counted.AddDays(1);
        string[] paths = ["/posts/old/", "/posts/new/"];
        var fingerprint = PostFingerprint.Of([.. paths]);

        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, month, fingerprint, counted, new PostMonthViews("/posts/new/", 40, 20), new PostMonthViews("/posts/old/", 9, 9)));
        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(
                site,
                calendar,
                month,
                fingerprint,
                recounted,
                new PostMonthViews("/posts/elsewhere/", 100, 100),
                new PostMonthViews("/posts/new/", 7, 3),
                new PostMonthViews("/posts/old/", 5, 2)));

        var kept = await KeptAsync(site.Id, [.. paths], population);

        var only = kept.Should().ContainSingle().Subject;
        only.Month.Should().Be(month);
        only.PageViews.Should().Be(population == Population.People ? 5 : 12);
        only.Named.Should().BeTrue();
        only.CountedAt.Should().Be(recounted);
        only.Counted.Should().Be(new TimeRange(calendar.FirstInstantOn(month), calendar.FirstInstantOn(month.AddMonths(1))));
    }

    /// <summary>
    /// A month in which nobody read any post is kept with nothing at any path, and is still a month of the
    /// post's: nought, rather than missing.
    /// </summary>
    [Fact]
    public async Task A_Month_Kept_With_No_Post_Read_In_It_Is_Nought_Rather_Than_Missing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var calendar = new ZoneCalendar(site.TimeZoneId);
        var month = ZoneCalendar.FirstOf(calendar.DateOf(Midnight)).AddMonths(-3);

        await KeptPosts.KeepAsync(
            stack,
            KeptPosts.Counted(site, calendar, month, PostFingerprint.Of(["/posts/a/"]), calendar.MidnightOn(month.AddMonths(1))));

        var kept = await KeptAsync(site.Id, ["/posts/a/"]);

        var only = kept.Should().ContainSingle().Subject;
        only.Month.Should().Be(month);
        only.PageViews.Should().Be(0);
        only.Named.Should().BeFalse();
    }

    private Task<IReadOnlyList<TimeSeriesPoint>> SeriesAsync(
        Guid siteId,
        string[] paths,
        Population population = Population.Everybody) =>
        Queries.GetPostSeriesAsync(
            Scope(siteId),
            new PostSeriesQuery(Day(), TimeGranularity.Day, [.. paths]) { Population = population },
            Cancellation.Token);

    private Task<ImmutableArray<KeptMonthOfPost>> KeptAsync(
        Guid siteId,
        string[] paths,
        Population population = Population.Everybody) =>
        Queries.GetKeptMonthsOfPostAsync(
            Scope(siteId),
            new KeptMonthsOfPostQuery(KeptRecord.Now(stack), [.. paths]) { Population = population },
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>The day the reports are written on, which is one bucket of a day.</summary>
    private static TimeRange Day() => new(Midnight, Midnight.AddDays(1));

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC", DateTimeOffset.UnixEpoch);

    private static string Domain() => $"post-reading-{Guid.NewGuid():n}.example";

    private static RawEvent FromBrowser(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        string? correlationId = null) =>
        View(siteId, at, visitorKey, path, IngestSurface.BrowserTracker, correlationId);

    private static RawEvent FromServer(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        string? correlationId = null) =>
        View(siteId, at, visitorKey, path, IngestSurface.NextJsMiddleware, correlationId);

    private static RawEvent View(
        Guid siteId,
        DateTimeOffset at,
        string visitorKey,
        string path,
        IngestSurface surface,
        string? correlationId) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = surface,
            ServerTimestamp = at,
            VisitorKey = visitorKey,
            Host = "example.com",
            Path = path,
            CorrelationId = correlationId,
        };
}

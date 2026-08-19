using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that the busiest pages are counted on the same terms as the headline they sit under.
/// </summary>
/// <remarks>
/// A share is a claim about two numbers, and it is only true if both were arrived at the same way.
/// This list counts pages delivered, exactly as the headline does, and takes its shares against
/// everything the window held rather than against the rows it had room to show.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SitePagesTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pages_Are_Listed_Busiest_First()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/pricing"),
            FromServer(siteId, Midnight.AddHours(1), "visitor-b", "/"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-c", "/"),
            FromServer(siteId, Midnight.AddHours(3), "visitor-d", "/"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/", "/pricing");
        pages.Pages.Select(page => page.PageViews).Should().Equal(3, 1);
    }

    /// <summary>
    /// The rule the headline is counted by, applied here too. A site running both halves would
    /// otherwise be shown its busiest page at twice the traffic it had.
    /// </summary>
    [Fact]
    public async Task A_Page_Both_Halves_Saw_Is_Counted_Once()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"),
            FromBrowser(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Should().ContainSingle();
        pages.Pages[0].PageViews.Should().Be(1);
        pages.Pages[0].Visitors.Should().Be(1);
    }

    /// <summary>
    /// The most important property in this file: a page's share is taken against the same number
    /// the customer is shown above it. Two counting rules on one screen is a screen that
    /// contradicts itself.
    /// </summary>
    [Fact]
    public async Task The_Total_Agrees_With_The_Headline_Over_The_Same_Window()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/", "delivery-1"),
            FromBrowser(siteId, Midnight.AddHours(1), "browser-saw-a", "/", "delivery-1"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-a", "/missing"),
            FromBrowser(siteId, Midnight.AddHours(3), "browser-saw-a", "/pricing"),
            FromServer(siteId, Midnight.AddHours(4), "visitor-b", "/"),
            FromServer(siteId, Midnight.AddHours(5), null, "/"));

        var pages = await PageOfAddresses(siteId);
        var overview = await OverviewOf(siteId);

        pages.TotalPageViews.Should().Be(overview.PageViews);
    }

    /// <summary>
    /// A slice stops at a limit and the figures beside it do not. Summing the rows shown would
    /// report a page at a share of the pages that fitted on the screen rather than of the site's
    /// traffic.
    /// </summary>
    [Fact]
    public async Task The_Figures_Cover_Pages_The_Slice_Was_Cut_Off_Before_Reaching()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/first"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-b", "/second"),
            FromServer(siteId, Midnight.AddHours(3), "visitor-c", "/third"));

        var pages = await PageOfAddresses(siteId, limit: 1);

        pages.Pages.Should().ContainSingle();
        pages.TotalPageViews.Should().Be(3);
        pages.TotalPaths.Should().Be(3);
    }

    /// <summary>
    /// The whole list is reachable a slice at a time, and every address appears exactly once
    /// across the slices — which is the property that makes the arrows on the screen honest.
    /// </summary>
    [Fact]
    public async Task Every_Address_Is_Reached_Exactly_Once_By_Walking_The_Slices()
    {
        var siteId = Guid.NewGuid();
        var written = Enumerable.Range(1, 25)
            .Select(rank => FromServer(siteId, Midnight.AddMinutes(rank), $"visitor-{rank}", $"/page-{rank:00}"))
            .ToArray();

        await WriteAsync(written);

        var walked = new List<string>();

        for (var offset = 0; offset < 30; offset += 4)
        {
            var slice = await PageOfAddresses(siteId, limit: 4, offset: offset);

            walked.AddRange(slice.Pages.Select(page => page.Path));
        }

        walked.Should().HaveCount(25);
        walked.Should().OnlyHaveUniqueItems();
        walked.Should().BeEquivalentTo(written.Select(entry => entry.Path));
    }

    /// <summary>
    /// Pages with identical traffic are ordered by their address, so a slice boundary falling in
    /// the middle of a tie neither repeats one of them nor loses one.
    /// </summary>
    [Fact]
    public async Task A_Slice_Boundary_Inside_A_Tie_Neither_Repeats_Nor_Loses_A_Page()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/alike-a"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-b", "/alike-b"),
            FromServer(siteId, Midnight.AddHours(3), "visitor-c", "/alike-c"),
            FromServer(siteId, Midnight.AddHours(4), "visitor-d", "/alike-d"));

        var first = await PageOfAddresses(siteId, limit: 2);
        var second = await PageOfAddresses(siteId, limit: 2, offset: 2);

        first.Pages.Select(page => page.Path).Should().Equal("/alike-a", "/alike-b");
        second.Pages.Select(page => page.Path).Should().Equal("/alike-c", "/alike-d");
    }

    /// <summary>
    /// Asked for a slice past the end of the list — which happens when traffic ages out of the
    /// window between one request and the next — the answer is empty rather than an error.
    /// </summary>
    [Fact]
    public async Task A_Slice_Past_The_End_Of_The_List_Is_Simply_Empty()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/only"));

        var pages = await PageOfAddresses(siteId, limit: 10, offset: 50);

        pages.Pages.Should().BeEmpty();
    }

    /// <summary>
    /// A bar measured against whatever led the slice would start every slice with a full-length
    /// one, saying that the quietest page on a site is as busy as the busiest.
    /// </summary>
    [Fact]
    public async Task The_Busiest_Page_Is_Reported_On_Every_Slice()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/busy"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-b", "/busy"),
            FromServer(siteId, Midnight.AddHours(3), "visitor-c", "/busy"),
            FromServer(siteId, Midnight.AddHours(4), "visitor-d", "/quiet"));

        var second = await PageOfAddresses(siteId, limit: 1, offset: 1);

        second.Pages.Select(page => page.Path).Should().Equal("/quiet");
        second.MostPageViews.Should().Be(3);
    }

    /// <summary>
    /// Visitors are counted per page on the same daily terms as the headline, and the two halves
    /// of one person are one person here as well.
    /// </summary>
    [Fact]
    public async Task A_Visitor_Both_Halves_Saw_Is_One_Visitor_On_The_Page_They_Read()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Midnight.AddHours(1), "as-the-browser-saw-them", "/", "delivery-1"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-b", "/"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Should().ContainSingle();
        pages.Pages[0].Visitors.Should().Be(2);
    }

    /// <summary>
    /// Reading a page and being on one are different reports. A page nobody was ever delivered is
    /// not a page anybody read, and a list carrying a nought is a list nobody can use.
    /// </summary>
    [Fact]
    public async Task A_Page_With_No_Delivery_Behind_It_Is_Not_Listed()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/read"),
            Engaged(siteId, Midnight.AddHours(2), "visitor-a", "/never-delivered"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/read");
    }

    [Fact]
    public async Task Pages_Outside_The_Window_Are_Left_Out()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/inside"),
            FromServer(siteId, Midnight.AddDays(2), "visitor-a", "/outside"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/inside");
        pages.TotalPageViews.Should().Be(1);
    }

    [Fact]
    public async Task Another_Sites_Pages_Are_Never_Listed()
    {
        var siteId = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/mine"),
            FromServer(neighbour, Midnight.AddHours(1), "visitor-b", "/theirs"));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal("/mine");
    }

    /// <summary>
    /// A window nobody visited answers with an empty list and a nought, rather than with nothing
    /// for a screen to tell apart from a failure.
    /// </summary>
    [Fact]
    public async Task A_Window_With_No_Traffic_Answers_With_Nothing_And_A_Nought()
    {
        var pages = await PageOfAddresses(Guid.NewGuid());

        pages.Pages.Should().BeEmpty();
        pages.TotalPageViews.Should().Be(0);
    }

    /// <summary>
    /// A path is written by whoever asked for the page, including somebody who would rather the
    /// statement did something other than count them. It is grouped on and read back, never built
    /// into the statement.
    /// </summary>
    [Fact]
    public async Task A_Path_Written_To_Break_The_Statement_Is_Counted_Like_Any_Other()
    {
        var siteId = Guid.NewGuid();
        const string hostile = "/'; DROP TABLE events; --";

        await WriteAsync(FromServer(siteId, Midnight.AddHours(1), "visitor-a", hostile));

        var pages = await PageOfAddresses(siteId);

        pages.Pages.Select(page => page.Path).Should().Equal(hostile);
    }

    private Task<SitePages> PageOfAddresses(Guid siteId, int limit = 10, int offset = 0) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSitePagesAsync(
            Scope(siteId),
            new SitePagesQuery(new TimeRange(Midnight, Midnight.AddDays(1)), limit, offset),
            Cancellation.Token);

    private Task<OverviewResult> OverviewOf(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetOverviewAsync(
            Scope(siteId),
            new OverviewQuery(new TimeRange(Midnight, Midnight.AddDays(1))),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC");

    private static RawEvent FromBrowser(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        string? correlationId = null) =>
        Observed(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.BrowserTracker, correlationId);

    private static RawEvent FromServer(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        string? correlationId = null) =>
        Observed(siteId, at, visitorKey, path, EventKind.PageView, IngestSurface.NextJsMiddleware, correlationId);

    private static RawEvent Engaged(Guid siteId, DateTimeOffset at, string? visitorKey, string path) =>
        Observed(siteId, at, visitorKey, path, EventKind.Engagement, IngestSurface.BrowserTracker, null);

    private static RawEvent Observed(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        EventKind kind,
        IngestSurface surface,
        string? correlationId)
    {
        return new RawEvent
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
}

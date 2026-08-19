using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a site running both halves of the measurement is not told its traffic is twice what
/// it is.
/// </summary>
/// <remarks>
/// The arrangement this product asks for is a tracker in the browser and a reporter on the site's
/// own server, because neither sees what the other does. Every page a person reads is therefore
/// reported twice, and each half works out for itself who the visitor was — from addresses that
/// are not reliably the same one, since the page and the collector are different hosts. Taken at
/// face value that is double the page views and double the people, on the most important numbers
/// the product has, for every customer who installed it properly.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PageDeliveryTests(AnalyticsStackFixture stack)
{
    private static readonly DateTimeOffset Midnight = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_Page_Delivered_Is_One_Page_View_However_Many_Surfaces_Saw_It()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"),
            FromBrowser(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(1);
        overview.Events.Should().Be(2);
    }

    /// <summary>
    /// The reason the rule is the larger of the two halves rather than a pairing of reports that
    /// look alike. A reader who asks for the same page twice asked for it twice.
    /// </summary>
    [Fact]
    public async Task A_Page_Asked_For_Twice_Is_Counted_Twice()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"),
            FromBrowser(siteId, Midnight.AddHours(1), "visitor-a", "/posts/hello"),
            FromServer(siteId, Midnight.AddHours(2), "visitor-a", "/posts/hello"),
            FromBrowser(siteId, Midnight.AddHours(2), "visitor-a", "/posts/hello"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(2);
    }

    /// <summary>
    /// Something that takes the markup and leaves never runs the tracker, so the server's half is
    /// the only account of it there is — and the whole reason the product asks for that half.
    /// </summary>
    [Fact]
    public async Task Traffic_Only_The_Server_Saw_Is_Counted_In_Full()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "crawler", "/"),
            FromServer(siteId, Midnight.AddHours(1), "crawler", "/pricing"),
            FromServer(siteId, Midnight.AddHours(1), "crawler", "/about"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(3);
    }

    /// <summary>
    /// A site that redraws itself instead of reloading delivers pages the server never handled.
    /// The browser's half is the only account of those, and dropping them would lose most of the
    /// reading on a modern site.
    /// </summary>
    [Fact]
    public async Task Pages_Reached_Without_A_Fresh_Request_Are_Counted()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            FromBrowser(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            FromBrowser(siteId, Midnight.AddHours(1).AddMinutes(1), "visitor-a", "/pricing"),
            FromBrowser(siteId, Midnight.AddHours(1).AddMinutes(2), "visitor-a", "/about"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(3);
    }

    /// <summary>
    /// The two halves derive their own key for the same person, from addresses that need not
    /// match. Where the browser sent back the identifier the server put on the page, that settles
    /// it — and the browser's answer is the one kept, because it was measured from the visitor's
    /// own connection rather than asserted on their behalf.
    /// </summary>
    [Fact]
    public async Task The_Two_Halves_Of_One_Visit_Are_One_Visitor()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Midnight.AddHours(1), "as-the-browser-saw-them", "/", "delivery-1"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(1);
        overview.Visitors.Should().Be(1);
    }

    /// <summary>
    /// What one echoed page establishes is that two keys belong to one person, not merely that one
    /// report was a duplicate. Without carrying that across the whole visit, everything the browser
    /// never rendered — a redirect, a page that was not there — would be stranded as a visitor of
    /// its own, and the counting would be wrong in the other direction.
    /// </summary>
    [Fact]
    public async Task A_Page_The_Browser_Never_Rendered_Stays_With_The_Visitor_It_Belongs_To()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "as-the-server-saw-them", "/old", "delivery-1"),
            FromServer(siteId, Midnight.AddHours(1), "as-the-server-saw-them", "/new", "delivery-2"),
            FromBrowser(siteId, Midnight.AddHours(1), "as-the-browser-saw-them", "/new", "delivery-2"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(2);
        overview.Visitors.Should().Be(1);
    }

    /// <summary>
    /// Nothing about a report with no key says which visitor asked for the page, so there is no
    /// second sighting to recognise. Folding them together would discard views rather than
    /// duplicates.
    /// </summary>
    [Fact]
    public async Task Reports_That_Name_No_Visitor_Are_Counted_As_They_Arrive()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), null, "/posts/hello"),
            FromBrowser(siteId, Midnight.AddHours(1), null, "/posts/hello"));

        var overview = await OverviewOf(siteId);

        overview.PageViews.Should().Be(2);
    }

    [Fact]
    public async Task A_Series_Counts_Deliveries_Rather_Than_Reports()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            FromBrowser(siteId, Midnight.AddHours(1), "visitor-a", "/"),
            FromServer(siteId, Midnight.AddHours(4), "visitor-a", "/pricing"),
            FromBrowser(siteId, Midnight.AddHours(4), "visitor-a", "/pricing"));

        var series = await stack.Services.GetRequiredService<ITelemetryQueries>().GetTimeSeriesAsync(
            Scope(siteId),
            new TimeSeriesQuery(
                new TimeRange(Midnight, Midnight.AddHours(6)),
                TimeGranularity.Hour,
                TimeSeriesMetric.PageViews),
            Cancellation.Token);

        series.Select(point => point.Value).Should().Equal(0, 1, 0, 0, 1, 0);
    }

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
        View(siteId, at, visitorKey, path, IngestSurface.BrowserTracker, correlationId);

    private static RawEvent FromServer(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        string? correlationId = null) =>
        View(siteId, at, visitorKey, path, IngestSurface.NextJsMiddleware, correlationId);

    private static RawEvent View(
        Guid siteId,
        DateTimeOffset at,
        string? visitorKey,
        string path,
        IngestSurface surface,
        string? correlationId)
    {
        return new RawEvent
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
}

using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that a name a site's server gave a page joins two keys only when it can mean one person.
/// </summary>
/// <remarks>
/// <para>
/// A reporter names each page it delivers and the tracker sends the name back, which is how the two
/// halves of one reader's visit are recognised as one visitor when each half saw a different
/// address. But a page cache in front of the site hands the same page — name and all — to everybody
/// who asks for it while the copy lasts, so a name can come back from a crawler's request in a
/// stranger's browser, or from one reader's request in a dozen. Read naively, that moves one
/// visitor's day onto another, and the visitor it lands on can be judged on what somebody else did.
/// </para>
/// <para>
/// Each test names the visitors by what the two halves would have called them and reads the result
/// through the statements the dashboard, the live view and the installation's accounting use.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class EchoedDeliveryTests(AnalyticsStackFixture stack)
{
    private const string SiteDomain = "example.com";

    private static readonly DateTimeOffset Morning = StillHeld.Midnight.AddHours(9);

    private DateTimeOffset? _now;

    /// <summary>
    /// The moment the live view is read at: the stack's clock when the test first asks, taken back
    /// to a whole minute and held there.
    /// </summary>
    private DateTimeOffset Now =>
        _now ??= OnTheMinute(stack.Services.GetRequiredService<TimeProvider>().GetUtcNow());

    /// <summary>
    /// A cache served one copy of the page to two readers. Nothing says which of them — if either —
    /// the server delivered it to, so the name settles nobody and all three are counted as what
    /// they were seen as.
    /// </summary>
    [Fact]
    public async Task A_Name_A_Cache_Handed_To_Two_Readers_Settles_Nobody()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Morning, "filled-the-cache", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(10), "reader-a", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(20), "reader-b", "/posts/hello", "delivery-1"));

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(3);
        overview.PageViews.Should().Be(3);
    }

    /// <summary>
    /// The reader the page was delivered to sends the name back at once; a cache that goes on
    /// handing out the same copy reaches other browsers minutes later. Those later copies are not
    /// answers, so the reader keeps their own, whatever the later readers happen to be called.
    /// </summary>
    [Fact]
    public async Task The_Reader_A_Page_Was_Delivered_To_Keeps_It_However_Many_Copies_A_Cache_Hands_Out_Later()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-10), "as-the-server-saw-them", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-10).AddSeconds(1), "reader", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-5), "another-from-the-cache", "/posts/hello", "delivery-1"));

        var here = await WhoIsHere(siteId);

        here.Visitors.Select(visitor => visitor.VisitorKey)
            .Should().BeEquivalentTo("reader", "another-from-the-cache");
        here.Visitors.Single(visitor => visitor.VisitorKey == "reader").Evidence.Surfaces
            .Should().Contain(IngestSurface.NextJsMiddleware);
        here.Visitors.Single(visitor => visitor.VisitorKey == "another-from-the-cache").Evidence.Surfaces
            .Should().Equal(IngestSurface.BrowserTracker);
    }

    /// <summary>
    /// Two browsers sending the name back within moments of each other were both served the copy,
    /// so even the one that came first is not known to be the one the server saw.
    /// </summary>
    [Fact]
    public async Task Two_Answers_Inside_The_Answer_Window_Settle_Nothing()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Morning, "as-the-server-saw-them", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(1), "reader", "/posts/hello", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(60), "another-from-the-cache", "/posts/hello", "delivery-1"));

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(3);
    }

    /// <summary>
    /// A name the reporter gave comes back hours later in another key's report — a cached response
    /// reported again, a batch sent twice after the day's keys were renewed. That is a copy, as a late
    /// echo is, so what the name settled when it was delivered stays settled.
    /// </summary>
    [Fact]
    public async Task A_Name_Reported_Again_Hours_Later_Under_Another_Key_Changes_Nothing()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Morning, "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(1), "as-the-browser-saw-them", "/", "delivery-1"),
            FromServer(siteId, Morning.AddMinutes(5), "as-the-server-saw-them", "/feed/"),
            FromServer(siteId, Morning.AddHours(3), "reported-again", "/", "delivery-1"));

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(2);
    }

    /// <summary>
    /// A crawler that asked for three hundred pages, one of whose copies a cache passed to a reader
    /// who sent its name back. One answer among three hundred names is not that reader's visit, and
    /// folding it in would hand the reader a crawl to be judged on.
    /// </summary>
    [Fact]
    public async Task A_Key_That_Named_Hundreds_Of_Pages_Is_Not_Folded_Into_A_Reader_Who_Answered_One()
    {
        var siteId = Guid.NewGuid();

        var crawl = Enumerable.Range(0, 300)
            .Select(page => FromServer(siteId, Morning.AddSeconds(page * 5), "crawler", $"/p/{page}", $"delivery-{page}"))
            .ToList();

        await WriteAsync(
            [
                .. crawl,
                FromBrowser(siteId, Morning.AddSeconds(500).AddSeconds(1), "reader", "/p/100", "delivery-100"),
            ]);

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(2);
        overview.PageViews.Should().Be(301);
    }

    /// <summary>
    /// A crawler whose copies a cache handed to two readers at a time, and one of which a single
    /// reader happened to send back alone. The copies two readers sent back were answered by nobody
    /// in particular, which is not the same as never having been given: they count among the
    /// crawler's names, and one answer among ten is not that reader's visit.
    /// </summary>
    [Fact]
    public async Task Names_A_Cache_Handed_To_Several_Readers_Still_Count_Against_The_One_Who_Answered_Alone()
    {
        var siteId = Guid.NewGuid();
        var crawl = new List<RawEvent>();

        for (var page = 0; page < 10; page++)
        {
            var at = Morning.AddMinutes(page);
            var name = $"delivery-{page}";

            crawl.Add(FromServer(siteId, at, "crawler", $"/p/{page}", name));

            if (page == 5)
            {
                crawl.Add(FromBrowser(siteId, at.AddSeconds(5), "reader", $"/p/{page}", name));
            }
            else
            {
                crawl.Add(FromBrowser(siteId, at.AddSeconds(5), $"first-from-the-cache-{page}", $"/p/{page}", name));
                crawl.Add(FromBrowser(siteId, at.AddSeconds(15), $"second-from-the-cache-{page}", $"/p/{page}", name));
            }
        }

        await WriteAsync([.. crawl]);

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(20);
        overview.PageViews.Should().Be(29);
    }

    /// <summary>
    /// One address and one browser can stand for several people — a household, or a relay that
    /// shares its addresses among many — and their pages are answered by as many browsers. Which of
    /// them each of the server's reports belonged to is not knowable, so none of them is given it.
    /// </summary>
    [Fact]
    public async Task Two_Browsers_Answering_One_Key_Are_Folded_Into_Neither()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Morning, "shared-address", "/", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(1), "first-person", "/", "delivery-1"),
            FromServer(siteId, Morning.AddMinutes(1), "shared-address", "/pricing", "delivery-2"),
            FromBrowser(siteId, Morning.AddMinutes(1).AddSeconds(1), "second-person", "/pricing", "delivery-2"));

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(3);
    }

    /// <summary>
    /// A page whose name has not come back yet — the reader opened it a moment ago — is one of the
    /// reader's own, so the live view goes on showing one person rather than splitting them for
    /// the second or two it takes the answer to arrive.
    /// </summary>
    [Fact]
    public async Task A_Page_Whose_Answer_Has_Not_Arrived_Yet_Leaves_The_Reader_One_Visitor()
    {
        var siteId = Guid.NewGuid();

        await WriteAsync(
            FromServer(siteId, Now.AddMinutes(-5), "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Now.AddMinutes(-5).AddSeconds(1), "as-the-browser-saw-them", "/", "delivery-1"),
            FromServer(siteId, Now.AddSeconds(-10), "as-the-server-saw-them", "/pricing", "delivery-2"));

        var here = await WhoIsHere(siteId);

        here.VisitorsSeen.Should().Be(1);
        here.Visitors[0].VisitorKey.Should().Be("as-the-browser-saw-them");
        here.Visitors[0].Evidence.PageCount.Should().Be(2);
    }

    /// <summary>
    /// An answer speaks for what its reporter did around it and no further. What the key did inside
    /// the reach is the reader's; what it did after the reach has passed is its own again, so an
    /// answer in the morning says nothing about whose the evening was.
    /// </summary>
    [Fact]
    public async Task An_Answer_Speaks_For_What_Its_Reporter_Did_Within_The_Reach_And_No_Further()
    {
        var siteId = Guid.NewGuid();
        var reach = EchoedDeliveries.Reach;

        await WriteAsync(
            FromServer(siteId, Morning, "as-the-server-saw-them", "/", "delivery-1"),
            FromBrowser(siteId, Morning.AddSeconds(1), "as-the-browser-saw-them", "/", "delivery-1"),
            FromServer(siteId, Morning + reach - TimeSpan.FromMinutes(1), "as-the-server-saw-them", "/feed/"),
            FromServer(siteId, Morning + reach + TimeSpan.FromMinutes(1), "as-the-server-saw-them", "/sitemap.xml"));

        var overview = await OverviewOf(siteId);

        overview.Visitors.Should().Be(2);
        overview.PageViews.Should().Be(3);
    }

    /// <summary>
    /// Two websites' servers can perfectly well give two pages the same name. Inside each website it
    /// is one delivery and settles the reader it was delivered to; read across both, it would be one
    /// name with two reporters, and settle nobody on either.
    /// </summary>
    [Fact]
    public async Task A_Name_Two_Websites_Both_Gave_Is_Settled_Inside_Each()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await WriteAsync(
            FromServer(first, Morning, "first-website-server", "/", "delivery-1"),
            FromServer(second, Morning, "second-website-server", "/", "delivery-1"),
            FromBrowser(second, Morning.AddSeconds(1), "second-website-reader", "/", "delivery-1"));

        var counted = await stack.Services.GetRequiredService<ISiteVolume>().CountAsync(
            new SiteVolumeWindow
            {
                Range = new TimeRange(StillHeld.Midnight, StillHeld.Midnight.AddDays(1)),
                SiteIds = [first, second],
            },
            Cancellation.Token);

        counted.Should().BeEquivalentTo([new SiteVolume(first, 1), new SiteVolume(second, 1)]);
    }

    private Task<OverviewResult> OverviewOf(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetOverviewAsync(
            Scope(siteId),
            new OverviewQuery(new TimeRange(StillHeld.Midnight, StillHeld.Midnight.AddDays(1))),
            Cancellation.Token);

    private Task<LiveVisitors> WhoIsHere(Guid siteId) =>
        stack.Services.GetRequiredService<ITelemetryQueries>().GetSiteLiveVisitorsAsync(
            Scope(siteId),
            new SiteLiveVisitorsQuery(new TimeRange(Now - TimeSpan.FromMinutes(30), Now), SiteDomain, 50),
            Cancellation.Token);

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    private static DateTimeOffset OnTheMinute(DateTimeOffset at) =>
        at.AddTicks(-(at.Ticks % TimeSpan.TicksPerMinute));

    private static TenantScope Scope(Guid siteId) =>
        new(siteId, Guid.NewGuid(), SiteRole.Viewer, "Etc/UTC", DateTimeOffset.UnixEpoch);

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
            Host = SiteDomain,
            Path = path,
            CorrelationId = correlationId,
        };
}

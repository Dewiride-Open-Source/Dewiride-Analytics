using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that counting a site for accounting and counting it for a screen produce one number.
/// </summary>
/// <remarks>
/// This is the assertion the arrangement exists for. A customer whose dashboard says one figure and
/// whose allowance says another has no way to tell which of them is wrong — so both are built from
/// the same fragment, and this holds them to one set of events, read the way each of them is really
/// read: the screen's figure through the screen's own address, and the metered one through the port
/// the accounting uses.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteVolumeTests(AnalyticsStackFixture stack)
{
    private const string Password = Passwords.Acceptable;

    /// <summary>An ordinary desktop browser, so nothing here is turned away as machinery.</summary>
    private const string Agent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    [Fact]
    public async Task Metered_Volume_Is_The_Figure_The_Dashboard_Reports()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var window = await SeedBothHalvesAsync(site.Id, visitors: 4);

        var reported = await ReadDashboardPageViewsAsync(site.Id, window);
        var metered = await CountAsync(window, site.Id);

        reported.Should().BePositive();
        metered.Should().Be(reported);
    }

    /// <summary>
    /// One statement per organisation rather than one per site, so the answer has to say which site
    /// each figure belongs to and has to leave every other site out of it.
    /// </summary>
    [Fact]
    public async Task Several_Sites_Are_Counted_Separately_In_One_Question()
    {
        var first = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var second = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var elsewhere = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var window = await SeedBothHalvesAsync(first.Id, visitors: 2);
        await SeedBothHalvesAsync(second.Id, visitors: 3);
        await SeedBothHalvesAsync(elsewhere.Id, visitors: 5);

        var counted = await ReadAsync(window, first.Id, second.Id);

        counted.Should().HaveCount(2);
        counted.Should().NotContain(row => row.SiteId == elsewhere.Id);

        counted.Single(row => row.SiteId == first.Id).PageViews
            .Should().Be(await ReadDashboardPageViewsAsync(first.Id, window));
        counted.Single(row => row.SiteId == second.Id).PageViews
            .Should().Be(await ReadDashboardPageViewsAsync(second.Id, window));
    }

    /// <summary>
    /// A correlation identifier is minted by the reporting site's own server, so two sites can mint
    /// the same one. Settling identity across all of them at once would move one site's activity
    /// onto the other's key and quietly charge the wrong account for it.
    /// </summary>
    [Fact]
    public async Task One_Site_Is_Not_Credited_With_Another_That_Shares_A_Correlation_Identifier()
    {
        var mine = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var theirs = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var at = Clock().GetUtcNow().AddHours(-2);
        var window = new TimeRange(at.AddMinutes(-5), at.AddMinutes(30));
        const string shared = "one-identifier-two-sites";

        await WriteAsync(
        [
            Reported(mine.Id, at, "/posts/hello", shared, $"server-mine-{mine.Id:n}"),
            Watched(mine.Id, at.AddSeconds(1), "/posts/hello", shared, $"browser-mine-{mine.Id:n}"),
            Reported(theirs.Id, at.AddSeconds(2), "/posts/hello", shared, $"server-theirs-{theirs.Id:n}"),
            Watched(theirs.Id, at.AddSeconds(3), "/posts/hello", shared, $"browser-theirs-{theirs.Id:n}"),
        ]);

        var counted = await ReadAsync(window, mine.Id, theirs.Id);

        counted.Single(row => row.SiteId == mine.Id).PageViews.Should().Be(1);
        counted.Single(row => row.SiteId == theirs.Id).PageViews.Should().Be(1);
    }

    [Fact]
    public async Task A_Site_That_Delivered_Nothing_Is_Absent_Rather_Than_Nought()
    {
        var quiet = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var now = Clock().GetUtcNow();

        var counted = await ReadAsync(new TimeRange(now.AddHours(-1), now), quiet.Id);

        counted.Should().BeEmpty();
    }

    /// <summary>
    /// Asked about nothing, the answer is nothing and no statement is sent. The accounting runs over
    /// every organisation on the installation, and the ones that have not added a site yet would
    /// otherwise each cost a query an hour for ever.
    /// </summary>
    [Fact]
    public async Task Asking_About_No_Sites_At_All_Answers_Without_Asking_The_Store()
    {
        var now = Clock().GetUtcNow();

        var counted = await ReadAsync(new TimeRange(now.AddHours(-1), now));

        counted.Should().BeEmpty();
    }

    /// <summary>
    /// Writes both halves of the measurement for a handful of visitors, which is the arrangement a
    /// site running the product properly produces: every page reported twice, once by the tracker
    /// in the browser and once by the site's own server.
    /// </summary>
    /// <param name="siteId">The site to write for.</param>
    /// <param name="visitors">How many separate visitors.</param>
    /// <returns>A window containing everything written.</returns>
    private async Task<TimeRange> SeedBothHalvesAsync(Guid siteId, int visitors)
    {
        var at = Clock().GetUtcNow().AddHours(-6);
        var events = new List<RawEvent>();

        for (var visitor = 0; visitor < visitors; visitor++)
        {
            var began = at.AddMinutes(visitor * 10);
            var browserKey = $"browser-{visitor}-{siteId:n}";
            var serverKey = $"server-{visitor}-{siteId:n}";

            // Two different pages each, because both halves reporting one page is one page view and
            // a test that gave every visitor the same address would pass whether or not the
            // arithmetic distinguished them.
            foreach (var (path, offset) in Pages)
            {
                var correlation = $"correlation-{visitor}-{offset}-{siteId:n}";

                events.Add(Reported(siteId, began.AddMinutes(offset), path, correlation, serverKey));
                events.Add(Watched(
                    siteId,
                    began.AddMinutes(offset).AddSeconds(1),
                    path,
                    correlation,
                    browserKey));
            }
        }

        await WriteAsync(events);

        return new TimeRange(at.AddMinutes(-5), at.AddHours(2));
    }

    /// <summary>The two pages every seeded visitor reads, and how far into their visit.</summary>
    private static readonly (string Path, int Minute)[] Pages = [("/posts/hello", 0), ("/pricing", 1)];

    private async Task WriteAsync(IReadOnlyCollection<RawEvent> events) =>
        await stack.Services.GetRequiredService<IEventSink>()
            .WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// The page-view figure exactly as the dashboard receives it, through the address the dashboard
    /// asks.
    /// </summary>
    private async Task<long> ReadDashboardPageViewsAsync(Guid siteId, TimeRange window)
    {
        var browser = await SignedInAsync(siteId);

        using (browser)
        {
            var response = await browser.GetAsync(
                $"/api/sites/{siteId}/overview?from={Moment(window.From)}&to={Moment(window.To)}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview = await response.Content.ReadFromJsonAsync<OverviewResponse>(Cancellation.Token);

            overview.Should().NotBeNull();

            return overview.PageViews;
        }
    }

    private async Task<long> CountAsync(TimeRange window, Guid siteId)
    {
        var counted = await ReadAsync(window, siteId);

        return counted.Sum(row => row.PageViews);
    }

    private async Task<IReadOnlyList<SiteVolume>> ReadAsync(TimeRange window, params Guid[] siteIds) =>
        await stack.Services.GetRequiredService<ISiteVolume>()
            .CountAsync(
                new SiteVolumeWindow { Range = window, SiteIds = [.. siteIds] },
                Cancellation.Token);

    private async Task<Browser> SignedInAsync(Guid siteId)
    {
        var address = $"reader-{Guid.NewGuid():n}@example.com";
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, address, Password);
        await ControlPlaneSeed.GrantAsync(stack, siteId, user.Id, SiteRole.Viewer);

        var browser = await Browser.OpenAsync(stack);
        var response = await browser.PostAsync(
            "/api/session",
            new SignInRequest { EmailAddress = address, Password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await browser.DescribeAsync();

        return browser;
    }

    private TimeProvider Clock() => stack.Services.GetRequiredService<TimeProvider>();

    private static string Moment(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    private static RawEvent Reported(
        Guid siteId,
        DateTimeOffset at,
        string path,
        string correlation,
        string visitorKey) =>
        Event(siteId, at, path, correlation, visitorKey, IngestSurface.CloudflareWorker);

    private static RawEvent Watched(
        Guid siteId,
        DateTimeOffset at,
        string path,
        string correlation,
        string visitorKey) =>
        Event(siteId, at, path, correlation, visitorKey, IngestSurface.BrowserTracker);

    private static RawEvent Event(
        Guid siteId,
        DateTimeOffset at,
        string path,
        string correlation,
        string visitorKey,
        IngestSurface surface) =>
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
            UserAgent = Agent,
            StatusCode = 200,
            CorrelationId = correlation,
        };

    private static string Domain() => $"volume-{Guid.NewGuid():n}.example";
}

using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves that every question about a period can be asked of the people alone, and that the
/// answer is the same arithmetic over fewer reports.
/// </summary>
/// <remarks>
/// <para>
/// Two visits do everything the twelve questions count — arrive from a search engine, read two
/// pages, press a control, report how the reading went — and differ in one thing: one came over a
/// household network and one from a rented server. The engine concludes the first was a person
/// and the second was not, and each fact below asks one question of everybody and then of the
/// people, expecting the second answer to be the first with the scraper taken out.
/// </para>
/// <para>
/// A security scanner would not do as the second visit. It presses nothing, reports no reading,
/// names no referrer and no device, so half the questions could not tell the two populations
/// apart. A reader-shaped visit from a rented server is dropped by every one of them.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class PopulationTests(AnalyticsStackFixture stack)
{
    private const string Reader = "reader";

    private const string Scraper = "scraper";

    private const string Subscribe = "Subscribe";

    /// <summary>What a crawler says when it does not pretend to be a browser at all.</summary>
    private const string Scanner = "python-requests/2.32.3";

    /// <summary>
    /// Alibaba's international network, which is where a live installation's hundred phantom
    /// readers turned out to be sitting.
    /// </summary>
    private const uint RentedNetwork = 45102;

    /// <summary>How the registry publishes that network, which is what the collector records.</summary>
    private const string RentedNetworkOwner = "ALIBABA-CN-NET Alibaba US Technology Co., Ltd.";

    private const string SearchResults = "https://www.google.co.in/search?q=analytics";

    private const string SearchHost = "www.google.co.in";

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>
    /// Far enough in the past that every visit written here has been silent for longer than the
    /// idle timeout, so a run judges them rather than waiting for them to finish.
    /// </summary>
    private DateTimeOffset Yesterday => Now.AddDays(-1);

    [Fact]
    public async Task The_Headline_Totals_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await OverviewAsync(site, Population.Everybody);
        var people = await OverviewAsync(site, Population.People);

        everybody.Should().Be(new OverviewResult(PageViews: 4, Visitors: 2, Events: 8));
        people.Should().Be(new OverviewResult(PageViews: 2, Visitors: 1, Events: 4));
    }

    /// <summary>
    /// The buckets are the same arithmetic as the headline over the same reports, so they add up
    /// to it rather than to a second, differently-derived figure.
    /// </summary>
    [Fact]
    public async Task A_Series_Of_People_Adds_Up_To_Their_Headline_Total()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await SeriesAsync(site, Population.Everybody);
        var people = await SeriesAsync(site, Population.People);
        var headline = await OverviewAsync(site, Population.People);

        people.Sum(point => point.Value).Should().Be(headline.PageViews);
        people.Sum(point => point.Value).Should().BeLessThan(everybody.Sum(point => point.Value));
    }

    [Fact]
    public async Task The_Busiest_Pages_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSitePagesAsync(Scope(site), Pages(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSitePagesAsync(Scope(site), Pages(Population.People), Cancellation.Token);

        everybody.TotalPageViews.Should().Be(4);
        people.TotalPageViews.Should().Be(2);
        people.Pages.Select(page => page.Path).Should().BeEquivalentTo("/", "/posts/hello");
        people.Pages.Should().OnlyContain(page => page.Visitors == 1);
    }

    [Fact]
    public async Task The_Places_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteLocationsAsync(Scope(site), Places(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSiteLocationsAsync(Scope(site), Places(Population.People), Cancellation.Token);

        everybody.TotalVisitors.Should().Be(2);
        everybody.Places.Single(place => place.Place == DescribedTraffic.Country).Visitors.Should().Be(2);
        people.TotalVisitors.Should().Be(1);
        people.Places.Single(place => place.Place == DescribedTraffic.Country).Visitors.Should().Be(1);
    }

    [Fact]
    public async Task The_Sources_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteSourcesAsync(Scope(site), Sources(site, Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSiteSourcesAsync(Scope(site), Sources(site, Population.People), Cancellation.Token);

        everybody.Sources.Single(source => source.Source == DescribedTraffic.Source).Visitors.Should().Be(2);
        people.Sources.Single(source => source.Source == DescribedTraffic.Source).Visitors.Should().Be(1);
    }

    [Fact]
    public async Task The_Devices_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteDeviceKindsAsync(
            Scope(site),
            new SiteDeviceKindsQuery(Window()),
            Cancellation.Token);
        var people = await Queries.GetSiteDeviceKindsAsync(
            Scope(site),
            new SiteDeviceKindsQuery(Window()) { Population = Population.People },
            Cancellation.Token);

        everybody.Single(device => device.Device == DescribedTraffic.Device).Visitors.Should().Be(2);
        people.Single(device => device.Device == DescribedTraffic.Device).Visitors.Should().Be(1);
    }

    [Fact]
    public async Task The_Software_Of_People_Counts_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteSoftwareAsync(Scope(site), Software(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSiteSoftwareAsync(Scope(site), Software(Population.People), Cancellation.Token);

        everybody.Names.Single(name => name.Name == DescribedTraffic.Browser).Visitors.Should().Be(2);
        people.Names.Single(name => name.Name == DescribedTraffic.Browser).Visitors.Should().Be(1);
    }

    [Fact]
    public async Task The_Presses_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteActionsAsync(Scope(site), Presses(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSiteActionsAsync(Scope(site), Presses(Population.People), Cancellation.Token);

        var everybodyPressed = everybody.Controls.Single(control => control.Name == Subscribe);
        var peoplePressed = people.Controls.Single(control => control.Name == Subscribe);

        everybody.TotalPresses.Should().Be(2);
        everybodyPressed.Presses.Should().Be(2);
        everybodyPressed.Visitors.Should().Be(2);
        people.TotalPresses.Should().Be(1);
        peoplePressed.Presses.Should().Be(1);
        peoplePressed.Visitors.Should().Be(1);
    }

    [Fact]
    public async Task The_Readings_Of_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteEngagementAsync(
            Scope(site),
            new SiteEngagementQuery(Window()),
            Cancellation.Token);
        var people = await Queries.GetSiteEngagementAsync(
            Scope(site),
            new SiteEngagementQuery(Window()) { Population = Population.People },
            Cancellation.Token);

        everybody.TotalReadings.Should().Be(4);
        everybody.MeasuredReadings.Should().Be(2);
        people.TotalReadings.Should().Be(2);
        people.MeasuredReadings.Should().Be(1);
    }

    [Fact]
    public async Task The_Pages_Held_By_People_Count_The_Reader_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSitePageEngagementAsync(
            Scope(site),
            ReadPages(Population.Everybody),
            Cancellation.Token);
        var people = await Queries.GetSitePageEngagementAsync(
            Scope(site),
            ReadPages(Population.People),
            Cancellation.Token);

        everybody.TotalPages.Should().Be(1);
        everybody.Pages.Single(page => page.Path == "/").Readings.Should().Be(2);
        people.TotalPages.Should().Be(1);
        people.Pages.Single(page => page.Path == "/").Readings.Should().Be(1);
    }

    [Fact]
    public async Task The_Visits_Of_People_Are_The_Readers_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteVisitShapeAsync(
            Scope(site),
            new SiteVisitShapeQuery(Window(), Boundaries()),
            Cancellation.Token);
        var people = await Queries.GetSiteVisitShapeAsync(
            Scope(site),
            new SiteVisitShapeQuery(Window(), Boundaries()) { Population = Population.People },
            Cancellation.Token);

        everybody.Should().Be(new SiteVisitShape(Visits: 2, SinglePageVisits: 0, PageViews: 4));
        people.Should().Be(new SiteVisitShape(Visits: 1, SinglePageVisits: 0, PageViews: 2));
    }

    [Fact]
    public async Task The_Doorways_Of_People_Are_The_Readers_Alone()
    {
        var site = await AReaderAndAScraperAsync();

        var everybody = await Queries.GetSiteVisitFlowAsync(Scope(site), Doorways(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSiteVisitFlowAsync(Scope(site), Doorways(Population.People), Cancellation.Token);

        everybody.TotalVisits.Should().Be(2);
        everybody.Pages.Single(page => page.Path == "/").Visits.Should().Be(2);
        people.TotalVisits.Should().Be(1);
        people.Pages.Single(page => page.Path == "/").Visits.Should().Be(1);
    }

    /// <summary>
    /// "People" is a verdict, and a visit nothing has judged yet has none — however like a person
    /// it looks. It is counted for everybody, because everybody is counted as it arrives.
    /// </summary>
    [Fact]
    public async Task A_Visit_Nothing_Has_Judged_Yet_Is_Not_Among_The_People()
    {
        var site = await AReaderAndAScraperAsync();

        await WriteAsync(AReading(site.Id, "latecomer", Now.AddMinutes(-5)));

        var everybody = await OverviewAsync(site, Population.Everybody);
        var people = await OverviewAsync(site, Population.People);

        everybody.Visitors.Should().Be(3);
        people.Visitors.Should().Be(1);
    }

    /// <summary>
    /// A visit judged again under a newer ruleset is kept or dropped by the newer verdict, which
    /// is the verdict the breakdown of who came counts it by.
    /// </summary>
    [Fact]
    public async Task A_Visit_Judged_Again_Under_A_Newer_Ruleset_Counts_By_The_Newer_Verdict()
    {
        var site = await AReaderAndAScraperAsync();

        await OverturnedAsync(site, new RulesetVersion(RulesetVersion.Current.Major + 1, 0));

        var people = await OverviewAsync(site, Population.People);

        people.Visitors.Should().Be(0);
    }

    /// <summary>
    /// Which verdict is newest is decided by the ruleset rather than by when the row was written,
    /// so judging an older ruleset again does not overturn what the current one concluded.
    /// </summary>
    [Fact]
    public async Task An_Older_Ruleset_Does_Not_Overturn_What_The_Current_One_Concluded()
    {
        var site = await AReaderAndAScraperAsync();

        await OverturnedAsync(site, new RulesetVersion(RulesetVersion.Current.Major - 1, 0));

        var people = await OverviewAsync(site, Population.People);

        people.Visitors.Should().Be(1);
    }

    /// <summary>
    /// A verdict names a visit by the browser's key, and a report the site's own server sent
    /// arrives under a key of its own. Kept after identity is settled, the server's report
    /// follows the person it was about; kept before, it would match no verdict and vanish.
    /// </summary>
    [Fact]
    public async Task A_Report_The_Sites_Own_Server_Sent_Follows_The_Person_It_Was_About()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;
        const string asTheServerSawThem = "reader-as-the-server-saw-them";

        await WriteAsync(
            Echoed(Page(site.Id, Reader, at, "/"), "first"),
            Echoed(Served(site.Id, asTheServerSawThem, at, "/"), "first"),
            Echoed(Page(site.Id, Reader, at.AddMinutes(2), "/posts/hello"), "second"),
            Echoed(Served(site.Id, asTheServerSawThem, at.AddMinutes(2), "/posts/hello"), "second"),
            Pressed(site.Id, Reader, at.AddMinutes(3), "/posts/hello"),
            Read(site.Id, Reader, at.AddMinutes(4), "/"));

        await DescribedTraffic.JudgeAsync(stack, site);
        await JudgedAsExpectedAsync(site, Window(), visits: 1, people: 1);

        var people = await OverviewAsync(site, Population.People);

        people.Should().Be(new OverviewResult(PageViews: 2, Visitors: 1, Events: 6));
    }

    /// <summary>
    /// The verdicts are read from a day before the window, so a visit that began the evening
    /// before keeps the reports it made inside the window — exactly as everybody's count keeps
    /// them.
    /// </summary>
    [Fact]
    public async Task A_Visit_That_Began_The_Evening_Before_Keeps_Its_Reports_Inside_The_Window()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var from = Yesterday;

        await WriteAsync(
            Page(site.Id, Reader, from.AddMinutes(-10), "/"),
            Page(site.Id, Reader, from.AddMinutes(5), "/posts/hello"),
            Read(site.Id, Reader, from.AddMinutes(6), "/posts/hello"));

        await DescribedTraffic.JudgeAsync(stack, site);
        await JudgedAsExpectedAsync(site, Window(), visits: 1, people: 1);

        var window = new TimeRange(from, Now.AddMinutes(1));
        var everybody = await Queries.GetOverviewAsync(Scope(site), new OverviewQuery(window), Cancellation.Token);
        var people = await Queries.GetOverviewAsync(
            Scope(site),
            new OverviewQuery(window) { Population = Population.People },
            Cancellation.Token);

        everybody.PageViews.Should().Be(1);
        people.PageViews.Should().Be(1);
    }

    /// <summary>
    /// What the product exists to leave out. Something probing for a way in is judged as such,
    /// and its pages are counted for everybody and never for people.
    /// </summary>
    [Fact]
    public async Task A_Scanner_Is_Never_Among_The_People()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            [
                .. AReading(site.Id, Reader, at),
                Probe(site.Id, "scanner", at.AddHours(2), "/.env"),
                Probe(site.Id, "scanner", at.AddHours(2).AddSeconds(1), "/.git/config"),
                Probe(site.Id, "scanner", at.AddHours(2).AddSeconds(2), "/wp-login.php"),
            ]);

        await DescribedTraffic.JudgeAsync(stack, site);

        var everybody = await Queries.GetSitePagesAsync(Scope(site), Pages(Population.Everybody), Cancellation.Token);
        var people = await Queries.GetSitePagesAsync(Scope(site), Pages(Population.People), Cancellation.Token);

        everybody.TotalPaths.Should().Be(5);
        people.TotalPaths.Should().Be(2);
    }

    /// <summary>
    /// One site holding a reader and a scraper, judged, with the judging checked before any fact
    /// reads it — so a ruleset that stopped telling the two apart fails here, in one sentence,
    /// rather than in every fact's arithmetic.
    /// </summary>
    private async Task<Site> AReaderAndAScraperAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var at = Yesterday;

        await WriteAsync(
            [
                .. AReading(site.Id, Reader, at),
                .. AReading(site.Id, Scraper, at.AddHours(1)).Select(Rented),
            ]);

        await DescribedTraffic.JudgeAsync(stack, site);
        await JudgedAsExpectedAsync(site, Window(), visits: 2, people: 1);

        return site;
    }

    /// <summary>
    /// Checks what the engine concluded before a fact reads it, so a ruleset that stopped telling
    /// a reader from a scraper fails in one sentence rather than in the arithmetic.
    /// </summary>
    private async Task JudgedAsExpectedAsync(Site site, TimeRange window, int visits, int people)
    {
        var groups = await Queries.GetTrafficBreakdownAsync(
            Scope(site),
            new TrafficBreakdownQuery(window),
            Cancellation.Token);

        groups.Sum(group => group.Sessions).Should().Be(visits, "every visit written should have been judged");
        groups.Where(group => group.Category == TrafficCategory.LikelyHuman).Sum(group => group.Sessions)
            .Should().Be(people, "the reader should be judged a person and nobody else should");
    }

    /// <summary>
    /// Stores a newer or older verdict about the reader's visit that says it was not a person.
    /// </summary>
    private async Task OverturnedAsync(Site site, RulesetVersion ruleset)
    {
        var judged = await Queries.GetJudgedSessionsAsync(
            Scope(site),
            new JudgedSessionsQuery(Window(), Boundaries().IdleTimeout, site.Domain, 10),
            Cancellation.Token);
        var reader = judged.Visits.Single(visit => visit.Verdict.Category == TrafficCategory.LikelyHuman);

        await stack.Services.GetRequiredService<IClassificationStore>().SaveAsync(
            site.Id,
            [
                new SessionJudgement(
                    new SessionEvidence
                    {
                        SessionKey = reader.SessionKey,
                        StartedAt = reader.StartedAt,
                        EndedAt = reader.EndedAt,
                        Requests = [new ObservedRequest(reader.StartedAt, "/", null)],
                        Surfaces = [IngestSurface.BrowserTracker],
                    },
                    new ClassificationVerdict
                    {
                        Category = TrafficCategory.SecurityScanner,
                        Strength = EvidenceStrength.Strong,
                        Supporting = [],
                        RulesetVersion = ruleset,
                    }),
            ],
            Now,
            Cancellation.Token);
    }

    private Task<OverviewResult> OverviewAsync(Site site, Population population) =>
        Queries.GetOverviewAsync(
            Scope(site),
            new OverviewQuery(Window()) { Population = population },
            Cancellation.Token);

    private Task<IReadOnlyList<TimeSeriesPoint>> SeriesAsync(Site site, Population population) =>
        Queries.GetTimeSeriesAsync(
            Scope(site),
            new TimeSeriesQuery(Window(), TimeGranularity.Day, TimeSeriesMetric.PageViews) { Population = population },
            Cancellation.Token);

    private SitePagesQuery Pages(Population population) =>
        new(Window(), 10) { Population = population };

    private SiteLocationsQuery Places(Population population) =>
        new(Window(), LocationGrouping.Country, 10) { Population = population };

    private SiteSourcesQuery Sources(Site site, Population population) =>
        new(Window(), SourceGrouping.Site, site.Domain, 10) { Population = population };

    private SiteSoftwareQuery Software(Population population) =>
        new(Window(), SoftwareGrouping.Browser, 10) { Population = population };

    private SiteActionsQuery Presses(Population population) =>
        new(Window(), ActionGrouping.Control, 10) { Population = population };

    private SitePageEngagementQuery ReadPages(Population population) =>
        new(Window(), EngagementRanking.Attention, 10) { Population = population };

    private SiteVisitFlowQuery Doorways(Population population) =>
        new(Window(), Boundaries(), VisitPosition.Entry, 10) { Population = population };

    private ITelemetryQueries Queries => stack.Services.GetRequiredService<ITelemetryQueries>();

    private Task WriteAsync(params RawEvent[] events) => DescribedTraffic.WriteAsync(stack, events);

    private TimeRange Window() => new(Now.AddDays(-2), Now.AddMinutes(1));

    private VisitBoundaries Boundaries() => new(TimeSpan.FromMinutes(30), Now.AddMinutes(-30));

    private static TenantScope Scope(Site site) =>
        new(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId);

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    /// <summary>
    /// A visit that does everything the twelve questions count: arrives from a search engine,
    /// reads two pages, presses a control, and reports how the reading went.
    /// </summary>
    private static RawEvent[] AReading(Guid siteId, string visitor, DateTimeOffset at) =>
    [
        Page(siteId, visitor, at, "/") with { Referrer = SearchResults, ReferrerDomain = SearchHost },
        Page(siteId, visitor, at.AddMinutes(2), "/posts/hello"),
        Pressed(siteId, visitor, at.AddMinutes(3), "/posts/hello"),
        Read(siteId, visitor, at.AddMinutes(4), "/"),
    ];

    private static RawEvent Page(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        DescribedTraffic.Placed(
            new RawEvent
            {
                EventId = Guid.CreateVersion7(at),
                SiteId = siteId,
                Kind = EventKind.PageView,
                Surface = IngestSurface.BrowserTracker,
                ServerTimestamp = at,
                VisitorKey = visitor,
                Host = "example.com",
                Path = path,
                Language = "en-GB",
                ViewportWidth = 1440,
            });

    private static RawEvent Read(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        Page(siteId, visitor, at, path) with
        {
            Kind = EventKind.Exit,
            EngagedMs = 30_000,
            ScrollDepthPercent = 70,
            HadPointerInteraction = true,
            HadKeyboardInteraction = false,
            DeclaredWebDriver = false,
        };

    private static RawEvent Pressed(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        Page(siteId, visitor, at, path) with
        {
            Kind = EventKind.Action,
            ActionControl = ControlKind.Button,
            ActionLabel = Subscribe,
        };

    /// <summary>
    /// The same page, as a reporter on the site's own server saw it being delivered: a status
    /// code, and no viewport because nothing between the visitor and the site can see one.
    /// </summary>
    private static RawEvent Served(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        Page(siteId, visitor, at, path) with
        {
            Surface = IngestSurface.CloudflareWorker,
            StatusCode = 200,
            ViewportWidth = null,
        };

    /// <summary>The same report, carrying the identifier the server stamped onto the page.</summary>
    private static RawEvent Echoed(RawEvent report, string correlationId) =>
        report with { CorrelationId = correlationId };

    /// <summary>The same activity, arriving from a computer rented in a datacentre.</summary>
    private static RawEvent Rented(RawEvent report) =>
        report with { AutonomousSystem = RentedNetwork, NetworkOwner = RentedNetworkOwner };

    /// <summary>
    /// One request as a surface in the request path sees it: a status code, no viewport, and no
    /// reading of anything a browser would have reported.
    /// </summary>
    private static RawEvent Probe(Guid siteId, string visitor, DateTimeOffset at, string path) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = EventKind.PageView,
            Surface = IngestSurface.CloudflareWorker,
            ServerTimestamp = at,
            VisitorKey = visitor,
            Host = "example.com",
            Path = path,
            UserAgent = Scanner,
            StatusCode = 404,
        };
}

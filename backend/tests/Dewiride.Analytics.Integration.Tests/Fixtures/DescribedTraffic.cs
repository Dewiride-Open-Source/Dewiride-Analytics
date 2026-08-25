using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Activity whose every detail is something a reader could pick off a card, and the judging of it.
/// </summary>
/// <remarks>
/// Two suites need the same visit: one asks the store what a period held, the other asks the
/// endpoints the dashboard reads. What each proves is only worth having while both describe the
/// same traffic, so the visit is written once and the values it holds are named once — an
/// assertion in either suite compares against the constant the seed was built from rather than
/// against a spelling typed out a second time.
/// </remarks>
internal static class DescribedTraffic
{
    /// <summary>A browser that says it is running on a handset, which is what places the visit on one.</summary>
    public const string MobileFirefox =
        "Mozilla/5.0 (Android 15; Mobile; rv:143.0) Gecko/143.0 Firefox/143.0";

    /// <summary>An Indian mobile carrier, which is a network nobody rents a server on.</summary>
    public const uint HouseholdNetwork = 55836;

    /// <summary>How the registry publishes that carrier, which is what the collector records.</summary>
    public const string RegisteredNetwork = "AS55836 Reliance Jio Infocomm Limited";

    /// <summary>
    /// What that carrier is called once the handle the registry publishes it under is dropped,
    /// which is how a network the hosting catalogue does not name is shown — and therefore how it
    /// is asked for.
    /// </summary>
    public const string Network = "Reliance Jio Infocomm Limited";

    /// <summary>The kind of device the visit was made on.</summary>
    public const DeviceClass Device = DeviceClass.Phone;

    /// <summary>The kind of place the visit was sent from.</summary>
    public const SourceChannel SentBy = SourceChannel.Search;

    /// <summary>The browser the visit was made with.</summary>
    public const string Browser = "Firefox";

    /// <summary>The system that browser was running on.</summary>
    public const string SystemName = "Android";

    /// <summary>The country the visit arrived from.</summary>
    public const string Country = "IN";

    /// <summary>The town within it.</summary>
    public const string Town = "Jaipur";

    /// <summary>The site that sent the visit, as this product names it.</summary>
    public const string Source = "Google";

    /// <summary>The page the visit began on.</summary>
    public const string EntryPage = "/pricing";

    private const string SearchResults = "https://www.google.co.in/search?q=analytics";

    private const string SearchHost = "www.google.co.in";

    /// <summary>
    /// A site with one judged visit holding every detail a list can be narrowed by.
    /// </summary>
    /// <remarks>
    /// Placed a day back, so the visit has been silent for longer than the idle timeout by the
    /// time it is judged and is settled rather than still under way.
    /// </remarks>
    /// <param name="stack">The running stack.</param>
    /// <returns>The site.</returns>
    public static async Task<Site> ADescribedVisitAsync(AnalyticsStackFixture stack)
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: $"{Guid.NewGuid():n}.example.com");
        var at = stack.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-1);

        await WriteAsync(
            stack,
            Placed(Arrived(site.Id, at, EntryPage) with
            {
                Referrer = SearchResults,
                ReferrerDomain = SearchHost,
            }),
            Placed(Arrived(site.Id, at.AddMinutes(3), "/docs")),
            Placed(Read(site.Id, at.AddMinutes(5), "/docs")));

        await JudgeAsync(stack, site);

        return site;
    }

    /// <summary>What the collector resolved about where a visit came from and what it was made on.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The report, placed.</returns>
    public static RawEvent Placed(RawEvent report) => report with
    {
        CountryCode = Country,
        City = Town,
        AutonomousSystem = HouseholdNetwork,
        NetworkOwner = RegisteredNetwork,
        DeviceClass = Device,
        BrowserFamily = Browser,
        OperatingSystem = SystemName,
        UserAgent = MobileFirefox,
    };

    /// <summary>A page being delivered.</summary>
    /// <param name="siteId">The site it belongs to.</param>
    /// <param name="at">When it was delivered.</param>
    /// <param name="path">Which page.</param>
    /// <returns>The report.</returns>
    public static RawEvent Arrived(Guid siteId, DateTimeOffset at, string path) =>
        Reported(siteId, at, path, EventKind.PageView);

    /// <summary>A page saying how the reading is going.</summary>
    /// <param name="siteId">The site it belongs to.</param>
    /// <param name="at">When it said so.</param>
    /// <param name="path">Which page.</param>
    /// <returns>The report.</returns>
    public static RawEvent Read(Guid siteId, DateTimeOffset at, string path) =>
        Reported(siteId, at, path, EventKind.Engagement) with
        {
            EngagedMs = 42_000,
            ScrollDepthPercent = 80,
            HadPointerInteraction = true,
        };

    /// <summary>Writes reports the way the collector does.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="events">The reports.</param>
    /// <returns>When they have been written.</returns>
    public static Task WriteAsync(AnalyticsStackFixture stack, params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>Judges everything a site has recorded.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="site">The site.</param>
    /// <returns>When it has been judged.</returns>
    public static async Task JudgeAsync(AnalyticsStackFixture stack, Site site)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<SessionClassifier>()
            .CatchUpAsync(site.Id, site.CreatedAt.AddDays(-2), Cancellation.Token)
            .ConfigureAwait(false);
    }

    private static RawEvent Reported(Guid siteId, DateTimeOffset at, string path, EventKind kind) =>
        new()
        {
            EventId = Guid.CreateVersion7(at),
            SiteId = siteId,
            Kind = kind,
            Surface = IngestSurface.BrowserTracker,
            ServerTimestamp = at,
            VisitorKey = "reader",
            Host = "example.com",
            Path = path,
        };
}

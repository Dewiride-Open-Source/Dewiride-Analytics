using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Writes a website's history a month at a time, keeps its finished months, and reads back what was
/// kept.
/// </summary>
/// <remarks>
/// Every instant is placed relative to the running clock — last month, the month before — so nothing
/// written here is older than the twelve months a report is kept, whenever the suite runs.
/// </remarks>
internal static class KeptRecord
{
    /// <summary>An ordinary desktop browser, so nothing written here is turned away as machinery.</summary>
    public const string Agent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>How long before a month ends no test begins, comfortably more than any one test takes.</summary>
    private static readonly TimeSpan MonthEndMargin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// One page delivered, reported by the visitor's own browser.
    /// </summary>
    /// <param name="siteId">The website.</param>
    /// <param name="at">When it arrived.</param>
    /// <param name="path">The page.</param>
    /// <param name="visitorKey">Whose it was.</param>
    /// <returns>The report.</returns>
    public static RawEvent Page(Guid siteId, DateTimeOffset at, string path, string visitorKey) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = at,
        VisitorKey = visitorKey,
        Host = "example.com",
        Path = path,
        UserAgent = Agent,
        StatusCode = 200,
    };

    /// <summary>Writes reports as the collector would.</summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="events">The reports.</param>
    /// <returns>A task that completes once they are stored.</returns>
    public static Task WriteAsync(AnalyticsStackFixture stack, IReadOnlyCollection<RawEvent> events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// Stores what the engine concluded about one visit, without running the engine over it.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <param name="visitorKey">Whose visit it was.</param>
    /// <param name="began">Its first report.</param>
    /// <param name="ended">Its last report.</param>
    /// <param name="category">What it was concluded to be.</param>
    /// <returns>A task that completes once the verdict is stored.</returns>
    public static Task JudgedAsync(
        AnalyticsStackFixture stack,
        Guid siteId,
        string visitorKey,
        DateTimeOffset began,
        DateTimeOffset ended,
        TrafficCategory category) =>
        stack.Services.GetRequiredService<IClassificationStore>().SaveAsync(
            siteId,
            [
                new SessionJudgement(
                    new SessionEvidence
                    {
                        SessionKey = new VisitKey(visitorKey, began).ToString(),
                        StartedAt = began,
                        EndedAt = ended,
                        Requests = [new ObservedRequest(began, "/", null)],
                        Surfaces = [IngestSurface.BrowserTracker],
                        UserAgent = Agent,
                    },
                    new ClassificationVerdict
                    {
                        Category = category,
                        Strength = EvidenceStrength.Strong,
                        Supporting = [],
                        RulesetVersion = RulesetVersion.Current,
                    }),
            ],
            ended.AddMinutes(30),
            Cancellation.Token);

    /// <summary>
    /// Runs one round of keeping a website's finished months, as the background work does.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="site">The website.</param>
    /// <returns>A task that completes once the round's months are kept.</returns>
    public static Task CountAsync(AnalyticsStackFixture stack, Site site) => CountAsync(stack.Services, site);

    /// <summary>
    /// Runs one round of keeping a website's finished months on a host of a test's own.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="site">The website.</param>
    /// <returns>A task that completes once the round's months are kept.</returns>
    public static async Task CountAsync(IServiceProvider services, Site site)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<MonthlyConsolidation>()
            .CatchUpAsync(new SiteRegistration(site.Id, site.Domain, site.CreatedAt, site.TimeZoneId), Cancellation.Token);
    }

    /// <summary>
    /// Keeps a month directly, as an engine would have kept it long ago.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="month">The month.</param>
    /// <returns>A task that completes once it is kept.</returns>
    public static Task KeepAsync(AnalyticsStackFixture stack, ConsolidatedMonth month) =>
        stack.Services.GetRequiredService<IMonthConsolidation>().KeepAsync([month], Cancellation.Token);

    /// <summary>
    /// A month as an engine kept it: everybody's figures, half as many daily visitors as pages,
    /// nobody judged a person, and one kind of visit.
    /// </summary>
    /// <param name="site">The website.</param>
    /// <param name="calendar">Its calendar.</param>
    /// <param name="month">The first of the month.</param>
    /// <param name="pageViews">Pages delivered in it.</param>
    /// <returns>The month, counted a day after it ended.</returns>
    public static ConsolidatedMonth Counted(Site site, ZoneCalendar calendar, DateOnly month, long pageViews)
    {
        var window = new TimeRange(calendar.FirstInstantOn(month), calendar.FirstInstantOn(month.AddMonths(1)));
        var nobody = new MonthFigures(0, 0, null, 0, [.. new long[7]], [.. new long[24]]);

        return new ConsolidatedMonth(
            new MonthToCount(site.Id, site.TimeZoneId, month, window),
            null,
            DateTime.DaysInMonth(month.Year, month.Month),
            new MonthFigures(pageViews, pageViews / 2, month, pageViews, [.. new long[7]], [.. new long[24]]),
            nobody,
            [new VerdictTally(TrafficCategory.KnownSearchCrawler, pageViews, pageViews)],
            RulesetVersion.Current,
            window.To,
            window.To,
            window.To,
            window.To.AddDays(1));
    }

    /// <summary>
    /// Reads every month kept for a website, each as its newest count.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>The rows, oldest month first, keyed by column name.</returns>
    public static Task<List<Dictionary<string, object?>>> RowsAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.RowsAsync(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            """
            SELECT *
            FROM consolidated_months
            WHERE site_id = {site_id:UUID}
            ORDER BY month, counted_at DESC
            LIMIT 1 BY month
            """,
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>
    /// Counts every row written for a website, before the store has replaced any of them.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The website.</param>
    /// <returns>How many rows there are.</returns>
    public static Task<ulong> WrittenAsync(AnalyticsStackFixture stack, Guid siteId) =>
        TelemetryStore.ScalarAsync<ulong>(
            stack.Services.GetRequiredService<IClickHouseClient>(),
            "SELECT count() FROM consolidated_months WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    /// <summary>Reads an answer the dashboard asks for, and insists it was answered.</summary>
    /// <typeparam name="T">The answer's shape.</typeparam>
    /// <param name="browser">A signed-in browser.</param>
    /// <param name="address">The address.</param>
    /// <returns>The answer.</returns>
    public static async Task<T> GetAsync<T>(Browser browser, string address)
    {
        ArgumentNullException.ThrowIfNull(browser);

        var response = await browser.GetAsync(address);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation.Token));

        var answer = await response.Content.ReadFromJsonAsync<T>(Cancellation.Token);

        answer.Should().NotBeNull();

        return answer;
    }

    /// <summary>An instant as an address carries it.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The instant, escaped.</returns>
    public static string Moment(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>The running clock.</summary>
    /// <param name="stack">The running stack.</param>
    /// <returns>Now.</returns>
    public static DateTimeOffset Now(AnalyticsStackFixture stack) =>
        stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>
    /// The running clock, once the month running now where a website is has long enough left that a
    /// test begun here ends inside it.
    /// </summary>
    /// <remarks>
    /// The engine reads the same clock, after the test has. A test that works out last month and
    /// this one, and then has the engine count or read them, would find the engine a month further on
    /// if a month began in between — and the month the test calls running would be finished, and
    /// kept. Waiting out the last two minutes of a month costs a test that starts there two minutes,
    /// once a month.
    /// </remarks>
    /// <param name="stack">The running stack.</param>
    /// <param name="calendar">The website's calendar.</param>
    /// <returns>Now, with the month running now ending at least two minutes later.</returns>
    public static async Task<DateTimeOffset> NowInsideTheMonthAsync(AnalyticsStackFixture stack, ZoneCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var now = Now(stack);
        var next = calendar.MidnightOn(ZoneCalendar.FirstOf(calendar.DateOf(now)).AddMonths(1));

        if (next - now < MonthEndMargin)
        {
            await Task.Delay(next - now + TimeSpan.FromSeconds(1), Cancellation.Token);
            now = Now(stack);
        }

        return now;
    }
}

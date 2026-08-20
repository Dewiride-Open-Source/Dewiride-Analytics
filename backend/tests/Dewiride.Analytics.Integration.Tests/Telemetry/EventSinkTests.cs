using ClickHouse.Driver;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves an accepted event reaches the store intact.
/// </summary>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class EventSinkTests(AnalyticsStackFixture stack)
{
    private const string EventsForSiteSql = "SELECT * FROM events WHERE site_id = {site_id:UUID}";

    [Fact]
    public async Task Every_Field_On_An_Event_Comes_Back_As_It_Went_In()
    {
        var siteId = Guid.NewGuid();
        var observed = Recently();

        await Sink.WriteAsync(
            Full(siteId, observed),
            Cancellation.Token);

        var row = await SingleRowAsync(siteId);

        row["kind"].Should().Be("PageView");
        row["surface"].Should().Be("CloudflareWorker");
        row["server_ts"].Should().Be(observed.UtcDateTime);
        row["client_ts"].Should().Be(observed.AddSeconds(-2).UtcDateTime);
        row["clock_skew_ms"].Should().Be(-2000);
        row["visitor_key"].Should().Be("9f2a1c4e8b6d0a3f");
        row["host"].Should().Be("example.com");
        row["path"].Should().Be("/posts/hello");
        row["query_string"].Should().Be("?utm_source=news");
        row["referrer"].Should().Be("https://news.example.org/story/42");
        row["referrer_domain"].Should().Be("news.example.org");
        row["status_code"].Should().Be((short)404);
        row["content_type"].Should().Be("text/html");
        row["response_bytes"].Should().Be(1536L);
        row["ip_address"].Should().Be("203.0.113.7");
        row["viewport_width"].Should().Be(1440);
        row["viewport_height"].Should().Be(900);
        row["language"].Should().Be("en-GB");
        row["timezone_offset_minutes"].Should().Be((short)60);
        row["engaged_ms"].Should().Be(8_400);
        row["scroll_depth_percent"].Should().Be((byte)72);
        row["correlation_id"].Should().Be("2a6f5b9c");
    }

    /// <summary>
    /// Not observed, observed as absent, and observed as present are three different statements,
    /// and the column keeps them apart.
    /// </summary>
    [Fact]
    public async Task Interaction_Presence_Keeps_Unobserved_Apart_From_Absent()
    {
        var siteId = Guid.NewGuid();

        await Sink.WriteAsync(
            Minimal(siteId) with
            {
                HadPointerInteraction = true,
                HadKeyboardInteraction = false,
                DeclaredWebDriver = null,
            },
            Cancellation.Token);

        var row = await SingleRowAsync(siteId);

        row["had_pointer_interaction"].Should().Be("Yes");
        row["had_keyboard_interaction"].Should().Be("No");
        row["declared_web_driver"].Should().Be("Unobserved");
    }

    /// <summary>
    /// A reading nobody could take is stored as absent rather than as nought, because nought is a
    /// legal reading and would be indistinguishable from one.
    /// </summary>
    [Fact]
    public async Task A_Reading_A_Surface_Could_Not_Take_Is_Stored_As_Absent()
    {
        var siteId = Guid.NewGuid();

        await Sink.WriteAsync(Minimal(siteId), Cancellation.Token);

        var row = await SingleRowAsync(siteId);

        row["client_ts"].Should().BeNull();
        row["status_code"].Should().BeNull();
        row["response_bytes"].Should().BeNull();
        row["viewport_width"].Should().BeNull();
        row["engaged_ms"].Should().BeNull();
        row["scroll_depth_percent"].Should().BeNull();
    }

    /// <summary>
    /// Rows go in as binary rather than as generated statement text, so what a crawler wrote into
    /// its own user agent is data and only ever data.
    /// </summary>
    [Fact]
    public async Task A_User_Agent_Written_To_Look_Like_A_Statement_Is_Stored_As_Text()
    {
        const string hostile = "'; DROP TABLE events; --";
        var siteId = Guid.NewGuid();

        await Sink.WriteAsync(Minimal(siteId) with { UserAgent = hostile }, Cancellation.Token);

        var row = await SingleRowAsync(siteId);

        row["user_agent"].Should().Be(hostile);
        (await TelemetryStore.ScalarAsync<ulong>(Client, "SELECT count() FROM events")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Every_Capture_Surface_Can_Write_An_Event()
    {
        var siteId = Guid.NewGuid();
        var surfaces = Enum.GetValues<IngestSurface>();

        await Sink.WriteBatchAsync(
            [.. surfaces.Select(surface => Minimal(siteId) with { Surface = surface })],
            Cancellation.Token);

        var stored = await TelemetryStore.RowsAsync(
            Client,
            "SELECT surface FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

        stored.Select(row => row["surface"]).Should().BeEquivalentTo(surfaces.Select(surface => surface.ToString()));
    }

    [Fact]
    public async Task Every_Kind_Of_Report_Can_Be_Written()
    {
        var siteId = Guid.NewGuid();
        EventKind[] kinds = [EventKind.PageView, EventKind.Engagement, EventKind.Exit];

        await Sink.WriteBatchAsync(
            [.. kinds.Select(kind => Minimal(siteId) with { Kind = kind })],
            Cancellation.Token);

        var stored = await TelemetryStore.RowsAsync(
            Client,
            "SELECT kind FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

        stored.Select(row => row["kind"]).Should().BeEquivalentTo(kinds.Select(kind => kind.ToString()));
    }

    [Fact]
    public async Task Writing_An_Empty_Batch_Does_Nothing()
    {
        var before = await TelemetryStore.ScalarAsync<ulong>(Client, "SELECT count() FROM events");

        await Sink.WriteBatchAsync([], Cancellation.Token);

        (await TelemetryStore.ScalarAsync<ulong>(Client, "SELECT count() FROM events")).Should().Be(before);
    }

    private IEventSink Sink => stack.Services.GetRequiredService<IEventSink>();

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private async Task<Dictionary<string, object?>> SingleRowAsync(Guid siteId)
    {
        var rows = await TelemetryStore.RowsAsync(
            Client,
            EventsForSiteSql,
            TelemetryStore.Bind("site_id", siteId));

        return rows.Should().ContainSingle().Subject;
    }

    /// <summary>
    /// A moment inside the window the store keeps an address for.
    /// </summary>
    /// <remarks>
    /// The address is cleared 72 hours after the event it belongs to, so an event stamped with a
    /// fixed date has its address taken away the moment the store gets round to tidying the part
    /// it landed in — and the field this test exists to prove comes back empty for a reason that
    /// has nothing to do with the sink. Truncated to whole milliseconds, which is what the column
    /// holds.
    /// </remarks>
    /// <returns>The moment.</returns>
    private static DateTimeOffset Recently()
    {
        var now = TimeProvider.System.GetUtcNow();

        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static RawEvent Minimal(Guid siteId) => new()
    {
        EventId = Guid.CreateVersion7(),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = TimeProvider.System.GetUtcNow(),
        Host = "example.com",
        Path = "/posts/hello",
    };

    private static RawEvent Full(Guid siteId, DateTimeOffset observed) => new()
    {
        EventId = Guid.CreateVersion7(observed),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.CloudflareWorker,
        ServerTimestamp = observed,
        ClientTimestamp = observed.AddSeconds(-2),
        ClockSkewMs = -2000,
        VisitorKey = "9f2a1c4e8b6d0a3f",
        Host = "example.com",
        Path = "/posts/hello",
        QueryString = "?utm_source=news",
        Referrer = "https://news.example.org/story/42",
        ReferrerDomain = "news.example.org",
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        StatusCode = 404,
        ContentType = "text/html",
        ResponseBytes = 1536,
        IpAddress = "203.0.113.7",
        ViewportWidth = 1440,
        ViewportHeight = 900,
        Language = "en-GB",
        TimezoneOffsetMinutes = 60,
        EngagedMs = 8_400,
        ScrollDepthPercent = 72,
        HadPointerInteraction = true,
        HadKeyboardInteraction = true,
        DeclaredWebDriver = false,
        CorrelationId = "2a6f5b9c",
    };
}

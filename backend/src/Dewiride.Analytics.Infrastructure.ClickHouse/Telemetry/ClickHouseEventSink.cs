using System.Collections.Frozen;
using ClickHouse.Driver;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Telemetry;

/// <summary>
/// Writes accepted events to the telemetry store.
/// </summary>
/// <remarks>
/// Rows are written in ClickHouse's binary row format rather than as generated INSERT text, so
/// nothing an event carries is ever parsed as SQL. Since every string on an event — the user
/// agent, the referrer, the path a crawler asked for — is written by whoever is visiting the
/// customer's site, that is a property worth having by construction rather than by review.
/// </remarks>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseEventSink(IClickHouseClient client) : IEventSink
{
    private const string TableName = "events";
    private const string Unobserved = "Unobserved";

    /// <summary>
    /// Insert columns, in the order <see cref="ToRow"/> produces values for them.
    /// </summary>
    private static readonly string[] Columns =
    [
        "event_id",
        "site_id",
        "kind",
        "surface",
        "server_ts",
        "client_ts",
        "clock_skew_ms",
        "visitor_key",
        "host",
        "path",
        "query_string",
        "referrer",
        "referrer_domain",
        "user_agent",
        "status_code",
        "content_type",
        "response_bytes",
        "ip_address",
        "viewport_width",
        "viewport_height",
        "language",
        "timezone_offset_minutes",
        "engaged_ms",
        "scroll_depth_percent",
        "had_pointer_interaction",
        "had_keyboard_interaction",
        "declared_web_driver",
        "correlation_id",
    ];

    /// <summary>
    /// Stored name for each event kind. Written out rather than derived from the member name, so
    /// that renaming a member in code cannot quietly change the meaning of years of stored rows.
    /// </summary>
    private static readonly FrozenDictionary<EventKind, string> KindNames =
        new Dictionary<EventKind, string>
        {
            [EventKind.PageView] = "PageView",
            [EventKind.Engagement] = "Engagement",
            [EventKind.Exit] = "Exit",
        }.ToFrozenDictionary();

    /// <summary>Stored name for each capture surface, on the same terms as <see cref="KindNames"/>.</summary>
    private static readonly FrozenDictionary<IngestSurface, string> SurfaceNames =
        new Dictionary<IngestSurface, string>
        {
            [IngestSurface.Unknown] = "Unknown",
            [IngestSurface.BrowserTracker] = "BrowserTracker",
            [IngestSurface.NoScriptPixel] = "NoScriptPixel",
            [IngestSurface.CloudflareWorker] = "CloudflareWorker",
            [IngestSurface.WordPressPlugin] = "WordPressPlugin",
            [IngestSurface.NetlifyEdge] = "NetlifyEdge",
            [IngestSurface.VercelEdge] = "VercelEdge",
            [IngestSurface.AspNetCoreMiddleware] = "AspNetCoreMiddleware",
            [IngestSurface.NextJsMiddleware] = "NextJsMiddleware",
            [IngestSurface.LogImport] = "LogImport",
        }.ToFrozenDictionary();

    /// <inheritdoc />
    public Task WriteAsync(RawEvent rawEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        return WriteRowsAsync([ToRow(rawEvent)], cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteBatchAsync(IReadOnlyCollection<RawEvent> rawEvents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawEvents);

        return rawEvents.Count == 0
            ? Task.CompletedTask
            : WriteRowsAsync(rawEvents.Select(ToRow), cancellationToken);
    }

    private static object[] ToRow(RawEvent source) =>
    [
        source.EventId,
        source.SiteId,
        KindNames[source.Kind],
        SurfaceNames[source.Surface],
        source.ServerTimestamp.UtcDateTime,
        Optional(source.ClientTimestamp),
        source.ClockSkewMs,
        Text(source.VisitorKey),
        source.Host,
        source.Path,
        Text(source.QueryString),
        Text(source.Referrer),
        Text(source.ReferrerDomain),
        Text(source.UserAgent),
        Optional(source.StatusCode),
        Text(source.ContentType),
        Optional(source.ResponseBytes),
        Text(source.IpAddress),
        Optional(source.ViewportWidth),
        Optional(source.ViewportHeight),
        Text(source.Language),
        Optional(source.TimezoneOffsetMinutes),
        Optional(source.EngagedMs),
        Optional(source.ScrollDepthPercent),
        Observed(source.HadPointerInteraction),
        Observed(source.HadKeyboardInteraction),
        Observed(source.DeclaredWebDriver),
        Text(source.CorrelationId),
    ];

    /// <summary>
    /// Renders an optional string. Absent and empty are the same thing for every text column on
    /// an event: no visitor key, referrer or content type is ever the empty string, so one
    /// representation carries both without ambiguity and without a null mask on disk.
    /// </summary>
    /// <param name="value">The text, if there was any.</param>
    /// <returns>The text, or the empty string.</returns>
    private static string Text(string? value) => value ?? string.Empty;

    /// <summary>
    /// Renders an optional instant in UTC, which is the only form the store holds.
    /// </summary>
    /// <param name="value">The instant, if there was one.</param>
    /// <returns>The instant, or a null marker.</returns>
    private static object Optional(DateTimeOffset? value) =>
        value.HasValue ? value.Value.UtcDateTime : (object)DBNull.Value;

    /// <summary>
    /// Renders an optional reading, distinguishing "not observed" from any value it could hold.
    /// </summary>
    /// <typeparam name="T">The reading's type.</typeparam>
    /// <param name="value">The reading, if there was one.</param>
    /// <returns>The value, or a null marker.</returns>
    private static object Optional<T>(T? value)
        where T : struct =>
        value.HasValue ? value.Value : (object)DBNull.Value;

    /// <summary>
    /// Renders a three-state observation. A surface that cannot see an interaction records that
    /// it could not see it, which is not the same claim as recording that none happened.
    /// </summary>
    /// <param name="value">What was observed, if anything could be.</param>
    /// <returns>The stored name.</returns>
    private static string Observed(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        _ => Unobserved,
    };

    private async Task WriteRowsAsync(IEnumerable<object[]> rows, CancellationToken cancellationToken)
    {
        var options = new InsertOptions
        {
            CustomSettings = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // The server coalesces the small writes a beacon produces into parts large
                // enough for the merge tree to keep up with, instead of one part per page view.
                ["async_insert"] = 1,

                // The call still waits for that buffer to be flushed, so an event this method
                // reports as written has been stored rather than merely handed over.
                ["wait_for_async_insert"] = 1,
            },
        };

        await client.InsertBinaryAsync(TableName, Columns, rows, options, cancellationToken)
            .ConfigureAwait(false);
    }
}

using System.Collections.Frozen;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Turns a question from the analytics vocabulary into a statement for the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in the product where SQL is produced, and it produces it from two
/// sources and no others: text written here, and the fixed tables below that map each closed
/// enumeration onto an approved fragment. Nothing a caller supplies is ever concatenated —
/// values are bound, and the site identifier and time zone come from an authorisation decision
/// rather than from the request.
/// </para>
/// <para>
/// That matters more here than in most applications. This product's own data is written by
/// whoever is crawling the customer's site, so user agents, referrers and requested paths are
/// hostile text by default. Keeping them permanently on the value side of the boundary is what
/// lets the rest of the system treat them as ordinary data.
/// </para>
/// </remarks>
public static class AnalyticsSqlCompiler
{
    private const string SiteIdParameter = "site_id";
    private const string FromParameter = "from_ms";
    private const string ToParameter = "to_ms";
    private const string TimeZoneParameter = "time_zone";

    /// <summary>
    /// Bucket function per granularity. Bucketing runs in the site's own time zone, so the
    /// boundaries are the ones its owner experiences rather than UTC's.
    /// </summary>
    private static readonly FrozenDictionary<TimeGranularity, string> BucketFunctions =
        new Dictionary<TimeGranularity, string>
        {
            [TimeGranularity.Hour] = "toStartOfHour",
            [TimeGranularity.Day] = "toStartOfDay",
        }.ToFrozenDictionary();

    /// <summary>
    /// Gap-filling step per granularity. Expressed as a calendar interval rather than a fixed
    /// number of seconds so that a day which is not twenty-four hours long still produces
    /// exactly one bucket.
    /// </summary>
    private static readonly FrozenDictionary<TimeGranularity, string> StepIntervals =
        new Dictionary<TimeGranularity, string>
        {
            [TimeGranularity.Hour] = "INTERVAL 1 HOUR",
            [TimeGranularity.Day] = "INTERVAL 1 DAY",
        }.ToFrozenDictionary();

    /// <summary>
    /// Aggregate per metric. Visitors excludes events that carry no visitor key: a surface that
    /// could not derive one has not observed an anonymous visitor, it has observed nothing about
    /// who was there, and counting those together would invent a single busy phantom.
    /// </summary>
    private static readonly FrozenDictionary<TimeSeriesMetric, string> MetricExpressions =
        new Dictionary<TimeSeriesMetric, string>
        {
            [TimeSeriesMetric.PageViews] = "toInt64(countIf(kind = 'PageView'))",
            [TimeSeriesMetric.Visitors] = "toInt64(uniqExactIf(visitor_key, visitor_key != ''))",
        }.ToFrozenDictionary();

    /// <summary>
    /// Compiles a question into a statement.
    /// </summary>
    /// <param name="scope">The authorisation decision the statement is bound to.</param>
    /// <param name="query">The question.</param>
    /// <returns>The statement and its bound values.</returns>
    /// <exception cref="NotSupportedException">The vocabulary has a case this compiler has not been taught.</exception>
    public static CompiledStatement Compile(TenantScope scope, AnalyticsQuery query)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        return query switch
        {
            OverviewQuery overview => CompileOverview(scope, overview),
            TimeSeriesQuery series => CompileTimeSeries(scope, series),
            _ => throw new NotSupportedException(
                $"No statement is defined for {query.GetType().Name}."),
        };
    }

    private static CompiledStatement CompileOverview(TenantScope scope, OverviewQuery query)
    {
        const string sql = """
            SELECT
                toInt64(countIf(kind = 'PageView')) AS page_views,
                toInt64(uniqExactIf(visitor_key, visitor_key != '')) AS visitors,
                toInt64(count()) AS events
            FROM events
            WHERE site_id = {site_id:UUID}
              AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
              AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
            """;

        return new CompiledStatement(sql, WindowParameters(scope, query.Range));
    }

    private static CompiledStatement CompileTimeSeries(TenantScope scope, TimeSeriesQuery query)
    {
        var bucket = BucketFunctions[query.Granularity];
        var step = StepIntervals[query.Granularity];
        var metric = MetricExpressions[query.Metric];

        // The upper fill bound is derived from one millisecond before the exclusive end of the
        // window, so the series stops at the last bucket that could hold data. Bounding it on the
        // end instant itself would append an empty bucket whenever a window ends exactly on a
        // boundary, and truncate the final partial bucket whenever it does not.
        var sql = $$"""
            SELECT
                {{bucket}}(server_ts, {time_zone:String}) AS bucket,
                {{metric}} AS value
            FROM events
            WHERE site_id = {site_id:UUID}
              AND server_ts >= fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC')
              AND server_ts < fromUnixTimestamp64Milli({to_ms:Int64}, 'UTC')
            GROUP BY bucket
            ORDER BY bucket
            WITH FILL
                FROM {{bucket}}(fromUnixTimestamp64Milli({from_ms:Int64}, 'UTC'), {time_zone:String})
                TO {{bucket}}(fromUnixTimestamp64Milli({to_ms:Int64} - 1, 'UTC'), {time_zone:String}) + {{step}}
                STEP {{step}}
            """;

        return new CompiledStatement(
            sql,
            [.. WindowParameters(scope, query.Range), new QueryParameter(TimeZoneParameter, scope.TimeZoneId)]);
    }

    private static QueryParameter[] WindowParameters(TenantScope scope, TimeRange range) =>
    [
        new(SiteIdParameter, scope.SiteId),
        new(FromParameter, range.From.ToUnixTimeMilliseconds()),
        new(ToParameter, range.To.ToUnixTimeMilliseconds()),
    ];
}

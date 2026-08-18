using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// Reads the telemetry store.
/// </summary>
/// <remarks>
/// Every method takes a <see cref="TenantScope"/>, which can only be produced by
/// <see cref="ITenantScopeProvider"/> after it has checked membership. Tenant isolation is
/// therefore a property of the type signature rather than a rule implementers are asked to
/// remember: there is no way to express a telemetry read without having been authorised for
/// the site it reads.
/// </remarks>
public interface ITelemetryQueries
{
    /// <summary>Returns headline totals for a site over a window.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to summarise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The totals.</returns>
    Task<OverviewResult> GetOverviewAsync(
        TenantScope scope,
        OverviewQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns one metric bucketed over time.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The metric, window and bucket size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The buckets, in ascending time order, with empty buckets present and zeroed.</returns>
    Task<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        TenantScope scope,
        TimeSeriesQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <param name="PageViews">Page views observed.</param>
/// <param name="Visitors">
/// Distinct visitor keys observed. Because the key rotates daily, a window longer than a day
/// counts a returning visitor once per day rather than once overall — which is stated in the
/// UI rather than quietly presented as a unique-people count.
/// </param>
/// <param name="Events">Total events of every kind, including engagement and exit reports.</param>
public readonly record struct OverviewResult(long PageViews, long Visitors, long Events);

/// <summary>
/// One bucket of a time series.
/// </summary>
/// <param name="BucketStart">Inclusive start of the bucket.</param>
/// <param name="Value">The metric's value within the bucket.</param>
public readonly record struct TimeSeriesPoint(DateTimeOffset BucketStart, long Value);

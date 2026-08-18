using System.Collections.Immutable;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

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

    /// <summary>Returns judged visits grouped by what generated them.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window to group over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per category and evidence strength, busiest first.</returns>
    Task<IReadOnlyList<TrafficBreakdownRow>> GetTrafficBreakdownAsync(
        TenantScope scope,
        TrafficBreakdownQuery query,
        CancellationToken cancellationToken);

    /// <summary>Returns individual judged visits with the evidence behind each verdict.</summary>
    /// <param name="scope">Proof the caller may read this site.</param>
    /// <param name="query">The window and how many visits to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The visits, newest first.</returns>
    Task<IReadOnlyList<JudgedSession>> GetJudgedSessionsAsync(
        TenantScope scope,
        JudgedSessionsQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// One group of visits that reached the same conclusion with the same weight behind it.
/// </summary>
/// <remarks>
/// Category and strength are reported together rather than summed across strengths, because a
/// hundred visits called a crawler on weak evidence is a different statement from a hundred
/// called one on strong evidence, and collapsing the two would hide exactly the distinction this
/// product exists to make.
/// </remarks>
/// <param name="Category">What the engine concluded generated these visits.</param>
/// <param name="Strength">How much weight stood behind that conclusion.</param>
/// <param name="Sessions">How many visits fell into the group.</param>
/// <param name="PageViews">How many pages those visits asked for between them.</param>
public readonly record struct TrafficBreakdownRow(
    TrafficCategory Category,
    EvidenceStrength Strength,
    long Sessions,
    long PageViews);

/// <summary>
/// One judged visit, as it is read back for display.
/// </summary>
public sealed record JudgedSession
{
    /// <summary>Identity of the visit, derived from the visitor key and when the visit began.</summary>
    public required string SessionKey { get; init; }

    /// <summary>When the visit began.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the last activity on it was seen.</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>How many pages the visit asked for.</summary>
    public required int PageCount { get; init; }

    /// <summary>Which capture surfaces saw it.</summary>
    public required ImmutableArray<IngestSurface> Surfaces { get; init; }

    /// <summary>The conclusion, with the evidence for and against it.</summary>
    public required ClassificationVerdict Verdict { get; init; }
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

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The complete vocabulary of questions that may be asked of the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is <c>private protected</c>, so the cases live here and are added here. The
/// SQL compiler in the ClickHouse infrastructure project pattern-matches over them and builds
/// statements from a static identifier allow-list with every value bound as a parameter, which
/// means there is no code path anywhere that turns caller-supplied text into SQL. A question the
/// compiler has not been taught produces no statement at all: it throws.
/// </para>
/// <para>
/// That property carries more weight here than in an ordinary application, because this
/// product's own dataset is attacker-controlled text — user agents, referrers, and the URL
/// paths crawlers ask for. It is also what makes the natural-language layer safe to build
/// later: the model chooses <em>which</em> of these questions to ask and never how to
/// execute one.
/// </para>
/// </remarks>
public abstract record AnalyticsQuery
{
    private protected AnalyticsQuery(TimeRange range)
    {
        Range = range;
    }

    /// <summary>The window the question is asked over.</summary>
    public TimeRange Range { get; }
}

/// <summary>
/// Headline totals for a site over a window.
/// </summary>
/// <param name="Range">The window to summarise.</param>
public sealed record OverviewQuery(TimeRange Range) : AnalyticsQuery(Range);

/// <summary>
/// A single metric bucketed over time.
/// </summary>
/// <param name="Range">The window to cover.</param>
/// <param name="Granularity">Bucket size.</param>
/// <param name="Metric">Which metric to bucket.</param>
public sealed record TimeSeriesQuery(TimeRange Range, TimeGranularity Granularity, TimeSeriesMetric Metric)
    : AnalyticsQuery(Range);

/// <summary>Bucket size for a time series.</summary>
public enum TimeGranularity
{
    /// <summary>One bucket per hour.</summary>
    Hour = 1,

    /// <summary>One bucket per day, in the site's configured time zone.</summary>
    Day = 2,
}

/// <summary>Which metric a time series reports.</summary>
public enum TimeSeriesMetric
{
    /// <summary>Number of page views.</summary>
    PageViews = 1,

    /// <summary>Number of distinct visitor keys observed.</summary>
    Visitors = 2,
}

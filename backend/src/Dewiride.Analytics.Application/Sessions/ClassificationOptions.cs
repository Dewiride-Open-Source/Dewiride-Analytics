using System.ComponentModel.DataAnnotations;

namespace Dewiride.Analytics.Application.Sessions;

/// <summary>
/// How the engine works through a site's traffic.
/// </summary>
/// <remarks>
/// Every one of these changes what gets judged rather than merely how fast, so they are settings
/// an operator may need and not knobs for their own sake. The defaults suit a site with ordinary
/// traffic on a machine that is also running everything else.
/// </remarks>
public sealed class ClassificationOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Classification";

    /// <summary>
    /// Whether the engine judges traffic in the background.
    /// </summary>
    /// <remarks>
    /// Switching this off stops new verdicts being reached; it does not delete the ones already
    /// stored, and turning it back on resumes from where it stopped rather than starting again.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>How long to wait between runs.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a visitor may be quiet before their next activity counts as a new visit.
    /// </summary>
    /// <remarks>
    /// Half an hour is the convention across web analytics, and the value matters to more than
    /// tidiness: it is also how long a visit has to have been silent before the engine will judge
    /// it, so a longer timeout means verdicts arrive later.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:01:00", "12:00:00")]
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Longest stretch of time one pass covers.
    /// </summary>
    /// <remarks>
    /// Caps how much is reconstructed at once, which is what keeps an install that has been
    /// switched off for a month from trying to group a month of activity in a single statement.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:15:00", "7.00:00:00")]
    public TimeSpan LongestPass { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Passes one run makes before yielding.
    /// </summary>
    /// <remarks>
    /// With the default stretch this catches up six days of backlog per run, so an install that
    /// has been down for a while recovers over a few minutes rather than a few days, without any
    /// single pass being large.
    /// </remarks>
    [Range(1, 1000)]
    public int PassesPerRun { get; init; } = 24;

    /// <summary>
    /// Most pages carried back for any one visit.
    /// </summary>
    /// <remarks>
    /// The page count stays exact whatever this is; what it bounds is how many of the requested
    /// paths the engine examines, and a sweep is recognisable long before the thousandth one.
    /// </remarks>
    [Range(50, 100000)]
    public int MaxRequestsPerSession { get; init; } = 1000;
}

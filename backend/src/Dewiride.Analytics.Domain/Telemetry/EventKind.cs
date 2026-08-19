namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// The kind of activity a raw event records.
/// </summary>
public enum EventKind
{
    /// <summary>Unknown or not yet attributed. Never valid on a persisted event.</summary>
    Unknown = 0,

    /// <summary>A page was requested or rendered.</summary>
    PageView = 1,

    /// <summary>
    /// A periodic report of engaged time and interaction presence for a page already
    /// reported as a <see cref="PageView"/>.
    /// </summary>
    Engagement = 2,

    /// <summary>
    /// The final report for a page, sent when the tab is hidden or unloaded. Carries the
    /// closing engaged-time and scroll-depth totals.
    /// </summary>
    Exit = 3,

    /// <summary>
    /// A control on a page was operated: what it was, what it said, and where it pointed.
    /// Reported only by the browser tracker, and only for sites that have it switched on.
    /// </summary>
    Action = 4,
}

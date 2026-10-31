using System.ComponentModel.DataAnnotations;

namespace Dewiride.Analytics.Application.Consolidation;

/// <summary>
/// How the engine keeps each website's monthly totals.
/// </summary>
public sealed class ConsolidationOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Consolidation";

    /// <summary>
    /// Whether the engine counts and keeps finished months in the background.
    /// </summary>
    /// <remarks>
    /// Switching this off keeps nothing new; it deletes nothing already kept. A month whose
    /// reports expire while it is off is never counted, and its totals are lost with them.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>How long after start-up the first run waits.</summary>
    /// <remarks>
    /// Long enough for the process to be serving requests before it starts reading a whole month
    /// of a columnar store, and short enough that a new installation's history is kept within
    /// minutes of it starting.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan StartDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How long to wait between runs.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Most months one run counts for any one website.
    /// </summary>
    /// <remarks>
    /// Counting a month reads every report it holds, so a website with a year of history is
    /// caught up over a few runs rather than in one long one that holds up every other website.
    /// The oldest go first, because they are the nearest to losing their reports.
    /// </remarks>
    [Range(1, 36)]
    public int MonthsPerRun { get; init; } = 4;
}

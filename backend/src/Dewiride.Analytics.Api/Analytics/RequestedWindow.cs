using System.Globalization;
using Dewiride.Analytics.Application.Analytics;

namespace Dewiride.Analytics.Api.Analytics;

/// <summary>
/// Turns the window a caller asked for into one the telemetry store will be asked to read.
/// </summary>
/// <remarks>
/// The limits here are not arbitrary politeness. Every question reaches a columnar store that
/// will happily scan a decade, and an endpoint that accepts any window at all is a way for one
/// signed-in person to occupy the whole machine. Refusing with an explanation is better than
/// answering slowly.
/// </remarks>
internal static class RequestedWindow
{
    /// <summary>Window used when the caller names neither end.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromDays(7);

    /// <summary>
    /// Longest window any question may cover.
    /// </summary>
    /// <remarks>
    /// A little over a year, which is past the point where raw events have aged out of the store
    /// under the default retention, so a longer window could only ever return less than it looks
    /// like it should.
    /// </remarks>
    public static readonly TimeSpan Longest = TimeSpan.FromDays(400);

    /// <summary>
    /// Longest window that may be cut into hourly buckets.
    /// </summary>
    /// <remarks>
    /// A month of hours is around seven hundred points, which is already more than a chart can
    /// show distinctly. Beyond that the answer is large, slow and unreadable at once, so the
    /// caller is asked for daily buckets instead.
    /// </remarks>
    public static readonly TimeSpan LongestByHour = TimeSpan.FromDays(31);

    /// <summary>
    /// Resolves the window, filling in whichever end was left out.
    /// </summary>
    /// <param name="from">Inclusive start the caller asked for, if any.</param>
    /// <param name="to">Exclusive end the caller asked for, if any.</param>
    /// <param name="longest">Longest window acceptable for this question.</param>
    /// <param name="clock">Clock, for the end of an open window.</param>
    /// <param name="range">The resolved window, when one could be resolved.</param>
    /// <param name="refusal">Why it could not be, in a sentence fit to show somebody.</param>
    /// <returns><see langword="true"/> when the window is usable.</returns>
    public static bool TryResolve(
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeSpan longest,
        TimeProvider clock,
        out TimeRange range,
        out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(clock);

        range = default;
        refusal = null;

        var end = to ?? clock.GetUtcNow();
        var start = from ?? end - Default;

        if (end <= start)
        {
            refusal = "The end of the period has to come after its start.";

            return false;
        }

        if (end - start > longest)
        {
            var days = longest.TotalDays.ToString("0", CultureInfo.InvariantCulture);

            refusal = $"That period is longer than the {days} days this can cover at once. "
                + "Ask for a shorter one.";

            return false;
        }

        range = new TimeRange(start, end);

        return true;
    }
}

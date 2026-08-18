namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// A half-open window over event time, <c>[From, To)</c>.
/// </summary>
/// <remarks>
/// Half-open by construction so that consecutive windows tile without double-counting the
/// boundary instant — the arithmetic that quietly inflates "yesterday versus today"
/// comparisons in analytics products.
/// </remarks>
public readonly record struct TimeRange
{
    /// <summary>Inclusive start of the window.</summary>
    public DateTimeOffset From { get; }

    /// <summary>Exclusive end of the window.</summary>
    public DateTimeOffset To { get; }

    /// <summary>Creates a window.</summary>
    /// <param name="from">Inclusive start.</param>
    /// <param name="to">Exclusive end. Must be strictly after <paramref name="from"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The window is empty or inverted.</exception>
    public TimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(to),
                to,
                "The end of a time range must be strictly after its start.");
        }

        From = from;
        To = to;
    }

    /// <summary>Length of the window.</summary>
    public TimeSpan Duration => To - From;

    /// <summary>
    /// Builds the window ending at <paramref name="now"/> and covering the preceding
    /// <paramref name="duration"/>.
    /// </summary>
    /// <param name="now">The current instant, from the injected clock.</param>
    /// <param name="duration">How far back to look. Must be positive.</param>
    /// <returns>The window.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public static TimeRange EndingAt(DateTimeOffset now, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be positive.");
        }

        return new TimeRange(now - duration, now);
    }

    /// <summary>Determines whether an instant falls inside the window.</summary>
    /// <param name="instant">The instant to test.</param>
    /// <returns><see langword="true"/> when the instant is within <c>[From, To)</c>.</returns>
    public bool Contains(DateTimeOffset instant) => instant >= From && instant < To;
}

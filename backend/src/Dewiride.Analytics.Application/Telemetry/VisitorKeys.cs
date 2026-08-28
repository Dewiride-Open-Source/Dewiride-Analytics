namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// What holds true of every visitor key, wherever one is read.
/// </summary>
public static class VisitorKeys
{
    /// <summary>
    /// The longest a visit can be, and the reason it is knowable rather than guessed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A visit is a visitor key's activity, and <see cref="IVisitorKeyFactory.Derive"/> mixes the
    /// day the report was observed into the key itself. Every report under one key was therefore
    /// received on the same day, and a visit under it cannot run longer than one. A statement that
    /// has read a day either side of what it is asking about has read the whole of every visit
    /// that could touch it.
    /// </para>
    /// <para>
    /// It bounds a visit rather than a reading. How long a visitor is quiet before their next
    /// activity counts as a new visit is a setting an operator chooses; this is a consequence of
    /// how the key is built, and changing it would mean changing that.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan LongestVisit = TimeSpan.FromHours(24);
}

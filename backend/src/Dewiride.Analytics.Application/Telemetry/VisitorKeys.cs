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

    /// <summary>
    /// Longest visitor key any value naming one may carry.
    /// </summary>
    /// <remarks>
    /// A bound rather than the exact length <see cref="IVisitorKeyFactory.Derive"/> produces, which
    /// is a property of how the key is built and not of what a visitor is called. Generous enough to
    /// survive a change to that, and short enough that nothing large is ever examined.
    /// </remarks>
    public const int LongestKey = 64;

    /// <summary>
    /// Tests whether a value is written the way every derived visitor key is.
    /// </summary>
    /// <remarks>
    /// Values of this shape reach the engine from an address somebody typed, so the test is strict:
    /// a key is lower-case hexadecimal and no longer than <see cref="LongestKey"/>. It is belt and
    /// braces, since a key always travels to the store as a bound value, and it is the point at
    /// which something that can name nobody is turned away rather than answered with an empty list.
    /// </remarks>
    /// <param name="value">The value as it was written.</param>
    /// <returns><see langword="true"/> when the value could name a visitor.</returns>
    public static bool IsWellFormed(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > LongestKey)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character) && !char.IsBetween(character, 'a', 'f'))
            {
                return false;
            }
        }

        return true;
    }
}

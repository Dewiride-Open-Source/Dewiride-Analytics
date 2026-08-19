using System.Globalization;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// The identity of one visit: whose it was, and when it began.
/// </summary>
/// <remarks>
/// <para>
/// Derived rather than allocated, so re-running the engine over the same activity produces the
/// same identity and a verdict can be matched back to the events behind it. The two parts are what
/// make it derivable, and they are also everything needed to find the visit again: the activity is
/// one visitor's, and it starts at a known instant.
/// </para>
/// <para>
/// The visitor key is a keyed hash rebuilt daily, so this identifies a visit for as long as the
/// visit's own activity is retained and identifies nobody afterwards. It is not a name for a
/// person and cannot be turned back into one.
/// </para>
/// <para>
/// Values of this shape reach the engine from an address somebody typed, so parsing is strict:
/// anything that is not a hexadecimal key followed by a whole number of milliseconds is refused
/// before it reaches a statement. The refusal is belt and braces — both parts travel as bound
/// values — but a key that cannot name a visit should be turned away where it arrives rather than
/// answered with an empty list.
/// </para>
/// </remarks>
public readonly record struct VisitKey
{
    /// <summary>Separates the two parts. Absent from the alphabet either of them can use.</summary>
    private const char Separator = ':';

    /// <summary>
    /// Longest visitor key accepted.
    /// </summary>
    /// <remarks>
    /// A bound rather than the exact length the engine produces, which is a property of how the
    /// key is derived and not of what a visit is called. Generous enough to survive a change to
    /// that, and short enough that nothing large is ever parsed.
    /// </remarks>
    private const int LongestVisitorKey = 64;

    /// <summary>
    /// Latest instant an identity may name, in milliseconds since the epoch.
    /// </summary>
    /// <remarks>
    /// A number too large to be an instant would otherwise be a throw rather than a refusal, and
    /// this value arrives from an address somebody typed.
    /// </remarks>
    private static readonly long LatestInstantMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>Creates a visit identity.</summary>
    /// <param name="visitorKey">The visitor whose activity it is.</param>
    /// <param name="startedAt">When the first activity on it was received.</param>
    /// <exception cref="ArgumentException">The visitor key is empty.</exception>
    public VisitKey(string visitorKey, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(visitorKey);

        VisitorKey = visitorKey;
        StartedAt = startedAt;
    }

    /// <summary>The visitor whose activity the visit is.</summary>
    public string VisitorKey { get; }

    /// <summary>When the first activity on the visit was received.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Reads a visit identity from the form it is published in.
    /// </summary>
    /// <param name="value">The identity as it was written.</param>
    /// <param name="visit">The identity, where it was one.</param>
    /// <returns><see langword="true"/> when the value names a visit.</returns>
    public static bool TryParse(string? value, out VisitKey visit)
    {
        visit = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf(Separator, StringComparison.Ordinal);

        if (separator <= 0 || separator > LongestVisitorKey)
        {
            return false;
        }

        var visitorKey = value[..separator];

        if (!IsHexadecimal(visitorKey))
        {
            return false;
        }

        if (!long.TryParse(
                value[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var startedAtMs))
        {
            return false;
        }

        if (startedAtMs > LatestInstantMs)
        {
            return false;
        }

        visit = new VisitKey(visitorKey, DateTimeOffset.FromUnixTimeMilliseconds(startedAtMs));

        return true;
    }

    /// <summary>Writes the identity in the form it is published in.</summary>
    /// <returns>The visitor key, a colon, and the start in milliseconds since the epoch.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{VisitorKey}{Separator}{StartedAt.ToUnixTimeMilliseconds()}");

    /// <summary>
    /// Tests whether every character is a lower-case hexadecimal digit, which is the whole
    /// alphabet a derived visitor key is written in.
    /// </summary>
    private static bool IsHexadecimal(ReadOnlySpan<char> value)
    {
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

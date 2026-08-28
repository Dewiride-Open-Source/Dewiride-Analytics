using System.Globalization;

namespace Dewiride.Analytics.Classification;

/// <summary>
/// Identifies the exact set of detection rules that produced a verdict.
/// </summary>
/// <remarks>
/// Stamped on every stored classification and on every aggregate row derived from one. This
/// is what lets the dashboard state which ruleset produced a number, lets a window be
/// deterministically re-classified when the rules improve, and lets a rebuild be told apart
/// from a regression. Without it, improving the rules would silently rewrite history with no
/// way to explain the change to a customer who noticed.
/// </remarks>
/// <param name="Major">Incremented when a change can move sessions between categories.</param>
/// <param name="Minor">Incremented when weights or thresholds change within the same categories.</param>
public readonly record struct RulesetVersion(int Major, int Minor) : IComparable<RulesetVersion>
{
    /// <summary>The ruleset currently compiled into this build.</summary>
    /// <remarks>
    /// <para>
    /// Eight, because a visit is now judged on everything stored about it rather than on the part
    /// that fell inside the stretch of time being worked through. A backlog is walked in stretches
    /// of a few hours, and a visit lying across the end of one of them was judged on what fitted
    /// and never looked at again — so what a verdict rested on depended on where a boundary
    /// happened to fall, which is the one thing a stored ruleset is supposed to rule out. Whether a
    /// visit is over is now asked of the moment nothing more can arrive, and activity is read a full
    /// day either side, which is as long as a visit can be.
    /// </para>
    /// <para>
    /// No category, band, weight or threshold moved. What changed is how much of a visit the engine
    /// is shown. Re-judging is where this is repaired: a departure sent hours after the page it
    /// names has long since arrived by the time history is walked again, so a visit judged in the
    /// moment on half its reading is judged on all of it now. What cannot be repaired is the last
    /// stretch before the present, where a report that has not arrived yet is a report nothing can
    /// account for.
    /// </para>
    /// <para>
    /// Seven, because a visit begins where somebody arrived. A tracker reports how a page is
    /// going, and reports it being left, from the page itself, and either report can reach the
    /// collector under a visitor key that never announced anything — a key is derived from the
    /// network a report came over and the day it arrived, so it changes under a reader who moves
    /// between networks and again at midnight. Six treated such a report as a visit in its own
    /// right, which counted one reader twice and put the second of them down as a person who
    /// arrived, read for a quarter of an hour and left.
    /// </para>
    /// <para>
    /// A report now belongs to the visit its own page was arrived at in, and one naming a page its
    /// visitor was never seen arriving at is left out. The page was delivered to somebody, but
    /// nothing on the report says which visit it belonged to, and inventing one is the kind of
    /// certainty this product does not claim.
    /// </para>
    /// <para>
    /// No category, band, weight or threshold moved. Visits that were always visits are judged on
    /// exactly the evidence they were judged on before, except where a departure reported after a
    /// long pause is read in the same pass as the visit it names, in which case the reading it
    /// carries goes back to that visit. What changed is which activity is a visit at all.
    /// </para>
    /// <para>
    /// Verdicts are kept per ruleset, so every earlier answer stays on record and history is
    /// re-judged rather than rewritten. What six recorded as a visit and seven does not is removed
    /// by <c>0008_visits_that_never_began</c>, because no later ruleset can supersede a verdict on
    /// something that will never be reconstructed again.
    /// </para>
    /// </remarks>
    public static RulesetVersion Current => new(8, 0);

    /// <summary>Compares two ruleset versions by major then minor component.</summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns>A signed value indicating relative order.</returns>
    public int CompareTo(RulesetVersion other)
    {
        var major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    /// <summary>Determines whether one version precedes another.</summary>
    public static bool operator <(RulesetVersion left, RulesetVersion right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one version precedes or equals another.</summary>
    public static bool operator <=(RulesetVersion left, RulesetVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one version follows another.</summary>
    public static bool operator >(RulesetVersion left, RulesetVersion right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one version follows or equals another.</summary>
    public static bool operator >=(RulesetVersion left, RulesetVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Renders the version as <c>major.minor</c>.</summary>
    /// <returns>The canonical string form.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    /// <summary>Parses the canonical <c>major.minor</c> form.</summary>
    /// <param name="value">The string to parse.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException">The value is not in <c>major.minor</c> form.</exception>
    public static RulesetVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var separator = value.IndexOf('.', StringComparison.Ordinal);
        if (separator > 0
            && int.TryParse(value.AsSpan(0, separator), CultureInfo.InvariantCulture, out var major)
            && int.TryParse(value.AsSpan(separator + 1), CultureInfo.InvariantCulture, out var minor))
        {
            return new RulesetVersion(major, minor);
        }

        throw new FormatException($"'{value}' is not a valid ruleset version. Expected 'major.minor'.");
    }
}

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
    public static RulesetVersion Current => new(1, 0);

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

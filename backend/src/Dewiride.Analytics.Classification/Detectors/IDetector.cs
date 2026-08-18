using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Looks at a session and reports what it sees, without drawing a conclusion.
/// </summary>
/// <remarks>
/// <para>
/// A detector never chooses a category. It reports observations and the weight each carries, and
/// the scorecard alone decides what they add up to. Keeping the two apart is what makes the
/// reasoning legible: every verdict can be traced to the observations behind it, and a detector
/// can be reasoned about, tested and replaced without touching what any category means.
/// </para>
/// <para>
/// Implementations must be pure. Same session in, same signals out, on any machine at any time —
/// that is what makes the golden-fixture suite a regression gate rather than a source of noise.
/// </para>
/// </remarks>
public interface IDetector
{
    /// <summary>
    /// Examines a session.
    /// </summary>
    /// <param name="session">The session to look at.</param>
    /// <returns>
    /// What was observed, in any order. Empty when the detector has nothing to say — which is the
    /// correct answer whenever the surfaces involved could not observe what it looks for.
    /// </returns>
    ImmutableArray<Signal> Examine(SessionEvidence session);
}

/// <summary>
/// Shorthand for building signals, so detectors read as observations rather than as construction.
/// </summary>
internal static class Observed
{
    /// <summary>Builds a signal.</summary>
    /// <param name="code">Stable code from <see cref="SignalCodes"/>.</param>
    /// <param name="direction">Which way the evidence points.</param>
    /// <param name="weight">Relative weight, 0 to 100.</param>
    /// <param name="parameters">Values the rendered sentence substitutes in.</param>
    /// <returns>The signal.</returns>
    public static Signal Signal(
        string code,
        SignalDirection direction,
        int weight,
        params (string Key, string Value)[] parameters) => new()
        {
            Code = code,
            Direction = direction,
            Weight = weight,
            Parameters = parameters.Length == 0
                ? FrozenDictionary<string, string>.Empty
                : parameters.ToFrozenDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    StringComparer.Ordinal),
        };

    /// <summary>
    /// Renders a number for a signal parameter.
    /// </summary>
    /// <remarks>
    /// Always invariant. The value is stored and compared by fixtures, and the interface formats
    /// it for the reader; a number written here in the server's locale would make a verdict
    /// depend on where it was computed.
    /// </remarks>
    /// <param name="value">The number.</param>
    /// <returns>Its invariant form.</returns>
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

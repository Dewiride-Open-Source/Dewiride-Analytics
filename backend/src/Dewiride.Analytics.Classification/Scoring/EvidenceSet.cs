using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Scoring;

/// <summary>
/// Everything the detectors observed about one session, arranged so the rules can ask questions of it.
/// </summary>
/// <remarks>
/// A read-only view rather than a list, so a rule reads as a question about the evidence — "was a
/// crawler named, and was it an AI one" — instead of as a search through an array. The rules are
/// the part of the engine most likely to be argued over, so they are the part that has to be
/// legible.
/// </remarks>
public sealed class EvidenceSet
{
    private readonly ImmutableDictionary<string, Signal> _byCode;

    /// <summary>Everything that was observed, in the order the detectors reported it.</summary>
    public ImmutableArray<Signal> All { get; }

    /// <summary>Builds a view over what was observed.</summary>
    /// <param name="signals">The observations.</param>
    public EvidenceSet(ImmutableArray<Signal> signals)
    {
        All = signals;

        // Two detectors reporting the same code would be a fault in the engine rather than
        // something to reconcile here, so the first is kept and the shape stays a lookup.
        _byCode = signals
            .GroupBy(signal => signal.Code, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>Whether an observation was made.</summary>
    /// <param name="code">The signal code.</param>
    /// <returns><see langword="true"/> when a detector reported it.</returns>
    public bool Has(string code) => _byCode.ContainsKey(code);

    /// <summary>Reads a value carried by an observation.</summary>
    /// <param name="code">The signal code.</param>
    /// <param name="key">The parameter name.</param>
    /// <returns>The value, or <see langword="null"/> when the signal or the parameter is absent.</returns>
    public string? Parameter(string code, string key) =>
        _byCode.TryGetValue(code, out var signal) && signal.Parameters.TryGetValue(key, out var value)
            ? value
            : null;

    /// <summary>Weight of one observation, or nought when it was not made.</summary>
    /// <param name="code">The signal code.</param>
    /// <returns>The weight.</returns>
    public int WeightOf(string code) => _byCode.TryGetValue(code, out var signal) ? signal.Weight : 0;

    /// <summary>Every observation pointing a given way.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The observations, heaviest first.</returns>
    public ImmutableArray<Signal> Pointing(SignalDirection direction) =>
        [.. All.Where(signal => signal.Direction == direction).OrderByDescending(signal => signal.Weight)];

    /// <summary>The heaviest observation pointing a given way, or nought when there is none.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The weight.</returns>
    public int HeaviestPointing(SignalDirection direction) =>
        All.Where(signal => signal.Direction == direction).Select(signal => signal.Weight).DefaultIfEmpty(0).Max();
}

using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification;

/// <summary>
/// The engine's conclusion about one session, together with everything that led to it.
/// </summary>
/// <remarks>
/// <para>
/// Supporting and contradicting evidence are both retained. A verdict that only shows what
/// agrees with it is an argument, not an assessment — and the product's claim is that it
/// shows its working. When a session scrolled and executed JavaScript and was still
/// classified as automation, the user is entitled to see both facts and the reason the
/// first did not outweigh the second.
/// </para>
/// <para>
/// <see cref="RulesetVersion"/> is stamped on every verdict because the rules change. Without
/// it, a number on a dashboard could not be reproduced or explained a month later, and
/// re-running a window against a newer ruleset could not be distinguished from a bug.
/// </para>
/// </remarks>
public sealed record ClassificationVerdict
{
    /// <summary>What the engine concluded generated the session.</summary>
    public required TrafficCategory Category { get; init; }

    /// <summary>How much weight stands behind that conclusion.</summary>
    public required EvidenceStrength Strength { get; init; }

    /// <summary>
    /// Whether this is a provisional verdict from the synchronous path, reached before the
    /// session closed and before out-of-band enrichment ran. The live view renders these
    /// visibly as not-final, and they are replaced when the session is closed and classified
    /// in full. Presenting a provisional verdict as settled would be the product making a
    /// claim it has not yet earned.
    /// </summary>
    public bool IsProvisional { get; init; }

    /// <summary>Evidence supporting the conclusion, most significant first.</summary>
    public required ImmutableArray<Signal> Supporting { get; init; }

    /// <summary>
    /// Evidence pointing the other way, retained and shown rather than discarded.
    /// </summary>
    public ImmutableArray<Signal> Contradicting { get; init; } = [];

    /// <summary>The ruleset that produced this verdict.</summary>
    public required RulesetVersion RulesetVersion { get; init; }

    /// <summary>
    /// A verdict for a session about which nothing can yet be said.
    /// </summary>
    /// <param name="rulesetVersion">The ruleset in force.</param>
    /// <returns>An <see cref="TrafficCategory.InsufficientEvidence"/> verdict.</returns>
    public static ClassificationVerdict Insufficient(RulesetVersion rulesetVersion) => new()
    {
        Category = TrafficCategory.InsufficientEvidence,
        Strength = EvidenceStrength.None,
        Supporting = [],
        RulesetVersion = rulesetVersion,
    };
}

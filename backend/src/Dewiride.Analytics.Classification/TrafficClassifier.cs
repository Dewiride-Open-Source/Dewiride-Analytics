using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Scoring;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification;

/// <summary>
/// Decides what generated a session, and why.
/// </summary>
/// <remarks>
/// <para>
/// The whole engine, and the only thing outside this assembly needs to know about. Detectors
/// observe, the scorecard weighs, and this puts the two together — so a caller cannot obtain a
/// verdict without the evidence that produced it, and cannot obtain the evidence without the
/// ruleset that was in force.
/// </para>
/// <para>
/// Pure. Nothing here reads a clock, opens a connection or consults a cache, so the same session
/// judged on two machines a year apart produces the same verdict. That is what the golden-fixture
/// suite depends on, and it is what makes it possible to re-judge a window of stored history
/// against improved rules and know that any difference came from the rules.
/// </para>
/// </remarks>
/// <param name="detectors">The detectors to run, in the order their evidence should be listed.</param>
/// <param name="rulesetVersion">The ruleset stamped on every verdict this instance produces.</param>
public sealed class TrafficClassifier(ImmutableArray<IDetector> detectors, RulesetVersion rulesetVersion)
{
    /// <summary>
    /// The detectors that make up the ruleset compiled into this build.
    /// </summary>
    /// <remarks>
    /// Ordered so that what a session said about itself is read before what it did, which is the
    /// order the evidence reads in when somebody is shown it.
    /// </remarks>
    public static ImmutableArray<IDetector> Standard =>
    [
        new DeclaredIdentityDetector(),
        new ClientDeclarationDetector(),
        new NetworkDetector(),
        new RenderingDetector(),
        new ProbingDetector(),
        new RetrievalDetector(),
        new EngagementDetector(),
    ];

    /// <summary>The engine as this build ships it.</summary>
    /// <returns>A classifier running the standard detectors under the current ruleset.</returns>
    public static TrafficClassifier Current() => new(Standard, RulesetVersion.Current);

    /// <summary>
    /// The ruleset stamped on every verdict this instance produces.
    /// </summary>
    /// <remarks>
    /// Exposed because a stored verdict is filed under it. Verdicts are kept per ruleset, so
    /// improving the rules adds to history rather than rewriting it, and whatever reads them back
    /// has to know which rules it is looking at.
    /// </remarks>
    public RulesetVersion RulesetVersion => rulesetVersion;

    /// <summary>
    /// Judges one session.
    /// </summary>
    /// <param name="session">Everything known about it.</param>
    /// <returns>The verdict, with the evidence for and against.</returns>
    public ClassificationVerdict Classify(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // A session that asked for nothing is not a visit anybody can have an opinion about.
        // Without this, the surfaces alone would still speak — a session reported by the tracker
        // carries "a script ran", which points toward a person — and a partial or malformed
        // session would be counted as a reader. That is the one direction this product must not
        // be wrong in, so the absence of a visit is answered as the absence of an answer.
        if (session.Requests.IsEmpty)
        {
            return ClassificationVerdict.Insufficient(rulesetVersion);
        }

        var observed = ImmutableArray.CreateBuilder<Signal>();

        foreach (var detector in detectors)
        {
            observed.AddRange(detector.Examine(session));
        }

        return EvidenceScorecard.Weigh(new EvidenceSet(observed.ToImmutable()), rulesetVersion);
    }
}

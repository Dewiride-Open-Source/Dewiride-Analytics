using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Scoring;

/// <summary>
/// Decides what the evidence adds up to.
/// </summary>
/// <remarks>
/// <para>
/// Ordered rules, first match wins, then a fallback that weighs the two directions against each
/// other. The order is the design: a session can look like several things at once, and what it is
/// called should be decided by the most specific thing that can be said about it rather than by
/// whichever detector shouted loudest. A scanner that also scrolled a page is a scanner.
/// </para>
/// <para>
/// Nothing here can produce <see cref="EvidenceStrength.Verified"/>. That band is reserved for an
/// identity established from an operator's published addresses, which is a check this engine
/// cannot perform because it performs no I/O. Behaviour has no route to it, by construction
/// rather than by discipline.
/// </para>
/// </remarks>
public static class EvidenceScorecard
{
    /// <summary>Purposes that make a crawler an AI crawler rather than a search one.</summary>
    private static readonly ImmutableArray<string> AiPurposes = ["ai-training", "ai-assistant", "ai-search"];

    /// <summary>Weight at or above which one observation is hard to produce by accident.</summary>
    private const int Decisive = 65;

    /// <summary>Weight at or above which an observation is worth more than a passing remark.</summary>
    private const int Substantial = 55;

    /// <summary>Weight at or above which an observation counts toward corroboration.</summary>
    private const int Corroborating = 25;

    /// <summary>Weight at or above which evidence the other way must temper the conclusion.</summary>
    private const int Troubling = 50;

    /// <summary>
    /// Weighs the evidence.
    /// </summary>
    /// <param name="evidence">What the detectors observed.</param>
    /// <param name="rulesetVersion">The ruleset in force, stamped on the verdict.</param>
    /// <returns>The verdict, with the evidence for and against it.</returns>
    public static ClassificationVerdict Weigh(EvidenceSet evidence, RulesetVersion rulesetVersion)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.All.IsEmpty)
        {
            return ClassificationVerdict.Insufficient(rulesetVersion);
        }

        var category = Decide(evidence);
        var direction = DirectionOf(category);

        var supporting = direction == SignalDirection.Neutral
            ? evidence.All
            : [.. evidence.Pointing(direction), .. evidence.Pointing(SignalDirection.Neutral)];

        var contradicting = direction == SignalDirection.Neutral
            ? []
            : evidence.Pointing(Opposite(direction));

        return new ClassificationVerdict
        {
            Category = category,
            Strength = Strength(category, supporting, contradicting),
            Supporting = supporting,
            Contradicting = contradicting,
            RulesetVersion = rulesetVersion,
        };
    }

    /// <summary>
    /// Picks the most specific thing that can be said about the session.
    /// </summary>
    private static TrafficCategory Decide(EvidenceSet evidence)
    {
        // Asking for the places only an intruder looks for settles it. Nothing else a visitor does
        // explains a request for a credential store that was never published.
        if (evidence.Has(SignalCodes.SensitivePaths)
            || evidence.WeightOf(SignalCodes.MissingPaths) >= Decisive)
        {
            return TrafficCategory.SecurityScanner;
        }

        // The browser said so itself. Whatever else it did, it was being driven.
        if (evidence.Has(SignalCodes.DeclaredWebDriver))
        {
            return TrafficCategory.BrowserAutomation;
        }

        if (evidence.Has(SignalCodes.DeclaredCrawler))
        {
            var purpose = evidence.Parameter(SignalCodes.DeclaredCrawler, "purpose");

            // Named itself, and the name was not confirmed — so the category says "suspected" and
            // the interface is obliged to say so too. Confirming the claim is what moves this to
            // KnownAiCrawler, and only address verification can do that.
            return purpose is not null && AiPurposes.Contains(purpose)
                ? TrafficCategory.SuspectedAiCrawler
                : TrafficCategory.GenericWebCrawler;
        }

        if (IsSystematicRetrieval(evidence))
        {
            return TrafficCategory.ContentScraper;
        }

        return Weighed(evidence);
    }

    /// <summary>
    /// Whether the session took content systematically rather than used the site.
    /// </summary>
    private static bool IsSystematicRetrieval(EvidenceSet evidence)
    {
        if (string.Equals(
                evidence.Parameter(SignalCodes.DeclaredTool, "kind"),
                "scraping-framework",
                StringComparison.Ordinal))
        {
            return true;
        }

        // Covered the site, executed nothing, and left no trace of anybody reading it. Any one of
        // those has an innocent explanation; all three together do not.
        return evidence.Has(SignalCodes.RetrievalBreadth)
            && evidence.Has(SignalCodes.NoScriptExecution)
            && !evidence.Has(SignalCodes.ReadTime);
    }

    /// <summary>
    /// The answer when nothing specific can be said: which way does the evidence lean, and by
    /// enough to be worth saying?
    /// </summary>
    /// <remarks>
    /// A near-tie produces <see cref="TrafficCategory.Unknown"/> rather than the side that happens
    /// to be a point ahead. That is a real answer — the evidence was gathered and weighed and it
    /// does not support a conclusion — and reporting it honestly is worth more than a coin toss
    /// dressed up as a classification.
    /// </remarks>
    private static TrafficCategory Weighed(EvidenceSet evidence)
    {
        var human = evidence.HeaviestPointing(SignalDirection.TowardHuman);
        var automation = evidence.HeaviestPointing(SignalDirection.TowardAutomation);

        if (human >= Corroborating && human > automation)
        {
            return TrafficCategory.LikelyHuman;
        }

        if (automation < Corroborating || automation <= human)
        {
            return TrafficCategory.Unknown;
        }

        // Something automated, and it did not say what it was. A tool that names itself is
        // ordinary; one that arrives anonymously and behaves like this is the case worth looking at.
        return evidence.Has(SignalCodes.DeclaredTool)
            ? TrafficCategory.GenericWebCrawler
            : TrafficCategory.SuspiciousAutomation;
    }

    /// <summary>
    /// How much weight stands behind the conclusion.
    /// </summary>
    /// <remarks>
    /// Corroboration matters more than magnitude. Two independent observations agreeing is a
    /// stronger position than one loud one, because a single heavy signal is usually a single
    /// thing the visitor chose to say about itself.
    /// </remarks>
    private static EvidenceStrength Strength(
        TrafficCategory category,
        ImmutableArray<Signal> supporting,
        ImmutableArray<Signal> contradicting)
    {
        if (category is TrafficCategory.InsufficientEvidence)
        {
            return EvidenceStrength.None;
        }

        var counted = supporting.Where(signal => signal.Weight >= Corroborating).ToArray();

        if (counted.Length == 0)
        {
            return category is TrafficCategory.Unknown ? EvidenceStrength.None : EvidenceStrength.Weak;
        }

        var heaviest = counted.Max(signal => signal.Weight);
        var independent = counted.Length;

        var reached = (heaviest, independent) switch
        {
            ( >= Decisive, >= 2) => EvidenceStrength.Strong,
            ( >= Substantial, _) => EvidenceStrength.Moderate,
            (_, >= 2) => EvidenceStrength.Moderate,
            _ => EvidenceStrength.Weak,
        };

        // Evidence pointing the other way that could not simply be explained away holds the
        // conclusion back. It stays the conclusion — it is just no longer one to state firmly.
        var objection = contradicting.Select(signal => signal.Weight).DefaultIfEmpty(0).Max();

        return objection >= Troubling && reached > EvidenceStrength.Moderate
            ? EvidenceStrength.Moderate
            : reached;
    }

    /// <summary>Which way a category's evidence has to point to support it.</summary>
    private static SignalDirection DirectionOf(TrafficCategory category) => category switch
    {
        TrafficCategory.LikelyHuman => SignalDirection.TowardHuman,
        TrafficCategory.Unknown or TrafficCategory.InsufficientEvidence => SignalDirection.Neutral,
        _ => SignalDirection.TowardAutomation,
    };

    private static SignalDirection Opposite(SignalDirection direction) =>
        direction == SignalDirection.TowardHuman ? SignalDirection.TowardAutomation : SignalDirection.TowardHuman;
}

using System.Collections.Frozen;
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
/// One rule sits outside that order rather than at the top of it: an identity settled against what
/// a company says about its own crawlers' addresses is not weighed at all. It is the only thing
/// here the visitor did not author, so it decides the category and the strength together, and it
/// is the only route to <see cref="EvidenceStrength.Verified"/>. Behaviour never reaches that
/// band, by construction rather than by discipline — this engine performs no I/O, so it could not
/// check an address if it wanted to, and the answer arrives as evidence gathered when the visit
/// was.
/// </para>
/// </remarks>
public static class EvidenceScorecard
{
    /// <summary>
    /// What a crawler whose identity was established is called, by what its operator says it is for.
    /// </summary>
    /// <remarks>
    /// Reached only once the address has settled whose crawler it is, which is what makes every
    /// category here one of the ones with "known" in its name. A purpose absent from this table —
    /// archiving, auditing who links to whom, or none stated at all — leaves an ordinary crawler,
    /// which is what those are; the verdict still carries the company's name and the verified band.
    /// </remarks>
    private static readonly FrozenDictionary<string, TrafficCategory> ConfirmedCategories =
        new Dictionary<string, TrafficCategory>(StringComparer.Ordinal)
        {
            ["ai-training"] = TrafficCategory.KnownAiCrawler,
            ["ai-assistant"] = TrafficCategory.KnownAiCrawler,
            ["ai-search"] = TrafficCategory.KnownAiCrawler,
            ["search-index"] = TrafficCategory.KnownSearchCrawler,
            ["monitoring"] = TrafficCategory.MonitoringOrSynthetic,
            ["social-preview"] = TrafficCategory.KnownAutomatedService,
            ["site-tooling"] = TrafficCategory.KnownAutomatedService,
            ["advertising"] = TrafficCategory.KnownAutomatedService,
        }.ToFrozenDictionary();

    /// <summary>Weight at or above which one observation is hard to produce by accident.</summary>
    private const int Decisive = 65;

    /// <summary>Weight at or above which an observation is worth more than a passing remark.</summary>
    private const int Substantial = 55;

    /// <summary>Weight at or above which an observation counts toward corroboration.</summary>
    private const int Corroborating = 25;

    /// <summary>Weight at or above which evidence the other way must temper the conclusion.</summary>
    private const int Troubling = 50;

    /// <summary>
    /// How many counted observations, agreeing, reach the firmest behavioural band whatever the
    /// heaviest of them weighs.
    /// </summary>
    private const int Agreeing = 3;

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
        // Wearing one company's name while arriving from another company's crawler addresses.
        // Nothing legitimate produces that, and it rests on what two companies say about their
        // own machines rather than on anything the visitor chose to say about itself.
        if (evidence.Has(SignalCodes.FalseCrawlerClaim))
        {
            return TrafficCategory.SuspiciousAutomation;
        }

        // Who it is, established rather than claimed. Read before the probing rules on purpose: a
        // search crawler asking for pages that are not there is following links somebody once
        // published, and calling the largest search engine a scanner of the site it is indexing
        // would be this product being confidently wrong about the one thing it can actually know.
        if (evidence.Has(SignalCodes.ConfirmedCrawler))
        {
            return WhenConfirmed(evidence.Parameter(SignalCodes.ConfirmedCrawler, "purpose"));
        }

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

            // Named itself, and got past the rule above, so nothing bore the name out — the
            // category therefore says "suspected" and the interface is obliged to say so too.
            // The same visit arriving from the company's own addresses is the branch above.
            return IsAi(purpose)
                ? TrafficCategory.SuspectedAiCrawler
                : TrafficCategory.GenericWebCrawler;
        }

        // Called itself a crawler, in no name this product could look up. There is no operator to
        // confirm and no purpose to file it under, but what it said is still the most specific
        // thing known about it, and nothing it did on the way past makes it a person.
        if (evidence.Has(SignalCodes.DeclaredGenericCrawler))
        {
            return TrafficCategory.GenericWebCrawler;
        }

        if (IsSystematicRetrieval(evidence))
        {
            return TrafficCategory.ContentScraper;
        }

        return Weighed(evidence);
    }

    /// <summary>What a crawler is called once its identity has been established.</summary>
    private static TrafficCategory WhenConfirmed(string? purpose) =>
        purpose is not null && ConfirmedCategories.TryGetValue(purpose, out var category)
            ? category
            : TrafficCategory.GenericWebCrawler;

    /// <summary>Whether a purpose makes a crawler an AI one rather than a search one.</summary>
    private static bool IsAi(string? purpose) => WhenConfirmed(purpose) is TrafficCategory.KnownAiCrawler;

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
    /// <para>
    /// A near-tie produces <see cref="TrafficCategory.Unknown"/> rather than the side that happens
    /// to be a point ahead. That is a real answer — the evidence was gathered and weighed and it
    /// does not support a conclusion — and reporting it honestly is worth more than a coin toss
    /// dressed up as a classification.
    /// </para>
    /// <para>
    /// A person is concluded only from corroboration: two observations that each count, or one
    /// that is substantial on its own. One thing pointing toward a person against nothing at all
    /// is a lean rather than a conclusion, and is answered as something the product could not
    /// tell — which is the honest reading of a page opened and left. So a person is never reported
    /// on slight signs.
    /// </para>
    /// </remarks>
    private static TrafficCategory Weighed(EvidenceSet evidence)
    {
        var human = evidence.HeaviestPointing(SignalDirection.TowardHuman);
        var automation = evidence.HeaviestPointing(SignalDirection.TowardAutomation);

        if (PointsFirmly(evidence, SignalDirection.TowardHuman) && human > automation)
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
    /// Whether what points one way is enough to conclude that way: one substantial observation, or
    /// two that each count.
    /// </summary>
    /// <remarks>
    /// One passing remark is a lean rather than a conclusion. A browser running the tracker is the
    /// commonest thing an automated browser does too, so on its own it decides nothing; the same
    /// browser with somebody scrolling is two things agreeing, and somebody reading for a while is
    /// substantial by itself.
    /// </remarks>
    private static bool PointsFirmly(EvidenceSet evidence, SignalDirection direction) =>
        evidence.HeaviestPointing(direction) >= Substantial
        || evidence.CountPointing(direction, Corroborating) >= 2;

    /// <summary>
    /// How much weight stands behind the conclusion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Corroboration matters more than magnitude. Two independent observations agreeing is a
    /// stronger position than one loud one, because a single heavy signal is usually a single
    /// thing the visitor chose to say about itself.
    /// </para>
    /// <para>
    /// Three observations agreeing reach the firmest behavioural band whatever the heaviest of them
    /// weighs: nothing a person does is decisive on its own, and a reader who read, scrolled and
    /// clicked has done three things that each had to be produced.
    /// </para>
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

        // Established, not weighed. How much else was observed about a visitor arriving from an
        // address its operator vouches for changes nothing about whether it is that operator,
        // and letting corroboration decide the band would leave the firmest thing this product can
        // say about a visit depending on how talkative the visit happened to be.
        if (supporting.Any(Established))
        {
            return EvidenceStrength.Verified;
        }

        var counted = supporting.Where(signal => signal.Weight >= Corroborating).ToArray();

        if (counted.Length == 0)
        {
            return category is TrafficCategory.Unknown ? EvidenceStrength.None : EvidenceStrength.Weak;
        }

        return Corroborated(counted, contradicting);
    }

    /// <summary>
    /// How firmly observations that count agree, once anything pointing the other way is allowed
    /// for.
    /// </summary>
    /// <param name="counted">The supporting observations heavy enough to count.</param>
    /// <param name="contradicting">Everything pointing the other way.</param>
    /// <returns>The band.</returns>
    private static EvidenceStrength Corroborated(Signal[] counted, ImmutableArray<Signal> contradicting)
    {
        var heaviest = counted.Max(signal => signal.Weight);
        var independent = counted.Length;

        var reached = (heaviest, independent) switch
        {
            (_, >= Agreeing) => EvidenceStrength.Strong,
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

    /// <summary>Whether an observation is the one that settles who a visitor is.</summary>
    private static bool Established(Signal signal) =>
        string.Equals(signal.Code, SignalCodes.ConfirmedCrawler, StringComparison.Ordinal);

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

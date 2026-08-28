using System.Collections.Frozen;
using Dewiride.Analytics.Classification.Scoring;

namespace Dewiride.Analytics.Classification.Tests.Scoring;

/// <summary>
/// What the engine concludes from a given set of observations.
/// </summary>
/// <remarks>
/// The detectors' own suites prove what each of them sees. This one proves what those observations
/// add up to, assembled by hand rather than produced by a detector, so a rule can be stated on its
/// own and a change to a detector's weights cannot quietly rewrite what a category means.
/// </remarks>
public sealed class EvidenceScorecardTests
{
    private static readonly RulesetVersion Ruleset = new(5, 0);

    /// <summary>
    /// The firmest band this product has, and the only route to it. Behaviour cannot reach it,
    /// which is the promise the whole product rests on.
    /// </summary>
    [Fact]
    public void An_Established_Identity_Is_The_Only_Thing_Reported_As_Confirmed()
    {
        Weigh(Confirmed("Microsoft", "search-index")).Strength.Should().Be(EvidenceStrength.Verified);

        Weigh(
                Signal(SignalCodes.HostingNetwork, SignalDirection.TowardAutomation, 65),
                Signal(SignalCodes.RetrievalRate, SignalDirection.TowardAutomation, 60),
                Signal(SignalCodes.NoScriptExecution, SignalDirection.TowardAutomation, 55))
            .Strength.Should().NotBe(EvidenceStrength.Verified);
    }

    /// <summary>
    /// An identity is settled or it is not. A visit that asked for one page and a visit that asked
    /// for a thousand are equally that company, so how much else was observed must not move the
    /// band — otherwise the firmest thing the product can say would depend on how talkative the
    /// visit happened to be.
    /// </summary>
    [Fact]
    public void How_Much_Else_Was_Observed_Does_Not_Change_An_Established_Identity()
    {
        Weigh(
                Confirmed("Google", "search-index"),
                Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60),
                Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55))
            .Strength.Should().Be(EvidenceStrength.Verified);
    }

    [Theory]
    [InlineData("ai-training", TrafficCategory.KnownAiCrawler)]
    [InlineData("ai-assistant", TrafficCategory.KnownAiCrawler)]
    [InlineData("ai-search", TrafficCategory.KnownAiCrawler)]
    [InlineData("search-index", TrafficCategory.KnownSearchCrawler)]
    [InlineData("monitoring", TrafficCategory.MonitoringOrSynthetic)]
    [InlineData("social-preview", TrafficCategory.KnownAutomatedService)]
    [InlineData("site-tooling", TrafficCategory.KnownAutomatedService)]
    [InlineData("advertising", TrafficCategory.KnownAutomatedService)]
    [InlineData("archival", TrafficCategory.GenericWebCrawler)]
    [InlineData("seo-audit", TrafficCategory.GenericWebCrawler)]
    [InlineData("unstated", TrafficCategory.GenericWebCrawler)]
    public void A_Confirmed_Crawler_Is_Named_By_What_Its_Operator_Says_It_Is_For(
        string purpose,
        TrafficCategory expected)
    {
        Weigh(Confirmed("Somebody", purpose)).Category.Should().Be(expected);
    }

    /// <summary>
    /// The two must never collapse into each other. One means the address was checked; the other
    /// means a visitor typed a name.
    /// </summary>
    [Fact]
    public void A_Confirmed_Ai_Crawler_And_One_That_Merely_Says_So_Are_Different_Answers()
    {
        Weigh(Confirmed("OpenAI", "ai-training")).Category.Should().Be(TrafficCategory.KnownAiCrawler);

        Weigh(
                Signal(SignalCodes.DeclaredCrawler, SignalDirection.TowardAutomation, 70, ("purpose", "ai-training")),
                Signal(SignalCodes.UnverifiedClaim, SignalDirection.Neutral, 0))
            .Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
    }

    /// <summary>
    /// A search engine follows links that people published years ago, so a stream of requests for
    /// pages that are gone is exactly what indexing a site that has been rearranged looks like.
    /// Calling that a scanner would be the product being confidently wrong about the one thing it
    /// can actually know.
    /// </summary>
    [Fact]
    public void A_Confirmed_Crawler_Asking_For_Pages_That_Are_Gone_Is_Not_A_Scanner()
    {
        Weigh(
                Confirmed("Microsoft", "search-index"),
                Signal(SignalCodes.MissingPaths, SignalDirection.TowardAutomation, 70))
            .Category.Should().Be(TrafficCategory.KnownSearchCrawler);
    }

    /// <summary>
    /// One company's name over another company's published addresses. Both halves are facts
    /// somebody published, which is what makes this the rare case the product may call out.
    /// </summary>
    [Fact]
    public void A_Visitor_Wearing_Another_Companys_Name_Is_Reported_As_Automation_To_Look_At()
    {
        Weigh(Signal(SignalCodes.FalseCrawlerClaim, SignalDirection.TowardAutomation, 90))
            .Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// A contradiction is a strong observation but not a settled identity: it rests on two files
    /// both being current at the moment the visit arrived.
    /// </summary>
    [Fact]
    public void A_Contradicted_Name_Is_Not_Reported_As_Confirmed()
    {
        Weigh(Signal(SignalCodes.FalseCrawlerClaim, SignalDirection.TowardAutomation, 90))
            .Strength.Should().NotBe(EvidenceStrength.Verified);
    }

    /// <summary>
    /// Evidence was gathered and it does not support a conclusion. That is an answer, and giving it
    /// honestly is worth more than a coin toss dressed up as a classification.
    /// </summary>
    [Fact]
    public void A_Near_Tie_Is_Answered_As_Not_Recognised()
    {
        var verdict = Weigh(
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 55),
            Signal(SignalCodes.NoScriptExecution, SignalDirection.TowardAutomation, 55));

        verdict.Category.Should().Be(TrafficCategory.Unknown);

        // Both readings are kept and shown. The answer is that they did not settle it, not that
        // one of them was thrown away for being inconvenient.
        verdict.Supporting.Select(signal => signal.Code).Should()
            .Contain(SignalCodes.ReadTime).And.Contain(SignalCodes.NoScriptExecution);
    }

    [Fact]
    public void Nothing_Observed_Is_Answered_As_Nothing_To_Go_On()
    {
        var verdict = EvidenceScorecard.Weigh(new EvidenceSet([]), Ruleset);

        verdict.Category.Should().Be(TrafficCategory.InsufficientEvidence);
        verdict.Strength.Should().Be(EvidenceStrength.None);
    }

    /// <summary>
    /// A reading that points the other way and cannot be explained away is kept, shown, and holds
    /// the conclusion back — never discarded to make the answer look tidier.
    /// </summary>
    [Fact]
    public void Evidence_Pointing_The_Other_Way_Is_Kept_And_Holds_The_Conclusion_Back()
    {
        var verdict = Weigh(
            Signal(SignalCodes.HostingNetwork, SignalDirection.TowardAutomation, 65),
            Signal(SignalCodes.RetrievalRate, SignalDirection.TowardAutomation, 60),
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60));

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.ReadTime);
    }

    private static ClassificationVerdict Weigh(params Signal[] observed) =>
        EvidenceScorecard.Weigh(new EvidenceSet([.. observed]), Ruleset);

    private static Signal Confirmed(string operatorName, string purpose) => Signal(
        SignalCodes.ConfirmedCrawler,
        SignalDirection.TowardAutomation,
        100,
        ("operator", operatorName),
        ("purpose", purpose));

    private static Signal Signal(
        string code,
        SignalDirection direction,
        int weight,
        params (string Key, string Value)[] parameters) => new()
        {
            Code = code,
            Direction = direction,
            Weight = weight,
            Parameters = parameters.ToFrozenDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal),
        };
}

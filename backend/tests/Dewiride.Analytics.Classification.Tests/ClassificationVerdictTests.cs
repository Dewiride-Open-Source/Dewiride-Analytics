using System.Collections.Frozen;

namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// Covers the shape of a verdict.
/// </summary>
public sealed class ClassificationVerdictTests
{
    [Fact]
    public void An_Insufficient_Verdict_Claims_Nothing()
    {
        var verdict = ClassificationVerdict.Insufficient(RulesetVersion.Current);

        verdict.Category.Should().Be(TrafficCategory.InsufficientEvidence);
        verdict.Strength.Should().Be(EvidenceStrength.None);
        verdict.Supporting.Should().BeEmpty();
        verdict.IsProvisional.Should().BeFalse();
    }

    [Fact]
    public void An_Insufficient_Verdict_Still_Names_The_Ruleset_That_Reached_It()
    {
        var verdict = ClassificationVerdict.Insufficient(new RulesetVersion(3, 1));

        verdict.RulesetVersion.Should().Be(new RulesetVersion(3, 1));
    }

    /// <summary>
    /// Evidence pointing the other way is kept and shown. A verdict that lists only what agrees
    /// with it is an argument rather than an assessment, and showing its working is the whole
    /// proposition.
    /// </summary>
    [Fact]
    public void Contradicting_Evidence_Has_A_Place_To_Live_And_Starts_Empty()
    {
        var verdict = ClassificationVerdict.Insufficient(RulesetVersion.Current);

        verdict.Contradicting.Should().BeEmpty();
        verdict.Contradicting.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void A_Verdict_Retains_Both_Sides_Of_The_Evidence()
    {
        var verdict = new ClassificationVerdict
        {
            Category = TrafficCategory.SuspectedAiCrawler,
            Strength = EvidenceStrength.Moderate,
            Supporting = [NewSignal("traversal.sitemap_order", SignalDirection.TowardAutomation, 60)],
            Contradicting = [NewSignal("interaction.pointer_present", SignalDirection.TowardHuman, 25)],
            RulesetVersion = RulesetVersion.Current,
        };

        verdict.Supporting.Should().ContainSingle()
            .Which.Code.Should().Be("traversal.sitemap_order");
        verdict.Contradicting.Should().ContainSingle()
            .Which.Code.Should().Be("interaction.pointer_present");
    }

    /// <summary>
    /// A provisional verdict is reached before the session closed. The live view renders it as
    /// unfinished, so the flag has to survive on the record rather than be inferred later.
    /// </summary>
    [Fact]
    public void A_Verdict_Is_Settled_Unless_It_Says_Otherwise()
    {
        var settled = ClassificationVerdict.Insufficient(RulesetVersion.Current);
        var provisional = settled with { IsProvisional = true };

        settled.IsProvisional.Should().BeFalse();
        provisional.IsProvisional.Should().BeTrue();
    }

    /// <summary>
    /// A signal carries a code and the values a sentence is rendered from, never the sentence.
    /// That is what makes the explanation translatable and what stops a detector editorialising.
    /// </summary>
    [Fact]
    public void A_Signal_Carries_No_Prose_And_Its_Parameters_Start_Empty()
    {
        var signal = NewSignal("coverage.near_complete", SignalDirection.TowardAutomation, 45);

        signal.Code.Should().Be("coverage.near_complete");
        signal.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void A_Signal_Carries_The_Values_Its_Sentence_Is_Rendered_From()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pageCount"] = "42",
            ["seconds"] = "83",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        var signal = NewSignal("coverage.near_complete", SignalDirection.TowardAutomation, 45) with
        {
            Parameters = parameters,
        };

        signal.Parameters.Should().ContainKey("pageCount").WhoseValue.Should().Be("42");
    }

    private static Signal NewSignal(string code, SignalDirection direction, int weight) => new()
    {
        Code = code,
        Direction = direction,
        Weight = weight,
    };
}

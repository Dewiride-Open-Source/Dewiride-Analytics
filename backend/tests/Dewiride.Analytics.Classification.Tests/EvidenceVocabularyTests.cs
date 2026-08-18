namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// Guards the vocabulary the product's central claim is expressed in.
/// </summary>
/// <remarks>
/// These members are stored on every classification and rendered on every screen, and each one
/// carries a promise about how much is actually known. Collapsing two of them, or adding a band
/// above the one reserved for verified identity, would change what the product claims — so the
/// set is asserted here rather than left to whoever next edits the enumeration.
/// </remarks>
public sealed class EvidenceVocabularyTests
{
    /// <summary>
    /// Strength is a band, never a percentage: there is no labelled data to calibrate a
    /// probability against, so a number would look like a measurement while being an opinion.
    /// </summary>
    [Fact]
    public void Evidence_Strength_Is_The_Five_Bands_And_Nothing_Else()
    {
        Enum.GetNames<EvidenceStrength>()
            .Should()
            .BeEquivalentTo("None", "Weak", "Moderate", "Strong", "Verified");
    }

    [Fact]
    public void Evidence_Bands_Run_From_Nothing_To_Verified_Identity()
    {
        var ordered = Enum.GetValues<EvidenceStrength>().OrderBy(band => (int)band);

        ordered.Should().Equal(
            EvidenceStrength.None,
            EvidenceStrength.Weak,
            EvidenceStrength.Moderate,
            EvidenceStrength.Strong,
            EvidenceStrength.Verified);
    }

    /// <summary>
    /// Behaviour alone never reaches the top band, so nothing may sit above it that behavioural
    /// evidence could climb to.
    /// </summary>
    [Fact]
    public void Nothing_Ranks_Above_Verified_Identity()
    {
        Enum.GetValues<EvidenceStrength>().Max().Should().Be(EvidenceStrength.Verified);
    }

    [Fact]
    public void Having_Nothing_To_Weigh_Is_The_Default_Band()
    {
        default(EvidenceStrength).Should().Be(EvidenceStrength.None);
    }

    /// <summary>
    /// Two ways of saying nothing useful, and they mean different things: one is a question not
    /// yet answered, the other an answer that came back inconclusive.
    /// </summary>
    [Fact]
    public void An_Unanswered_Question_Is_Distinct_From_An_Inconclusive_Answer()
    {
        TrafficCategory.InsufficientEvidence.Should().NotBe(TrafficCategory.Unknown);
        default(TrafficCategory).Should().Be(TrafficCategory.InsufficientEvidence);
    }

    /// <summary>
    /// Verified identity and an inference that resembles it are never the same category. Merging
    /// them would attribute traffic to a named company on the strength of a guess.
    /// </summary>
    [Fact]
    public void A_Verified_Ai_Crawler_Is_A_Different_Category_From_A_Suspected_One()
    {
        TrafficCategory.KnownAiCrawler.Should().NotBe(TrafficCategory.SuspectedAiCrawler);
    }

    [Fact]
    public void Every_Traffic_Category_Has_A_Distinct_Number()
    {
        var categories = Enum.GetValues<TrafficCategory>();

        categories.Cast<int>().Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_Signal_Points_One_Of_Three_Ways()
    {
        Enum.GetNames<SignalDirection>()
            .Should()
            .BeEquivalentTo("TowardHuman", "Neutral", "TowardAutomation");
    }

    [Fact]
    public void Carrying_Context_Only_Is_The_Neutral_Direction()
    {
        default(SignalDirection).Should().Be(SignalDirection.Neutral);
    }
}

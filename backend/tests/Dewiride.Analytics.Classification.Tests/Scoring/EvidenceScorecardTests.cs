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

    /// <summary>
    /// A browser running the tracker is the commonest thing an automated browser does too. On its
    /// own it is a lean rather than a conclusion, and the honest answer is that nothing was settled.
    /// </summary>
    [Fact]
    public void One_Thing_Pointing_At_A_Person_Is_Not_A_Person()
    {
        var verdict = Weigh(Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35));

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Supporting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.ScriptExecuted);
    }

    [Fact]
    public void A_Browser_That_Fetched_Pictures_And_Nothing_Else_Is_Not_A_Person()
    {
        Weigh(Signal(SignalCodes.ImagesOnly, SignalDirection.TowardHuman, 25))
            .Category.Should().Be(TrafficCategory.Unknown);
    }

    [Fact]
    public void Two_Things_Pointing_At_A_Person_Are_A_Person()
    {
        var verdict = Weigh(
            Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
            Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45));

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    [Fact]
    public void One_Substantial_Thing_Pointing_At_A_Person_Is_Enough()
    {
        var verdict = Weigh(Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55));

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// A lean toward a person is not settled by a passing remark the other way. Neither side is
    /// firm enough to be a conclusion, and the answer says so.
    /// </summary>
    [Fact]
    public void A_Lean_Toward_A_Person_Against_A_Passing_Remark_The_Other_Way_Settles_Nothing()
    {
        Weigh(
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
                Signal(SignalCodes.NoLanguageDeclared, SignalDirection.TowardAutomation, 25))
            .Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>
    /// The promise behind every row that names a person: it is never said on slight signs. Every
    /// combination of a person's own observations that reaches the conclusion reaches it with at
    /// least some evidence behind it, by construction rather than by luck.
    /// </summary>
    [Fact]
    public void A_Person_Is_Never_Reported_On_Slight_Signs()
    {
        Signal[][] readings =
        [
            [Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35)],
            [Signal(SignalCodes.ImagesOnly, SignalDirection.TowardHuman, 25)],
            [
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
                Signal(SignalCodes.NoLanguageDeclared, SignalDirection.TowardAutomation, 25),
            ],
            [
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
                Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45),
            ],
            [Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55)],
            [
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
                Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45),
                Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 40),
            ],
            [
                Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60),
                Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55),
                Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45),
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
            ],
        ];

        foreach (var reading in readings)
        {
            var verdict = Weigh(reading);

            if (verdict.Category == TrafficCategory.LikelyHuman)
            {
                verdict.Strength.Should().BeOneOf(EvidenceStrength.Moderate, EvidenceStrength.Strong);
            }
        }
    }

    /// <summary>
    /// Nothing a person does is decisive on its own, so three ordinary observations agreeing is the
    /// firmest position their behaviour can reach — and two is not.
    /// </summary>
    [Fact]
    public void Three_Ordinary_Observations_Agreeing_Are_Stated_Firmly()
    {
        var verdict = Weigh(
            Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
            Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45),
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 40));

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Strong);
    }

    [Fact]
    public void Two_Ordinary_Observations_Agreeing_Are_Not_Stated_Firmly()
    {
        Weigh(
                Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35),
                Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45))
            .Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// Agreement reaches the firmest band and a troubling observation the other way still takes it
    /// back down, because the objection is not answered by there being more on the other side.
    /// </summary>
    [Fact]
    public void Three_Agreeing_Observations_Are_Still_Held_Back_By_A_Troubling_One()
    {
        var verdict = Weigh(
            Signal(SignalCodes.HostingNetwork, SignalDirection.TowardAutomation, 65),
            Signal(SignalCodes.RetrievalRate, SignalDirection.TowardAutomation, 50),
            Signal(SignalCodes.NoScriptExecution, SignalDirection.TowardAutomation, 50),
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60));

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.ReadTime);
    }

    /// <summary>
    /// What a visitor said about itself is the most specific thing known about it, and nothing it
    /// did on the way past makes a self-described crawler a person.
    /// </summary>
    [Fact]
    public void A_Nameless_Crawler_Is_A_Crawler_Whatever_Else_It_Did()
    {
        var verdict = Weigh(
            Signal(SignalCodes.DeclaredGenericCrawler, SignalDirection.TowardAutomation, 60),
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60),
            Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45));

        verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
        verdict.Contradicting.Select(signal => signal.Code).Should()
            .Contain(SignalCodes.ReadTime).And.Contain(SignalCodes.Scrolled);
    }

    [Fact]
    public void A_Nameless_Crawler_Sweeping_For_A_Way_In_Is_A_Scanner()
    {
        Weigh(
                Signal(SignalCodes.SensitivePaths, SignalDirection.TowardAutomation, 85),
                Signal(SignalCodes.DeclaredGenericCrawler, SignalDirection.TowardAutomation, 60))
            .Category.Should().Be(TrafficCategory.SecurityScanner);
    }

    [Fact]
    public void A_Nameless_Crawler_Is_Never_Reported_As_Confirmed()
    {
        Weigh(Signal(SignalCodes.DeclaredGenericCrawler, SignalDirection.TowardAutomation, 60))
            .Strength.Should().NotBe(EvidenceStrength.Verified);
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

    /// <summary>
    /// A key pressed over a rented network, with the tracker having run: the network is the only
    /// objection, and what was done to the page is what a program on that network almost never does.
    /// </summary>
    [Fact]
    public void A_Key_Pressed_Over_A_Rented_Network_Is_A_Person_With_The_Network_Against_It()
    {
        var verdict = Weigh(Rented, Tracker, Key);

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Contradicting.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.HostingNetwork);
    }

    [Fact]
    public void A_Visit_Another_Site_Sent_Over_A_Rented_Network_And_Then_Pressed_Is_A_Person()
    {
        var verdict = Weigh(Rented, Sent("search"), Tracker, Pointer);

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.ArrivedFromAnotherSite);
    }

    /// <summary>
    /// Sent by another site and then read: open long enough for the reading to count by itself, and
    /// seen at least a quarter of the way down. Nothing had to be pressed.
    /// </summary>
    [Fact]
    public void A_Visit_Another_Site_Sent_Over_A_Rented_Network_And_Then_Read_Is_A_Person()
    {
        var verdict = Weigh(Rented, Sent("search"), Tracker, Seen, LongRead);

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.ArrivedFromAnotherSite);
    }

    /// <summary>
    /// Sent by another site and then only glanced at: a short page seen a quarter of the way down
    /// by being shown, the same for less time than a reading counts, or the first screen of a long
    /// page left open. None of them is a person over a rented network, and none of them is
    /// something automated on the network's word alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Glances))]
    public void A_Visit_Another_Site_Sent_Over_A_Rented_Network_And_Only_Glanced_At_Is_Not_Recognised(string glance)
    {
        var verdict = Weigh([Rented, Sent("search"), Tracker, .. GlancesByName[glance]]);

        verdict.Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>The glances a rented network does not let pass for reading, by name.</summary>
    public static TheoryData<string> Glances => new(GlancesByName.Keys);

    private static readonly Dictionary<string, Signal[]> GlancesByName = new(StringComparer.Ordinal)
    {
        ["a short page shown"] = [Seen],
        ["a short page shown for less time than a reading counts"] = [Seen, Glimpsed],
        ["the first screen of a long page left open"] = [LongRead],
    };

    /// <summary>
    /// Sent by another site and then nothing more: not enough for a person, and not enough for
    /// automation on the network's word alone either. The band says how much was seen, as it does
    /// for any open answer.
    /// </summary>
    [Fact]
    public void A_Visit_Another_Site_Sent_Over_A_Rented_Network_That_Did_Nothing_More_Is_Not_Recognised()
    {
        var verdict = Weigh(Rented, Sent("social"), Tracker);

        verdict.Category.Should().Be(TrafficCategory.Unknown);
        verdict.Strength.Should().Be(EvidenceStrength.Strong);
    }

    /// <summary>
    /// The hands-off shape measured on Microsoft's network: straight to a page, a while on it, a
    /// press of the pointer, and no key. The network is its only objection and nothing the visit
    /// did answers it, so it is something automated.
    /// </summary>
    [Fact]
    public void A_Visit_That_Came_Straight_In_With_No_Key_Pressed_Is_Something_Automated()
    {
        var verdict = Weigh(Rented, Tracker, Pointer, LongRead);

        verdict.Category.Should().Be(TrafficCategory.SuspiciousAutomation);
    }

    /// <summary>
    /// A guard on the rule rather than a promise the product makes: the second observation is
    /// ordinarily the tracker having run, and without one a single key is not enough.
    /// </summary>
    [Fact]
    public void A_Key_Pressed_With_Nothing_Else_That_Counts_Is_Not_A_Person_Over_A_Rented_Network()
    {
        Weigh(Rented, Key).Category.Should().NotBe(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// A guard on the rule as well: a press of the pointer is substantial on its own, and over a
    /// rented network it still needs a second observation that counts beside it.
    /// </summary>
    [Fact]
    public void One_Substantial_Observation_Is_Not_Enough_Over_A_Rented_Network()
    {
        Weigh(Rented, Sent("search"), Pointer).Category.Should().NotBe(TrafficCategory.LikelyHuman);
    }

    /// <summary>
    /// The network stays among the reasons, pointing the other way, and holds the answer back
    /// however much more the visit did.
    /// </summary>
    [Fact]
    public void A_Person_Over_A_Rented_Network_Is_Never_Stated_More_Firmly_Than_Some_Signs()
    {
        var verdict = Weigh(
            Rented,
            Sent("search"),
            Tracker,
            Key,
            Pointer,
            Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45),
            Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60));

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// Every other observation that counts toward automation is a second objection, and a visit
    /// with two is weighed as any other — whatever sent it and whatever it did.
    /// </summary>
    [Theory]
    [InlineData(SignalCodes.NoLanguageDeclared, 25)]
    [InlineData(SignalCodes.NoUserAgent, 30)]
    [InlineData(SignalCodes.NoEngagement, 50)]
    [InlineData(SignalCodes.RetrievalRate, 50)]
    [InlineData(SignalCodes.RetrievalBreadth, 50)]
    [InlineData(SignalCodes.MissingPaths, 45)]
    [InlineData(SignalCodes.NoScriptExecution, 50)]
    [InlineData(SignalCodes.DeclaredTool, 60)]
    public void A_Second_Objection_Leaves_A_Visit_Over_A_Rented_Network_Weighed_As_Any_Other(string objection, int weight)
    {
        var verdict = Weigh(
            Rented,
            Signal(objection, SignalDirection.TowardAutomation, weight),
            Sent("search"),
            Tracker,
            Key,
            Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45));

        verdict.Category.Should().NotBe(TrafficCategory.LikelyHuman);
        verdict.Category.Should().NotBe(TrafficCategory.Unknown);
    }

    /// <summary>
    /// An arrival is too light to count toward anything. On its own, or beside the tracker having
    /// run, it concludes nothing — the referring address it rests on is the visitor's own text.
    /// </summary>
    [Fact]
    public void An_Arrival_Concludes_Nothing_On_Its_Own()
    {
        var alone = Weigh(Sent("search"));
        var withTheTracker = Weigh(Sent("search"), Tracker);

        alone.Category.Should().Be(TrafficCategory.Unknown);
        alone.Strength.Should().Be(EvidenceStrength.None);
        withTheTracker.Category.Should().Be(TrafficCategory.Unknown);
    }

    /// <summary>
    /// Where the network is not involved, an arrival beside anything else observed changes neither
    /// the answer nor how firmly it is stated.
    /// </summary>
    [Fact]
    public void An_Arrival_Beside_Anything_Else_Changes_Nothing_Where_No_Rented_Network_Objects()
    {
        var straight = Weigh(Tracker, Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45));
        var sent = Weigh(Tracker, Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45), Sent("link"));

        sent.Category.Should().Be(straight.Category);
        sent.Strength.Should().Be(straight.Strength);
    }

    /// <summary>A rented network, as the engine reports one.</summary>
    private static Signal Rented => Signal(
        SignalCodes.HostingNetwork,
        SignalDirection.TowardAutomation,
        65,
        ("operator", "Microsoft"));

    /// <summary>A browser having run the tracker at all.</summary>
    private static Signal Tracker => Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35);

    /// <summary>A key pressed on the page.</summary>
    private static Signal Key => Signal(SignalCodes.KeyboardUsed, SignalDirection.TowardHuman, 50);

    /// <summary>The pointer pressed on the page.</summary>
    private static Signal Pointer => Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55);

    /// <summary>A page seen a quarter of the way down or further.</summary>
    private static Signal Seen => Signal(SignalCodes.Scrolled, SignalDirection.TowardHuman, 45, ("percent", "50"));

    /// <summary>A page open long enough for the reading to count by itself.</summary>
    private static Signal LongRead => Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 60, ("seconds", "40"));

    /// <summary>A page open for less time than a reading counts by itself.</summary>
    private static Signal Glimpsed => Signal(SignalCodes.ReadTime, SignalDirection.TowardHuman, 40, ("seconds", "5"));

    /// <summary>Another site having sent the visit.</summary>
    private static Signal Sent(string kind) =>
        Signal(SignalCodes.ArrivedFromAnotherSite, SignalDirection.TowardHuman, 20, ("kind", kind));

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

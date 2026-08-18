using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// The promises this product makes, stated as tests.
/// </summary>
/// <remarks>
/// These are not tests of the arithmetic. Each one is a claim the product makes to a customer
/// about what it will and will not say, and the engine is only worth shipping while every one of
/// them holds.
/// </remarks>
public sealed class TrafficClassifierTests
{
    private static readonly TrafficClassifier Engine = TrafficClassifier.Current();

    [Fact]
    public void Somebody_Reading_A_Site_Is_Not_Called_Automation()
    {
        var verdict = Engine.Classify(Visits.AReader());

        verdict.Category.Should().Be(TrafficCategory.LikelyHuman);
        verdict.Strength.Should().BeOneOf(EvidenceStrength.Moderate, EvidenceStrength.Strong);
    }

    /// <summary>
    /// The rule the whole product rests on. A user agent is one line of text the visitor writes
    /// itself, so a name that has not been checked against the operator's published addresses is
    /// a claim — and the category has to keep saying so.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)")]
    [InlineData("Mozilla/5.0 (compatible; PerplexityBot/1.0; +https://perplexity.ai/perplexitybot)")]
    public void An_Ai_Crawler_That_Names_Itself_Is_Only_Ever_Suspected(string userAgent)
    {
        var verdict = Engine.Classify(Visits.ANamedCrawler(userAgent));

        verdict.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
        verdict.Category.Should().NotBe(TrafficCategory.KnownAiCrawler);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.UnverifiedClaim);
    }

    /// <summary>
    /// Behaviour cannot establish identity, however much of it there is. The band is reserved for
    /// a request from an address its operator published, and nothing in this assembly can check
    /// that — it performs no I/O at all.
    /// </summary>
    [Fact]
    public void Nothing_Behavioural_Ever_Reaches_Verified()
    {
        SessionEvidence[] everything =
        [
            Visits.AReader(),
            Visits.AScanner(),
            Visits.Anonymous(),
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)"),
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"),
        ];

        everything.Select(session => Engine.Classify(session).Strength)
            .Should().NotContain(EvidenceStrength.Verified);
    }

    [Fact]
    public void A_Search_Crawler_Naming_Itself_Is_Not_Filed_As_An_Ai_One()
    {
        var verdict = Engine.Classify(
            Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"));

        verdict.Category.Should().Be(TrafficCategory.GenericWebCrawler);
    }

    /// <summary>
    /// Google runs one crawler for search and another for model training, and a publisher may
    /// feel entirely differently about the two. Reading the longer name correctly is the whole
    /// difference.
    /// </summary>
    [Fact]
    public void Googles_Training_Crawler_Is_Told_Apart_From_Its_Search_One()
    {
        var training = Engine.Classify(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Google-Extended/1.0)"));
        var search = Engine.Classify(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1)"));

        training.Category.Should().Be(TrafficCategory.SuspectedAiCrawler);
        search.Category.Should().Be(TrafficCategory.GenericWebCrawler);
    }

    [Fact]
    public void Something_Sweeping_For_A_Way_In_Is_Called_A_Scanner()
    {
        var verdict = Engine.Classify(Visits.AScanner());

        verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
        verdict.Supporting.Should().Contain(signal => signal.Code == SignalCodes.SensitivePaths);
    }

    /// <summary>
    /// A session can look like several things at once, and the most specific thing that can be
    /// said about it is what it should be called. Something asking for a credential store is a
    /// scanner whatever else it did on the way past.
    /// </summary>
    [Fact]
    public void A_Scanner_That_Also_Scrolled_A_Page_Is_Still_A_Scanner()
    {
        var busy = Visits.AScanner() with
        {
            Surfaces = [IngestSurface.CloudflareWorker, IngestSurface.BrowserTracker],
            EngagedMs = 30_000,
            MaxScrollDepthPercent = 90,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        };

        var verdict = Engine.Classify(busy);

        verdict.Category.Should().Be(TrafficCategory.SecurityScanner);
    }

    /// <summary>
    /// A verdict that shows only what agrees with it is an argument rather than an assessment.
    /// The product's claim is that it shows its working, including the parts that cut against
    /// the answer.
    /// </summary>
    [Fact]
    public void Evidence_Pointing_The_Other_Way_Is_Kept_And_Holds_The_Conclusion_Back()
    {
        var busy = Visits.AScanner() with
        {
            Surfaces = [IngestSurface.CloudflareWorker, IngestSurface.BrowserTracker],
            EngagedMs = 30_000,
            MaxScrollDepthPercent = 90,
            HadPointerInteraction = true,
            HadKeyboardInteraction = true,
        };

        var verdict = Engine.Classify(busy);

        verdict.Contradicting.Should().NotBeEmpty();
        verdict.Strength.Should().Be(EvidenceStrength.Moderate);
    }

    /// <summary>
    /// The single most important line in the engine. A server-side surface sees no interaction
    /// because it cannot, and reading that silence as evidence would classify every real person
    /// on a site measured that way as automation.
    /// </summary>
    [Fact]
    public void A_Surface_That_Could_Not_Watch_Produces_No_Evidence_Of_Absence()
    {
        var verdict = Engine.Classify(Visits.Anonymous());

        verdict.Supporting.Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
        verdict.Contradicting.Should().NotContain(signal => signal.Code == SignalCodes.NoEngagement);
    }

    /// <summary>
    /// A real answer rather than a failure. One ordinary page fetch that named an ordinary browser
    /// and did nothing remarkable does not support a conclusion, and saying so is worth more than
    /// a guess.
    /// </summary>
    [Fact]
    public void One_Unremarkable_Visit_Produces_An_Honest_Shrug()
    {
        var verdict = Engine.Classify(Visits.Anonymous());

        verdict.Category.Should().BeOneOf(TrafficCategory.Unknown, TrafficCategory.SuspiciousAutomation);
        verdict.Strength.Should().BeOneOf(EvidenceStrength.None, EvidenceStrength.Weak);
    }

    [Fact]
    public void A_Session_Nothing_Was_Observed_About_Is_Answered_As_Not_Yet_Known()
    {
        var nothing = new SessionEvidence
        {
            SessionKey = "empty",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon,
            Requests = [],
            Surfaces = [IngestSurface.BrowserTracker],
            UserAgent = "Mozilla/5.0",
            Language = "en",
        };

        var verdict = Engine.Classify(nothing);

        verdict.Category.Should().Be(TrafficCategory.InsufficientEvidence);
        verdict.Strength.Should().Be(EvidenceStrength.None);
    }

    [Fact]
    public void A_Browser_Somebody_Is_Driving_With_Software_Says_So_And_Is_Believed()
    {
        var driven = Visits.AReader() with { DeclaredWebDriver = true };

        Engine.Classify(driven).Category.Should().Be(TrafficCategory.BrowserAutomation);
    }

    [Fact]
    public void Taking_The_Whole_Site_Without_Rendering_Any_Of_It_Is_Called_Scraping()
    {
        var sweep = new SessionEvidence
        {
            SessionKey = "sweep",
            StartedAt = Visits.Noon,
            EndedAt = Visits.Noon.AddMinutes(2),
            Requests = Visits.Pages(60, TimeSpan.FromMinutes(2)),
            Surfaces = [IngestSurface.CloudflareWorker],
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
            Language = "en-US",
        };

        Engine.Classify(sweep).Category.Should().Be(TrafficCategory.ContentScraper);
    }

    /// <summary>
    /// Every verdict carries the rules that produced it, so a number on a screen can still be
    /// explained a month after the rules changed.
    /// </summary>
    [Fact]
    public void Every_Verdict_Records_Which_Rules_Produced_It()
    {
        Engine.Classify(Visits.AReader()).RulesetVersion.Should().Be(RulesetVersion.Current);
    }

    /// <summary>
    /// The same session judged twice must produce the same answer, or the fixture suite is noise
    /// and re-judging stored history means nothing.
    /// </summary>
    [Fact]
    public void The_Same_Session_Judged_Twice_Gives_The_Same_Answer()
    {
        var session = Visits.AReader();

        var first = Engine.Classify(session);
        var second = Engine.Classify(session);

        second.Should().BeEquivalentTo(first);
    }
}

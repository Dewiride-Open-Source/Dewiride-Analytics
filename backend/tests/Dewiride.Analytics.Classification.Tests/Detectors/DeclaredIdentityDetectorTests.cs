using Dewiride.Analytics.Classification.Detectors;
using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Classification.Tests.Detectors;

/// <summary>
/// What the engine makes of what a visitor said it was.
/// </summary>
/// <remarks>
/// Everything here is one line of text the visitor wrote itself, so every test in this file is a
/// claim about what was <em>said</em>. Nothing in it may become a claim about who was there.
/// </remarks>
public sealed class DeclaredIdentityDetectorTests
{
    /// <summary>The spelling a purpose falls back to when nothing named it.</summary>
    private const string Unstated = "unstated";

    private readonly DeclaredIdentityDetector detector = new();

    [Fact]
    public void A_Recognised_Crawler_Is_Reported_With_The_Name_It_Gave()
    {
        var found = detector.Examine(Visits.ANamedCrawler("Mozilla/5.0 (compatible; GPTBot/1.2)"));

        var claim = found.Single(signal => signal.Code == SignalCodes.DeclaredCrawler);

        claim.Parameters["operator"].Should().Be("OpenAI");
        claim.Parameters["token"].Should().Be("GPTBot");
        claim.Parameters["purpose"].Should().Be("ai-training");
    }

    /// <summary>
    /// The pairing that stops a name becoming an identity. Without it the interface has no way to
    /// know it must write "says it is" rather than "is".
    /// </summary>
    [Fact]
    public void A_Name_Is_Always_Reported_As_Unconfirmed()
    {
        var found = detector.Examine(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1)"));

        found.Select(signal => signal.Code).Should()
            .Contain(SignalCodes.DeclaredCrawler).And.Contain(SignalCodes.UnverifiedClaim);
    }

    /// <summary>
    /// Saying a name changes how automated a visit looks and nothing else, so the observation that
    /// the name is unconfirmed must not tip the scales on its own.
    /// </summary>
    [Fact]
    public void Saying_A_Name_Could_Not_Be_Confirmed_Counts_For_Nothing_By_Itself()
    {
        var found = detector.Examine(Visits.ANamedCrawler("Mozilla/5.0 (compatible; ClaudeBot/1.0)"));

        found.Single(signal => signal.Code == SignalCodes.UnverifiedClaim).Weight.Should().Be(0);
    }

    /// <summary>
    /// Every catalogued crawler has to arrive on the screen as a sentence about what it is for.
    /// A purpose the detector cannot name falls back to a catch-all, and the reader is then told a
    /// crawler visited without being told what kind — which is the question they opened the page
    /// to ask. Adding a purpose without its stored spelling fails here rather than in production.
    /// </summary>
    [Fact]
    public void Every_Catalogued_Crawler_Reports_What_It_Is_For()
    {
        foreach (var entry in CrawlerCatalogue.Known)
        {
            var found = detector.Examine(Visits.ANamedCrawler($"Mozilla/5.0 (compatible; {entry.Token}/1.0)"));

            var claim = found.Single(signal => signal.Code == SignalCodes.DeclaredCrawler);

            claim.Parameters["token"].Should().Be(entry.Token);
            claim.Parameters["purpose"].Should().NotBe(Unstated, "{0} says what it is for", entry.Token);
        }
    }

    /// <summary>
    /// Every purpose the catalogue can hold has a spelling of its own, so two different kinds of
    /// crawler are never described to a reader in the same words.
    /// </summary>
    [Fact]
    public void No_Two_Purposes_Are_Reported_As_The_Same_Thing()
    {
        var spellings = CrawlerCatalogue.Known
            .DistinctBy(entry => entry.Purpose)
            .Select(entry => detector
                .Examine(Visits.ANamedCrawler($"Mozilla/5.0 (compatible; {entry.Token}/1.0)"))
                .Single(signal => signal.Code == SignalCodes.DeclaredCrawler)
                .Parameters["purpose"])
            .ToArray();

        spellings.Should().OnlyHaveUniqueItems();
        spellings.Should().HaveCount(Enum.GetValues<CrawlerPurpose>().Length);
    }

    [Fact]
    public void A_Visitor_That_Said_Nothing_At_All_Is_Reported_As_Having_Said_Nothing()
    {
        var found = detector.Examine(Visits.ANamedCrawler(string.Empty));

        found.Should().ContainSingle().Which.Code.Should().Be(SignalCodes.NoUserAgent);
    }

    /// <summary>
    /// Lower than a named tool, because a stripped header is also what a privacy extension
    /// produces, and the person behind it is not automation.
    /// </summary>
    [Fact]
    public void Saying_Nothing_Counts_For_Less_Than_Naming_A_Tool()
    {
        var silent = detector.Examine(Visits.ANamedCrawler(string.Empty)).Single();
        var named = detector.Examine(Visits.ANamedCrawler("curl/8.19.0")).Single();

        silent.Weight.Should().BeLessThan(named.Weight);
    }

    [Fact]
    public void A_Fetching_Tool_Is_Reported_As_The_Kind_Of_Program_It_Is()
    {
        var found = detector.Examine(Visits.ANamedCrawler("Scrapy/2.11 (+https://scrapy.org)"))
            .Should().ContainSingle().Subject;

        found.Code.Should().Be(SignalCodes.DeclaredTool);
        found.Parameters["kind"].Should().Be("scraping-framework");
    }

    [Fact]
    public void An_Ordinary_Browser_Is_Reported_As_Nothing_At_All()
    {
        detector.Examine(Visits.AReader()).Should().BeEmpty();
    }

    /// <summary>
    /// A crawler that introduces itself under a name nobody has catalogued is reported as having
    /// called itself a crawler and nothing more. The name is text the visitor wrote, and it does
    /// not travel with the report.
    /// </summary>
    [Fact]
    public void A_Crawler_Naming_Itself_Only_As_A_Crawler_Is_Reported_As_One_Without_A_Name()
    {
        var found = detector.Examine(Visits.ANamelessCrawler()).Should().ContainSingle().Subject;

        found.Code.Should().Be(SignalCodes.DeclaredGenericCrawler);
        found.Direction.Should().Be(SignalDirection.TowardAutomation);
        found.Weight.Should().Be(60);
        found.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void A_Catalogued_Name_Is_Reported_As_The_Name_Rather_Than_As_A_Crawler_Word()
    {
        var found = detector.Examine(Visits.ANamedCrawler("Mozilla/5.0 (compatible; Googlebot/2.1)"));

        found.Select(signal => signal.Code).Should()
            .BeEquivalentTo(SignalCodes.DeclaredCrawler, SignalCodes.UnverifiedClaim);
    }

    [Fact]
    public void A_Named_Tool_Is_Reported_As_The_Tool_Rather_Than_As_A_Crawler_Word()
    {
        var found = detector.Examine(Visits.ANamedCrawler("MyCrawler/1.0 python-requests/2.32.3"))
            .Should().ContainSingle().Subject;

        found.Code.Should().Be(SignalCodes.DeclaredTool);
        found.Parameters["kind"].Should().Be("script");
    }

    [Fact]
    public void A_Crawler_Word_Is_Not_Reported_Where_The_Address_Settled_Who_It_Was()
    {
        detector.Examine(Visits.AConfirmedCrawler("Microsoft", Visits.NamelessCrawler))
            .Should().ContainSingle().Which.Code.Should().Be(SignalCodes.ConfirmedCrawler);
    }

    /// <summary>
    /// Both say what kind of thing was fetching and neither says whose, so neither is worth more
    /// than the other — and both are worth more than saying nothing at all.
    /// </summary>
    [Fact]
    public void A_Crawler_Word_Weighs_The_Same_As_A_Named_Tool()
    {
        var described = detector.Examine(Visits.ANamelessCrawler()).Single();
        var named = detector.Examine(Visits.ANamedCrawler("curl/8.19.0")).Single();
        var silent = detector.Examine(Visits.ANamedCrawler(string.Empty)).Single();

        described.Weight.Should().Be(named.Weight);
        described.Weight.Should().BeGreaterThan(silent.Weight);
    }

    /// <summary>
    /// Nothing a visitor says about itself can be evidence that a person was there. It is text the
    /// visitor chose, and a crawler that wanted to be taken for a reader would choose differently.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)")]
    [InlineData("meta-externalagent/1.1")]
    [InlineData("curl/8.19.0")]
    [InlineData(Visits.NamelessCrawler)]
    [InlineData("")]
    public void Nothing_A_Visitor_Says_About_Itself_Points_Toward_A_Person(string userAgent)
    {
        var found = detector.Examine(Visits.ANamedCrawler(userAgent));

        found.Should().NotContain(signal => signal.Direction == SignalDirection.TowardHuman);
    }

    /// <summary>
    /// The one thing here that is an identity. Where the address settled it, the observation that
    /// the name could not be confirmed has to go, because it is no longer true — and leaving it
    /// would put "we have not checked this" on the screen beside a check that was made.
    /// </summary>
    [Fact]
    public void A_Name_The_Address_Bears_Out_Is_No_Longer_Reported_As_Unconfirmed()
    {
        var found = detector.Examine(
            Visits.AConfirmedCrawler("OpenAI", "Mozilla/5.0 (compatible; GPTBot/1.2)"));

        found.Select(signal => signal.Code).Should()
            .Contain(SignalCodes.ConfirmedCrawler)
            .And.Contain(SignalCodes.DeclaredCrawler)
            .And.NotContain(SignalCodes.UnverifiedClaim);
    }

    /// <summary>
    /// What the address establishes is the company and nothing finer: several of them publish one
    /// list covering crawlers that do quite different things. Naming the crawler as well would be
    /// reporting the visitor's own word as though the check had covered it.
    /// </summary>
    [Fact]
    public void What_The_Address_Establishes_Is_The_Company_And_Not_The_Crawler()
    {
        var found = detector.Examine(
            Visits.AConfirmedCrawler("OpenAI", "Mozilla/5.0 (compatible; GPTBot/1.2)"));

        var confirmed = found.Single(signal => signal.Code == SignalCodes.ConfirmedCrawler);

        confirmed.Parameters["operator"].Should().Be("OpenAI");
        confirmed.Parameters.Should().NotContainKey("token");
    }

    /// <summary>
    /// The case a catalogue of names can never reach, and the reason this check exists at all.
    /// </summary>
    [Fact]
    public void A_Crawler_That_Named_Nothing_Is_Still_Recognised_By_Where_It_Came_From()
    {
        var found = detector.Examine(Visits.AConfirmedCrawler("Microsoft"));

        var confirmed = found.Should().ContainSingle().Subject;

        confirmed.Code.Should().Be(SignalCodes.ConfirmedCrawler);
        confirmed.Parameters["operator"].Should().Be("Microsoft");
    }

    /// <summary>
    /// A company whose crawlers all do one thing lends that to a visit that named nothing. One
    /// running crawlers for several purposes lends nothing, because the difference between being
    /// indexed and being collected as training material is the difference a publisher cares about.
    /// </summary>
    [Theory]
    [InlineData("Microsoft", "search-index")]
    [InlineData("Google", Unstated)]
    public void A_Silent_Crawler_Takes_Its_Purpose_From_Its_Operator_Only_Where_There_Is_One(
        string operatorName,
        string expected)
    {
        var found = detector.Examine(Visits.AConfirmedCrawler(operatorName));

        found.Single(signal => signal.Code == SignalCodes.ConfirmedCrawler)
            .Parameters["purpose"].Should().Be(expected);
    }

    /// <summary>
    /// One company's name over another company's addresses. Both halves are published facts, so
    /// this is the rare case where the product can say outright that a visitor was not what it
    /// said — and it says so without repeating the name as though it were still in question.
    /// </summary>
    [Fact]
    public void A_Name_The_Address_Contradicts_Is_Reported_As_Contradicted()
    {
        var found = detector.Examine(
            Visits.AConfirmedCrawler("Microsoft", "Mozilla/5.0 (compatible; Googlebot/2.1)"));

        var contradiction = found.Should().ContainSingle().Subject;

        contradiction.Code.Should().Be(SignalCodes.FalseCrawlerClaim);
        contradiction.Parameters["claimed"].Should().Be("Google");
        contradiction.Parameters["operator"].Should().Be("Microsoft");
    }

    /// <summary>
    /// Weight is what decides how firmly a conclusion may be stated, so an identity established
    /// from a published file has to outweigh every claim a visitor could have written for itself.
    /// </summary>
    [Fact]
    public void An_Established_Identity_Outweighs_Anything_A_Visitor_Could_Have_Said()
    {
        var confirmed = detector.Examine(Visits.AConfirmedCrawler("Microsoft")).Single();
        var claimed = detector
            .Examine(Visits.ANamedCrawler("Mozilla/5.0 (compatible; GPTBot/1.2)"))
            .Single(signal => signal.Code == SignalCodes.DeclaredCrawler);

        confirmed.Weight.Should().BeGreaterThan(claimed.Weight);
    }
}

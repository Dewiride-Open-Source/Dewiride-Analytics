using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Classification.Tests.Identity;

/// <summary>
/// Proves the catalogue names the right operator, and never the wrong one.
/// </summary>
/// <remarks>
/// This is the file that puts a company's name beside somebody's traffic. Everything in it was
/// read from the operator's own documentation, and a mistake here is the product telling a
/// customer something false about who is reading their site.
/// </remarks>
public sealed class CatalogueTests
{
    [Theory]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (compatible; GPTBot/1.2; +https://openai.com/gptbot)", "OpenAI", "GPTBot")]
    [InlineData("Mozilla/5.0 (compatible; OAI-SearchBot/1.0; +https://openai.com/searchbot)", "OpenAI", "OAI-SearchBot")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)", "Anthropic", "ClaudeBot")]
    [InlineData("Mozilla/5.0 (compatible; Claude-User/1.0)", "Anthropic", "Claude-User")]
    [InlineData("Mozilla/5.0 (compatible; PerplexityBot/1.0)", "Perplexity", "PerplexityBot")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Google", "Googlebot")]
    public void A_Crawler_Is_Attributed_To_The_Operator_That_Publishes_It(
        string userAgent,
        string expectedOperator,
        string expectedToken)
    {
        var found = CrawlerCatalogue.Match(userAgent);

        found.Should().NotBeNull();
        found.Operator.Should().Be(expectedOperator);
        found.Token.Should().Be(expectedToken);
    }

    /// <summary>
    /// Several operators use one token as the prefix of another, and the two are used for quite
    /// different things. Matching the shorter one first would file a training crawler as a search
    /// crawler — a distinction publishers care about a great deal.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Google-Extended/1.0)", "Google-Extended", CrawlerPurpose.AiTraining)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot-News/2.1)", "Googlebot-News", CrawlerPurpose.SearchIndex)]
    [InlineData("Mozilla/5.0 (compatible; GoogleOther-Image/1.0)", "GoogleOther-Image", CrawlerPurpose.SearchIndex)]
    [InlineData("Mozilla/5.0 (compatible; Claude-SearchBot/1.0)", "Claude-SearchBot", CrawlerPurpose.AiSearch)]
    public void A_Longer_Name_Is_Never_Captured_By_A_Shorter_One(
        string userAgent,
        string expectedToken,
        CrawlerPurpose expectedPurpose)
    {
        var found = CrawlerCatalogue.Match(userAgent);

        found.Should().NotBeNull();
        found.Token.Should().Be(expectedToken);
        found.Purpose.Should().Be(expectedPurpose);
    }

    /// <summary>
    /// A crawler this build has never heard of is judged on what it did. Guessing at a name would
    /// be the one mistake the catalogue exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0.0.0")]
    [InlineData("SomeCrawlerNobodyHasDocumented/3.1")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_Unrecognised_Is_Reported_As_Unrecognised(string? userAgent)
    {
        CrawlerCatalogue.Match(userAgent).Should().BeNull();
    }

    /// <summary>
    /// Every entry has to name where a claim to be it can be checked, because that address is the
    /// only route from "says it is" to "is".
    /// </summary>
    [Fact]
    public void Every_Crawler_Says_Where_Its_Claim_Can_Be_Checked()
    {
        CrawlerCatalogue.Known.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.PublishedRanges));
    }

    [Fact]
    public void No_Token_Is_Listed_Twice()
    {
        CrawlerCatalogue.Known.Select(entry => entry.Token).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("python-requests/2.32.3", "script")]
    [InlineData("curl/8.19.0", "command-line")]
    [InlineData("Scrapy/2.11 (+https://scrapy.org)", "scraping-framework")]
    [InlineData("Mozilla/5.0 HeadlessChrome/141.0.0.0", "headless-browser")]
    public void A_Fetching_Tool_Is_Reported_As_A_Kind_Of_Program(string userAgent, string expectedKind)
    {
        ToolCatalogue.Match(userAgent).Should().Be(expectedKind);
    }

    [Fact]
    public void An_Ordinary_Browser_Is_Not_Mistaken_For_A_Tool()
    {
        const string chrome =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
            + "Chrome/141.0.0.0 Safari/537.36";

        ToolCatalogue.Match(chrome).Should().BeNull();
    }
}

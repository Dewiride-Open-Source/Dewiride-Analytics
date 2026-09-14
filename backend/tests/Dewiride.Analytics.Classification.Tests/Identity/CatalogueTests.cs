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
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/151.0.0.0 Safari/537.36";

    [Theory]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (compatible; GPTBot/1.2; +https://openai.com/gptbot)", "OpenAI", "GPTBot")]
    [InlineData("Mozilla/5.0 (compatible; OAI-SearchBot/1.0; +https://openai.com/searchbot)", "OpenAI", "OAI-SearchBot")]
    [InlineData("Mozilla/5.0 (compatible; ClaudeBot/1.0; +claudebot@anthropic.com)", "Anthropic", "ClaudeBot")]
    [InlineData("Mozilla/5.0 (compatible; Claude-User/1.0)", "Anthropic", "Claude-User")]
    [InlineData("Mozilla/5.0 (compatible; PerplexityBot/1.0)", "Perplexity", "PerplexityBot")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Google", "Googlebot")]
    [InlineData("Mozilla/5.0 (compatible; Amazonbot/0.1; +https://developer.amazon.com/support/amazonbot)", "Amazon", "Amazonbot")]
    [InlineData("CCBot/2.0 (https://commoncrawl.org/faq/)", "Common Crawl", "CCBot")]
    [InlineData("Mozilla/5.0 (compatible; MistralAI-User/1.0; +https://docs.mistral.ai/robots)", "Mistral AI", "MistralAI-User")]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)", "Slack", "Slackbot-LinkExpanding")]
    [InlineData("Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)", "Ahrefs", "AhrefsBot")]
    [InlineData("Mozilla/5.0 (compatible; Baiduspider/2.0; +http://www.baidu.com/search/spider.html)", "Baidu", "Baiduspider")]
    [InlineData("DuckDuckBot/1.1; (+http://duckduckgo.com/duckduckbot.html)", "DuckDuckGo", "DuckDuckBot")]
    [InlineData("Mozilla/5.0 (compatible; SemrushBot/7~bl; +http://www.semrush.com/bot.html)", "Semrush", "SemrushBot")]
    [InlineData("Mozilla/5.0 (compatible; SiteAuditBot/0.97; +http://www.semrush.com/bot.html)", "Semrush", "SiteAuditBot")]
    [InlineData("Mozilla/5.0 (compatible; MJ12bot/v1.4.8; http://mj12bot.com/)", "Majestic", "MJ12bot")]
    [InlineData("Mozilla/5.0 (compatible; DotBot/1.2; +https://opensiteexplorer.org/dotbot; help@moz.com)", "Moz", "DotBot")]
    [InlineData("Mozilla/5.0 (compatible; Pinterestbot/1.0; +https://www.pinterest.com/bot.html)", "Pinterest", "Pinterestbot")]
    [InlineData("Mozilla/5.0 (compatible; Yahoo! Slurp; http://help.yahoo.com/help/us/ysearch/slurp)", "Yahoo", "Yahoo! Slurp")]
    [InlineData("Mozilla/5.0 (compatible; SeznamBot/4.0; +https://o-seznam.cz/napoveda/vyhledavani/en/seznambot-crawler/)", "Seznam", "SeznamBot")]
    [InlineData("Mozilla/5.0 (compatible; coccocbot-web/1.0; +http://help.coccoc.com/searchengine)", "Cốc Cốc", "coccocbot")]
    [InlineData("Mozilla/5.0 (compatible; coccocbot-ads/1.0; +http://help.coccoc.com/searchengine)", "Cốc Cốc", "coccocbot-ads")]
    [InlineData("Mozilla/5.0+(compatible; UptimeRobot/2.0; http://www.uptimerobot.com/)", "UptimeRobot", "UptimeRobot")]
    [InlineData("Pingdom.com_bot_version_1.4_(http://www.pingdom.com/)", "Pingdom", "Pingdom.com_bot")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) StatusCake/1.0", "StatusCake", "StatusCake")]
    [InlineData("Better Stack Better Uptime Bot Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Better Stack", "Better Uptime Bot")]
    [InlineData("Mozilla/5.0 (compatible; DataForSeoBot; +https://dataforseo.com/dataforseo-bot)", "DataForSEO", "DataForSeoBot")]
    [InlineData("Mozilla/5.0 (compatible; barkrowler/0.9; +https://babbar.tech/crawler)", "Babbar", "barkrowler")]
    [InlineData("FeedFetcher-Google; (+http://www.google.com/feedfetcher.html)", "Google", "FeedFetcher-Google")]
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
    /// The strings below were taken verbatim from a week of live traffic, and each of them was
    /// answered with the wrong category before its operator was catalogued. They are here so that
    /// dropping one of these entries fails a test rather than quietly returning a person.
    /// </summary>
    [Theory]
    [InlineData(
        "meta-externalagent/1.1 (+https://developers.facebook.com/docs/sharing/webmasters/crawler)",
        "Meta",
        CrawlerPurpose.AiTraining)]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/145.0.0.0 Safari/537.36 (compatible; meta-externalagent/1.1 "
        + "(+https://developers.facebook.com/docs/sharing/webmasters/crawler))",
        "Meta",
        CrawlerPurpose.AiTraining)]
    [InlineData(
        "Mozilla/5.0 (compatible; YandexRenderResourcesBot/1.0; +http://yandex.com/bots) "
        + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0",
        "Yandex",
        CrawlerPurpose.SearchIndex)]
    [InlineData(
        "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; bingbot/2.0; "
        + "+http://www.bing.com/bingbot.htm) Chrome/136.0.0.0 Safari/537.36",
        "Microsoft",
        CrawlerPurpose.SearchIndex)]
    public void A_Crawler_Seen_In_Live_Traffic_Is_Recognised(
        string userAgent,
        string expectedOperator,
        CrawlerPurpose expectedPurpose)
    {
        var found = CrawlerCatalogue.Match(userAgent);

        found.Should().NotBeNull();
        found.Operator.Should().Be(expectedOperator);
        found.Purpose.Should().Be(expectedPurpose);
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
    [InlineData("Mozilla/5.0 (compatible; Applebot-Extended/0.1)", "Applebot-Extended", CrawlerPurpose.AiTraining)]
    [InlineData("Mozilla/5.0 (compatible; Baiduspider-news/2.0)", "Baiduspider-news", CrawlerPurpose.SearchIndex)]
    [InlineData("Mozilla/5.0 (compatible; MistralAI-Training/1.0)", "MistralAI-Training", CrawlerPurpose.AiTraining)]
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
    [InlineData(ChromeOnWindows)]
    [InlineData("SomeCrawlerNobodyHasDocumented/3.1")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_Unrecognised_Is_Reported_As_Unrecognised(string? userAgent)
    {
        CrawlerCatalogue.Match(userAgent).Should().BeNull();
    }

    /// <summary>
    /// The other way the catalogue can be wrong, and the more damaging one: a token short enough
    /// to appear inside an ordinary browser's own description would file readers as machinery on
    /// every site running the product.
    /// </summary>
    /// <remarks>
    /// Two of these are the reason the test exists. Yandex Browser is a browser people use, and
    /// eleven Yandex crawler tokens sit in the catalogue beside it; and almost every browser on
    /// earth says <c>AppleWebKit</c>, which is one letter away from Apple's crawler.
    /// </remarks>
    [Theory]
    [InlineData(ChromeOnWindows)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Safari/605.1.15")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 YaBrowser/26.6.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:154.0) Gecko/20100101 Firefox/154.0")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 16_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.2 Mobile/15E148 Safari/604.1")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0")]
    public void A_Browser_Somebody_Reads_With_Is_Never_Mistaken_For_A_Crawler(string userAgent)
    {
        CrawlerCatalogue.Match(userAgent).Should().BeNull();
    }

    /// <summary>
    /// Where an operator publishes a way to tell its own crawler from something wearing its name,
    /// the entry records it, because that is the only route from "says it is" to "is". Where an
    /// operator publishes neither — and several large ones publish neither — the entry records
    /// neither, and a claim to be that crawler stays a claim for ever. What must never happen is
    /// an address that could not be followed, because a route nobody can take reads as a check
    /// that was made.
    /// </summary>
    [Fact]
    public void A_Way_To_Check_A_Claim_Is_One_That_Could_Actually_Be_Followed()
    {
        foreach (var proof in CrawlerCatalogue.Proofs)
        {
            foreach (var published in proof.PublishedRanges)
            {
                Uri.TryCreate(published, UriKind.Absolute, out var address)
                    .Should().BeTrue("{0} publishes its addresses somewhere that can be fetched", proof.Operator);

                address!.Scheme.Should().Be(Uri.UriSchemeHttps);
            }

            proof.ConfirmingHosts.Should().OnlyContain(host => IsHostSuffix(host));
        }
    }

    /// <summary>
    /// A company listed as checkable that publishes nothing to check against would be worse than
    /// one left out: the catalogue would be promising a route that does not exist.
    /// </summary>
    [Fact]
    public void Every_Company_Listed_As_Checkable_Publishes_Something_To_Check_Against()
    {
        CrawlerCatalogue.Proofs.Should().OnlyContain(
            proof => !proof.PublishedRanges.IsEmpty || !proof.ConfirmingHosts.IsEmpty);
    }

    /// <summary>
    /// The two halves of the catalogue are joined by the company's name and by nothing else, so a
    /// spelling that differs by a space would quietly mean that nothing that company runs could
    /// ever be confirmed. Held in both directions: a company nobody has an entry for is a file
    /// fetched for nothing.
    /// </summary>
    [Fact]
    public void Every_Company_That_Can_Be_Checked_Runs_A_Crawler_This_Catalogue_Knows()
    {
        var named = CrawlerCatalogue.Known.Select(entry => entry.Operator).Distinct();

        CrawlerCatalogue.Proofs.Select(proof => proof.Operator).Should().BeSubsetOf(named);
    }

    [Fact]
    public void No_Company_Is_Listed_Twice()
    {
        CrawlerCatalogue.Proofs.Select(proof => proof.Operator).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// What a company's crawlers are for can only stand in for a name the visitor did not give if
    /// every one of them is for the same thing. A company running both a search crawler and a
    /// collector of training data must answer nothing, because those are the two a publisher most
    /// wants told apart.
    /// </summary>
    [Fact]
    public void A_Company_Running_Crawlers_For_Several_Purposes_Is_Not_Given_One()
    {
        CrawlerCatalogue.PurposeOf("Microsoft").Should().Be(CrawlerPurpose.SearchIndex);
        CrawlerCatalogue.PurposeOf("Google").Should().BeNull();
        CrawlerCatalogue.PurposeOf("OpenAI").Should().BeNull();
        CrawlerCatalogue.PurposeOf("Nobody").Should().BeNull();
        CrawlerCatalogue.PurposeOf(null).Should().BeNull();
    }

    [Fact]
    public void No_Token_Is_Listed_Twice()
    {
        CrawlerCatalogue.Known.Select(entry => entry.Token).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Nothing is matched by accident. A token of one or two characters would appear inside half
    /// the strings on the web, and the damage would be silent.
    /// </summary>
    [Fact]
    public void Every_Token_Is_Long_Enough_To_Mean_Something()
    {
        CrawlerCatalogue.Known.Should().OnlyContain(entry => entry.Token.Length >= 5);
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
        ToolCatalogue.Match(ChromeOnWindows).Should().BeNull();
    }

    /// <summary>A bare domain a reverse lookup could end with, rather than an address or a path.</summary>
    private static bool IsHostSuffix(string host) =>
        host.Length > 3
        && host.Contains('.', StringComparison.Ordinal)
        && !host.Contains('/', StringComparison.Ordinal)
        && host.Equals(host.ToLowerInvariant(), StringComparison.Ordinal);
}

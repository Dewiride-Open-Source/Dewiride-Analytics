using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Classification.Tests.Identity;

/// <summary>
/// Proves the words a program uses to describe itself as a crawler are read as they are written by
/// operators, and never found inside a word that merely contains them.
/// </summary>
public sealed class CrawlerWordsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; PetalBot;+https://webmaster.petalsearch.com/site/petalbot)")]
    [InlineData("Mozilla/5.0 (Linux; Android 5.0) AppleWebKit/537.36 (KHTML, like Gecko) Mobile Safari/537.36 (compatible; Bytespider; spider-feedback@bytedance.com)")]
    [InlineData("SomeCrawler/3.1")]
    [InlineData("Mozilla/5.0 (compatible; DataForSeoBot; +https://dataforseo.com/dataforseo-bot)")]
    [InlineData("Better Stack Better Uptime Bot Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36")]
    [InlineData("Pingdom.com_bot_version_1.4_(http://www.pingdom.com/)")]
    [InlineData("FeedFetcher-Google; (+http://www.google.com/feedfetcher.html)")]
    [InlineData("ACME Scraper 2.0")]
    [InlineData("bot")]
    public void A_Program_Describing_Itself_As_A_Crawler_Is_Recognised(string userAgent)
    {
        CrawlerWords.AppearIn(userAgent).Should().BeTrue();
    }

    /// <summary>
    /// The word has to end where a name ends. Inside a longer word it is letters that happen to be
    /// adjacent, and a phone maker whose name ends in one is a phone.
    /// </summary>
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.6 Safari/605.1.15")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 YaBrowser/26.6.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:154.0) Gecko/20100101 Firefox/154.0")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 16_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.2 Mobile/15E148 Safari/604.1")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0")]
    [InlineData("Mozilla/5.0 (Linux; Android 10; CUBOT_X30) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36")]
    [InlineData("Mozilla/5.0 (Linux; Android 11; CUBOT NOTE 20) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36")]
    [InlineData("Abbott/1.0")]
    [InlineData("Spiderman-Fan/1.0")]
    [InlineData("Roboto/2.0")]
    [InlineData("Mozilla/5.0 (+https://example.com/robots.txt)")]
    public void A_Word_That_Merely_Contains_One_Is_Not_A_Description(string userAgent)
    {
        CrawlerWords.AppearIn(userAgent).Should().BeFalse();
    }

    [Theory]
    [InlineData("SOMECRAWLER/1.0")]
    [InlineData("somecrawler/1.0")]
    public void Case_Does_Not_Matter(string userAgent)
    {
        CrawlerWords.AppearIn(userAgent).Should().BeTrue();
    }

    /// <summary>
    /// The string is written by the visitor and can be as long as they like. Reading a bounded
    /// prefix is what keeps the cost of the check independent of what is sent.
    /// </summary>
    [Fact]
    public void Only_The_First_Kilobyte_Is_Read()
    {
        CrawlerWords.AppearIn(new string('a', 2000) + " bot").Should().BeFalse();
        CrawlerWords.AppearIn("bot " + new string('a', 2000)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_Is_Not_A_Description(string? userAgent)
    {
        CrawlerWords.AppearIn(userAgent).Should().BeFalse();
    }

    /// <summary>
    /// A word of one or two letters would end half the names on the web, and the damage would be
    /// silent.
    /// </summary>
    [Fact]
    public void Every_Word_Is_Lower_Case_And_Long_Enough_To_Mean_Something()
    {
        CrawlerWords.Known.Should().OnlyHaveUniqueItems();
        CrawlerWords.Known.Should().OnlyContain(word => word.Length >= 3);
        CrawlerWords.Known.Should().OnlyContain(word => word.All(char.IsAsciiLetterLower));
    }
}

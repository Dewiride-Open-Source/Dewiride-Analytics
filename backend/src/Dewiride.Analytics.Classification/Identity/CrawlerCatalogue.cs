using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Identity;

/// <summary>
/// What a crawler is for, as its operator describes it.
/// </summary>
/// <remarks>
/// The distinction decides which category an unverified claim lands in, and it is one customers
/// care about for reasons that are not technical: a publisher who is content to be indexed by a
/// search engine may feel quite differently about being used as training material.
/// </remarks>
public enum CrawlerPurpose
{
    /// <summary>Collects content that may be used to train generative models.</summary>
    AiTraining = 1,

    /// <summary>Fetches a page because somebody asked an assistant a question just now.</summary>
    AiAssistant = 2,

    /// <summary>Builds the index behind an assistant's search feature.</summary>
    AiSearch = 3,

    /// <summary>Builds a conventional search index.</summary>
    SearchIndex = 4,

    /// <summary>Checks pages submitted as advertisements.</summary>
    Advertising = 5,

    /// <summary>Runs on behalf of a site's own owner, through a testing or inspection tool.</summary>
    SiteTooling = 6,

    /// <summary>Fetches a page because somebody shared its link, to show what is behind it.</summary>
    SocialPreview = 7,

    /// <summary>Checks that a page is still answering.</summary>
    Monitoring = 8,

    /// <summary>Collects pages into an archive or a public dataset.</summary>
    Archival = 9,

    /// <summary>Collects pages to build a commercial index of who links to whom.</summary>
    SeoAudit = 10,
}

/// <summary>
/// One crawler, as documented by whoever runs it.
/// </summary>
/// <param name="Token">
/// The exact string the operator publishes as its user-agent token. Matched case-insensitively
/// as a substring, because operators wrap their token in a longer user-agent string whose
/// surrounding text changes without notice.
/// </param>
/// <param name="Operator">The company that runs it, as it names itself.</param>
/// <param name="Purpose">What the operator says it is for.</param>
/// <remarks>
/// A token is what the visitor said, and how a claim to it can be checked belongs to whoever runs
/// the crawler rather than to the crawler itself — so that lives in <see cref="OperatorProof"/>,
/// one entry per company. Google publishes four files covering all of its crawlers together and
/// OpenAI publishes one for each of its own, and in both cases what a matching address establishes
/// is the company.
/// </remarks>
public sealed record CrawlerIdentity(string Token, string Operator, CrawlerPurpose Purpose);

/// <summary>
/// How one company's crawlers can be told apart from something wearing their name.
/// </summary>
/// <param name="Operator">The company, spelt as its catalogue entries spell it.</param>
/// <param name="PublishedRanges">
/// Machine-readable files of the addresses its crawlers connect from, each at the address the
/// company itself publishes it at.
/// </param>
/// <param name="ConfirmingHosts">
/// The domains a genuine visit's address answers to, as the company documents them. A name counts
/// as one of these only where it is the domain itself or sits below it, and only where the domain
/// points back at the address that produced it.
/// </param>
/// <remarks>
/// <para>
/// A company appears here only if it publishes at least one of the two, and several do not: a
/// crawler whose operator offers no way to tell a genuine visit from an impostor stays a claim
/// however it behaves, and saying so is more useful than inventing a check.
/// </para>
/// <para>
/// Neither is listed where the thing it identifies is shared. Google publishes a fifth file
/// covering fetchers that run on shared App Engine infrastructure, and documents the domain those
/// machines answer to alongside its own; both are deliberately absent, because those addresses and
/// that domain belong to everybody who runs an app there, and arriving from one proves nothing
/// about who is calling.
/// </para>
/// </remarks>
public sealed record OperatorProof(
    string Operator,
    ImmutableArray<string> PublishedRanges,
    ImmutableArray<string> ConfirmingHosts);

/// <summary>
/// The crawlers this build can recognise by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every entry was read from the operator's own documentation.</b> Third-party directories of
/// crawler user agents are larger and more convenient, and are not used here: this catalogue is
/// what puts a company's name beside somebody's traffic, and a name sourced from a directory is a
/// claim this product cannot stand behind. The pages each entry came from are listed against it
/// below.
/// </para>
/// <para>
/// It is therefore deliberately incomplete, and that is the safe direction to be wrong in. A
/// crawler absent from here is reported as unrecognised and judged on its behaviour alone, which
/// is honest. A crawler present under the wrong operator's name would be the product telling a
/// customer something false about who is reading their site.
/// </para>
/// <para>
/// Matching the token is not identification. It establishes only what the visitor said, which is
/// one line of text anybody can copy. Checking where the request came from against
/// <see cref="Proofs"/> is what turns the claim into an identity, and only that path may reach
/// <see cref="EvidenceStrength.Verified"/>.
/// </para>
/// </remarks>
public static class CrawlerCatalogue
{
    private const string OpenAi = "OpenAI";
    private const string Anthropic = "Anthropic";
    private const string Perplexity = "Perplexity";
    private const string Google = "Google";
    private const string Microsoft = "Microsoft";
    private const string Meta = "Meta";
    private const string Yandex = "Yandex";
    private const string Apple = "Apple";
    private const string Amazon = "Amazon";
    private const string CommonCrawl = "Common Crawl";
    private const string DuckDuckGo = "DuckDuckGo";
    private const string Mistral = "Mistral AI";
    private const string Baidu = "Baidu";
    private const string Slack = "Slack";
    private const string Ahrefs = "Ahrefs";

    /// <summary>Where Google publishes each of its files, before the file's own name.</summary>
    private const string GoogleRanges = "https://developers.google.com/static/crawling/ipranges/";

    /// <summary>
    /// How each company that publishes a way of checking its crawlers publishes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every address here was read from the company's own documentation and fetched to confirm
    /// it answers</b>, on the same terms as the tokens below. A file listed here decides whose name
    /// goes beside somebody's traffic with no claim from the visitor involved at all, so a
    /// third-party mirror of one would be worse than having none.
    /// </para>
    /// <para>
    /// Absent by design: Amazon, which publishes its crawler addresses as a web page rather than as
    /// a file anything can read; and Meta and Slack, which publish neither route. Their crawlers
    /// are recognised by name and reported as unverified claims, which is the whole truth about
    /// them.
    /// </para>
    /// </remarks>
    public static readonly ImmutableArray<OperatorProof> Proofs =
    [
        // https://developers.openai.com/api/docs/bots
        new(OpenAi,
            [
                "https://openai.com/gptbot.json",
                "https://openai.com/chatgpt-user.json",
                "https://openai.com/searchbot.json",
                "https://openai.com/adsbot.json",
            ],
            []),

        // https://platform.claude.com/docs
        new(Anthropic, ["https://claude.com/crawling/bots.json"], []),

        // https://docs.perplexity.ai/guides/bots
        new(Perplexity,
            [
                "https://www.perplexity.com/perplexitybot.json",
                "https://www.perplexity.com/perplexity-user.json",
            ],
            []),

        // https://developers.google.com/search/docs/crawling-indexing/verifying-googlebot
        // The same page documents a third domain, googleusercontent.com, which is left out for the
        // reason the fifth file is: it is where anybody's App Engine application answers from.
        new(Google,
            [
                GoogleRanges + "common-crawlers.json",
                GoogleRanges + "special-crawlers.json",
                GoogleRanges + "user-triggered-fetchers-google.json",
            ],
            ["googlebot.com", "google.com"]),

        // https://blogs.bing.com/webmaster/August-2012/How-to-Verify-that-Bingbot-is-Bingbot/
        new(Microsoft, ["https://www.bing.com/toolbox/bingbot.json"], ["search.msn.com"]),

        // https://yandex.com/support/webmaster/robot-workings/check-yandex-robots.html
        new(Yandex, [], ["yandex.ru", "yandex.net", "yandex.com"]),

        // https://support.apple.com/en-us/119829
        new(Apple, ["https://search.developer.apple.com/applebot.json"], ["applebot.apple.com"]),

        // https://commoncrawl.org/ccbot
        new(CommonCrawl, ["https://index.commoncrawl.org/ccbot.json"], ["crawl.commoncrawl.org"]),

        // https://duckduckgo.com/duckduckgo-help-pages/results/duckduckbot/ and
        // https://duckduckgo.com/duckduckgo-help-pages/results/duckassistbot/. Both addresses
        // currently serve the same list; each is the one its own crawler is documented at, and
        // repeated blocks cost nothing because the table holds each block once.
        new(DuckDuckGo,
            [
                "https://duckduckgo.com/duckduckbot.json",
                "https://duckduckgo.com/duckassistbot.json",
            ],
            []),

        // https://docs.mistral.ai/robots/
        new(Mistral,
            [
                "https://mistral.ai/mistralai-index-ips.json",
                "https://mistral.ai/mistralai-user-ips.json",
            ],
            []),

        // https://help.baidu.com/question?prod_id=99&class=0&id=3001
        new(Baidu, [], ["baidu.com", "baidu.jp"]),

        // https://ahrefs.com/robot
        new(Ahrefs, [], ["ahrefs.com", "ahrefs.net"]),
    ];

    /// <summary>
    /// Every recognised crawler, longest token first.
    /// </summary>
    /// <remarks>
    /// The order is what makes matching correct rather than merely fast. Several operators use
    /// one token as the prefix of another — <c>GoogleOther</c> and <c>GoogleOther-Image</c>,
    /// <c>Applebot</c> and <c>Applebot-Extended</c>, <c>Baiduspider</c> and
    /// <c>Baiduspider-news</c> — so a shorter token would otherwise capture a visitor that named
    /// the longer one and file it under the wrong purpose.
    /// </remarks>
    public static readonly ImmutableArray<CrawlerIdentity> Known =
    [
        .. Ordered(
        [
            // OpenAI — https://developers.openai.com/api/docs/bots
            new("OAI-SearchBot", OpenAi, CrawlerPurpose.AiSearch),
            new("OAI-AdsBot", OpenAi, CrawlerPurpose.Advertising),
            new("ChatGPT-User", OpenAi, CrawlerPurpose.AiAssistant),
            new("GPTBot", OpenAi, CrawlerPurpose.AiTraining),

            // Anthropic — https://platform.claude.com/docs, ranges at https://claude.com/crawling/bots.json
            new("Claude-SearchBot", Anthropic, CrawlerPurpose.AiSearch),
            new("Claude-User", Anthropic, CrawlerPurpose.AiAssistant),
            new("ClaudeBot", Anthropic, CrawlerPurpose.AiTraining),

            // Perplexity — https://docs.perplexity.ai/guides/bots
            new("PerplexityBot", Perplexity, CrawlerPurpose.AiSearch),
            new("Perplexity-User", Perplexity, CrawlerPurpose.AiAssistant),

            // Google — https://developers.google.com/search/docs/crawling-indexing/google-common-crawlers
            new("Google-CloudVertexBot", Google, CrawlerPurpose.AiTraining),
            new("Google-InspectionTool", Google, CrawlerPurpose.SiteTooling),
            new("GoogleOther-Image", Google, CrawlerPurpose.SearchIndex),
            new("GoogleOther-Video", Google, CrawlerPurpose.SearchIndex),
            new("Google-Extended", Google, CrawlerPurpose.AiTraining),
            new("Storebot-Google", Google, CrawlerPurpose.SearchIndex),
            new("Googlebot-Image", Google, CrawlerPurpose.SearchIndex),
            new("Googlebot-Video", Google, CrawlerPurpose.SearchIndex),
            new("Googlebot-News", Google, CrawlerPurpose.SearchIndex),
            new("GoogleOther", Google, CrawlerPurpose.SearchIndex),
            new("Googlebot", Google, CrawlerPurpose.SearchIndex),

            // Microsoft — https://blogs.bing.com/webmaster/april-2022/Announcing-user-agent-change-for-Bing-crawler-bingbot
            // Host suffix from https://blogs.bing.com/webmaster/August-2012/How-to-Verify-that-Bingbot-is-Bingbot/
            new("bingbot", Microsoft, CrawlerPurpose.SearchIndex),

            // Meta — https://developers.facebook.com/docs/sharing/webmasters/web-crawlers
            new("meta-externalfetcher", Meta, CrawlerPurpose.AiAssistant),
            new("meta-externalagent", Meta, CrawlerPurpose.AiTraining),
            new("facebookexternalhit", Meta, CrawlerPurpose.SocialPreview),
            new("meta-externalads", Meta, CrawlerPurpose.Advertising),
            new("meta-webindexer", Meta, CrawlerPurpose.AiSearch),

            // Yandex — https://yandex.com/support/webmaster/robot-workings/check-yandex-robots.html
            new("YandexRenderResourcesBot", Yandex, CrawlerPurpose.SearchIndex),
            new("YandexAccessibilityBot", Yandex, CrawlerPurpose.SiteTooling),
            new("YandexScreenshotBot", Yandex, CrawlerPurpose.SiteTooling),
            new("YandexMobileBot", Yandex, CrawlerPurpose.SearchIndex),
            new("YandexWebmaster", Yandex, CrawlerPurpose.SiteTooling),
            new("YandexCheckBot", Yandex, CrawlerPurpose.Monitoring),
            new("YandexMetrika", Yandex, CrawlerPurpose.Monitoring),
            new("YandexComBot", Yandex, CrawlerPurpose.SearchIndex),
            new("YandexImages", Yandex, CrawlerPurpose.SearchIndex),
            new("YandexVideo", Yandex, CrawlerPurpose.SearchIndex),
            new("YandexBot", Yandex, CrawlerPurpose.SearchIndex),

            // Apple — https://support.apple.com/en-us/119829
            new("Applebot-Extended", Apple, CrawlerPurpose.AiTraining),
            new("Applebot", Apple, CrawlerPurpose.SearchIndex),

            // Amazon — https://developer.amazon.com/amazonbot
            new("Amazonbot", Amazon, CrawlerPurpose.AiTraining),

            // Common Crawl — https://commoncrawl.org/ccbot
            new("CCBot", CommonCrawl, CrawlerPurpose.Archival),

            // DuckDuckGo — https://duckduckgo.com/duckduckgo-help-pages/results/duckduckbot/
            // and https://duckduckgo.com/duckduckgo-help-pages/results/duckassistbot/, which says
            // outright that what it fetches is not used to train anything.
            new("DuckAssistBot", DuckDuckGo, CrawlerPurpose.AiAssistant),
            new("DuckDuckBot", DuckDuckGo, CrawlerPurpose.SearchIndex),

            // Mistral AI — https://docs.mistral.ai/robots/
            new("MistralAI-Training", Mistral, CrawlerPurpose.AiTraining),
            new("MistralAI-Index", Mistral, CrawlerPurpose.AiSearch),
            new("MistralAI-User", Mistral, CrawlerPurpose.AiAssistant),

            // Baidu — https://help.baidu.com/question?prod_id=99&class=0&id=3001
            new("Baiduspider-image", Baidu, CrawlerPurpose.SearchIndex),
            new("Baiduspider-video", Baidu, CrawlerPurpose.SearchIndex),
            new("Baiduspider-news", Baidu, CrawlerPurpose.SearchIndex),
            new("Baiduspider-favo", Baidu, CrawlerPurpose.Archival),
            new("Baiduspider-cpro", Baidu, CrawlerPurpose.Advertising),
            new("Baiduspider-ads", Baidu, CrawlerPurpose.Advertising),
            new("Baiduspider", Baidu, CrawlerPurpose.SearchIndex),

            // Slack — https://api.slack.com/robots
            new("Slackbot-LinkExpanding", Slack, CrawlerPurpose.SocialPreview),

            // Ahrefs — https://ahrefs.com/robot
            new("AhrefsSiteAudit", Ahrefs, CrawlerPurpose.SiteTooling),
            new("AhrefsBot", Ahrefs, CrawlerPurpose.SeoAudit),
        ]),
    ];

    /// <summary>
    /// Finds the crawler a user agent names itself as.
    /// </summary>
    /// <param name="userAgent">The string the visitor sent. Attacker-controlled.</param>
    /// <returns>
    /// The catalogue entry whose token appears in it, or <see langword="null"/> when none does.
    /// A result is what the visitor <em>said</em>, never who it is.
    /// </returns>
    public static CrawlerIdentity? Match(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        return Known.FirstOrDefault(
            candidate => userAgent.Contains(candidate.Token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What a company's crawlers are for, where every one of them is for the same thing.
    /// </summary>
    /// <param name="operatorName">The company, spelt as the catalogue spells it.</param>
    /// <returns>
    /// The shared purpose, or <see langword="null"/> where the company runs crawlers for more than
    /// one and where the name is not one this catalogue holds.
    /// </returns>
    /// <remarks>
    /// The answer for a visit whose address established whose crawler it is while its user agent
    /// named nothing — which is ordinary rather than odd, since a crawler is under no obligation to
    /// introduce itself and several fetch pages behind a stock browser string. Where a company runs
    /// crawlers for several purposes there is nothing to say, and saying nothing is what stops one
    /// of them being reported as a collector of training data because its operator also runs one.
    /// </remarks>
    public static CrawlerPurpose? PurposeOf(string? operatorName)
    {
        if (string.IsNullOrEmpty(operatorName))
        {
            return null;
        }

        CrawlerPurpose? shared = null;

        foreach (var entry in Known)
        {
            if (!string.Equals(entry.Operator, operatorName, StringComparison.Ordinal))
            {
                continue;
            }

            if (shared is not null && shared != entry.Purpose)
            {
                return null;
            }

            shared = entry.Purpose;
        }

        return shared;
    }

    private static ImmutableArray<CrawlerIdentity> Ordered(ImmutableArray<CrawlerIdentity> entries) =>
        [.. entries.OrderByDescending(entry => entry.Token.Length).ThenBy(entry => entry.Token, StringComparer.Ordinal)];
}

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
/// <param name="PublishedRanges">
/// Where the operator publishes the addresses it crawls from. This is what a claim will be
/// checked against; until that check runs, a match on the token alone is an unverified claim and
/// is reported as one.
/// </param>
public sealed record CrawlerIdentity(
    string Token,
    string Operator,
    CrawlerPurpose Purpose,
    string? PublishedRanges);

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
/// one line of text anybody can copy. Confirming it against <see cref="CrawlerIdentity.PublishedRanges"/>
/// is what turns the claim into an identity, and only that path may reach
/// <see cref="EvidenceStrength.Verified"/>.
/// </para>
/// </remarks>
public static class CrawlerCatalogue
{
    /// <summary>
    /// Every recognised crawler, longest token first.
    /// </summary>
    /// <remarks>
    /// The order is what makes matching correct rather than merely fast. Several operators use
    /// one token as the prefix of another — <c>GoogleOther</c> and <c>GoogleOther-Image</c>,
    /// <c>Googlebot</c> and <c>Googlebot-News</c> — so a shorter token would otherwise capture a
    /// visitor that named the longer one and file it under the wrong purpose.
    /// </remarks>
    public static readonly ImmutableArray<CrawlerIdentity> Known =
    [
        // OpenAI — https://developers.openai.com/api/docs/bots
        .. Ordered(
        [
            new("OAI-SearchBot", "OpenAI", CrawlerPurpose.AiSearch, "https://openai.com/searchbot.json"),
            new("OAI-AdsBot", "OpenAI", CrawlerPurpose.Advertising, "https://openai.com/adsbot.json"),
            new("ChatGPT-User", "OpenAI", CrawlerPurpose.AiAssistant, "https://openai.com/chatgpt-user.json"),
            new("GPTBot", "OpenAI", CrawlerPurpose.AiTraining, "https://openai.com/gptbot.json"),

            // Anthropic — https://platform.claude.com/docs, ranges at https://claude.com/crawling/bots.json
            new("Claude-SearchBot", "Anthropic", CrawlerPurpose.AiSearch, AnthropicRanges),
            new("Claude-User", "Anthropic", CrawlerPurpose.AiAssistant, AnthropicRanges),
            new("ClaudeBot", "Anthropic", CrawlerPurpose.AiTraining, AnthropicRanges),

            // Perplexity — https://docs.perplexity.ai/guides/bots
            new("PerplexityBot", "Perplexity", CrawlerPurpose.AiSearch, "https://www.perplexity.com/perplexitybot.json"),
            new("Perplexity-User", "Perplexity", CrawlerPurpose.AiAssistant, "https://www.perplexity.com/perplexity-user.json"),

            // Google — https://developers.google.com/search/docs/crawling-indexing/google-common-crawlers
            new("Google-CloudVertexBot", "Google", CrawlerPurpose.AiTraining, GoogleRanges),
            new("Google-InspectionTool", "Google", CrawlerPurpose.SiteTooling, GoogleRanges),
            new("GoogleOther-Image", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("GoogleOther-Video", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("Google-Extended", "Google", CrawlerPurpose.AiTraining, GoogleRanges),
            new("Storebot-Google", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("Googlebot-Image", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("Googlebot-Video", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("Googlebot-News", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("GoogleOther", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
            new("Googlebot", "Google", CrawlerPurpose.SearchIndex, GoogleRanges),
        ]),
    ];

    private const string AnthropicRanges = "https://claude.com/crawling/bots.json";

    private const string GoogleRanges =
        "https://developers.google.com/static/crawling/ipranges/common-crawlers.json";

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

    private static ImmutableArray<CrawlerIdentity> Ordered(ImmutableArray<CrawlerIdentity> entries) =>
        [.. entries.OrderByDescending(entry => entry.Token.Length).ThenBy(entry => entry.Token, StringComparer.Ordinal)];
}

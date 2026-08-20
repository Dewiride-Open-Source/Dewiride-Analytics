using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// What kind of thing sent a visitor.
/// </summary>
/// <remarks>
/// A closed set, and every member is an answer rather than a gap. The words are stored nowhere:
/// they are compiled into a statement, compared against a column, and read back — so this is a
/// vocabulary rather than a schema, and correcting it re-answers every period a site has ever
/// recorded instead of only the ones recorded after the correction.
/// </remarks>
public enum SourceChannel
{
    /// <summary>
    /// The visitor's browser named nowhere at all.
    /// </summary>
    /// <remarks>
    /// Typing the address, opening a bookmark, following a link from an application, and arriving
    /// from a site that withholds the address are indistinguishable, and this covers all of them.
    /// It is not "nobody sent them": it is "nothing said who did".
    /// </remarks>
    Direct = 1,

    /// <summary>A search engine.</summary>
    Search = 2,

    /// <summary>
    /// A conversational assistant.
    /// </summary>
    /// <remarks>
    /// Kept apart from search rather than folded into it. Somebody who arrives having been told
    /// about a page did not read a list of results and choose it, and a product whose subject is
    /// telling machinery and people apart should not be the one to blur that.
    /// </remarks>
    Assistant = 3,

    /// <summary>A social network.</summary>
    Social = 4,

    /// <summary>An ordinary link on some other website.</summary>
    Link = 5,
}

/// <summary>
/// One entry in the catalogue of things that send traffic.
/// </summary>
/// <param name="Key">
/// What is matched. A single word with no full stop is matched against the name a site is known
/// by — the label in front of its public suffix — so one entry covers every address that site
/// answers on. Anything containing a full stop is matched against a whole hostname, for the cases
/// where one site's addresses do different jobs.
/// </param>
/// <param name="Name">What the site is called on screen.</param>
/// <param name="Channel">What kind of thing it is.</param>
public readonly record struct TrafficSource(string Key, string Name, SourceChannel Channel);

/// <summary>
/// The sites a visitor is commonly sent by, and what kind of thing each one is.
/// </summary>
/// <remarks>
/// <para>
/// Two problems are solved here, and they are the same problem. One search engine answers on
/// hundreds of addresses — <c>google.com</c>, <c>www.google.com</c> and <c>google.co.in</c> are
/// one place — so without this the busiest source of a site's traffic is spread over a dozen rows
/// and appears on none of them at its real size. And a reader who wants to know how much of their
/// audience search brings them cannot work it out from a list of hostnames unless they already
/// know which hostnames are search engines.
/// </para>
/// <para>
/// <b>A key without a full stop is matched against the site's name rather than its address.</b>
/// The name is the label in front of the public suffix, so <c>google.com</c>,
/// <c>www.google.co.in</c> and <c>search.yahoo.co.jp</c> reduce to <c>google</c>, <c>google</c>
/// and <c>yahoo</c>. That is also what makes the match safe: a referrer is written by whoever
/// visited the site, and somebody who registers <c>google.attacker.test</c> reduces to
/// <c>attacker</c>, not to <c>google</c>. Matching on any label would have let a stranger put
/// their traffic under Google's name on somebody else's dashboard.
/// </para>
/// <para>
/// <b>A key containing a full stop is matched against the whole address</b>, and is checked first.
/// It exists for the addresses where reducing to the site's name gives the wrong answer:
/// <c>mail.google.com</c> is somebody sending a link to somebody, not a search, and counting it
/// under search engines would overstate the one figure this card exists to give honestly.
/// </para>
/// <para>
/// <b>Absence from this catalogue is not a failure.</b> Anything unlisted is a link from another
/// website, which is what it is. The catalogue holds what is worth naming and grouping, not
/// everything that exists — there is no list of every website, and a product that pretended
/// otherwise would be inventing precision.
/// </para>
/// </remarks>
public static class TrafficSources
{
    /// <summary>
    /// Suffix labels that sit in front of a country's own, so that the name of the site is the
    /// label before them rather than the label before the country.
    /// </summary>
    /// <remarks>
    /// Enough of a public suffix list to tell <c>google.co.in</c> from <c>google.attacker.test</c>,
    /// and deliberately no more. The full list is a published file that changes weekly; carrying a
    /// copy of it to improve the spelling of a row on a chart would be a dependency to keep
    /// current for the rest of the product's life.
    /// </remarks>
    public static ImmutableArray<string> SecondLevelSuffixes { get; } =
    [
        "ac", "co", "com", "edu", "go", "gob", "gouv", "gov", "govt", "ne", "net", "or", "org",
    ];

    /// <summary>Every catalogued source, checked whole-address entries first.</summary>
    public static ImmutableArray<TrafficSource> All { get; } =
    [
        // Addresses whose job differs from the rest of their site's, checked before names.
        new("mail.google.com", "Gmail", SourceChannel.Link),
        new("news.google.com", "Google News", SourceChannel.Link),
        new("translate.google.com", "Google Translate", SourceChannel.Link),
        new("gemini.google.com", "Gemini", SourceChannel.Assistant),
        new("chat.openai.com", "ChatGPT", SourceChannel.Assistant),
        new("copilot.microsoft.com", "Copilot", SourceChannel.Assistant),
        new("com.google.android.gm", "Gmail", SourceChannel.Link),

        // Search engines.
        new("google", "Google", SourceChannel.Search),
        new("bing", "Bing", SourceChannel.Search),
        new("yahoo", "Yahoo", SourceChannel.Search),
        new("duckduckgo", "DuckDuckGo", SourceChannel.Search),
        new("ecosia", "Ecosia", SourceChannel.Search),
        new("qwant", "Qwant", SourceChannel.Search),
        new("startpage", "Startpage", SourceChannel.Search),
        new("brave", "Brave Search", SourceChannel.Search),
        new("kagi", "Kagi", SourceChannel.Search),
        new("mojeek", "Mojeek", SourceChannel.Search),
        new("marginalia", "Marginalia", SourceChannel.Search),
        new("searx", "SearXNG", SourceChannel.Search),
        new("yandex", "Yandex", SourceChannel.Search),
        new("baidu", "Baidu", SourceChannel.Search),
        new("naver", "Naver", SourceChannel.Search),
        new("daum", "Daum", SourceChannel.Search),
        new("seznam", "Seznam", SourceChannel.Search),
        new("sogou", "Sogou", SourceChannel.Search),
        new("ask", "Ask", SourceChannel.Search),
        new("aol", "AOL", SourceChannel.Search),
        new("lycos", "Lycos", SourceChannel.Search),

        // Conversational assistants.
        new("chatgpt", "ChatGPT", SourceChannel.Assistant),
        new("openai", "ChatGPT", SourceChannel.Assistant),
        new("perplexity", "Perplexity", SourceChannel.Assistant),
        new("claude", "Claude", SourceChannel.Assistant),
        new("copilot", "Copilot", SourceChannel.Assistant),
        new("phind", "Phind", SourceChannel.Assistant),
        new("poe", "Poe", SourceChannel.Assistant),

        // Social networks.
        new("facebook", "Facebook", SourceChannel.Social),
        new("instagram", "Instagram", SourceChannel.Social),
        new("threads", "Threads", SourceChannel.Social),
        new("twitter", "X", SourceChannel.Social),
        new("x", "X", SourceChannel.Social),
        new("t", "X", SourceChannel.Social),
        new("linkedin", "LinkedIn", SourceChannel.Social),
        new("lnkd", "LinkedIn", SourceChannel.Social),
        new("reddit", "Reddit", SourceChannel.Social),
        new("youtube", "YouTube", SourceChannel.Social),
        new("youtu", "YouTube", SourceChannel.Social),
        new("pinterest", "Pinterest", SourceChannel.Social),
        new("tiktok", "TikTok", SourceChannel.Social),
        new("bsky", "Bluesky", SourceChannel.Social),
        new("mastodon", "Mastodon", SourceChannel.Social),
        new("tumblr", "Tumblr", SourceChannel.Social),
        new("vk", "VK", SourceChannel.Social),
        new("weibo", "Weibo", SourceChannel.Social),
        new("quora", "Quora", SourceChannel.Social),
    ];

    /// <summary>What each catalogued source is matched by.</summary>
    public static ImmutableArray<string> Keys { get; } = [.. All.Select(source => source.Key)];

    /// <summary>What each catalogued source is called, in the order <see cref="Keys"/> holds.</summary>
    public static ImmutableArray<string> Names { get; } = [.. All.Select(source => source.Name)];

    /// <summary>What kind each catalogued source is, in the order <see cref="Keys"/> holds.</summary>
    public static ImmutableArray<string> Channels { get; } =
        [.. All.Select(source => Spelling(source.Channel))];

    /// <summary>
    /// Which kind each spelling names.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Spelling"/> over the whole enumeration rather than typed out a second
    /// time, so a kind added to the product cannot be written one way and read back another.
    /// </remarks>
    public static FrozenDictionary<string, SourceChannel> Kinds { get; } =
        Enum.GetValues<SourceChannel>().ToFrozenDictionary(Spelling, channel => channel, StringComparer.Ordinal);

    /// <summary>
    /// How a kind is written where it is compared and read back.
    /// </summary>
    /// <param name="channel">The kind.</param>
    /// <returns>Its spelling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the five.</exception>
    public static string Spelling(SourceChannel channel) => channel switch
    {
        SourceChannel.Direct => "direct",
        SourceChannel.Search => "search",
        SourceChannel.Assistant => "assistant",
        SourceChannel.Social => "social",
        SourceChannel.Link => "link",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}

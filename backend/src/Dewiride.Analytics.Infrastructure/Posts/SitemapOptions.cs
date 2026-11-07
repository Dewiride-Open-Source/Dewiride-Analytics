namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// How this installation reads the sitemaps its websites name.
/// </summary>
public sealed class SitemapOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Sitemaps";

    /// <summary>
    /// The most a sitemap may hold, as the sitemaps.org protocol sets it for one file: fifty
    /// mebibytes, uncompressed.
    /// </summary>
    public const long ProtocolLimitBytes = 52_428_800;

    /// <summary>
    /// Whether this installation reads sitemaps at all.
    /// </summary>
    /// <remarks>
    /// Turn it off for an install with no way out to the internet, or for one whose operator would
    /// rather it fetched nothing from anybody's website. A sitemap can still be named, and whatever
    /// was read before stays where it is and is still reported, under a status that says reading is
    /// switched off.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How many websites this engine reads at the same time.
    /// </summary>
    /// <remarks>
    /// Nought leaves the reading to whichever other engines share this one's control plane. It is not
    /// the same as <see cref="Enabled"/>, which is about the whole installation: this engine still says
    /// how reading is going, because for all it knows it is going well.
    /// </remarks>
    public int SitesAtOnce { get; init; } = 2;

    /// <summary>How long a reader waits before looking again when no website's time has come.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often each website's sitemap is read from the top.
    /// </summary>
    /// <remarks>
    /// Never less than an hour. Asking costs a website very little, because what it is asked is whether
    /// anything has changed, and this is how a new post comes to be counted within the hour. The pages
    /// behind a sitemap are another matter: each is read once, and again only when the sitemap says it
    /// has changed.
    /// </remarks>
    public TimeSpan Cadence { get; init; } = TimeSpan.FromHours(1);

    /// <summary>The most pages read in one pass over a website, so that one large website is not all a reader does.</summary>
    public int PagesPerPass { get; init; } = 120;

    /// <summary>
    /// How long one pass over a website may go on starting new work. What is left waits for the next.
    /// </summary>
    public TimeSpan PassBudget { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a claim on a website lasts, after which it can be taken by another reader.
    /// </summary>
    /// <remarks>
    /// Long enough to be sure the pass holding it has finished: its budget, and then whatever it started
    /// at the last moment. A claim held by an engine that has died simply runs out.
    /// </remarks>
    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>The most files of one sitemap that are read, beyond which it reads as listing too many.</summary>
    public int MostSitemapParts { get; init; } = 500;

    /// <summary>
    /// The most pages one website's sitemap may list, however many files it splits them across, beyond
    /// which it reads as listing too many.
    /// </summary>
    public int MostPostsPerSite { get; init; } = 50_000;

    /// <summary>How long a sitemap that could not be read, or a website in trouble, is left before trying again.</summary>
    public TimeSpan RetryAfterFailure { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The least time between two requests to one website, whatever they are for.
    /// </summary>
    /// <remarks>
    /// Never less than a second. Somebody's website is doing this work for free and did not ask to,
    /// and a redirect or a robots.txt is a request like any other as far as its server goes. Never so
    /// long that the redirects of one asking would not fit inside <see cref="PageTimeout"/> and
    /// <see cref="SitemapTimeout"/> either, because each waits its turn inside that time, and a website
    /// that redirects would otherwise read as not answering.
    /// </remarks>
    public TimeSpan RequestGap { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a website has to accept a connection, name lookup and encryption included.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long one file of a sitemap has to arrive, from the first request to the last byte,
    /// redirects included.
    /// </summary>
    public TimeSpan SitemapTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long one page has to arrive, from the first request to the last byte read, redirects
    /// included. The same goes for a website's robots.txt.
    /// </summary>
    public TimeSpan PageTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The most read of one file of a sitemap, as it arrives and again as it unpacks.
    /// </summary>
    public long LargestSitemapBytes { get; init; } = ProtocolLimitBytes;

    /// <summary>
    /// How much of a page is read. What a page says about itself is at the top of it, so reaching
    /// this is not a failure and the rest is simply not asked for.
    /// </summary>
    public int PageBytesRead { get; init; } = 512 * 1024;
}

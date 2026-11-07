namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// One page a website's sitemap lists, and what its page said about itself.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by the page's path, the value the telemetry store knows the page by, and not by its whole
/// address, so that moving a website from one of its names to the other leaves every post where it
/// is.
/// </para>
/// <para>
/// Whatever is in here was written by the website. A title and an address are attacker-controlled
/// text, however they were cleaned on the way in: they are never logged, never rendered as anything
/// but text, and never placed in a model prompt as instructions.
/// </para>
/// </remarks>
public sealed class SitePost
{
    /// <summary>
    /// Longest title kept.
    /// </summary>
    /// <remarks>
    /// A title is shown on one line, and past this it has stopped being one. A page can say anything
    /// it likes, so whatever reads a title cuts it to this before it is stored.
    /// </remarks>
    public const int MaxTitleLength = 300;

    /// <summary>The website.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>The page's path, as the collector reduces a page to one.</summary>
    public required string Path { get; init; }

    /// <summary>The page's address, to link to.</summary>
    public required string Address { get; init; }

    /// <summary>
    /// The path the page answered at when the website redirected it to another of its own paths, or nothing
    /// where it answered at its own.
    /// </summary>
    /// <remarks>
    /// A browser following the link ends where the redirect leads, and that is the path the collector
    /// records a visit under. A page is read once, so where it landed is kept when it is read. The row stays
    /// keyed by the path the sitemap lists, because that is what the next listing finds it by.
    /// </remarks>
    public string? LandedPath { get; init; }

    /// <summary>The part of the sitemap that listed it.</summary>
    public Guid? PartId { get; init; }

    /// <summary>The reading of that part that listed it.</summary>
    public Guid? PartGeneration { get; init; }

    /// <summary>Whether that part is the one recognised as holding the website's posts.</summary>
    public bool ListedInPostsPart { get; init; }

    /// <summary>
    /// When the sitemap says the page last changed.
    /// </summary>
    /// <remarks>
    /// A reason to read the page again and nothing else. It says when somebody last touched the page,
    /// so it is never shown as the date a post was published.
    /// </remarks>
    public DateTimeOffset? ListedModifiedAt { get; init; }

    /// <summary>When a sitemap first listed it.</summary>
    public required DateTimeOffset FirstListedAt { get; init; }

    /// <summary>When a complete reading last found it gone, or nothing while it is listed.</summary>
    public DateTimeOffset? UnlistedAt { get; init; }

    /// <summary>What the page says it is called, cleaned of anything that is not text.</summary>
    public string? Title { get; init; }

    /// <summary>Where on the page that title came from.</summary>
    public PostTitleSource? TitleSource { get; init; }

    /// <summary>When the page says it was published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Where on the page that date came from.</summary>
    public PostDateSource? PublishedSource { get; init; }

    /// <summary>What the page says it is, or nothing where it did not say.</summary>
    public PostPageType? PageType { get; init; }

    /// <summary>
    /// Whether the page counts as a post, worked out by the database from the columns it rests on.
    /// </summary>
    public bool IsPost { get; init; }

    /// <summary>When the page is next due to be read, or nothing where it is not.</summary>
    public DateTimeOffset? PageDueAt { get; init; }

    /// <summary>How many times the page has been asked for since it was last read.</summary>
    public short PageAttempts { get; init; }

    /// <summary>When the page was last asked for.</summary>
    public DateTimeOffset? PageReadAt { get; init; }

    /// <summary>What came of asking for it, or nothing where nothing has yet.</summary>
    public PostPageOutcome? PageOutcome { get; init; }

    /// <summary>What <see cref="ListedModifiedAt"/> said when the page was last asked for.</summary>
    public DateTimeOffset? PageReadLastmod { get; init; }

    /// <summary>The entity tag the page answered with, to ask whether it has changed.</summary>
    public string? PageEtag { get; init; }

    /// <summary>When the page said it last changed, to ask whether it has changed since.</summary>
    public DateTimeOffset? PageLastModified { get; init; }
}

/// <summary>Where on a page its title was found.</summary>
public enum PostTitleSource
{
    /// <summary>The page's Open Graph title.</summary>
    OpenGraph = 1,

    /// <summary>The headline in the page's structured data.</summary>
    StructuredData = 2,

    /// <summary>The page's Twitter card title.</summary>
    Twitter = 3,

    /// <summary>The document's own title.</summary>
    Document = 4,
}

/// <summary>Where on a page the date it was published was found.</summary>
public enum PostDateSource
{
    /// <summary>The page's Open Graph article time.</summary>
    OpenGraph = 1,

    /// <summary>The date in the page's structured data.</summary>
    StructuredData = 2,

    /// <summary>The date in the page's microdata.</summary>
    Microdata = 3,

    /// <summary>The date in the page's microformats.</summary>
    Microformat = 4,

    /// <summary>The date WordPress's own post date block printed, where nothing else said.</summary>
    WordPressBlock = 5,
}

/// <summary>What a page says it is.</summary>
public enum PostPageType
{
    /// <summary>An article.</summary>
    Article = 1,

    /// <summary>Anything else, which keeps it from counting as a post.</summary>
    Other = 2,
}

/// <summary>What came of asking for a post's page.</summary>
public enum PostPageOutcome
{
    /// <summary>The page was read.</summary>
    Read = 1,

    /// <summary>The page has not changed since it was last read.</summary>
    Unchanged = 2,

    /// <summary>The page is gone, which keeps it from counting as a post.</summary>
    Gone = 3,

    /// <summary>What is at the address is not a web page.</summary>
    NotAPage = 4,

    /// <summary>The website would not let the reader in.</summary>
    TurnedAway = 5,

    /// <summary>The website answered that something had gone wrong at its end.</summary>
    ServerError = 6,

    /// <summary>The website did not answer.</summary>
    Unreachable = 7,

    /// <summary>The page is larger than is read.</summary>
    TooLarge = 8,

    /// <summary>The page redirected off the website.</summary>
    LeftTheWebsite = 9,

    /// <summary>The page's address leads into a private network.</summary>
    PrivateAddress = 10,

    /// <summary>The website's certificate could not be trusted.</summary>
    Insecure = 11,

    /// <summary>The page redirects too many times, or in a circle.</summary>
    TooManyRedirects = 12,
}

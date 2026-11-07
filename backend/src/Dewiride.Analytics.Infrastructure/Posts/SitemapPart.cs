namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// One file of a website's sitemap: a list of pages.
/// </summary>
/// <remarks>
/// A sitemap that is a single list has one of these, and one that is an index has one for each list
/// it names. They exist because a list that answers "unchanged" has to go on vouching for the pages
/// it listed last time, and a generation stamped only when a whole list was read is how a post that
/// has stopped being listed is told apart from one that was simply not reached.
/// </remarks>
public sealed class SitemapPart
{
    /// <summary>Identifies the part.</summary>
    public required Guid Id { get; init; }

    /// <summary>The website.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>The part's own address.</summary>
    public required string Address { get; init; }

    /// <summary>Whether the part is recognised as the one holding the website's posts.</summary>
    public bool IsPosts { get; init; }

    /// <summary>Stamped each time the whole part has been read.</summary>
    public Guid? Generation { get; init; }

    /// <summary>The entity tag the part answered with, to ask whether it has changed.</summary>
    public string? Etag { get; init; }

    /// <summary>When the part said it last changed, to ask whether it has changed since.</summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>When the part was last asked for, or nothing where it is waiting to be.</summary>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>What came of asking for it.</summary>
    public SitemapPartOutcome? Outcome { get; init; }

    /// <summary>How many pages it listed the last time all of it was read.</summary>
    public int Entries { get; init; }

    /// <summary>
    /// How many of its entries led off the website the last time all of it was read.
    /// </summary>
    /// <remarks>
    /// Skipped, and kept count of here because a part answering that it has not changed goes on saying
    /// what it said, and how the reading went has to go on saying so too.
    /// </remarks>
    public int OffSiteEntries { get; init; }

    /// <summary>
    /// How many of its entries were told apart only by a query string the last time all of it was read.
    /// </summary>
    /// <remarks>
    /// Skipped, because the collector keeps only a page's path, and every one of them would be counted
    /// as the same page.
    /// </remarks>
    public int QueryEntries { get; init; }
}

/// <summary>What came of asking for one part of a sitemap.</summary>
/// <remarks>
/// Everything past the first two reads as the <see cref="Application.Posts.SitemapProblem"/> of the
/// same name when a reading says what got in its way.
/// </remarks>
public enum SitemapPartOutcome
{
    /// <summary>All of it was read.</summary>
    Read = 1,

    /// <summary>It has not changed since it was last read.</summary>
    Unchanged = 2,

    /// <summary>The website did not answer.</summary>
    Unreachable = 3,

    /// <summary>There is nothing at its address.</summary>
    NotFound = 4,

    /// <summary>The website would not let the reader in.</summary>
    TurnedAway = 5,

    /// <summary>The website answered that something had gone wrong at its end.</summary>
    ServerError = 6,

    /// <summary>The website's certificate could not be trusted.</summary>
    Insecure = 7,

    /// <summary>What is at its address is not a list of pages.</summary>
    NotASitemap = 8,

    /// <summary>It is larger than is read.</summary>
    TooLarge = 9,

    /// <summary>It redirected off the website.</summary>
    LeftTheWebsite = 10,

    /// <summary>It redirects too many times, or in a circle.</summary>
    TooManyRedirects = 11,

    /// <summary>Its address leads into a private network.</summary>
    PrivateAddress = 12,

    /// <summary>The website answered too slowly for all of it to be read.</summary>
    TooSlow = 13,

    /// <summary>It lists more than is read.</summary>
    TooMany = 14,

    /// <summary>The website's robots.txt asks for it not to be read.</summary>
    RobotsDisallowed = 15,

    /// <summary>The website's robots.txt redirects somewhere it can not be followed, so nothing may be read.</summary>
    RobotsUnreadable = 16,
}

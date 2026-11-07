namespace Dewiride.Analytics.Application.Posts;

/// <summary>
/// Where reading a website's sitemap stands.
/// </summary>
/// <param name="Address">The sitemap being read, or nothing where none is named.</param>
/// <param name="State">How the reading stands.</param>
/// <param name="Problem">What got in the way, where anything did.</param>
/// <param name="ReadAt">When the last reading finished, however it finished.</param>
/// <param name="SucceededAt">When a reading last got through the sitemap.</param>
/// <param name="Posts">Pages it lists that are known to be posts.</param>
/// <param name="Dated">Those posts whose page said when it was published.</param>
/// <param name="Reading">Pages it lists that have not been read yet.</param>
/// <param name="TurnedAway">
/// Pages it lists that the website turned the reader away from the last time they were asked for, by
/// refusing it or by its robots.txt.
/// </param>
public sealed record SitemapReport(
    string? Address,
    SitemapState State,
    SitemapProblem? Problem,
    DateTimeOffset? ReadAt,
    DateTimeOffset? SucceededAt,
    int Posts,
    int Dated,
    int Reading,
    int TurnedAway);

/// <summary>How reading a website's sitemap stands.</summary>
public enum SitemapState
{
    /// <summary>Nobody has named a sitemap for this website.</summary>
    None = 1,

    /// <summary>This installation does not read sitemaps. What was read before is still reported.</summary>
    Off = 2,

    /// <summary>The sitemap has been named and has not finished being read for the first time.</summary>
    Waiting = 3,

    /// <summary>All of the sitemap was read.</summary>
    Read = 4,

    /// <summary>Some of the sitemap was read, and <see cref="SitemapProblem"/> says what stopped the rest.</summary>
    PartlyRead = 5,

    /// <summary>None of the sitemap could be read, and <see cref="SitemapProblem"/> says why.</summary>
    Failed = 6,
}

/// <summary>What got in the way of reading a website's sitemap.</summary>
public enum SitemapProblem
{
    /// <summary>The website did not answer.</summary>
    Unreachable = 1,

    /// <summary>There is nothing at the address.</summary>
    NotFound = 2,

    /// <summary>The website answered and would not let the reader in.</summary>
    TurnedAway = 3,

    /// <summary>The website answered that something had gone wrong at its end.</summary>
    ServerError = 4,

    /// <summary>The website's certificate could not be trusted.</summary>
    Insecure = 5,

    /// <summary>What is at the address is not a sitemap.</summary>
    NotASitemap = 6,

    /// <summary>The sitemap is larger than is read.</summary>
    TooLarge = 7,

    /// <summary>The sitemap leads to addresses that are not on the website.</summary>
    LeftTheWebsite = 8,

    /// <summary>The address redirects too many times, or in a circle.</summary>
    TooManyRedirects = 9,

    /// <summary>The address leads into a private network, which is never read.</summary>
    PrivateAddress = 10,

    /// <summary>The website answered too slowly for all of it to be read this time.</summary>
    TooSlow = 11,

    /// <summary>The sitemap lists more than is read.</summary>
    TooMany = 12,

    /// <summary>The posts' addresses differ only after a question mark, so no two can be told apart.</summary>
    AddressesWithQueries = 13,

    /// <summary>The website's robots.txt asks not to be read.</summary>
    RobotsDisallowed = 14,

    /// <summary>The website's robots.txt redirects somewhere it can not be followed, so nothing may be read.</summary>
    RobotsUnreadable = 15,
}

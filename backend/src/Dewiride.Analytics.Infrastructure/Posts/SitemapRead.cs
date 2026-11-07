using Dewiride.Analytics.Application.Posts;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// How far reading one website's sitemap has got.
/// </summary>
/// <remarks>
/// <para>
/// One row for each website with a sitemap, describing one address. It is apart from the site's own
/// row because the engine writes it constantly and from any number of processes, and nothing it
/// holds is anything a person chose: a settings save and a reading never touch the same row, and the
/// collector's cached copy of a site never carries it.
/// </para>
/// <para>
/// Every member only needs initialising, here and on the other tables of a reading. They are changed
/// by set-based statements that name the row and its claim together, never through the change
/// tracker, because a write has to come to nothing once its claim is lost.
/// </para>
/// </remarks>
public sealed class SitemapRead
{
    /// <summary>
    /// Longest entity tag kept, for the address and for the parts and pages it leads to.
    /// </summary>
    /// <remarks>
    /// A real one is a hash or a version. This one arrives in a header written by whoever answers, so
    /// whatever stores one leaves out anything longer, which costs no more than asking for that page
    /// or part in full next time.
    /// </remarks>
    public const int MaxEtagLength = 256;

    /// <summary>The website.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>The address this row describes.</summary>
    public required string Address { get; init; }

    /// <summary>
    /// The host of that address, in lower-case ASCII.
    /// </summary>
    /// <remarks>
    /// Kept apart from the address so that a claim can see whether the same host is already being
    /// read. Domains are not unique across an installation, and the limit on how often a website is
    /// asked for anything is the website's, however many rows name it.
    /// </remarks>
    public required string Host { get; init; }

    /// <summary>Identifies the one claim on this row, which every write the reader makes is fenced on.</summary>
    public Guid? ClaimToken { get; init; }

    /// <summary>
    /// When the website may next be taken by a reader, by the database's clock: when a claim runs out, or,
    /// for a moment after one has been let go of, when the gap after its last request is over.
    /// </summary>
    public DateTimeOffset? ClaimedUntil { get; init; }

    /// <summary>The earliest this row may be claimed again.</summary>
    public required DateTimeOffset NextPassAt { get; init; }

    /// <summary>When the sitemap is next read from the top.</summary>
    public required DateTimeOffset NextCycleAt { get; init; }

    /// <summary>When the reading in progress began, or nothing where none is.</summary>
    public DateTimeOffset? CycleStartedAt { get; init; }

    /// <summary>How the reading stands.</summary>
    public required SitemapState State { get; init; }

    /// <summary>What got in the way, where anything did.</summary>
    public SitemapProblem? Problem { get; init; }

    /// <summary>When the last reading finished, however it finished.</summary>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>When a reading last got through the sitemap.</summary>
    public DateTimeOffset? SucceededAt { get; init; }

    /// <summary>What the address held the last time it was read.</summary>
    public SitemapRootKind? RootKind { get; init; }

    /// <summary>The entity tag the address answered with, to ask whether it has changed.</summary>
    public string? RootEtag { get; init; }

    /// <summary>When the address said it last changed, to ask whether it has changed since.</summary>
    public DateTimeOffset? RootLastModified { get; init; }

    /// <summary>
    /// What was wrong with the list of parts the address held, the last time it was read.
    /// </summary>
    /// <remarks>
    /// Kept for as long as the address answers that nothing has changed, since the list it describes is
    /// still the one in force.
    /// </remarks>
    public SitemapProblem? RootProblem { get; init; }

    /// <summary>When the row last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>What a sitemap's own address held.</summary>
public enum SitemapRootKind
{
    /// <summary>A list of further sitemaps.</summary>
    Index = 1,

    /// <summary>A list of pages.</summary>
    UrlSet = 2,
}

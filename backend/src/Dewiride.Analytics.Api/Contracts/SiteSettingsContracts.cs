namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// A website's own settings.
/// </summary>
/// <param name="DisplayName">Name it is shown under.</param>
/// <param name="TimeZoneId">IANA time zone its days are counted in.</param>
/// <param name="CaptureClicks">
/// Whether the controls visitors operate are recorded. What is kept of a press is the words on the
/// control and where it pointed — on a page that shows visitors their own messages, names or
/// accounts those words are theirs — and nothing is ever read out of a text area.
/// </param>
/// <param name="SitemapAddress">
/// Where the website lists its posts, in the one spelling it is kept in, or nothing where no sitemap
/// is named.
/// </param>
public sealed record SiteSettingsResponse(
    string DisplayName,
    string TimeZoneId,
    bool CaptureClicks,
    string? SitemapAddress);

/// <summary>
/// A change to a website's settings.
/// </summary>
/// <remarks>
/// Every setting is optional and absent means unchanged, so a caller that knows about one setting
/// cannot reset another it has never heard of by leaving it out.
/// </remarks>
public sealed record UpdateSiteSettingsRequest
{
    /// <summary>The name to show the website under.</summary>
    public string? DisplayName { get; init; }

    /// <summary>IANA time zone its days should be counted in from now on.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Whether the controls visitors operate should be recorded from now on.</summary>
    public bool? CaptureClicks { get; init; }

    /// <summary>
    /// Where the website lists its posts.
    /// </summary>
    /// <remarks>
    /// An empty address stops it being read and forgets the posts read from it. Absent and null both
    /// mean unchanged, as for every other setting, so an empty string is what asks for that.
    /// </remarks>
    public string? SitemapAddress { get; init; }
}

/// <summary>
/// How reading a website's sitemap is going.
/// </summary>
/// <param name="Address">The sitemap being read, or nothing where none is named.</param>
/// <param name="State">
/// <c>none</c>, <c>off</c>, <c>waiting</c>, <c>read</c>, <c>partly-read</c> or <c>failed</c>.
/// </param>
/// <param name="Problem">
/// What got in the way, where anything did, as one of a closed set of words the dashboard writes its
/// own sentence for.
/// </param>
/// <param name="ReadAt">When the last reading finished, however it finished.</param>
/// <param name="SucceededAt">When a reading last got through the sitemap.</param>
/// <param name="Posts">Pages it lists that are known to be posts.</param>
/// <param name="Dated">Those posts whose page said when it was published.</param>
/// <param name="Reading">Pages it lists that have not been read yet.</param>
/// <param name="TurnedAway">
/// Pages it lists that the website turned the reader away from the last time they were asked for.
/// </param>
public sealed record SitemapStatusResponse(
    string? Address,
    string State,
    string? Problem,
    DateTimeOffset? ReadAt,
    DateTimeOffset? SucceededAt,
    int Posts,
    int Dated,
    int Reading,
    int TurnedAway);

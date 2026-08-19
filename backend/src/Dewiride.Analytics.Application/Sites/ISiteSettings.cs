namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Reads and changes the properties of one site.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ISiteCatalog"/>, which answers the collector out of a cache on every
/// single report and must never load an aggregate. This one loads the site itself and writes to
/// it, and is used when somebody opens a panel rather than when a page is read.
/// </remarks>
public interface ISiteSettings
{
    /// <summary>
    /// Reads a site's properties.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The properties, or <see langword="null"/> when no such site exists.</returns>
    Task<SiteProfile?> DescribeAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes a site's properties.
    /// </summary>
    /// <remarks>
    /// Everything the amendment leaves out is left as it stands, so a caller that knows about one
    /// property cannot reset another it has never heard of by not mentioning it.
    /// </remarks>
    /// <param name="siteId">The site.</param>
    /// <param name="amendment">The parts to change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it, and the properties as they now stand where anything was applied.</returns>
    Task<SiteChange> ApplyAsync(Guid siteId, SiteAmendment amendment, CancellationToken cancellationToken);
}

/// <summary>
/// The properties of one site.
/// </summary>
/// <param name="DisplayName">Name it is shown under.</param>
/// <param name="TimeZoneId">IANA time zone its days are counted in.</param>
/// <param name="CaptureClicks">Whether the controls visitors operate are recorded.</param>
public readonly record struct SiteProfile(string DisplayName, string TimeZoneId, bool CaptureClicks);

/// <summary>
/// A change to a site's properties.
/// </summary>
/// <remarks>
/// Every part is optional, and nothing distinguishes "leave this alone" from "set it to what it
/// already is" — both leave the stored value where it was.
/// </remarks>
/// <param name="DisplayName">The name to show it under, or nothing to leave it as it is.</param>
/// <param name="TimeZoneId">The zone to count its days in, or nothing to leave it as it is.</param>
/// <param name="CaptureClicks">
/// Whether to record the controls visitors operate, or nothing to leave it as it is.
/// </param>
public readonly record struct SiteAmendment(string? DisplayName, string? TimeZoneId, bool? CaptureClicks);

/// <summary>What came of trying to change a site's properties.</summary>
public enum SiteChangeOutcome
{
    /// <summary>Everything the amendment named was applied.</summary>
    Applied = 1,

    /// <summary>There is no such site.</summary>
    NoSuchSite = 2,

    /// <summary>The name is not one a site can be shown under.</summary>
    NameRejected = 3,

    /// <summary>The time zone is not one this installation knows.</summary>
    TimeZoneRejected = 4,
}

/// <summary>
/// The result of changing a site's properties.
/// </summary>
/// <remarks>
/// Nothing is stored unless the whole amendment is acceptable, so a refusal leaves the site
/// exactly as it was rather than half-changed.
/// </remarks>
/// <param name="Outcome">What came of it.</param>
/// <param name="Profile">The properties as they now stand, where the change was applied.</param>
public readonly record struct SiteChange(SiteChangeOutcome Outcome, SiteProfile? Profile);

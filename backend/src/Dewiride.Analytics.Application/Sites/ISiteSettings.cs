namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Reads and changes what a site collects.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ISiteCatalog"/>, which answers the collector out of a cache on every
/// single report and must never load an aggregate. This one loads the site itself and writes to
/// it, and is used when somebody opens a panel rather than when a page is read.
/// </remarks>
public interface ISiteSettings
{
    /// <summary>
    /// Reads what a site collects.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings, or <see langword="null"/> when no such site exists.</returns>
    Task<CollectionSettings?> DescribeAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes what a site collects.
    /// </summary>
    /// <param name="siteId">The site.</param>
    /// <param name="settings">What it should collect from now on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings as they now stand, or <see langword="null"/> when no such site exists.</returns>
    Task<CollectionSettings?> ApplyAsync(
        Guid siteId,
        CollectionSettings settings,
        CancellationToken cancellationToken);
}

/// <summary>
/// What a site collects, as far as its owner decides it.
/// </summary>
/// <param name="CaptureClicks">Whether the controls visitors operate are recorded.</param>
public readonly record struct CollectionSettings(bool CaptureClicks);

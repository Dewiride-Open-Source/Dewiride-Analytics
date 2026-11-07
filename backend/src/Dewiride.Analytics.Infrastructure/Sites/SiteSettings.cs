using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Reads and changes the properties of one site.
/// </summary>
/// <remarks>
/// <para>
/// A save throws away what the collector has cached for that site. Without it the collector would
/// keep acting on the old setting for up to a minute, which on the way from on to off means a
/// panel saying nothing is being recorded while records are still arriving.
/// </para>
/// <para>
/// A save that changes the sitemap also forgets how reading the old one went. That row is what every
/// write a reading makes is fenced on, so deleting it is also what stops a reading still under way,
/// on any engine, from writing anything further about an address nobody wants read any more.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="cache">Cache the collector resolves sites through.</param>
internal sealed class SiteSettings(ControlPlaneDbContext database, HybridCache cache) : ISiteSettings
{
    /// <inheritdoc />
    public async Task<SiteProfile?> DescribeAsync(Guid siteId, CancellationToken cancellationToken) =>
        await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == siteId)
            .Select(site => new SiteProfile(
                site.DisplayName,
                site.TimeZoneId,
                site.CaptureClicks,
                site.SitemapAddress))
            .Cast<SiteProfile?>()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>
    /// The aggregate decides what an acceptable name and an acceptable zone are, and this turns
    /// its refusal into an outcome the caller can answer with. Checking either a second time here
    /// would be a second definition, and the two would part company the first time one of them
    /// changed.
    /// </remarks>
    public async Task<SiteChange> ApplyAsync(
        Guid siteId,
        SiteAmendment amendment,
        CancellationToken cancellationToken)
    {
        var site = await database.Sites
            .FirstOrDefaultAsync(candidate => candidate.Id == siteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return new SiteChange(SiteChangeOutcome.NoSuchSite, null);
        }

        var sitemapBefore = site.SitemapAddress;
        var refusal = Amend(site, amendment);

        if (refusal is { } rejected)
        {
            return new SiteChange(rejected, null);
        }

        if (string.Equals(sitemapBefore, site.SitemapAddress, StringComparison.Ordinal))
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SaveWithANewSitemapAsync(site, cancellationToken).ConfigureAwait(false);
        }

        await cache.RemoveAsync(CachedSiteCatalog.CacheKey(siteId), cancellationToken).ConfigureAwait(false);

        return new SiteChange(
            SiteChangeOutcome.Applied,
            new SiteProfile(site.DisplayName, site.TimeZoneId, site.CaptureClicks, site.SitemapAddress));
    }

    /// <summary>
    /// Saves a change that names a different sitemap, or none, and forgets how the old one was read.
    /// </summary>
    /// <remarks>
    /// One transaction, so that nothing can find the new address beside what was read from the old
    /// one, or the old reading gone and the address not yet changed. A different address keeps the
    /// posts, which are keyed by their paths: the new sitemap lists them again or a complete reading of
    /// it unlists them. No address at all has nothing that could ever do either, so they go too.
    /// </remarks>
    /// <param name="site">The amended site, not yet saved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once everything is committed.</returns>
    private async Task SaveWithANewSitemapAsync(Site site, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await database.SitemapReads
            .Where(read => read.SiteId == site.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await database.SitemapParts
            .Where(part => part.SiteId == site.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (site.SitemapAddress is null)
        {
            await database.SitePosts
                .Where(post => post.SiteId == site.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the parts of an amendment that are present, or names the first one that cannot be.
    /// </summary>
    /// <remarks>
    /// The name is settled before the zone, so somebody who typed a name too long for a site and
    /// picked a zone this installation has never heard of is told about the name — the first thing
    /// actually wrong — rather than about whichever refusal happened to be looked for first. A sitemap
    /// is settled after both, as it comes after both on the panel. Nothing reaches the database until
    /// every part has been accepted, because the change is abandoned rather than saved when one is not.
    /// </remarks>
    /// <param name="site">The site to amend.</param>
    /// <param name="amendment">The parts to change.</param>
    /// <returns>The refusal, or nothing where the whole amendment was applied.</returns>
    private static SiteChangeOutcome? Amend(Site site, SiteAmendment amendment)
    {
        if (amendment.DisplayName is { } displayName && !TrySet(() => site.SetDisplayName(displayName)))
        {
            return SiteChangeOutcome.NameRejected;
        }

        if (amendment.TimeZoneId is { } timeZoneId && !TrySet(() => site.SetTimeZone(timeZoneId)))
        {
            return SiteChangeOutcome.TimeZoneRejected;
        }

        if (amendment.SitemapAddress is { } sitemapAddress && !TrySet(() => site.SetSitemapAddress(sitemapAddress)))
        {
            return SiteChangeOutcome.SitemapRejected;
        }

        if (amendment.CaptureClicks is { } captureClicks)
        {
            site.SetClickCapture(captureClicks);
        }

        return null;
    }

    /// <summary>
    /// Runs a change the aggregate may refuse.
    /// </summary>
    /// <param name="change">The change to attempt.</param>
    /// <returns>Whether the aggregate accepted it.</returns>
    private static bool TrySet(Action change)
    {
        try
        {
            change();

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

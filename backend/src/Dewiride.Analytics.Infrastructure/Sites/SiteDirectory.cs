using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Lists a person's sites from the control-plane database.
/// </summary>
/// <remarks>
/// Uncached, unlike the catalogue the collector uses. The dashboard asks this once when it loads
/// rather than once per beacon, and holding a person's list of sites in a cache is how somebody
/// keeps seeing a site after their access to it was taken away.
/// </remarks>
/// <param name="database">Control-plane database.</param>
public sealed class SiteDirectory(ControlPlaneDbContext database) : ISiteDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteMembershipView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.SiteMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                database.Sites.AsNoTracking(),
                membership => membership.SiteId,
                site => site.Id,
                (membership, site) => new { Site = site, membership.Role })
            // Ordered before the result is shaped, not after. Sorting on a property of a value
            // the query has already constructed is not something the database can be asked to do,
            // and the whole table would be read back to do it here instead.
            .OrderBy(row => row.Site.DisplayName)
            .ThenBy(row => row.Site.Id)
            .Select(row => new SiteMembershipView(
                row.Site.Id,
                row.Site.Domain,
                row.Site.DisplayName,
                row.Site.TimeZoneId,
                row.Role))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

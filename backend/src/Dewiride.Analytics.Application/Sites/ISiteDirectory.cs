using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Lists the sites a signed-in person may work with.
/// </summary>
/// <remarks>
/// Separate from <see cref="ISiteCatalog"/> on purpose. The catalogue answers the collector's
/// question — "does this site identifier exist, and what may it accept?" — for an anonymous
/// caller, and is cached hard because it sits on the busiest path in the product. This answers
/// the dashboard's question, which is about a particular person, must never be cached across
/// people, and has to reflect a membership change immediately.
/// </remarks>
public interface ISiteDirectory
{
    /// <summary>
    /// Lists the sites a person holds a role on, ordered by name.
    /// </summary>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Their sites, which may be none.</returns>
    Task<IReadOnlyList<SiteMembershipView>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// A site as it appears to one person, together with what they may do with it.
/// </summary>
/// <param name="Id">Identity of the site, and the identifier its tracker reports under.</param>
/// <param name="Domain">Primary hostname.</param>
/// <param name="DisplayName">Name shown in the dashboard.</param>
/// <param name="TimeZoneId">IANA time zone the site's days are cut in.</param>
/// <param name="Role">What this person may do with it.</param>
public readonly record struct SiteMembershipView(
    Guid Id,
    string Domain,
    string DisplayName,
    string TimeZoneId,
    SiteRole Role);

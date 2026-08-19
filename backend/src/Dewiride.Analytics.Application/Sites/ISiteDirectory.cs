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

    /// <summary>
    /// Adds a site, owned by the person adding it.
    /// </summary>
    /// <remarks>
    /// Whether the person may add one at all is decided here rather than by the caller, because it
    /// is a question about what they already hold: a site joins the organisation they already own
    /// a site in, and somebody who owns none has no organisation for it to join.
    /// </remarks>
    /// <param name="userId">The person adding it.</param>
    /// <param name="site">What is being added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it, and the site where one was added.</returns>
    Task<SiteAddition> AddAsync(Guid userId, NewSite site, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a site, along with everything measured for it.
    /// </summary>
    /// <remarks>
    /// Whether the person may remove this particular one at all is decided here for the same
    /// reason it is on <see cref="AddAsync"/>: it is a question about what they already hold. A
    /// new site joins the organisation they already own one in, so removing the last site they
    /// own would leave them with nowhere to put another — able to give up what they measure but
    /// never to begin again. Somebody's last owned site is therefore kept.
    /// </remarks>
    /// <param name="userId">The person removing it.</param>
    /// <param name="siteId">The site to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<SiteRemoval> RemoveAsync(Guid userId, Guid siteId, CancellationToken cancellationToken);
}

/// <summary>
/// A site somebody is asking for.
/// </summary>
/// <param name="Domain">
/// Primary hostname, as it was typed. Normalised and checked where the site is built, so nothing
/// here is trusted.
/// </param>
/// <param name="TimeZoneId">IANA time zone its days should be cut in.</param>
public readonly record struct NewSite(string Domain, string TimeZoneId);

/// <summary>What came of trying to add a site.</summary>
public enum SiteAdditionOutcome
{
    /// <summary>It was added, and the person adding it owns it.</summary>
    Added = 1,

    /// <summary>
    /// They own no site, so there is no organisation for a new one to join. Adding a site decides
    /// what an installation collects and who can see it, which is an owner's decision.
    /// </summary>
    NotAllowed = 2,

    /// <summary>A site for that hostname is already being measured here.</summary>
    AlreadyMeasured = 3,

    /// <summary>The hostname or the time zone is not one a site can be built from.</summary>
    Unusable = 4,
}

/// <summary>
/// The result of adding a site.
/// </summary>
/// <param name="Outcome">What came of it.</param>
/// <param name="Added">The site, where one was added.</param>
public readonly record struct SiteAddition(SiteAdditionOutcome Outcome, SiteMembershipView? Added);

/// <summary>What came of trying to remove a site.</summary>
public enum SiteRemovalOutcome
{
    /// <summary>It is gone, and so is everything that was measured for it.</summary>
    Removed = 1,

    /// <summary>There is no such site.</summary>
    NoSuchSite = 2,

    /// <summary>
    /// It is the only site this person owns, and removing it would leave them unable to add
    /// another.
    /// </summary>
    OnlyOne = 3,
}

/// <summary>
/// The result of removing a site.
/// </summary>
/// <param name="Outcome">What came of it.</param>
public readonly record struct SiteRemoval(SiteRemovalOutcome Outcome);

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

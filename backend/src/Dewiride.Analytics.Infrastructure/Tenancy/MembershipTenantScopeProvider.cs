using System.Security.Claims;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Tenancy;

/// <summary>
/// Resolves authorisation scope from what the caller has actually been granted.
/// </summary>
/// <remarks>
/// <para>
/// Two things can give somebody a role on a site: a grant made on that site, and a standing held
/// in the organisation that owns it. Both are read, and the wider of the two applies — an owner of
/// the account who was never named on one of its websites still owns that website, and somebody
/// granted editing on a single site does not lose it by joining the account as a reader.
/// </para>
/// <para>
/// A standing in the organisation is what makes a team workable at all: a person added to an
/// account expects to see the sites it owns, including the ones added after they joined, without
/// anybody remembering to grant them each one.
/// </para>
/// <para>
/// One implementation, both editions. An installation somebody runs themselves has one
/// organisation and the hosted service has many, but the question asked of a single site is the
/// same either way — which organisation owns it, and what does this caller hold in that one. An
/// edition whose authorisation was weaker than the other while running the same screens would be a
/// security advisory rather than a feature difference, and the surest way to keep them the same is
/// for there to be nothing to keep in step.
/// </para>
/// <para>
/// A site that does not exist and a site the caller has no role on are answered identically, and
/// must stay that way. Distinguishing them would turn the address of any dashboard read into a way
/// of discovering which site identifiers belong to somebody else's account.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="principalAccessor">Supplies the signed-in user.</param>
public sealed class MembershipTenantScopeProvider(
    ControlPlaneDbContext database,
    ICurrentPrincipalAccessor principalAccessor) : ITenantScopeProvider
{
    /// <inheritdoc />
    public async Task<TenantScope?> ResolveAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var userId = principalAccessor.GetUserId();

        if (userId is null)
        {
            return null;
        }

        // One round trip rather than three. The two memberships are correlated sub-queries so that
        // a site with neither still produces a row, which is what lets the absent-role and
        // absent-site cases converge on the same answer below.
        var found = await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == siteId)
            .Select(site => new
            {
                site.OrganizationId,
                site.TimeZoneId,
                Granted = database.SiteMemberships
                    .Where(membership => membership.SiteId == site.Id && membership.UserId == userId.Value)
                    .Select(membership => (SiteRole?)membership.Role)
                    .FirstOrDefault(),
                Standing = database.OrganizationMemberships
                    .Where(membership =>
                        membership.OrganizationId == site.OrganizationId && membership.UserId == userId.Value)
                    .Select(membership => (OrganizationRole?)membership.Role)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        var role = OrganizationRoles.Widest(found.Granted, found.Standing);

        return role is null
            ? null
            : new TenantScope(siteId, found.OrganizationId, role.Value, found.TimeZoneId);
    }
}

/// <summary>
/// Supplies the identity of the caller on the current request.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    /// <summary>
    /// Returns the signed-in user's identifier, or <see langword="null"/> when the request is
    /// unauthenticated.
    /// </summary>
    Guid? GetUserId();
}

/// <summary>
/// Reads the caller's identity from the ambient <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// The principal is fetched when it is asked for rather than captured when this is built. The
/// host supplies it from the request it is serving, and a request-scoped service can be
/// constructed before the identity on that request has been established.
/// </remarks>
/// <param name="principal">Supplies the principal the current request is running as.</param>
public sealed class ClaimsPrincipalAccessor(Func<ClaimsPrincipal?> principal) : ICurrentPrincipalAccessor
{
    /// <inheritdoc />
    public Guid? GetUserId()
    {
        var value = principal()?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

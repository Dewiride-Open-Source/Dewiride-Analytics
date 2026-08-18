using System.Security.Claims;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Tenancy;

/// <summary>
/// Resolves authorisation scope for a self-hosted install, where one organisation owns everything.
/// </summary>
/// <remarks>
/// Membership is still checked rather than assumed. A self-hosted install can have several people
/// with different roles, and skipping the check because "there is only one organisation" would
/// make the Community edition's authorisation weaker than the hosted edition's while running the
/// same screens — the kind of divergence that produces a security advisory rather than a feature
/// difference.
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="principalAccessor">Supplies the signed-in user.</param>
public sealed class SingleTenantScopeProvider(
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

        var grant = await database.Sites
            .AsNoTracking()
            .Where(site => site.Id == siteId)
            .Join(
                database.SiteMemberships.AsNoTracking().Where(m => m.UserId == userId.Value),
                site => site.Id,
                membership => membership.SiteId,
                (site, membership) => new { site.OrganizationId, site.TimeZoneId, membership.Role })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return grant is null
            ? null
            : new TenantScope(siteId, grant.OrganizationId, grant.Role, grant.TimeZoneId);
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

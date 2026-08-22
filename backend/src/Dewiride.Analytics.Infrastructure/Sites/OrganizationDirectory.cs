using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// Reads and changes an account and the people in it, from the control-plane database.
/// </summary>
/// <param name="database">Control-plane database.</param>
public sealed class OrganizationDirectory(ControlPlaneDbContext database) : IOrganizationDirectory
{
    /// <summary>Longest name an organisation may be shown under.</summary>
    private const int MaxNameLength = 200;

    /// <inheritdoc />
    public async Task<OrganizationStanding?> StandingForAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var held = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .OrderByDescending(membership => membership.Role)
            .ThenBy(membership => membership.GrantedAt)
            .ThenBy(membership => membership.OrganizationId)
            .Select(membership => new { membership.OrganizationId, membership.Role })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return held is null ? null : new OrganizationStanding(held.OrganizationId, held.Role);
    }

    /// <inheritdoc />
    public async Task<OrganizationAccount?> DescribeAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var name = await database.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (name is null)
        {
            return null;
        }

        var people = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.OrganizationId == organizationId)
            .Join(
                database.Users.AsNoTracking(),
                membership => membership.UserId,
                user => user.Id,
                (membership, user) => new { User = user, membership.Role, membership.GrantedAt })
            // Owners first, then by the name they are shown under, which is the order somebody
            // scanning a list of people is looking in.
            .OrderByDescending(row => row.Role)
            .ThenBy(row => row.User.DisplayName)
            .ThenBy(row => row.User.Id)
            .Select(row => new OrganizationPerson(
                row.User.Id,
                row.User.Email ?? string.Empty,
                row.User.DisplayName ?? row.User.Email ?? string.Empty,
                row.Role,
                row.GrantedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OrganizationAccount(organizationId, name, people);
    }

    /// <inheritdoc />
    public async Task<OrganizationRenameOutcome> RenameAsync(
        Guid organizationId,
        string name,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            return OrganizationRenameOutcome.NameRejected;
        }

        var organization = await database.Organizations
            .FirstOrDefaultAsync(candidate => candidate.Id == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (organization is null)
        {
            return OrganizationRenameOutcome.NoSuchOrganization;
        }

        organization.Rename(trimmed);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OrganizationRenameOutcome.Renamed;
    }

    /// <inheritdoc />
    public async Task<PersonChangeOutcome> ChangeStandingAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        CancellationToken cancellationToken)
    {
        var membership = await FindAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);

        if (membership is null)
        {
            return PersonChangeOutcome.NoSuchPerson;
        }

        if (membership.Role == role)
        {
            return PersonChangeOutcome.Changed;
        }

        if (await WouldStrandAsync(membership, cancellationToken).ConfigureAwait(false))
        {
            return PersonChangeOutcome.LastOwner;
        }

        membership.ChangeRole(role);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return PersonChangeOutcome.Changed;
    }

    /// <inheritdoc />
    public async Task<PersonChangeOutcome> RemovePersonAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var membership = await FindAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);

        if (membership is null)
        {
            return PersonChangeOutcome.NoSuchPerson;
        }

        if (await WouldStrandAsync(membership, cancellationToken).ConfigureAwait(false))
        {
            return PersonChangeOutcome.LastOwner;
        }

        // Both writes together. Half of this leaves somebody out of the account but still able to
        // read one of its websites, which is worse than not having started.
        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        database.OrganizationMemberships.Remove(membership);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The grants made on individual sites go with it. A standing in the organisation is what
        // somebody is removed from, and a grant on one of its websites left behind would keep them
        // reading it after everybody had been told they no longer could.
        await database.SiteMemberships
            .Where(grant => grant.UserId == userId)
            .Where(grant => database.Sites
                .Any(site => site.Id == grant.SiteId && site.OrganizationId == organizationId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return PersonChangeOutcome.Changed;
    }

    private async Task<OrganizationMembership?> FindAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.OrganizationMemberships
            .FirstOrDefaultAsync(
                membership => membership.OrganizationId == organizationId && membership.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Whether changing this standing would leave the account with nobody who can manage it.
    /// </summary>
    /// <remarks>
    /// Only ever true of an owner, and only where they are the last one. Removing an owner and
    /// demoting one leave the account in exactly the same state, so both ask this.
    /// </remarks>
    private async Task<bool> WouldStrandAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken) =>
        membership.Role == OrganizationRole.Owner
        && !await database.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                other => other.OrganizationId == membership.OrganizationId
                    && other.Role == OrganizationRole.Owner
                    && other.UserId != membership.UserId,
                cancellationToken)
            .ConfigureAwait(false);
}

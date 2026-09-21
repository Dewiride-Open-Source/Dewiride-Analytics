using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dewiride.Analytics.Infrastructure.Sites;

/// <summary>
/// The standings somebody holds, ordered the one way this product chooses between them.
/// </summary>
/// <remarks>
/// Widest standing first, then the oldest grant, then the organisation's identifier so that two
/// grants made at one instant still order the same way twice. Whether an open organisation or a
/// closed one comes first is the caller's to say — everything that asks where somebody is puts
/// the open one first, and the one act that is about the closed one puts it first — but the rest
/// of the rule lives here, once, so that the account somebody restores is chosen by the same
/// tiebreaks as the account they are in.
/// </remarks>
internal static class HeldStandings
{
    /// <summary>
    /// Every standing somebody holds, joined to its organisation and ordered.
    /// </summary>
    /// <param name="database">Control-plane database.</param>
    /// <param name="userId">The person.</param>
    /// <param name="closedFirst">Whether a closed organisation is preferred to an open one.</param>
    /// <returns>The standings, first choice first.</returns>
    public static IOrderedQueryable<HeldStanding> Of(ControlPlaneDbContext database, Guid userId, bool closedFirst)
    {
        var held = database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                database.Organizations,
                membership => membership.OrganizationId,
                organization => organization.Id,
                (membership, organization) => new HeldStanding
                {
                    OrganizationId = membership.OrganizationId,
                    Role = membership.Role,
                    GrantedAt = membership.GrantedAt,
                    ClosedAt = organization.ClosedAt,
                });

        // Two branches rather than one ordering on a translated conditional: the database sorts on
        // a boolean, and which boolean is the whole difference between the two callers.
        var preferred = closedFirst
            ? held.OrderBy(standing => standing.ClosedAt == null)
            : held.OrderBy(standing => standing.ClosedAt != null);

        return preferred
            .ThenByDescending(standing => standing.Role)
            .ThenBy(standing => standing.GrantedAt)
            .ThenBy(standing => standing.OrganizationId);
    }
}

/// <summary>
/// One standing somebody holds, with what is needed to order it against the others.
/// </summary>
/// <remarks>
/// Built with an initialiser rather than a constructor, because the ordering above is applied
/// after the projection and the database can only be asked to sort on a member it can see being
/// set — which a constructor argument is not.
/// </remarks>
internal sealed record HeldStanding
{
    /// <summary>The organisation.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The standing held in it.</summary>
    public required OrganizationRole Role { get; init; }

    /// <summary>When it was granted.</summary>
    public required DateTimeOffset GrantedAt { get; init; }

    /// <summary>When the organisation was closed, or <see langword="null"/> while it is open.</summary>
    public required DateTimeOffset? ClosedAt { get; init; }
}

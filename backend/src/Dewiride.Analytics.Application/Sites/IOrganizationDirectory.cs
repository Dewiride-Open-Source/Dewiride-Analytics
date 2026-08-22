using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Application.Sites;

/// <summary>
/// Reads and changes the account somebody belongs to, and the people in it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ISiteDirectory"/> because it answers a question about people rather
/// than about websites. A standing in an organisation reaches every site the organisation owns,
/// including the ones added after somebody joined, so it is the grant a team is actually built
/// from and a per-site grant is the exception beside it.
/// </para>
/// <para>
/// Nothing here takes an organisation from the caller without also establishing what standing they
/// hold in it. Every method that changes something is written to be reached only after
/// <see cref="StandingForAsync"/> has said so.
/// </para>
/// </remarks>
public interface IOrganizationDirectory
{
    /// <summary>
    /// The organisation somebody belongs to, and what they may do in it.
    /// </summary>
    /// <remarks>
    /// The one they hold the widest standing in, and the oldest of those where they hold the same
    /// standing in several. Belonging to more than one arrives with the screens for moving between
    /// them, and until then this is a rule rather than a choice anybody makes.
    /// </remarks>
    /// <param name="userId">The person asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Their standing, or <see langword="null"/> where they belong to no organisation.</returns>
    Task<OrganizationStanding?> StandingForAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Describes an organisation and everybody in it.
    /// </summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account, or <see langword="null"/> where there is no such organisation.</returns>
    Task<OrganizationAccount?> DescribeAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Renames an organisation.</summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<OrganizationRenameOutcome> RenameAsync(
        Guid organizationId,
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes what somebody may do in an organisation.
    /// </summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="userId">The person.</param>
    /// <param name="role">The standing to give them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<PersonChangeOutcome> ChangeStandingAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes somebody out of an organisation, along with every grant they held on its sites.
    /// </summary>
    /// <remarks>
    /// Their account is left alone. Being removed from an account is not the same as having one
    /// deleted, and somebody who belongs to another organisation would lose that too.
    /// </remarks>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<PersonChangeOutcome> RemovePersonAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);
}

/// <summary>
/// What somebody may do in the organisation they belong to.
/// </summary>
/// <param name="OrganizationId">The organisation.</param>
/// <param name="Role">Their standing in it.</param>
public readonly record struct OrganizationStanding(Guid OrganizationId, OrganizationRole Role);

/// <summary>
/// An organisation and everybody in it.
/// </summary>
/// <param name="Id">Identity of the organisation.</param>
/// <param name="Name">What it is called.</param>
/// <param name="People">Everybody who belongs to it, by name.</param>
public readonly record struct OrganizationAccount(
    Guid Id,
    string Name,
    IReadOnlyList<OrganizationPerson> People);

/// <summary>
/// Somebody who belongs to an organisation.
/// </summary>
/// <param name="UserId">Their identifier.</param>
/// <param name="EmailAddress">The address they sign in with.</param>
/// <param name="DisplayName">The name shown beside them.</param>
/// <param name="Role">What they may do.</param>
/// <param name="JoinedAt">When they were given it.</param>
public readonly record struct OrganizationPerson(
    Guid UserId,
    string EmailAddress,
    string DisplayName,
    OrganizationRole Role,
    DateTimeOffset JoinedAt);

/// <summary>What came of trying to rename an organisation.</summary>
public enum OrganizationRenameOutcome
{
    /// <summary>It was renamed.</summary>
    Renamed = 1,

    /// <summary>There is no such organisation.</summary>
    NoSuchOrganization = 2,

    /// <summary>The name is not one an organisation can be shown under.</summary>
    NameRejected = 3,
}

/// <summary>What came of trying to change or remove somebody.</summary>
public enum PersonChangeOutcome
{
    /// <summary>It was done.</summary>
    Changed = 1,

    /// <summary>Nobody in this organisation matches.</summary>
    NoSuchPerson = 2,

    /// <summary>
    /// They are the only owner, and an account with no owner is one nobody can manage.
    /// </summary>
    /// <remarks>
    /// Reached by demoting the last owner as well as by removing them, because the two leave the
    /// account in exactly the same state.
    /// </remarks>
    LastOwner = 3,
}

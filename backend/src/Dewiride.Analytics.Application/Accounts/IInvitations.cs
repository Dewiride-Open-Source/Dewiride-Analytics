using Dewiride.Analytics.Domain.Sites;

namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// Asking somebody to join an organisation, and letting them take it up.
/// </summary>
/// <remarks>
/// <para>
/// The only way a second person joins an installation. It is an offer rather than an act: nothing
/// exists in the person's name until they follow the link, so naming an address cannot claim it
/// and cannot be used to find out whether it already has an account here.
/// </para>
/// <para>
/// Whether the caller may do any of this is settled before these are reached, from the standing
/// they hold in the organisation. Nothing here checks it, and nothing here takes an organisation
/// that was not established that way.
/// </para>
/// </remarks>
public interface IInvitations
{
    /// <summary>
    /// Lists the invitations an organisation is waiting on.
    /// </summary>
    /// <remarks>
    /// Only the ones still usable. An invitation that was taken up is a person on the list of
    /// people, and one that was withdrawn or has run out is not something anybody is waiting for.
    /// </remarks>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The invitations, oldest first.</returns>
    Task<IReadOnlyList<PendingInvitation>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks somebody to join, and sends them a link.
    /// </summary>
    /// <remarks>
    /// Sending a second one to the same address renews the first rather than adding to it, which
    /// is what makes the button that sends an invitation again the same button that sent it.
    /// </remarks>
    /// <param name="request">Who to ask, and what to offer them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<InvitationOutcome> InviteAsync(InvitationRequest request, CancellationToken cancellationToken);

    /// <summary>Withdraws an invitation.</summary>
    /// <param name="organizationId">The organisation it was sent from.</param>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it.</returns>
    Task<InvitationWithdrawal> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads what an invitation is for, on behalf of whoever is holding it.
    /// </summary>
    /// <remarks>
    /// The screen that takes an invitation up has to know whether to ask for a password, and
    /// whoever holds the link is the person it was addressed to — so the name of the organisation
    /// and whether their own address already has an account here say nothing they could not find
    /// out by taking it up.
    /// </remarks>
    /// <param name="token">The secret from the link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What it is for, or <see langword="null"/> where the link is spent, withdrawn, expired or
    /// was never one of ours.
    /// </returns>
    Task<InvitationPreview?> PreviewAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Takes an invitation up.
    /// </summary>
    /// <param name="request">The link, and the details for an account where one is needed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What came of it, and who joined where somebody did.</returns>
    Task<Acceptance> AcceptAsync(AcceptanceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// An invitation somebody is asking to send.
/// </summary>
/// <param name="OrganizationId">The organisation to join.</param>
/// <param name="InvitedByUserId">Who is asking.</param>
/// <param name="EmailAddress">The address to send it to, as it was typed.</param>
/// <param name="Role">The standing to offer.</param>
public readonly record struct InvitationRequest(
    Guid OrganizationId,
    Guid InvitedByUserId,
    string EmailAddress,
    OrganizationRole Role);

/// <summary>
/// An invitation an organisation is waiting on.
/// </summary>
/// <param name="Id">Identity of the invitation.</param>
/// <param name="EmailAddress">The address it was sent to.</param>
/// <param name="Role">The standing offered.</param>
/// <param name="InvitedAt">When it was last sent.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public readonly record struct PendingInvitation(
    Guid Id,
    string EmailAddress,
    OrganizationRole Role,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt);

/// <summary>What came of trying to invite somebody.</summary>
public enum InvitationStatus
{
    /// <summary>It was sent.</summary>
    Invited = 1,

    /// <summary>That is not a mailbox anything could be sent to.</summary>
    AddressUnusable = 2,

    /// <summary>They already belong to this organisation.</summary>
    AlreadyHere = 3,
}

/// <summary>
/// The result of inviting somebody.
/// </summary>
/// <param name="Status">What came of it.</param>
/// <param name="Invitation">The invitation, where one was sent.</param>
public readonly record struct InvitationOutcome(InvitationStatus Status, PendingInvitation? Invitation);

/// <summary>What came of trying to withdraw an invitation.</summary>
public enum InvitationWithdrawal
{
    /// <summary>It was withdrawn, and the link stops working.</summary>
    Revoked = 1,

    /// <summary>This organisation is not waiting on that invitation.</summary>
    NoSuchInvitation = 2,
}

/// <summary>
/// What an invitation is for, as the person holding it is shown.
/// </summary>
/// <param name="OrganizationName">The account they have been asked to join.</param>
/// <param name="EmailAddress">The address it was sent to.</param>
/// <param name="NeedsAccount">
/// Whether they still have to choose a name and a password, or already have an account here.
/// </param>
public readonly record struct InvitationPreview(
    string OrganizationName,
    string EmailAddress,
    bool NeedsAccount);

/// <summary>
/// Somebody taking an invitation up.
/// </summary>
/// <param name="Token">The secret from the link.</param>
/// <param name="DisplayName">
/// What to call them, where an account is being created. The address is used when it is left out.
/// </param>
/// <param name="Password">
/// The password to set, where an account is being created. Ignored where one already exists:
/// holding the link proves the mailbox, and nothing about an account that exists is changed by it.
/// </param>
public readonly record struct AcceptanceRequest(string Token, string? DisplayName, string? Password);

/// <summary>What came of taking an invitation up.</summary>
public enum AcceptanceStatus
{
    /// <summary>They belong to the organisation, and an account was created for them.</summary>
    Joined = 1,

    /// <summary>
    /// They belong to the organisation, through the account they already had here.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Joined"/> because what follows differs: somebody who has just
    /// chosen a password is signed in on the spot, and somebody who already had an account signs
    /// in with the password they already have.
    /// </remarks>
    JoinedExisting = 2,

    /// <summary>The link is spent, withdrawn, expired or was never one of ours.</summary>
    LinkNotUsable = 3,

    /// <summary>An account is needed and no password was given.</summary>
    DetailsMissing = 4,

    /// <summary>The password is not one this installation accepts.</summary>
    PasswordRejected = 5,
}

/// <summary>
/// The result of taking an invitation up.
/// </summary>
/// <param name="Status">What came of it.</param>
/// <param name="UserId">Who joined, where somebody did.</param>
/// <param name="Problems">Why the password was refused, where it was.</param>
public readonly record struct Acceptance(
    AcceptanceStatus Status,
    Guid? UserId,
    IReadOnlyList<AccountProblem> Problems)
{
    /// <summary>Reports a link that will not do.</summary>
    /// <returns>The result.</returns>
    public static Acceptance NotUsable() => new(AcceptanceStatus.LinkNotUsable, null, []);
}

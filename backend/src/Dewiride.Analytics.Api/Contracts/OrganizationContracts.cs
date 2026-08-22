namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// The account somebody belongs to, everybody in it, and everybody who has been asked to join.
/// </summary>
/// <param name="Id">Identity of the organisation.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Role">What the caller may do in it.</param>
/// <param name="People">Everybody who belongs to it.</param>
/// <param name="Invitations">
/// Invitations still waiting to be taken up, which only somebody who may manage people is shown.
/// It is empty for everybody else rather than absent, so the shape of the answer never depends on
/// who asked.
/// </param>
public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Role,
    IReadOnlyList<PersonSummary> People,
    IReadOnlyList<InvitationSummary> Invitations);

/// <summary>
/// Somebody who belongs to an account.
/// </summary>
/// <param name="Id">Their identifier.</param>
/// <param name="EmailAddress">The address they sign in with.</param>
/// <param name="DisplayName">The name shown beside them.</param>
/// <param name="Role">What they may do.</param>
/// <param name="JoinedAt">When they were given it.</param>
public sealed record PersonSummary(
    Guid Id,
    string EmailAddress,
    string DisplayName,
    string Role,
    DateTimeOffset JoinedAt);

/// <summary>
/// Somebody who has been asked to join and has not yet.
/// </summary>
/// <param name="Id">Identity of the invitation.</param>
/// <param name="EmailAddress">The address it was sent to.</param>
/// <param name="Role">What they would be able to do.</param>
/// <param name="InvitedAt">When it was last sent.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record InvitationSummary(
    Guid Id,
    string EmailAddress,
    string Role,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt);

/// <summary>What an account is being renamed to.</summary>
public sealed record RenameOrganizationRequest
{
    /// <summary>The name to show.</summary>
    public string? Name { get; init; }
}

/// <summary>What somebody is being asked to do in an account.</summary>
public sealed record ChangeStandingRequest
{
    /// <summary>The standing to give them.</summary>
    public string? Role { get; init; }
}

/// <summary>Who is being asked to join, and as what.</summary>
public sealed record InvitePersonRequest
{
    /// <summary>The address to send the invitation to.</summary>
    public string? EmailAddress { get; init; }

    /// <summary>The standing to offer.</summary>
    public string? Role { get; init; }
}

/// <summary>The name somebody wants to be shown under.</summary>
public sealed record RenameAccountRequest
{
    /// <summary>The name to show.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// A password being replaced.
/// </summary>
/// <remarks>
/// The current one is asked for even though the caller is signed in. A session left open on a
/// shared machine is the case this guards against, and it is the one case where being signed in is
/// not evidence of being the account holder.
/// </remarks>
public sealed record ChangePasswordRequest
{
    /// <summary>The password they have now.</summary>
    public string? CurrentPassword { get; init; }

    /// <summary>The password to set.</summary>
    public string? NewPassword { get; init; }
}

/// <summary>An invitation somebody is holding.</summary>
public sealed record InvitationTokenRequest
{
    /// <summary>The secret from the link.</summary>
    public string? Token { get; init; }
}

/// <summary>
/// What an invitation is for, as the person holding it is shown.
/// </summary>
/// <param name="OrganizationName">The account they have been asked to join.</param>
/// <param name="EmailAddress">The address it was sent to.</param>
/// <param name="NeedsAccount">
/// Whether they still have to choose a name and a password, or already have an account here.
/// </param>
public sealed record InvitationPreviewResponse(
    string OrganizationName,
    string EmailAddress,
    bool NeedsAccount);

/// <summary>Somebody taking an invitation up.</summary>
public sealed record AcceptInvitationRequest
{
    /// <summary>The secret from the link.</summary>
    public string? Token { get; init; }

    /// <summary>What to call them, where an account is being created.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The password to set, where an account is being created.</summary>
    public string? Password { get; init; }
}

/// <summary>
/// What came of taking an invitation up.
/// </summary>
/// <param name="SignedIn">
/// Whether they are now signed in. Somebody who has just chosen a password is; somebody who
/// already had an account here signs in with the password they already have.
/// </param>
/// <param name="User">Who they are, where they are signed in.</param>
/// <param name="Token">Value to send back in the <c>X-Csrf-Token</c> header from now on.</param>
public sealed record JoinResponse(bool SignedIn, SignedInUser? User, string Token);

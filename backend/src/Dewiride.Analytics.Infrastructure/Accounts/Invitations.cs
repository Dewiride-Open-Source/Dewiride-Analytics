using System.Net.Mail;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Dashboard;
using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Asks people to join an organisation, and lets them take it up.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is created in the invited person's name until they follow the link. That is what keeps
/// naming an address from claiming it, and what keeps an installation from answering, through
/// whether the invitation succeeded, whether an address already has an account on it.
/// </para>
/// <para>
/// The secret is generated here and only its digest is stored, so a stolen copy of the control
/// plane hands over no way into anybody's account. Sending a second invitation to the same address
/// replaces the digest, which is what makes the older link stop working.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="accounts">Account store.</param>
/// <param name="email">How messages leave the building.</param>
/// <param name="dashboard">Where the screens are published, for the link.</param>
/// <param name="clock">Clock.</param>
/// <param name="logger">Log.</param>
public sealed class Invitations(
    ControlPlaneDbContext database,
    UserManager<ApplicationUser> accounts,
    IEmailSender email,
    IOptions<DashboardOptions> dashboard,
    TimeProvider clock,
    ILogger<Invitations> logger) : IInvitations
{
    /// <summary>The screen an invitation link opens.</summary>
    private const string JoinScreen = "app/join";

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingInvitation>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return await database.OrganizationInvitations
            .AsNoTracking()
            .Where(invitation => invitation.OrganizationId == organizationId)
            .Where(invitation =>
                invitation.AcceptedAt == null
                && invitation.RevokedAt == null
                && invitation.ExpiresAt > now)
            .OrderBy(invitation => invitation.InvitedAt)
            .ThenBy(invitation => invitation.Id)
            .Select(invitation => new PendingInvitation(
                invitation.Id,
                invitation.EmailAddress,
                invitation.Role,
                invitation.InvitedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InvitationOutcome> InviteAsync(
        InvitationRequest request,
        CancellationToken cancellationToken)
    {
        var address = request.EmailAddress?.Trim() ?? string.Empty;

        if (address.Length > OrganizationInvitation.MaxEmailAddressLength
            || !MailAddress.TryCreate(address, out _))
        {
            return new InvitationOutcome(InvitationStatus.AddressUnusable, null);
        }

        var normalized = OrganizationInvitation.Normalise(address);

        if (await AlreadyHereAsync(request.OrganizationId, normalized, cancellationToken).ConfigureAwait(false))
        {
            return new InvitationOutcome(InvitationStatus.AlreadyHere, null);
        }

        var now = clock.GetUtcNow();
        var (secret, hash) = InvitationSecret.Create();

        var invitation = await database.OrganizationInvitations
            .FirstOrDefaultAsync(
                candidate => candidate.OrganizationId == request.OrganizationId
                    && candidate.NormalizedEmailAddress == normalized,
                cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            invitation = new OrganizationInvitation(
                Guid.CreateVersion7(now),
                request.OrganizationId,
                address,
                request.Role,
                request.InvitedByUserId,
                hash,
                now);

            database.OrganizationInvitations.Add(invitation);
        }
        else
        {
            invitation.Renew(request.Role, hash, now);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SendAsync(invitation, secret, request.InvitedByUserId, cancellationToken).ConfigureAwait(false);

        InvitationLog.Sent(logger);

        return new InvitationOutcome(
            InvitationStatus.Invited,
            new PendingInvitation(
                invitation.Id,
                invitation.EmailAddress,
                invitation.Role,
                invitation.InvitedAt,
                invitation.ExpiresAt));
    }

    /// <inheritdoc />
    public async Task<InvitationWithdrawal> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var invitation = await database.OrganizationInvitations
            .FirstOrDefaultAsync(
                candidate => candidate.Id == invitationId && candidate.OrganizationId == organizationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null || invitation.StateAt(now) != InvitationState.Pending)
        {
            return InvitationWithdrawal.NoSuchInvitation;
        }

        invitation.Revoke(now);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return InvitationWithdrawal.Revoked;
    }

    /// <inheritdoc />
    public async Task<InvitationPreview?> PreviewAsync(string token, CancellationToken cancellationToken)
    {
        var invitation = await FindUsableAsync(token, tracked: false, cancellationToken).ConfigureAwait(false);

        if (invitation is null)
        {
            return null;
        }

        var name = await database.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == invitation.OrganizationId)
            .Select(organization => organization.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (name is null)
        {
            return null;
        }

        var existing = await accounts.FindByEmailAsync(invitation.EmailAddress).ConfigureAwait(false);

        return new InvitationPreview(name, invitation.EmailAddress, existing is null);
    }

    /// <inheritdoc />
    public async Task<Acceptance> AcceptAsync(
        AcceptanceRequest request,
        CancellationToken cancellationToken)
    {
        var invitation = await FindUsableAsync(request.Token, tracked: true, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return Acceptance.NotUsable();
        }

        var existing = await accounts.FindByEmailAsync(invitation.EmailAddress).ConfigureAwait(false);

        return existing is null
            ? await JoinAsNewAsync(invitation, request, cancellationToken).ConfigureAwait(false)
            : await JoinAsExistingAsync(invitation, existing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the account the invitation was addressed to, and puts it in the organisation.
    /// </summary>
    /// <remarks>
    /// The address counts as confirmed, because receiving a link at it is the whole of what
    /// confirming an address attests and this link arrived there.
    /// </remarks>
    private async Task<Acceptance> JoinAsNewAsync(
        OrganizationInvitation invitation,
        AcceptanceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Password))
        {
            return new Acceptance(AcceptanceStatus.DetailsMissing, null, []);
        }

        var now = clock.GetUtcNow();

        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = invitation.EmailAddress,
            Email = invitation.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? invitation.EmailAddress
                : request.DisplayName.Trim(),
            CreatedAt = now,
            EmailConfirmed = true,
        };

        var created = await accounts.CreateAsync(user, request.Password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return new Acceptance(
                AcceptanceStatus.PasswordRejected,
                null,
                [.. created.Errors.Select(error => new AccountProblem(error.Code, error.Description))]);
        }

        Grant(invitation, user.Id, now);
        invitation.Accept(now);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        InvitationLog.Accepted(logger);

        return new Acceptance(AcceptanceStatus.Joined, user.Id, []);
    }

    /// <summary>
    /// Puts an account that already exists here into the organisation.
    /// </summary>
    /// <remarks>
    /// No password is asked for and none is changed. Holding the link proves the mailbox, which is
    /// what the invitation was addressed to; what it buys is a standing in one organisation, and
    /// nothing about the account itself.
    /// </remarks>
    private async Task<Acceptance> JoinAsExistingAsync(
        OrganizationInvitation invitation,
        ApplicationUser existing,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var held = await database.OrganizationMemberships
            .FirstOrDefaultAsync(
                membership => membership.OrganizationId == invitation.OrganizationId
                    && membership.UserId == existing.Id,
                cancellationToken)
            .ConfigureAwait(false);

        // A standing they already hold is left as it is. Somebody can only be invited while they
        // are not on the list, so arriving here with one means it was granted while the invitation
        // was in flight — and overwriting it would quietly undo whoever granted it, including
        // where what it undid was the last owner.
        if (held is null)
        {
            Grant(invitation, existing.Id, now);
        }

        invitation.Accept(now);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvitationLog.Accepted(logger);

        return new Acceptance(AcceptanceStatus.JoinedExisting, existing.Id, []);
    }

    private void Grant(OrganizationInvitation invitation, Guid userId, DateTimeOffset now) =>
        database.OrganizationMemberships.Add(
            new OrganizationMembership(
                Guid.CreateVersion7(now),
                invitation.OrganizationId,
                userId,
                invitation.Role,
                now));

    /// <summary>
    /// Finds the invitation a secret belongs to, where it is still usable.
    /// </summary>
    /// <remarks>
    /// Spent, withdrawn, expired and never-issued all answer nothing. Whoever is holding a link
    /// that will not work needs one thing done about it — being asked again — and telling the four
    /// apart would say whether a link had been used by somebody else.
    /// </remarks>
    private async Task<OrganizationInvitation?> FindUsableAsync(
        string token,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (!InvitationSecret.LooksWellFormed(token))
        {
            return null;
        }

        var hash = InvitationSecret.Hash(token);

        var source = tracked
            ? database.OrganizationInvitations
            : database.OrganizationInvitations.AsNoTracking();

        var invitation = await source
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        return invitation?.StateAt(clock.GetUtcNow()) == InvitationState.Pending ? invitation : null;
    }

    private async Task SendAsync(
        OrganizationInvitation invitation,
        string secret,
        Guid invitedByUserId,
        CancellationToken cancellationToken)
    {
        var organizationName = await database.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == invitation.OrganizationId)
            .Select(organization => organization.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (organizationName is null)
        {
            InvitationLog.CouldNotSend(logger);

            return;
        }

        var invitedBy = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == invitedByUserId)
            .Select(user => user.DisplayName ?? user.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var message = InvitationMessage.For(
            invitation.EmailAddress,
            organizationName,
            invitedBy ?? organizationName,
            AccountLinks.Carrying(dashboard.Value.PublishedAt, JoinScreen, secret));

        if (!await QuietSend.TryAsync(email, message, logger, cancellationToken).ConfigureAwait(false))
        {
            InvitationLog.CouldNotSend(logger);
        }
    }

    private async Task<bool> AlreadyHereAsync(
        Guid organizationId,
        string normalizedAddress,
        CancellationToken cancellationToken) =>
        await database.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.OrganizationId == organizationId
                    && database.Users.Any(user =>
                        user.Id == membership.UserId && user.NormalizedEmail == normalizedAddress),
                cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>
/// What inviting somebody records.
/// </summary>
/// <remarks>
/// No address is written. Who was asked to join an account is in the account, where the people who
/// run it can see it and where it is deleted with everything else; a log has a different lifetime
/// and a different audience.
/// </remarks>
internal static partial class InvitationLog
{
    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Information,
        Message = "Somebody was invited to join an organisation.")]
    public static partial void Sent(ILogger logger);

    [LoggerMessage(
        EventId = 3402,
        Level = LogLevel.Information,
        Message = "An invitation was taken up.")]
    public static partial void Accepted(ILogger logger);

    [LoggerMessage(
        EventId = 3403,
        Level = LogLevel.Error,
        Message = "Somebody was invited, but the message could not be handed to a mail server. "
            + "They will not receive a link until the invitation is sent again.")]
    public static partial void CouldNotSend(ILogger logger);
}

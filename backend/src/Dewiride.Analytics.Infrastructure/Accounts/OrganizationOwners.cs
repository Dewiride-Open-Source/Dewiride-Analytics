using Dewiride.Analytics.Application.Notifications;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Writes to every owner of an organisation.
/// </summary>
/// <remarks>
/// <para>
/// Written once because several things tell an account's owners something — that it was closed,
/// that it is about to be deleted, that it is back — and a second copy of "who counts as an
/// owner" would be a second answer to a question about who is told what happens to an account.
/// </para>
/// <para>
/// Every message goes through <see cref="QuietSend"/>: a mail server that will not take one owner's
/// message must not stop the others' from being tried, and must never change the answer the act
/// that prompted the message gives. A message nobody received is logged where it happened.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="email">Where messages are handed over.</param>
/// <param name="logger">Log.</param>
public sealed partial class OrganizationOwners(
    ControlPlaneDbContext database,
    IEmailSender email,
    ILogger<OrganizationOwners> logger)
{
    /// <summary>
    /// Composes one message per owner and hands each over.
    /// </summary>
    /// <param name="organizationId">The organisation.</param>
    /// <param name="compose">Builds the message for one owner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TellAsync(
        Guid organizationId,
        Func<Recipient, EmailMessage> compose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compose);

        var owners = await database.OrganizationMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.OrganizationId == organizationId && membership.Role == OrganizationRole.Owner)
            .Join(
                database.Users,
                membership => membership.UserId,
                user => user.Id,
                (_, user) => new { user.Email, user.DisplayName })
            .Where(owner => owner.Email != null)
            .OrderBy(owner => owner.Email)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (owners.Count == 0)
        {
            Log.NobodyToTell(logger);

            return;
        }

        foreach (var owner in owners)
        {
            var message = compose(new Recipient(owner.Email!, owner.DisplayName));

            await QuietSend.TryAsync(email, message, logger, cancellationToken).ConfigureAwait(false);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3701,
            Level = LogLevel.Warning,
            Message = "An account has something to tell its owners, and has no owner with an address to tell.")]
        public static partial void NobodyToTell(ILogger logger);
    }
}

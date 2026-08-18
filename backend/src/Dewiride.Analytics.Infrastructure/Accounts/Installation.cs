using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.Accounts;

/// <summary>
/// Claims an empty install: one organisation, one account that owns it, one site to measure.
/// </summary>
/// <remarks>
/// <para>
/// Everything happens inside a single transaction that begins by taking an advisory lock, so two
/// people opening the setup screen at the same moment cannot both become the owner. The second
/// one waits for the first to commit and is then told the install is already claimed. Relying on
/// a read of "are there any accounts yet?" alone would not do it: both callers would read zero
/// before either wrote.
/// </para>
/// <para>
/// The account is created before the organisation and the site are written, because the password
/// rules are the most likely thing to refuse the request and there is no reason to have written
/// anything by the time that happens.
/// </para>
/// </remarks>
/// <param name="database">Control-plane database.</param>
/// <param name="accounts">Account store.</param>
/// <param name="clock">Clock.</param>
/// <param name="logger">Log.</param>
public sealed class Installation(
    ControlPlaneDbContext database,
    UserManager<ApplicationUser> accounts,
    TimeProvider clock,
    ILogger<Installation> logger) : IInstallation
{
    /// <summary>
    /// Key the setup transaction locks on.
    /// </summary>
    /// <remarks>
    /// PostgreSQL advisory locks share one namespace across the whole database, so the number is
    /// arbitrary but must not collide with another use. Nothing else in this product takes one.
    /// </remarks>
    private const long SetupLockKey = 0x4445_5749_5249_4445;

    /// <summary>Problem code reported when the first site's details are not usable.</summary>
    public const string SiteRejectedCode = "SiteDetailsRejected";

    /// <summary>Problem code reported when the organisation's name is not usable.</summary>
    public const string OrganizationRejectedCode = "OrganizationNameRejected";

    /// <inheritdoc />
    public Task<bool> IsClaimedAsync(CancellationToken cancellationToken) =>
        database.Users.AsNoTracking().AnyAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<InstallationOutcome> ClaimAsync(
        InstallationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Asked once before any lock is taken. An install is claimable for a few minutes of its
        // life and claimed for the rest of it, so this is the answer almost every time, and
        // taking an exclusive lock to arrive at it would let anyone who can reach the endpoint
        // make everybody else queue.
        if (await IsClaimedAsync(cancellationToken).ConfigureAwait(false))
        {
            return InstallationOutcome.AlreadyClaimed;
        }

        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await database.Database
            .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken)
            .ConfigureAwait(false);

        // Asked again, this time under the lock. The answer above was read before anybody was
        // holding anything back, so two callers could both have seen an unclaimed install.
        if (await IsClaimedAsync(cancellationToken).ConfigureAwait(false))
        {
            return InstallationOutcome.AlreadyClaimed;
        }

        var now = clock.GetUtcNow();

        if (!TryDescribe(request, now, out var organization, out var site, out var refusal))
        {
            return refusal;
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = request.EmailAddress,
            Email = request.EmailAddress,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.EmailAddress
                : request.DisplayName.Trim(),
            CreatedAt = now,

            // The person running the setup screen is the person who installed the product; there
            // is nobody else to confirm them to, and no mail server configured yet to do it with.
            EmailConfirmed = true,
        };

        var created = await accounts.CreateAsync(user, request.Password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return InstallationOutcome.Rejected(
                [.. created.Errors.Select(error => new InstallationProblem(error.Code, error.Description))]);
        }

        database.Organizations.Add(organization);
        database.Sites.Add(site);
        database.SiteMemberships.Add(
            new SiteMembership(Guid.CreateVersion7(now), site.Id, user.Id, SiteRole.Owner, now));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        InstallationLog.Claimed(logger, site.Domain);

        return new InstallationOutcome(InstallationStatus.Claimed, user.Id, site.Id, []);
    }

    /// <summary>
    /// Builds the organisation and the site the request asks for.
    /// </summary>
    /// <remarks>
    /// Both aggregates refuse details they cannot honour — an empty name, a time zone this
    /// machine has never heard of — by throwing from their constructors. Turning that into a
    /// refusal here is what lets the endpoint answer with something a person can act on instead
    /// of a server fault.
    /// </remarks>
    private static bool TryDescribe(
        InstallationRequest request,
        DateTimeOffset now,
        out Organization organization,
        out Site site,
        out InstallationOutcome refusal)
    {
        organization = null!;
        site = null!;
        refusal = null!;

        try
        {
            organization = new Organization(Guid.CreateVersion7(now), request.OrganizationName, now);
        }
        catch (ArgumentException exception)
        {
            refusal = InstallationOutcome.Rejected(
                [new InstallationProblem(OrganizationRejectedCode, exception.Message)]);

            return false;
        }

        try
        {
            site = new Site(
                Guid.CreateVersion7(now),
                organization.Id,
                request.SiteDomain,
                request.TimeZoneId,
                now);
        }
        catch (ArgumentException exception)
        {
            refusal = InstallationOutcome.Rejected(
                [new InstallationProblem(SiteRejectedCode, exception.Message)]);

            return false;
        }

        return true;
    }
}

/// <summary>
/// What setting an install up records.
/// </summary>
internal static partial class InstallationLog
{
    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Information,
        Message = "This install has been claimed and is now measuring {Domain}.")]
    public static partial void Claimed(ILogger logger, string domain);
}

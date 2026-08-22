namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// The one-off act of turning an empty install into a usable one.
/// </summary>
/// <remarks>
/// <para>
/// A self-hosted install starts with no accounts at all, and the collector it ships with is
/// reachable from the internet, so the dashboard beside it is too. The window between the
/// process first starting and somebody claiming ownership of it is the only moment at which an
/// unauthenticated caller may create an account, and it closes permanently the instant one
/// exists.
/// </para>
/// <para>
/// Implementations must make claiming an empty install safe against two callers arriving at
/// once. Whoever loses is told the install is already set up rather than being quietly made a
/// second owner.
/// </para>
/// </remarks>
public interface IInstallation
{
    /// <summary>
    /// Reports whether the install has already been claimed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> once at least one account exists.</returns>
    Task<bool> IsClaimedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Claims an unclaimed install, creating its organisation, its first account and its first site.
    /// </summary>
    /// <param name="request">What to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, and on success what was created.</returns>
    Task<InstallationOutcome> ClaimAsync(InstallationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// What to create when an install is claimed.
/// </summary>
/// <param name="EmailAddress">Address the first account signs in with.</param>
/// <param name="Password">Password for the first account.</param>
/// <param name="DisplayName">Name shown in the interface. Falls back to the address when absent.</param>
/// <param name="OrganizationName">Name of the organisation that will own the sites.</param>
/// <param name="SiteDomain">Hostname of the first site to measure.</param>
/// <param name="TimeZoneId">IANA time zone the first site's days are cut in.</param>
public sealed record InstallationRequest(
    string EmailAddress,
    string Password,
    string? DisplayName,
    string OrganizationName,
    string SiteDomain,
    string TimeZoneId);

/// <summary>
/// Why claiming an install did or did not succeed.
/// </summary>
public enum InstallationStatus
{
    /// <summary>The organisation, the account and the first site were created.</summary>
    Claimed = 1,

    /// <summary>An account already exists, so the install is not claimable.</summary>
    AlreadyClaimed = 2,

    /// <summary>Something in the request was not acceptable. See the reported problems.</summary>
    Rejected = 3,
}

/// <summary>
/// The result of claiming an install.
/// </summary>
/// <param name="Status">Whether it was claimed.</param>
/// <param name="UserId">The account created, when one was.</param>
/// <param name="SiteId">The first site created, when one was.</param>
/// <param name="Problems">
/// Why the request was refused, when it was. Each entry is a stable code paired with a sentence
/// fit to show somebody, so the interface can look one up in its own message catalogue and fall
/// back to the sentence when it has no translation for it.
/// </param>
public sealed record InstallationOutcome(
    InstallationStatus Status,
    Guid? UserId,
    Guid? SiteId,
    IReadOnlyList<AccountProblem> Problems)
{
    /// <summary>Builds the outcome for a request that was refused.</summary>
    /// <param name="problems">Why it was refused.</param>
    /// <returns>The outcome.</returns>
    public static InstallationOutcome Rejected(IReadOnlyList<AccountProblem> problems) =>
        new(InstallationStatus.Rejected, null, null, problems);

    /// <summary>The outcome for an install that somebody had already claimed.</summary>
    public static InstallationOutcome AlreadyClaimed { get; } =
        new(InstallationStatus.AlreadyClaimed, null, null, []);
}


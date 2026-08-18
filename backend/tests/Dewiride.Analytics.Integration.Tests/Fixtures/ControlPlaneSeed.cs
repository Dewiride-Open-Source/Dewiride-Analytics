using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Writes the control-plane rows a test needs before it can prove anything.
/// </summary>
internal static class ControlPlaneSeed
{
    /// <summary>
    /// Creates an organisation and a site inside it.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="domain">The site's primary hostname.</param>
    /// <param name="timeZoneId">IANA zone the site's days are cut in.</param>
    /// <param name="configure">Applied to the site before it is saved.</param>
    /// <returns>The saved site.</returns>
    public static async Task<Site> AddSiteAsync(
        AnalyticsStackFixture stack,
        string domain = "example.com",
        string timeZoneId = "Etc/UTC",
        Action<Site>? configure = null)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var organization = new Organization(Guid.NewGuid(), $"Owner of {domain}", now);
        var site = new Site(Guid.NewGuid(), organization.Id, domain, timeZoneId, now);
        configure?.Invoke(site);

        database.Add(organization);
        database.Add(site);
        await database.SaveChangesAsync(Cancellation.Token).ConfigureAwait(false);

        return site;
    }

    /// <summary>
    /// Creates an account.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="email">Address to register, which is also the user name.</param>
    /// <param name="password">Password to set.</param>
    /// <returns>The result of the attempt and the account it produced.</returns>
    public static async Task<(IdentityResult Result, ApplicationUser User)> AddAccountAsync(
        AnalyticsStackFixture stack,
        string email,
        string password)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = "Test Account",
            CreatedAt = now,
        };

        var result = await accounts.CreateAsync(user, password).ConfigureAwait(false);

        return (result, user);
    }

    /// <summary>
    /// Grants an account a role on a site.
    /// </summary>
    /// <param name="stack">The running stack.</param>
    /// <param name="siteId">The site.</param>
    /// <param name="userId">The account.</param>
    /// <param name="role">The role to grant.</param>
    /// <returns>A task that completes once the grant is saved.</returns>
    public static async Task GrantAsync(
        AnalyticsStackFixture stack,
        Guid siteId,
        Guid userId,
        SiteRole role)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        database.Add(new SiteMembership(Guid.NewGuid(), siteId, userId, role, now));
        await database.SaveChangesAsync(Cancellation.Token).ConfigureAwait(false);
    }
}

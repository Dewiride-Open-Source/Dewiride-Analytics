using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves that reading a site's telemetry requires a role on that site.
/// </summary>
/// <remarks>
/// Membership is checked even in the edition where one organisation owns everything. Skipping it
/// because "there is only one organisation" would make the open-source edition's authorisation
/// weaker than the hosted edition's while running the same screens, which is a security advisory
/// rather than a feature difference.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AuthorisationTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task A_Member_Is_Given_A_Scope_Carrying_Their_Role_And_The_Site_Time_Zone()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "scoped.example", timeZoneId: "Asia/Kolkata");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), Passwords.Acceptable);
        await ControlPlaneSeed.GrantAsync(stack, site.Id, user.Id, SiteRole.Editor);

        var scope = await ResolveAsync(site.Id, user.Id);

        scope.Should().NotBeNull();
        scope.SiteId.Should().Be(site.Id);
        scope.OrganizationId.Should().Be(site.OrganizationId);
        scope.Role.Should().Be(SiteRole.Editor);
        scope.TimeZoneId.Should().Be("Asia/Kolkata");
    }

    [Fact]
    public async Task Somebody_With_No_Role_On_A_Site_Is_Given_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "stranger.example");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), Passwords.Acceptable);

        var scope = await ResolveAsync(site.Id, user.Id);

        scope.Should().BeNull();
    }

    /// <summary>
    /// A member of one site asking about another gets the same answer as somebody asking about a
    /// site that was never created, so the interface cannot be used to test which identifiers are
    /// real.
    /// </summary>
    [Fact]
    public async Task A_Role_On_One_Site_Grants_Nothing_On_Another()
    {
        var owned = await ControlPlaneSeed.AddSiteAsync(stack, domain: "mine.example");
        var other = await ControlPlaneSeed.AddSiteAsync(stack, domain: "theirs.example");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), Passwords.Acceptable);
        await ControlPlaneSeed.GrantAsync(stack, owned.Id, user.Id, SiteRole.Owner);

        var granted = await ResolveAsync(owned.Id, user.Id);
        var refused = await ResolveAsync(other.Id, user.Id);
        var missing = await ResolveAsync(Guid.NewGuid(), user.Id);

        granted.Should().NotBeNull();
        refused.Should().BeNull();
        missing.Should().BeNull();
    }

    [Fact]
    public async Task An_Unauthenticated_Caller_Is_Given_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "anonymous.example");

        var scope = await ResolveAsync(site.Id, userId: null);

        scope.Should().BeNull();
    }

    private async Task<Application.Tenancy.TenantScope?> ResolveAsync(Guid siteId, Guid? userId)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var provider = new SingleTenantScopeProvider(database, new StubPrincipal(userId));

        return await provider.ResolveAsync(siteId, Cancellation.Token);
    }

    private static string Address() => $"scope-{Guid.NewGuid():n}@example.com";

    private sealed class StubPrincipal(Guid? userId) : ICurrentPrincipalAccessor
    {
        public Guid? GetUserId() => userId;
    }
}

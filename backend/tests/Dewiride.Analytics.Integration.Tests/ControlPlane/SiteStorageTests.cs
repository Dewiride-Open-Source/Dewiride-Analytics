using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves a site's settings survive the database.
/// </summary>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteStorageTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task A_Site_Is_Stored_With_Its_Domain_Already_Normalised()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "Blog.EXAMPLE.com.");

        var stored = await ReadAsync(site.Id);

        stored.Domain.Should().Be("blog.example.com");
        stored.DisplayName.Should().Be("blog.example.com");
    }

    [Fact]
    public async Task An_Origin_List_Survives_The_Round_Trip()
    {
        string[] origins = ["docs.example.com", "cdn.example.com"];
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: "origins.example",
            configure: created => created.ReplaceAllowedOrigins(origins));

        var stored = await ReadAsync(site.Id);

        stored.AllowedOrigins.Should().Equal("docs.example.com", "cdn.example.com");
    }

    [Fact]
    public async Task A_Site_With_No_Declared_Origins_Stores_An_Empty_List_Rather_Than_Nothing()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "plain.example");

        var stored = await ReadAsync(site.Id);

        stored.AllowedOrigins.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_String_Retention_Survives_The_Round_Trip()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: "retention.example",
            configure: created => created.SetQueryStringRetention(true));

        var stored = await ReadAsync(site.Id);

        stored.RetainQueryStrings.Should().BeTrue();
    }

    [Fact]
    public async Task Click_Capture_Survives_The_Round_Trip()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: "presses.example",
            configure: created => created.SetClickCapture(false));

        var stored = await ReadAsync(site.Id);

        stored.CaptureClicks.Should().BeFalse();
    }

    [Fact]
    public async Task A_Time_Zone_Identifier_Survives_The_Round_Trip()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "zones.example", timeZoneId: "Asia/Kolkata");

        var stored = await ReadAsync(site.Id);

        stored.TimeZoneId.Should().Be("Asia/Kolkata");
    }

    /// <summary>
    /// What the collector reads on every report. The settings have to arrive on the other side of
    /// the cache, not merely on the other side of the database.
    /// </summary>
    [Fact]
    public async Task The_Collector_Sees_The_Settings_A_Site_Was_Saved_With()
    {
        string[] origins = ["docs.example.com"];
        var site = await ControlPlaneSeed.AddSiteAsync(
            stack,
            domain: "catalog.example",
            configure: created =>
            {
                created.SetQueryStringRetention(true);
                created.SetClickCapture(false);
                created.ReplaceAllowedOrigins(origins);
            });

        await using var scope = stack.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISiteCatalog>();

        var snapshot = await catalog.FindAsync(site.Id, Cancellation.Token);

        snapshot.Should().NotBeNull();
        snapshot.Domain.Should().Be("catalog.example");
        snapshot.RetainQueryStrings.Should().BeTrue();
        snapshot.CaptureClicks.Should().BeFalse();
        snapshot.AllowedOrigins.Should().Equal("docs.example.com");
    }

    [Fact]
    public async Task The_Collector_Resolves_Nothing_For_A_Site_That_Does_Not_Exist()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISiteCatalog>();

        var snapshot = await catalog.FindAsync(Guid.NewGuid(), Cancellation.Token);

        snapshot.Should().BeNull();
    }

    /// <summary>
    /// A grant that refers to an account nobody can look up is an authorisation decision nobody
    /// can evaluate, so the database refuses it rather than storing it.
    /// </summary>
    [Fact]
    public async Task A_Grant_Cannot_Name_An_Account_That_Does_Not_Exist()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "orphan.example");

        var act = async () => await ControlPlaneSeed.GrantAsync(stack, site.Id, Guid.NewGuid(), SiteRole.Owner);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_Person_Cannot_Hold_Two_Roles_On_One_Site()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "duplicate.example");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(
            stack,
            $"duplicate-{Guid.NewGuid():n}@example.com",
            Passwords.Acceptable);

        await ControlPlaneSeed.GrantAsync(stack, site.Id, user.Id, SiteRole.Viewer);

        var act = async () => await ControlPlaneSeed.GrantAsync(stack, site.Id, user.Id, SiteRole.Owner);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Roles are stored as words rather than as numbers, so that the table can be read without a
    /// lookup somewhere else.
    /// </summary>
    [Fact]
    public async Task A_Granted_Role_Is_Stored_As_The_Word_For_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "roles.example");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(
            stack,
            $"roles-{Guid.NewGuid():n}@example.com",
            Passwords.Acceptable);

        await ControlPlaneSeed.GrantAsync(stack, site.Id, user.Id, SiteRole.Editor);

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var stored = await database.Database
            .SqlQueryRaw<string>(
                """SELECT role AS "Value" FROM site_memberships WHERE site_id = {0}""",
                site.Id)
            .ToListAsync(Cancellation.Token);

        stored.Should().Equal("Editor");
    }

    [Fact]
    public async Task Deleting_A_Site_Takes_Its_Grants_With_It()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: "cascade.example");
        var (_, user) = await ControlPlaneSeed.AddAccountAsync(
            stack,
            $"cascade-{Guid.NewGuid():n}@example.com",
            Passwords.Acceptable);

        await ControlPlaneSeed.GrantAsync(stack, site.Id, user.Id, SiteRole.Owner);

        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        await database.Sites.Where(candidate => candidate.Id == site.Id)
            .ExecuteDeleteAsync(Cancellation.Token);

        var remaining = await database.SiteMemberships
            .CountAsync(membership => membership.SiteId == site.Id, Cancellation.Token);

        remaining.Should().Be(0);
    }

    private async Task<Site> ReadAsync(Guid siteId)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Sites
            .AsNoTracking()
            .SingleAsync(site => site.Id == siteId, Cancellation.Token);
    }
}

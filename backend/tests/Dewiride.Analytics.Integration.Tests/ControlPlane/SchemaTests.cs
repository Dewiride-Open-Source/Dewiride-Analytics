using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the control-plane schema applies to an empty database and stays applied.
/// </summary>
/// <remarks>
/// Migrations are forward-only and run on somebody else's database with nobody to call when they
/// go wrong, so "it applied on a machine that already had the previous schema" is not the thing
/// worth knowing. These start from nothing.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SchemaTests(AnalyticsStackFixture stack)
{
    private const string TableNamesSql = """
        SELECT table_name AS "Value"
        FROM information_schema.tables
        WHERE table_schema = 'public'
        """;

    private const string SiteColumnsSql = """
        SELECT column_name AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'sites'
        """;

    [Fact]
    public async Task Every_Control_Plane_Table_Was_Created()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(["organizations", "sites", "site_memberships", "visitor_key_salts"]);
    }

    /// <summary>
    /// Self-hosters read their own database. Identity and the authorisation server both ship
    /// tables named for their libraries rather than for this product, and both were renamed.
    /// </summary>
    [Fact]
    public async Task The_Account_Tables_Carry_This_Product_Names()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(
            ["users", "roles", "user_roles", "user_claims", "user_logins", "user_tokens", "role_claims"]);
        tables.Should().NotContain(name => name.StartsWith("asp_net", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Authorisation_Server_Tables_Carry_This_Product_Names()
    {
        var tables = await TableNamesAsync();

        tables.Should().Contain(
        [
            "openiddict_applications",
            "openiddict_authorizations",
            "openiddict_scopes",
            "openiddict_tokens",
        ]);
    }

    [Fact]
    public async Task Every_Column_Is_Named_The_Way_Somebody_Would_Type_It()
    {
        var columns = await QueryAsync(SiteColumnsSql);

        columns.Should().Contain(
            ["organization_id", "display_name", "time_zone_id", "retain_query_strings", "allowed_origins"]);
    }

    /// <summary>
    /// Applying again is what a self-hoster's every restart does.
    /// </summary>
    [Fact]
    public async Task Applying_The_Migrations_Again_Changes_Nothing()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var before = await database.Database.GetAppliedMigrationsAsync(Cancellation.Token);
        await database.Database.MigrateAsync(Cancellation.Token);
        var after = await database.Database.GetAppliedMigrationsAsync(Cancellation.Token);

        before.Should().NotBeEmpty();
        after.Should().Equal(before);
        (await database.Database.GetPendingMigrationsAsync(Cancellation.Token)).Should().BeEmpty();
    }

    private Task<List<string>> TableNamesAsync() => QueryAsync(TableNamesSql);

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Database.SqlQueryRaw<string>(sql).ToListAsync(Cancellation.Token);
    }
}

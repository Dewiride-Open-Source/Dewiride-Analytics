using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

    private const string ColumnNamesSql = """
        SELECT column_name AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = {0}
        """;

    private const string OrganizationIndexNamesSql = """
        SELECT indexname AS "Value"
        FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'organizations'
        """;

    private const string InstallationClaimCheckConstraintsSql = """
        SELECT constraint_name AS "Value"
        FROM information_schema.table_constraints
        WHERE table_schema = 'public'
          AND table_name = 'installation_claims'
          AND constraint_type = 'CHECK'
        """;

    private const string DeleteRuleSql = """
        SELECT delete_rule AS "Value"
        FROM information_schema.referential_constraints
        WHERE constraint_schema = 'public' AND constraint_name = {0}
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
        var columns = await ColumnNamesAsync("sites");

        columns.Should().Contain(
            ["organization_id", "display_name", "time_zone_id", "retain_query_strings", "allowed_origins"]);
    }

    [Fact]
    public async Task An_Organisation_Records_When_It_Was_Closed_By_Whom_And_Whether_It_Was_Warned()
    {
        var columns = await ColumnNamesAsync("organizations");

        columns.Should().Contain(["closed_at", "closed_by_user_id", "deletion_reminder_sent_at"]);
    }

    /// <summary>
    /// The sweep asks every hour for the closed organisations whose time has run, and the closer
    /// is joined whenever a closure is explained; neither read should have to scan the table.
    /// </summary>
    [Fact]
    public async Task The_Columns_A_Closure_Is_Found_By_Are_Indexed()
    {
        var indexes = await QueryAsync(OrganizationIndexNamesSql);

        indexes.Should().Contain(["ix_organizations_closed_at", "ix_organizations_closed_by_user_id"]);
    }

    [Fact]
    public async Task The_Claim_On_An_Install_Is_Recorded_In_A_Table_Of_Its_Own()
    {
        var tables = await TableNamesAsync();
        var columns = await ColumnNamesAsync("installation_claims");

        tables.Should().Contain("installation_claims");
        columns.Should().BeEquivalentTo("id", "claimed_at");
    }

    [Fact]
    public async Task The_Database_Holds_The_Claim_To_A_Single_Row()
    {
        var constraints = await QueryAsync(InstallationClaimCheckConstraintsSql);

        constraints.Should().Contain("ck_installation_claims_one_row");
    }

    /// <summary>
    /// A second row is refused by the database rather than by whichever code happens to write
    /// the table — the first-run claim, the purge that empties an install, or a migration — so
    /// none of them can turn "claimed" into a count that has to be reasoned about.
    /// </summary>
    [Fact]
    public async Task A_Second_Claim_Row_Is_Refused_By_The_Database()
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var act = async () => await database.Database.ExecuteSqlAsync(
            $"INSERT INTO installation_claims (id, claimed_at) VALUES (2, {now})",
            Cancellation.Token);

        var refused = await act.Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    /// <summary>
    /// Deleting a person must not delete what they did. An organisation they closed stays closed
    /// and an invitation they sent into another organisation stays usable, each with the reference
    /// to them cleared rather than the row taken with them.
    /// </summary>
    [Theory]
    [InlineData("fk_organizations_users_closed_by_user_id")]
    [InlineData("fk_organization_invitations_users_invited_by_user_id")]
    public async Task Deleting_A_Person_Clears_The_Reference_To_Them_Rather_Than_Cascading(string constraint)
    {
        var rules = await QueryAsync(DeleteRuleSql, constraint);

        rules.Should().Equal("SET NULL");
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

    private Task<List<string>> ColumnNamesAsync(string table) => QueryAsync(ColumnNamesSql, table);

    private async Task<List<string>> QueryAsync(string sql, params object[] parameters)
    {
        await using var scope = stack.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await database.Database.SqlQueryRaw<string>(sql, parameters).ToListAsync(Cancellation.Token);
    }
}

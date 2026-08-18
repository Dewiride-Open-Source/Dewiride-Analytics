using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Health;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Proves the telemetry schema applies to an empty server and refuses to drift.
/// </summary>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class TelemetrySchemaTests(AnalyticsStackFixture stack)
{
    private const string ColumnTypeSql = """
        SELECT type
        FROM system.columns
        WHERE database = currentDatabase() AND table = {table:String} AND name = {column:String}
        """;

    [Fact]
    public async Task The_Migration_History_Records_What_Was_Applied()
    {
        var applied = await TelemetryStore.RowsAsync(
            Client,
            "SELECT version, name, checksum FROM schema_migrations ORDER BY version");

        applied.Should().NotBeEmpty();
        applied[0]["version"].Should().Be(1u);
        applied[0]["name"].Should().Be("events");
        applied[0]["checksum"].Should().BeOfType<string>().Which.Should().HaveLength(64);
    }

    [Fact]
    public async Task Applying_The_Scripts_Again_Changes_Nothing()
    {
        var runner = stack.Services.GetRequiredService<ClickHouseMigrationRunner>();

        var before = await AppliedCountAsync();
        await runner.ApplyAsync(Cancellation.Token);
        var after = await AppliedCountAsync();

        after.Should().Be(before);
    }

    /// <summary>
    /// Every capture surface the product knows about has to be a value this column accepts. A
    /// surface added in code and forgotten here fails at the moment somebody deploys it, which is
    /// after the traffic it was meant to collect has already gone.
    /// </summary>
    [Fact]
    public async Task The_Surface_Column_Accepts_Every_Capture_Surface()
    {
        var declared = await ColumnTypeAsync("surface");

        foreach (var surface in Enum.GetNames<IngestSurface>())
        {
            declared.Should().Contain($"'{surface}'");
        }
    }

    /// <summary>
    /// Same obligation as the surface column, and a worse failure: a category added in code and
    /// forgotten here would make every visit the engine put in it unstorable, so the site would
    /// silently stop being judged from the moment one arrived.
    /// </summary>
    [Fact]
    public async Task The_Category_Column_Accepts_Every_Verdict_The_Engine_Can_Reach()
    {
        var declared = await ColumnTypeAsync("category", "session_classifications");

        foreach (var category in Enum.GetNames<TrafficCategory>())
        {
            declared.Should().Contain($"'{category}'");
        }
    }

    [Fact]
    public async Task The_Strength_Column_Accepts_Every_Band()
    {
        var declared = await ColumnTypeAsync("strength", "session_classifications");

        foreach (var strength in Enum.GetNames<EvidenceStrength>())
        {
            declared.Should().Contain($"'{strength}'");
        }
    }

    [Fact]
    public async Task The_Direction_Column_Accepts_Every_Way_Evidence_Can_Point()
    {
        var declared = await ColumnTypeAsync("signal_directions", "session_classifications");

        foreach (var direction in Enum.GetNames<SignalDirection>())
        {
            declared.Should().Contain($"'{direction}'");
        }
    }

    /// <summary>
    /// Verdicts are kept per ruleset rather than overwritten, so improving the rules adds to
    /// history instead of rewriting it — and a number can still be attributed to the rules that
    /// produced it a month later.
    /// </summary>
    [Fact]
    public async Task Verdicts_Are_Kept_Per_Visit_And_Per_Ruleset()
    {
        var definition = await TableDefinitionAsync("session_classifications");

        definition.Should().Contain("ReplacingMergeTree(classified_at)");
        definition.Should().Contain("ORDER BY (site_id, session_key, ruleset_major, ruleset_minor)");
    }

    [Fact]
    public async Task The_Kind_Column_Accepts_Every_Kind_Of_Report()
    {
        var declared = await ColumnTypeAsync("kind");

        foreach (var kind in Enum.GetNames<EventKind>().Where(name => name != nameof(EventKind.Unknown)))
        {
            declared.Should().Contain($"'{kind}'");
        }
    }

    /// <summary>
    /// A surface that cannot see pointer activity records that it could not see it, which is a
    /// different claim from recording that none happened.
    /// </summary>
    [Theory]
    [InlineData("had_pointer_interaction")]
    [InlineData("had_keyboard_interaction")]
    [InlineData("declared_web_driver")]
    public async Task Interaction_Columns_Distinguish_Unobserved_From_Absent(string column)
    {
        var declared = await ColumnTypeAsync(column);

        declared.Should().Contain("'Unobserved'").And.Contain("'No'").And.Contain("'Yes'");
    }

    /// <summary>
    /// Both retention rules are enforced by the engine rather than by a job, so they hold even if
    /// the application is never started again.
    /// </summary>
    [Fact]
    public async Task The_Address_Column_Expires_Long_Before_The_Row_Does()
    {
        var definition = await TableDefinitionAsync();

        definition.Should().Contain("`ip_address` String TTL toDateTime(server_ts) + toIntervalHour(72)");
    }

    [Fact]
    public async Task The_Event_Table_Drops_Rows_Once_Retention_Runs_Out()
    {
        var definition = await TableDefinitionAsync();

        definition.Should().Contain("PARTITION BY toYYYYMM(server_ts)");
        definition.Should().Contain("ORDER BY (site_id, server_ts, event_id)");
        definition.Should().Contain("TTL toDateTime(server_ts) + toIntervalMonth(12)");
    }

    /// <summary>
    /// An applied migration is a record of what a database already contains. Editing one produces
    /// a schema that silently disagrees with the code reading it, so it is refused loudly instead.
    /// </summary>
    [Fact]
    public async Task An_Applied_Script_That_Has_Since_Been_Edited_Stops_The_Start_Up()
    {
        var database = $"drifted_{Guid.NewGuid():n}";
        using var client = ClientFor(database);

        await client.ExecuteNonQueryAsync(
            $"CREATE DATABASE IF NOT EXISTS `{database}`",
            options: new QueryOptions { Database = "default" },
            cancellationToken: Cancellation.Token);

        await client.ExecuteNonQueryAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations
            (
              version    UInt32,
              name       String,
              checksum   String,
              applied_at DateTime64(3, 'UTC')
            )
            ENGINE = MergeTree
            ORDER BY version
            """,
            cancellationToken: Cancellation.Token);

        await client.ExecuteNonQueryAsync(
            "INSERT INTO schema_migrations VALUES (1, 'events', 'a-checksum-from-a-different-script', now64(3))",
            cancellationToken: Cancellation.Token);

        var runner = new ClickHouseMigrationRunner(
            client,
            TimeProvider.System,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        var act = async () => await runner.ApplyAsync(Cancellation.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has changed since it was applied*");
    }

    /// <summary>
    /// The database name is quoted into a statement because ClickHouse takes an identifier there
    /// and not a value, so it has to be something a statement cannot be built out of.
    /// </summary>
    [Theory]
    [InlineData("dewiride; DROP DATABASE default")]
    [InlineData("dewiride-analytics")]
    [InlineData("1st_database")]
    public async Task A_Database_Name_That_Is_Not_A_Plain_Identifier_Is_Refused(string name)
    {
        using var client = ClientFor(name);

        var runner = new ClickHouseMigrationRunner(
            client,
            TimeProvider.System,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        var act = async () => await runner.ApplyAsync(Cancellation.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*letters, digits and underscores*");
    }

    /// <summary>
    /// A server that is listening on an empty database is exactly the state in which the collector
    /// accepts reports and loses every one of them, so readiness has to fail there.
    /// </summary>
    [Fact]
    public async Task Readiness_Fails_While_The_Telemetry_Store_Has_No_Schema()
    {
        var database = $"unmigrated_{Guid.NewGuid():n}";
        using var client = ClientFor(database);

        await client.ExecuteNonQueryAsync(
            $"CREATE DATABASE IF NOT EXISTS `{database}`",
            options: new QueryOptions { Database = "default" },
            cancellationToken: Cancellation.Token);

        await client.ExecuteNonQueryAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations
            (
              version    UInt32,
              name       String,
              checksum   String,
              applied_at DateTime64(3, 'UTC')
            )
            ENGINE = MergeTree
            ORDER BY version
            """,
            cancellationToken: Cancellation.Token);

        var check = new TelemetryStoreHealthCheck(client);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            Cancellation.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Readiness_Passes_Once_The_Schema_Is_In_Place()
    {
        var check = new TelemetryStoreHealthCheck(Client);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), Cancellation.Token);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    private IClickHouseClient Client => stack.Services.GetRequiredService<IClickHouseClient>();

    private ClickHouseClient ClientFor(string database) =>
        new(new ClickHouseClientSettings(stack.TelemetryConnectionString) { Database = database });

    private Task<ulong> AppliedCountAsync() =>
        TelemetryStore.ScalarAsync<ulong>(Client, "SELECT count() FROM schema_migrations");

    private Task<string> ColumnTypeAsync(string column, string table = "events")
    {
        var parameters = TelemetryStore.Bind("column", column);
        parameters.AddParameter("table", table);

        return TelemetryStore.ScalarAsync<string>(Client, ColumnTypeSql, parameters);
    }

    private Task<string> TableDefinitionAsync(string table = "events") =>
        TelemetryStore.ScalarAsync<string>(
            Client,
            """
            SELECT create_table_query
            FROM system.tables
            WHERE database = currentDatabase() AND name = {table:String}
            """,
            TelemetryStore.Bind("table", table));
}

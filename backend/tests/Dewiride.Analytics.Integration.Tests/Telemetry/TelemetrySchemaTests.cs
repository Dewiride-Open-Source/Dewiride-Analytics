using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Consolidation;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Dewiride.Analytics.Infrastructure.ClickHouse.Health;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
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

    /// <summary>
    /// A migration that copies or rewrites data reads a whole table, and a script is recorded only
    /// once all of it has finished. Held to the hundred seconds a question from a screen is given,
    /// an installation large enough would fail every start-up, each attempt beginning the same
    /// statement again — so the migrations travel over a client of their own, given an hour.
    /// </summary>
    [Fact]
    public async Task Migrations_Run_Over_A_Client_Given_Longer_Than_A_Question_From_A_Screen()
    {
        var sent = new Tally();

        using var watched = stack.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(ClickHouseRegistration.SchemaHttpClientName)
                .AddHttpMessageHandler(() => new Counting(sent))));

        var runner = watched.Services.GetRequiredService<ClickHouseMigrationRunner>();
        var before = sent.Count;

        await runner.ApplyAsync(Cancellation.Token);

        sent.Count.Should().BeGreaterThan(before, "the migrations send what they send over their own client");
        watched.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClickHouseRegistration.SchemaHttpClientName).Timeout
            .Should().Be(ClickHouseRegistration.LongestSchemaStatement);
    }

    /// <summary>
    /// Counting a finished month reads a month of a website's reports in one statement, on one
    /// thread. Held to the hundred seconds a question from a screen is given, a month of a busy
    /// website would fail on every run until its reports expired, and be lost — so counting travels
    /// over a client of its own, which waits longer than the ten minutes the store is told to stop a
    /// count at, so that it is the store that ends a statement and says why.
    /// </summary>
    [Fact]
    public async Task Months_Are_Counted_Over_A_Client_Given_Longer_Than_The_Store_Gives_A_Count()
    {
        var sent = new Tally();

        using var watched = stack.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(ClickHouseRegistration.ConsolidationHttpClientName)
                .AddHttpMessageHandler(() => new Counting(sent))));

        var months = watched.Services.GetRequiredService<IMonthConsolidation>();
        var now = watched.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var before = sent.Count;

        await months.SurveyAsync(Guid.NewGuid(), "Asia/Kolkata", now, Cancellation.Token);

        sent.Count.Should().BeGreaterThan(before, "counting a month sends what it sends over its own client");
        watched.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClickHouseRegistration.ConsolidationHttpClientName).Timeout
            .Should().Be(ClickHouseRegistration.LongestConsolidationStatement)
            .And.BeGreaterThan(TimeSpan.FromMinutes(10), "the store stops a count at ten minutes, and says why");
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

    /// <summary>
    /// An answer kept by name is the conclusion and never what it was drawn from, so the table has
    /// nowhere an address or a user agent could go — and it is kept for exactly as long as the
    /// verdicts it stands behind, one row to a visit.
    /// </summary>
    [Fact]
    public async Task An_Answer_Kept_By_Name_Is_The_Company_Beside_Its_Visit_And_Nothing_Else()
    {
        var columns = await TelemetryStore.RowsAsync(
            Client,
            """
            SELECT name, type
            FROM system.columns
            WHERE database = currentDatabase() AND table = 'name_confirmations'
            ORDER BY position
            """);

        columns.Select(column => $"{column["name"]} {column["type"]}").Should().Equal(
            "site_id UUID",
            "session_key String",
            "started_at DateTime64(3, 'UTC')",
            "operator LowCardinality(String)",
            "settled_at DateTime64(3, 'UTC')");

        var definition = await TableDefinitionAsync("name_confirmations");

        definition.Should().Contain("ReplacingMergeTree(settled_at)");
        definition.Should().Contain("PARTITION BY toYYYYMM(started_at)");
        definition.Should().Contain("ORDER BY (site_id, started_at, session_key)");
        definition.Should().Contain("TTL toDateTime(started_at) + toIntervalMonth(12)");
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
    /// A website's months are kept until the website is removed, even after it stops being measured,
    /// or until its closed account's records are deleted: one row per website and month, replaced
    /// whole by its newest count, with no rule that would ever expire it.
    /// </summary>
    [Fact]
    public async Task Kept_Months_Are_One_Row_Per_Month_And_Never_Expire()
    {
        var definition = await TableDefinitionAsync("consolidated_months");

        definition.Should().Contain("ENGINE = ReplacingMergeTree(counted_at)");
        definition.Should().Contain("PARTITION BY toYear(month)");
        definition.Should().Contain("ORDER BY (site_id, month)");
        definition.Should().NotContain("TTL");
    }

    /// <summary>
    /// Whether a website's oldest report is its first rests on when this installation began keeping
    /// months, which is when its store gained the table they are kept in. The store records that once,
    /// when the change is applied, and both the engine's count and the dashboard's record read the
    /// same moment from it.
    /// </summary>
    [Fact]
    public async Task Keeping_Months_Began_When_The_Store_Gained_Somewhere_To_Keep_Them()
    {
        var applied = await TelemetryStore.RowsAsync(
            Client,
            "SELECT name, applied_at FROM schema_migrations WHERE version = 14");
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: $"kept-{Guid.NewGuid():n}.example");
        var now = stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        var survey = await stack.Services.GetRequiredService<IMonthConsolidation>()
            .SurveyAsync(site.Id, site.TimeZoneId, now, Cancellation.Token);
        var record = await stack.Services.GetRequiredService<ITelemetryQueries>()
            .GetKeptMonthsAsync(
                new TenantScope(site.Id, site.OrganizationId, SiteRole.Viewer, site.TimeZoneId, site.CreatedAt),
                new KeptMonthsQuery(now),
                Cancellation.Token);

        applied.Should().ContainSingle().Which["name"].Should().Be("months_kept_after_their_reports");
        var began = new DateTimeOffset(DateTime.SpecifyKind((DateTime)applied[0]["applied_at"]!, DateTimeKind.Utc));
        survey.KeepingSince.Should().Be(began);
        record.KeepingSince.Should().Be(began);
    }

    /// <summary>
    /// The engine stops counting a month a day before its oldest report could go, reckoned from how
    /// long it believes a report is kept. That belief is held to every table the count reads, so the
    /// two cannot drift apart and leave months counted after their reports had begun to expire.
    /// </summary>
    [Theory]
    [InlineData("events")]
    [InlineData("session_classifications")]
    [InlineData("name_confirmations")]
    public async Task The_Months_The_Engine_Assumes_A_Report_Is_Kept_Are_The_Months_The_Store_Keeps_It(string table)
    {
        var definition = await TableDefinitionAsync(table);

        definition.Should().Contain($" + toIntervalMonth({Retention.ReportMonths})");
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

    /// <summary>How many requests have gone over one client.</summary>
    private sealed class Tally
    {
        private int _count;

        /// <summary>The requests counted so far.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>Counts one more.</summary>
        public void Add() => Interlocked.Increment(ref _count);
    }

    /// <summary>Counts every request that passes through it, and changes none of them.</summary>
    /// <param name="sent">Where the count is kept.</param>
    private sealed class Counting(Tally sent) : DelegatingHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            sent.Add();

            return base.SendAsync(request, cancellationToken);
        }
    }
}

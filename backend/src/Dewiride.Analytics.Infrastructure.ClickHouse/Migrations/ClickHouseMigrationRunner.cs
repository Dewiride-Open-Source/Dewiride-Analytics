using System.Text.RegularExpressions;
using ClickHouse.Driver;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;

/// <summary>
/// Brings the telemetry store's schema up to date.
/// </summary>
/// <remarks>
/// <para>
/// The control plane uses EF Core migrations and the telemetry store uses ordered SQL scripts.
/// The two are never interleaved: a change that touches both stores ships two files, one in each
/// system, and neither runner knows the other exists.
/// </para>
/// <para>
/// ClickHouse has no transactional DDL, so a script interrupted part-way leaves whatever it had
/// already done in place. Every script is therefore written to be safe to run again — DDL uses
/// <c>IF NOT EXISTS</c> — and a script is only recorded as applied once all of its statements
/// have succeeded. Re-running an interrupted migration is the recovery path, not a repair job.
/// </para>
/// <para>
/// Run this from a single process at start-up. Two instances racing to apply the same migration
/// would each see an empty history and both proceed; the scripts are idempotent so the schema
/// survives, but the history would gain duplicate rows.
/// </para>
/// </remarks>
/// <param name="client">Telemetry store client.</param>
/// <param name="timeProvider">Source of the applied-at stamp.</param>
/// <param name="logger">Log sink.</param>
public sealed partial class ClickHouseMigrationRunner(
    IClickHouseClient client,
    TimeProvider timeProvider,
    ILogger<ClickHouseMigrationRunner> logger)
{
    private const string HistoryTable = "schema_migrations";

    /// <summary>
    /// The database every server has, used to issue the statement that creates ours.
    /// </summary>
    private const string ServerDefaultDatabase = "default";

    private const string CreateHistoryTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations
        (
          version    UInt32,
          name       String,
          checksum   String,
          applied_at DateTime64(3, 'UTC')
        )
        ENGINE = MergeTree
        ORDER BY version
        """;

    private const string SelectAppliedSql = "SELECT version, checksum FROM schema_migrations";

    private static readonly string[] HistoryColumns = ["version", "name", "checksum", "applied_at"];

    /// <summary>
    /// Creates the database if it is absent and applies every migration that has not yet run.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The configured database name is not a plain identifier, or a script that has already been
    /// applied has since been edited.
    /// </exception>
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await client.ExecuteNonQueryAsync(CreateHistoryTableSql, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var applied = await LoadAppliedAsync(cancellationToken).ConfigureAwait(false);
        var scripts = MigrationScriptCatalog.Load();

        foreach (var script in scripts)
        {
            if (applied.TryGetValue(script.Version, out var recordedChecksum))
            {
                VerifyUnchanged(script, recordedChecksum);
                continue;
            }

            await ApplyScriptAsync(script, cancellationToken).ConfigureAwait(false);
        }

        Log.SchemaUpToDate(logger, scripts.Length);
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = client.Settings.Database;

        // The name is quoted below rather than bound, because ClickHouse takes an identifier
        // here and not a value. It comes from the operator's own connection string, and this
        // check is what keeps that the only thing it can ever be.
        if (!PlainIdentifier().IsMatch(database))
        {
            throw new InvalidOperationException(
                $"The telemetry database name '{database}' must contain only letters, digits and "
                + "underscores, and must not start with a digit.");
        }

        await client.ExecuteNonQueryAsync(
                $"CREATE DATABASE IF NOT EXISTS `{database}`",
                options: new QueryOptions { Database = ServerDefaultDatabase },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<uint, string>> LoadAppliedAsync(CancellationToken cancellationToken)
    {
        var applied = new Dictionary<uint, string>();

        await using var reader = await client
            .ExecuteReaderAsync(SelectAppliedSql, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied[reader.GetUInt32(0)] = reader.GetString(1);
        }

        return applied;
    }

    private async Task ApplyScriptAsync(MigrationScript script, CancellationToken cancellationToken)
    {
        foreach (var statement in script.Statements)
        {
            await client.ExecuteNonQueryAsync(statement, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        object[][] historyRow =
        [
            [script.Version, script.Name, script.Checksum, timeProvider.GetUtcNow().UtcDateTime],
        ];

        await client.InsertBinaryAsync(HistoryTable, HistoryColumns, historyRow, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Log.AppliedMigration(logger, script.Version, script.Name);
    }

    private static void VerifyUnchanged(MigrationScript script, string recordedChecksum)
    {
        if (!string.Equals(script.Checksum, recordedChecksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ClickHouse migration {script.Version} ({script.Name}) has changed since it was applied. "
                + "An applied migration is a record of what a database already contains and cannot be "
                + "edited; add a new migration that makes the change instead.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainIdentifier();

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Applied ClickHouse migration {Version} ({Name}).")]
        public static partial void AppliedMigration(ILogger logger, uint version, string name);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Information,
            Message = "ClickHouse schema is up to date across {Count} migration(s).")]
        public static partial void SchemaUpToDate(ILogger logger, int count);
    }
}

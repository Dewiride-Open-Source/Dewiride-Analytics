using System.Collections.Frozen;
using System.Collections.Immutable;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reads the telemetry store through the compiled analytics vocabulary.
/// </summary>
/// <param name="client">Telemetry store client.</param>
internal sealed class ClickHouseTelemetryQueries(IClickHouseClient client) : ITelemetryQueries
{
    /// <inheritdoc />
    public async Task<OverviewResult> GetOverviewAsync(
        TenantScope scope,
        OverviewQuery query,
        CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        // An aggregate with no grouping always produces exactly one row, including over an empty
        // window, where it produces zeroes.
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new OverviewResult(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : default;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        TenantScope scope,
        TimeSeriesQuery query,
        CancellationToken cancellationToken)
    {
        var points = new List<TimeSeriesPoint>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            points.Add(new TimeSeriesPoint(reader.GetDateTimeOffset(0), reader.GetInt64(1)));
        }

        return points;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrafficBreakdownRow>> GetTrafficBreakdownAsync(
        TenantScope scope,
        TrafficBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        var groups = new List<TrafficBreakdownRow>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new TrafficBreakdownRow(
                StoredNames.Categories[reader.GetString(0)],
                StoredNames.Strengths[reader.GetString(1)],
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return groups;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JudgedSession>> GetJudgedSessionsAsync(
        TenantScope scope,
        JudgedSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var visits = new List<JudgedSession>();

        await using var reader = await ExecuteAsync(
                AnalyticsSqlCompiler.Compile(scope, query),
                cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            visits.Add(ToJudgedSession(reader));
        }

        return visits;
    }

    private static JudgedSession ToJudgedSession(ClickHouseDataReader reader)
    {
        var evidence = ToEvidence(reader);

        return new JudgedSession
        {
            SessionKey = reader.GetString(0),
            StartedAt = reader.GetDateTimeOffset(1),
            EndedAt = reader.GetDateTimeOffset(2),
            PageCount = (int)reader.GetFieldValue<uint>(3),
            Surfaces =
            [
                .. reader.GetFieldValue<string[]>(4).Select(name =>
                    StoredNames.Surfaces.TryGetValue(name, out var surface) ? surface : IngestSurface.Unknown),
            ],
            Verdict = new ClassificationVerdict
            {
                Category = StoredNames.Categories[reader.GetString(5)],
                Strength = StoredNames.Strengths[reader.GetString(6)],
                IsProvisional = reader.GetBoolean(7),
                RulesetVersion = new RulesetVersion(
                    reader.GetFieldValue<ushort>(8),
                    reader.GetFieldValue<ushort>(9)),
                Supporting = [.. evidence.Where(entry => entry.Supporting).Select(entry => entry.Signal)],
                Contradicting = [.. evidence.Where(entry => !entry.Supporting).Select(entry => entry.Signal)],
            },
        };
    }

    /// <summary>
    /// Rebuilds the evidence list from the parallel arrays it is stored as.
    /// </summary>
    /// <remarks>
    /// Read back as signals rather than as sentences, because the sentence is produced from the
    /// message catalogue in the reader's language. A stored English string could not be shown to
    /// somebody reading in another one, and could not be corrected without rewriting history.
    /// </remarks>
    private static ImmutableArray<(Signal Signal, bool Supporting)> ToEvidence(ClickHouseDataReader reader)
    {
        var codes = reader.GetFieldValue<string[]>(10);
        var directions = reader.GetFieldValue<string[]>(11);
        var weights = reader.GetFieldValue<byte[]>(12);
        var supporting = reader.GetFieldValue<bool[]>(13);
        var parameters = reader.GetFieldValue<Dictionary<string, string>[]>(14);

        var evidence = ImmutableArray.CreateBuilder<(Signal, bool)>(codes.Length);

        for (var index = 0; index < codes.Length; index++)
        {
            evidence.Add((
                new Signal
                {
                    Code = codes[index],
                    Direction = StoredNames.Directions[directions[index]],
                    Weight = weights[index],
                    Parameters = parameters[index].ToFrozenDictionary(StringComparer.Ordinal),
                },
                supporting[index]));
        }

        return evidence.DrainToImmutable();
    }

    private async Task<ClickHouseDataReader> ExecuteAsync(
        CompiledStatement statement,
        CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();

        foreach (var parameter in statement.Parameters)
        {
            parameters.AddParameter(parameter.Name, parameter.Value);
        }

        return await client.ExecuteReaderAsync(statement.Sql, parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

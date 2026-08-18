using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;

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

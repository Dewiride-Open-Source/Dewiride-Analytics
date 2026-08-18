using ClickHouse.Driver;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Health;

/// <summary>
/// Reports whether the telemetry store can be reached and carries its schema.
/// </summary>
/// <remarks>
/// The probe reads the migration history rather than issuing a bare <c>SELECT 1</c>, because a
/// server that is listening on an empty database is exactly the state in which the collector
/// accepts events and loses every one of them. Answering "not ready" until the schema is in place
/// is what keeps a half-started stack from quietly discarding traffic.
/// </remarks>
/// <param name="client">Telemetry store client.</param>
public sealed class TelemetryStoreHealthCheck(IClickHouseClient client) : IHealthCheck
{
    /// <summary>Name this check is registered under.</summary>
    public const string Name = "telemetry-store";

    private const string ProbeSql = "SELECT count() FROM schema_migrations";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var reader = await client
            .ExecuteReaderAsync(ProbeSql, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return HealthCheckResult.Unhealthy("The telemetry store returned no result for the schema probe.");
        }

        return reader.GetUInt64(0) > 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The telemetry store has no schema applied.");
    }
}

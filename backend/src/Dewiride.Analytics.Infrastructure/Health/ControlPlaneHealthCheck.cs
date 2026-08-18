using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dewiride.Analytics.Infrastructure.Health;

/// <summary>
/// Reports whether the control-plane database can be reached.
/// </summary>
/// <remarks>
/// Deliberately a connection test and nothing more. A readiness probe runs every few seconds for
/// the life of the process, so anything that queries real data turns a health check into a
/// standing load; and a store that answers at all is the distinction this probe exists to draw.
/// </remarks>
/// <param name="database">Control-plane database.</param>
public sealed class ControlPlaneHealthCheck(ControlPlaneDbContext database) : IHealthCheck
{
    /// <summary>Name this check is registered under.</summary>
    public const string Name = "control-plane";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var reachable = await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

        return reachable
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The control-plane database did not accept a connection.");
    }
}

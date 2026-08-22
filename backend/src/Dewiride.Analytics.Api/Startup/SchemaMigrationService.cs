using Dewiride.Analytics.Application.Persistence;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Api.Startup;

/// <summary>
/// Brings both stores' schemas up to date before the process accepts a request.
/// </summary>
/// <remarks>
/// <para>
/// Runs during the starting phase, so it completes before the web server begins listening. A
/// collector that answers while the telemetry store has no table would accept reports and lose
/// every one of them, and it would do so silently.
/// </para>
/// <para>
/// The two systems run one after the other and never interleave: Entity Framework Core migrations
/// for the control plane, ordered SQL scripts for the telemetry store. Neither knows the other
/// exists, and a change that needs both ships a file in each.
/// </para>
/// <para>
/// Failure is fatal on purpose. There is no sensible half-migrated state to serve traffic from,
/// and a container that stops with the reason in its log is far easier to diagnose than one that
/// starts and behaves strangely.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the scope the control-plane context is resolved from.</param>
/// <param name="telemetrySchema">Runner for the telemetry store's scripts.</param>
/// <param name="options">Whether migration on start-up is enabled.</param>
/// <param name="logger">Log sink.</param>
internal sealed partial class SchemaMigrationService(
    IServiceScopeFactory scopeFactory,
    ClickHouseMigrationRunner telemetrySchema,
    IOptions<SchemaOptions> options,
    ILogger<SchemaMigrationService> logger) : IHostedLifecycleService
{
    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyOnStartup)
        {
            Log.MigrationSkipped(logger);
            return;
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        Log.ControlPlaneReady(logger);

        await telemetrySchema.ApplyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Information,
            Message = "Control-plane schema is up to date.")]
        public static partial void ControlPlaneReady(ILogger logger);

        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Warning,
            Message = "Schema migration on start-up is switched off. Both stores must already be "
                + "at the schema this build expects.")]
        public static partial void MigrationSkipped(ILogger logger);
    }
}

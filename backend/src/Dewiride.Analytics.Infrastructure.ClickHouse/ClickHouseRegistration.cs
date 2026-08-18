using System.Net;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Dewiride.Analytics.Application.Abstractions;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;
using Dewiride.Analytics.Infrastructure.ClickHouse.Health;
using Dewiride.Analytics.Infrastructure.ClickHouse.Migrations;
using Dewiride.Analytics.Infrastructure.ClickHouse.Sessions;
using Dewiride.Analytics.Infrastructure.ClickHouse.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dewiride.Analytics.Infrastructure.ClickHouse;

/// <summary>
/// Registers the telemetry store and the services that read and write it.
/// </summary>
public static class ClickHouseRegistration
{
    /// <summary>
    /// Configuration key holding the telemetry store connection string.
    /// </summary>
    public const string TelemetryConnectionName = "Telemetry";

    /// <summary>
    /// Name of the pooled HTTP client the store's traffic runs over.
    /// </summary>
    private const string HttpClientName = "dewiride-telemetry";

    /// <summary>
    /// Adds the telemetry store client, the migration runner, the event sink and the query reader.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static IHostApplicationBuilder AddTelemetryStore(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString(TelemetryConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{TelemetryConnectionName}' is not configured. "
                + "Set ConnectionStrings__Telemetry in the environment.");

        // The client asks the server to compress result sets, which is worth having on an
        // analytical store but only works if the handler unpacks them. A handler supplied by the
        // factory does not do that by default, and the failure appears as unreadable results
        // rather than as a configuration error.
        // Connection recycling is left to the handler's own pooled-connection lifetime, which
        // picks up a changed address without throwing away a warm connection on a timer.
        builder.Services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // Registered by whichever component needs it first, so that each is self-sufficient and
        // a test can still substitute a controlled clock for the whole host.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // One client for the process. It holds the connection pool and a cache of table schemas,
        // both of which are wasted if it is rebuilt per request.
        builder.Services.AddSingleton<IClickHouseClient>(provider => new ClickHouseClient(
            new ClickHouseClientSettings(connectionString)
            {
                HttpClientFactory = provider.GetRequiredService<IHttpClientFactory>(),
                HttpClientName = HttpClientName,
                LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
            }));

        builder.Services.AddSingleton<ClickHouseMigrationRunner>();
        builder.Services.AddSingleton<IEventSink, ClickHouseEventSink>();
        builder.Services.AddSingleton<ITelemetryQueries, ClickHouseTelemetryQueries>();
        builder.Services.AddSingleton<ISessionSource, ClickHouseSessionSource>();
        builder.Services.AddSingleton<IClassificationStore, ClickHouseClassificationStore>();

        builder.Services.AddHealthChecks()
            .AddCheck<TelemetryStoreHealthCheck>(
                TelemetryStoreHealthCheck.Name,
                tags: [HealthCheckTags.Readiness]);

        return builder;
    }
}

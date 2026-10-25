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
    /// Name of the pooled HTTP client every request but the schema migrations runs over.
    /// </summary>
    private const string HttpClientName = "dewiride-telemetry";

    /// <summary>
    /// Name of the HTTP client the schema migrations run over.
    /// </summary>
    internal const string SchemaHttpClientName = "dewiride-telemetry-schema";

    /// <summary>
    /// Key the telemetry client the schema migrations run through is registered under.
    /// </summary>
    internal const string SchemaClientKey = "telemetry-schema";

    /// <summary>
    /// How long one statement of a schema migration may run before the engine gives up on it.
    /// </summary>
    /// <remarks>
    /// Everything else the engine asks the store is held to the HTTP client's default of a hundred
    /// seconds, which is generous for a question asked by a screen. A migration that rewrites or
    /// copies data reads a whole table instead, and a script is recorded as applied only once all
    /// of it has finished — so a client that gave up sooner would fail every start-up of an
    /// installation large enough, each attempt beginning the same statement again. An hour bounds
    /// a statement that has stuck without failing one that is merely large.
    /// </remarks>
    internal static readonly TimeSpan LongestSchemaStatement = TimeSpan.FromHours(1);

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

        AddStoreHttpClient(builder.Services, HttpClientName);
        AddStoreHttpClient(builder.Services, SchemaHttpClientName)
            .ConfigureHttpClient(http => http.Timeout = LongestSchemaStatement);

        // Registered by whichever component needs it first, so that each is self-sufficient and
        // a test can still substitute a controlled clock for the whole host.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // One client for every question the engine asks outside the schema migrations. It holds a
        // cache of table schemas, which would be wasted if it were rebuilt per request.
        builder.Services.AddSingleton<IClickHouseClient>(
            provider => CreateClient(provider, connectionString, HttpClientName));

        // The migrations have a client of their own, so the time they are allowed is decided for
        // them alone rather than for every question a screen asks.
        builder.Services.AddKeyedSingleton<IClickHouseClient>(
            SchemaClientKey,
            (provider, _) => CreateClient(provider, connectionString, SchemaHttpClientName));

        builder.Services.AddSingleton(provider => new ClickHouseMigrationRunner(
            provider.GetRequiredKeyedService<IClickHouseClient>(SchemaClientKey),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ClickHouseMigrationRunner>>()));
        builder.Services.AddSingleton<IEventSink, ClickHouseEventSink>();
        builder.Services.AddSingleton<ITelemetryQueries, ClickHouseTelemetryQueries>();
        builder.Services.AddSingleton<ITelemetryPurge, ClickHouseTelemetryPurge>();
        builder.Services.AddSingleton<ISiteVolume, ClickHouseSiteVolume>();
        builder.Services.AddSingleton<ISessionSource, ClickHouseSessionSource>();
        builder.Services.AddSingleton<IClassificationStore, ClickHouseClassificationStore>();
        builder.Services.AddSingleton<INameConfirmationStore, ClickHouseNameConfirmationStore>();

        builder.Services.AddHealthChecks()
            .AddCheck<TelemetryStoreHealthCheck>(
                TelemetryStoreHealthCheck.Name,
                tags: [HealthCheckTags.Readiness]);

        return builder;
    }

    /// <summary>
    /// Adds one named HTTP client the store is reached over.
    /// </summary>
    /// <remarks>
    /// The client asks the store for compressed result sets, which is worth having on an analytical
    /// store, and unpacks them itself. The handler is left without automatic decompression on
    /// purpose: a handler that unpacks adds its own codecs to every request's Accept-Encoding,
    /// widening the set the client chose, and does for some encodings what the client already does
    /// for all of them. Connection recycling is left to the handler's own pooled-connection
    /// lifetime, which picks up a changed address without throwing away a warm connection on a
    /// timer.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name.</param>
    /// <returns>The client's builder, for anything only this client needs.</returns>
    private static IHttpClientBuilder AddStoreHttpClient(IServiceCollection services, string name) =>
        services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Creates a client of the store over one of the named HTTP clients.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="connectionString">The store's connection string.</param>
    /// <param name="httpClientName">Which HTTP client its traffic runs over.</param>
    /// <returns>The client.</returns>
    private static ClickHouseClient CreateClient(
        IServiceProvider provider,
        string connectionString,
        string httpClientName) =>
        new(new ClickHouseClientSettings(connectionString)
        {
            HttpClientFactory = provider.GetRequiredService<IHttpClientFactory>(),
            HttpClientName = httpClientName,
            LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
        });
}

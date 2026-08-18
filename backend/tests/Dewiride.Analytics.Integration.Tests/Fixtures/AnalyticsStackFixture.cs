using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Starts both stores and boots the host against them.
/// </summary>
/// <remarks>
/// <para>
/// The images are pinned to the versions the product is built for. The ClickHouse module's own
/// default is three major versions behind, which would leave the suite proving that the schema
/// works on an engine nobody runs.
/// </para>
/// <para>
/// The tuning files that ship with the product are mounted exactly as the compose stack mounts
/// them. That is deliberate: untuned, ClickHouse claims a five-gigabyte mark cache and most of
/// the host's memory, and a contributor's first test run gets killed. Mounting them here also
/// makes this suite the proof that those files still let the server start, which is the failure
/// they are most likely to cause.
/// </para>
/// </remarks>
public sealed class AnalyticsStackFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ControlPlaneImage = "postgres:18-alpine";
    private const string TelemetryImage = "clickhouse/clickhouse-server:26.3-alpine";

    private readonly PostgreSqlContainer _controlPlane = new PostgreSqlBuilder(ControlPlaneImage).Build();

    private readonly ClickHouseContainer _telemetry = new ClickHouseBuilder(TelemetryImage)
        .WithResourceMapping(
            RepositoryPaths.File("config/clickhouse/low-resources.xml"),
            "/etc/clickhouse-server/config.d/")
        .WithResourceMapping(
            RepositoryPaths.File("config/clickhouse/low-resources-profile.xml"),
            "/etc/clickhouse-server/users.d/")
        .Build();

    /// <summary>Connection string for the control-plane database.</summary>
    public string ControlPlaneConnectionString => _controlPlane.GetConnectionString();

    /// <summary>Connection string for the telemetry store.</summary>
    public string TelemetryConnectionString => _telemetry.GetConnectionString();

    /// <summary>Starts both containers and brings the host up against them.</summary>
    /// <returns>A task that completes once the stack is serving.</returns>
    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_controlPlane.StartAsync(), _telemetry.StartAsync()).ConfigureAwait(false);

        // Touching the provider builds the host, which applies both schemas from empty before any
        // test runs. Doing it here rather than lazily keeps a migration failure attributable to
        // the stack instead of to whichever test happened to run first.
        _ = Services;
    }

    /// <summary>Stops the host and both containers.</summary>
    /// <returns>A task that completes once everything has been torn down.</returns>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _telemetry.DisposeAsync().ConfigureAwait(false);
        await _controlPlane.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureRegistration.ControlPlaneConnectionName}",
            ControlPlaneConnectionString);

        builder.UseSetting(
            $"ConnectionStrings:{ClickHouseRegistration.TelemetryConnectionName}",
            TelemetryConnectionString);

        // Every request in the suite arrives from the same nonexistent address, so the sign-in
        // allowance — counted per address — would be spent partway through the run and every
        // later test would be turned away. Raised here and proved separately, on a host built
        // for the purpose with a deliberately tiny allowance.
        builder.UseSetting(TestSettings.SignInAllowance, TestSettings.NoPracticalLimit);
    }
}

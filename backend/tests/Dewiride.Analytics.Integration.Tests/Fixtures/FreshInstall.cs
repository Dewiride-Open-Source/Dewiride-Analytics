using System.Data.Common;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A second copy of the product with a control-plane database nobody has ever signed in to.
/// </summary>
/// <remarks>
/// <para>
/// First-run setup is the one moment at which an unauthenticated caller may create an account,
/// and it can only be observed on an install where no account exists. The shared stack cannot
/// provide that: by the time any test runs, other tests have created accounts on it, and the
/// window has closed permanently.
/// </para>
/// <para>
/// So this creates a brand-new database on the PostgreSQL server the shared stack already has
/// running and boots the host against it, alongside the same telemetry store. Starting another
/// pair of containers would cost twenty seconds a test to prove the same thing.
/// </para>
/// <para>
/// The database is left behind when the host is disposed. Removing it would mean forcing every
/// pooled connection shut during teardown, where a failure would be reported against whichever
/// test happened to be finishing; the container is discarded at the end of the run regardless.
/// </para>
/// </remarks>
internal sealed class FreshInstall : WebApplicationFactory<Program>
{
    private readonly string _controlPlane;
    private readonly string _telemetry;

    private FreshInstall(string controlPlane, string telemetry)
    {
        _controlPlane = controlPlane;
        _telemetry = telemetry;
    }

    /// <summary>
    /// Creates an empty control-plane database and brings a host up against it.
    /// </summary>
    /// <param name="stack">The running stack, whose servers are reused.</param>
    /// <returns>The host, serving an install nobody has claimed.</returns>
    public static async Task<FreshInstall> StartAsync(AnalyticsStackFixture stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var name = $"install_{Guid.NewGuid():n}";

        await using (var scope = stack.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            // A database name cannot be a bound parameter in any statement, so this one is built
            // by hand. What goes into it is thirty-two hexadecimal characters behind a fixed
            // prefix, generated a line above, and never seen by anything outside this method.
            var create = string.Concat("CREATE DATABASE \"", name, "\"");

            await database.Database
                .ExecuteSqlRawAsync(create, Cancellation.Token)
                .ConfigureAwait(false);
        }

        var connectionString = WithDatabase(stack.ControlPlaneConnectionString, name);
        var install = new FreshInstall(connectionString, stack.TelemetryConnectionString);

        // Touching the provider builds the host, which applies the control-plane schema to the
        // new database before the first request reaches it.
        _ = install.Services;

        return install;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureRegistration.ControlPlaneConnectionName}",
            _controlPlane);

        builder.UseSetting(
            $"ConnectionStrings:{ClickHouseRegistration.TelemetryConnectionName}",
            _telemetry);

        builder.UseSetting(TestSettings.SignInAllowance, TestSettings.NoPracticalLimit);
    }

    /// <summary>
    /// Points a connection string at a different database on the same server.
    /// </summary>
    /// <remarks>
    /// Built with the framework's own parser rather than by editing the text, so that a value
    /// needing quotes — which a generated password routinely does — survives the round trip.
    /// </remarks>
    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder["Database"] = database;

        return builder.ConnectionString;
    }
}

using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dewiride.Analytics.Migrations.Postgres;

/// <summary>
/// Builds the control-plane context for the Entity Framework Core command-line tools.
/// </summary>
/// <remarks>
/// <para>
/// Without this, adding a migration would mean starting the whole web host, which in turn means
/// a reachable telemetry store and a populated configuration — none of which has anything to do
/// with generating a schema diff. The factory keeps schema work possible from a clean checkout.
/// </para>
/// <para>
/// The options must match the ones the host applies, or the generated model snapshot describes a
/// database the product never creates. Adding an option in
/// <see cref="InfrastructureRegistration.AddControlPlane"/> means adding it here too.
/// </para>
/// </remarks>
public sealed class DesignTimeControlPlaneDbContextFactory
    : IDesignTimeDbContextFactory<ControlPlaneDbContext>
{
    /// <summary>
    /// Environment variable holding the connection string to use for design-time commands that
    /// reach the database, such as applying a migration or reverse-engineering one.
    /// </summary>
    private const string ConnectionVariable = "DEWIRIDE_DESIGNTIME_CONNECTION";

    /// <summary>
    /// Used when the variable is unset. Generating a migration never opens a connection, so this
    /// only has to describe a valid target; it carries no credentials and is not a fallback the
    /// running product ever uses.
    /// </summary>
    private const string UnconnectedPlaceholder =
        "Host=localhost;Port=5432;Database=dewiride_analytics;Username=dewiride";

    /// <inheritdoc />
    public ControlPlaneDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(
                string.IsNullOrWhiteSpace(connectionString) ? UnconnectedPlaceholder : connectionString,
                npgsql => npgsql
                    .MigrationsHistoryTable("__ef_migrations_history")
                    .MigrationsAssembly(InfrastructureRegistration.MigrationsAssemblyName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ControlPlaneDbContext(options);
    }
}

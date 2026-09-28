using Dewiride.Analytics.Api.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Dewiride.Analytics.Api.Tests.Observability;

/// <summary>
/// What the engine calls itself in the telemetry it exports.
/// </summary>
/// <remarks>
/// The host seeds a name of its own, the assembly's, beneath the one the engine states. An
/// operator's collector files everything under whichever of the two wins, so a dashboard or an
/// alert written against the engine's name would go quiet rather than red the day the host's took
/// over.
/// </remarks>
public sealed class ObservabilityTests
{
    /// <summary>
    /// A well-known environment is reported in the lower case the semantic conventions spell it in,
    /// whatever the host's own spelling of it.
    /// </summary>
    [Fact]
    public void The_engine_reports_under_its_own_name_and_the_environment_it_runs_in()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(ObservabilityRegistration).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
        });

        builder.AddObservability();

        using var services = builder.Services.BuildServiceProvider();

        var reported = services.GetRequiredService<TracerProvider>()
            .GetResource()
            .Attributes
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        reported.Should().Contain("service.name", "dewiride-analytics-api");
        reported.Should().Contain("deployment.environment.name", "production");
    }
}

using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure;
using Dewiride.Analytics.Infrastructure.ClickHouse;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// A copy of the product whose accounts have no room for another website.
/// </summary>
/// <remarks>
/// <para>
/// The open-source edition has no such limit — an installation somebody runs themselves measures
/// whatever they point at it — so the only way to reach the refusal here is to answer the allowance
/// the way an edition that sells room would. What is being proved is the open-source half: that the
/// endpoint turns that answer into a refusal a person can read, and that nothing is written on the
/// way to saying no.
/// </para>
/// <para>
/// It shares the running stack's stores, so a site added through one is visible to the other.
/// </para>
/// </remarks>
internal sealed class FullInstall : WebApplicationFactory<Program>
{
    private readonly string _controlPlane;
    private readonly string _telemetry;

    private FullInstall(string controlPlane, string telemetry)
    {
        _controlPlane = controlPlane;
        _telemetry = telemetry;
    }

    /// <summary>
    /// Brings a host up against the running stack, with no room left for another website.
    /// </summary>
    /// <param name="stack">The running stack, whose stores are shared.</param>
    /// <returns>The host, ready to answer.</returns>
    public static FullInstall Start(AnalyticsStackFixture stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var install = new FullInstall(stack.ControlPlaneConnectionString, stack.TelemetryConnectionString);

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
        builder.UseSetting(TestSettings.BackgroundJudging, "false");

        // Runs after the product has registered its own, so this is the one that answers.
        builder.ConfigureTestServices(services => services.AddSingleton<ISiteAllowance, NoRoom>());
    }
}

/// <summary>An allowance with nothing left in it.</summary>
internal sealed class NoRoom : ISiteAllowance
{
    /// <inheritdoc />
    public Task<bool> AllowsAnotherAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

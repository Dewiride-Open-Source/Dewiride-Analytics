using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Extensibility;
using Dewiride.Analytics.Infrastructure.Entitlements;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dewiride.Analytics.Community;

/// <summary>
/// Composes the open-source edition.
/// </summary>
/// <remarks>
/// There is very little to compose. Accounts, roles and the membership check behind them are part
/// of the free product and are registered with everything else, because an installation somebody
/// runs themselves is reachable from the internet and needs the same lock on its door. What is
/// settled here is the one question the two editions genuinely answer differently: whether
/// anything is being rationed.
/// </remarks>
public sealed class CommunityEditionModule : IEditionModule
{
    /// <inheritdoc />
    public string EditionName => "Community";

    /// <inheritdoc />
    public void Register(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // One instance answering both allowances, because on a self-hosted install they are one
        // decision: whoever runs the machine decides what it measures, and there is nobody for the
        // product to ration them on behalf of.
        builder.Services.AddSingleton<UnmeteredInstallation>();
        builder.Services.AddSingleton<IMeasurementAllowance>(
            provider => provider.GetRequiredService<UnmeteredInstallation>());
        builder.Services.AddSingleton<ISiteAllowance>(
            provider => provider.GetRequiredService<UnmeteredInstallation>());
    }
}

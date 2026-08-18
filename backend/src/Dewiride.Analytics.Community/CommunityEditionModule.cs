using Dewiride.Analytics.Application.Abstractions;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dewiride.Analytics.Community;

/// <summary>
/// Composes the open-source edition.
/// </summary>
/// <remarks>
/// A self-hosted install has one organisation, so scope resolution only has to establish which
/// role the signed-in person holds on the site being read. Membership is still checked: a
/// self-hosted install can have several people with different roles, and an edition whose
/// authorisation is weaker than the hosted one while running the same screens is a security
/// advisory waiting to happen, not a feature difference.
/// </remarks>
public sealed class CommunityEditionModule : IEditionModule
{
    /// <inheritdoc />
    public string EditionName => "Community";

    /// <inheritdoc />
    public void Register(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<ITenantScopeProvider, SingleTenantScopeProvider>();
    }
}

using Microsoft.AspNetCore.Routing;

namespace Dewiride.Analytics.Extensibility;

/// <summary>
/// Adds an edition's own endpoints to the host's routing table.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IEditionModule"/> because the two happen at different moments: services
/// are registered while the container is still being built, and routes are mapped against an
/// application that has already been built from it. An edition supplies exactly one module and any
/// number of these, so a group of related endpoints is a class of its own rather than another
/// paragraph in one method.
/// </para>
/// <para>
/// The open-source edition supplies none: everything a self-hosted install can do is answered by
/// the host's own endpoints. Finding none is therefore an ordinary outcome and not a misconfigured
/// build, which is the one way this differs from the module contract beside it.
/// </para>
/// <para>
/// Every endpoint added here is closed unless it says otherwise — the host runs a fallback
/// authorisation policy demanding a signed-in caller — and anything that changes something must
/// carry <see cref="AntiforgeryGuard.RequireProofOfOrigin"/>. An edition that opened an endpoint
/// with weaker rules than the host's own would be a security advisory rather than a feature.
/// </para>
/// </remarks>
public interface IEditionEndpoints
{
    /// <summary>
    /// Maps the endpoints.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    void Map(IEndpointRouteBuilder routes);
}

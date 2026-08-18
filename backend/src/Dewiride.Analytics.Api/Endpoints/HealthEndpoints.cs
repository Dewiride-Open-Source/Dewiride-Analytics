using Dewiride.Analytics.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// The two probes an orchestrator uses to decide what to do with this process.
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>
    /// Maps the liveness and readiness probes.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapHealth(this IEndpointRouteBuilder routes)
    {
        // Liveness answers one question: is this process still able to handle a request at all?
        // It deliberately runs no checks, because the only correct response to it failing is to
        // restart the process, and no store being unreachable is ever fixed by that.
        routes.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .ExcludeFromDescription();

        routes.MapHealthChecks(
                "/health/ready",
                new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Readiness) })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}

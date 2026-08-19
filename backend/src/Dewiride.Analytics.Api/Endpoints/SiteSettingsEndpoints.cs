using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Security;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading and changing what a website collects.
/// </summary>
/// <remarks>
/// <para>
/// A setting here decides what is written into the telemetry store, so changing one is restricted
/// to somebody who may already change how the site is measured. Reading is not: what a site
/// collects is something anybody who can see its numbers ought to be able to check.
/// </para>
/// <para>
/// A site that does not exist and a site the caller has no role on are answered identically, as
/// everywhere else, so this cannot be used to discover which sites on an install are real.
/// </para>
/// </remarks>
internal static class SiteSettingsEndpoints
{
    /// <summary>
    /// Maps reading and changing a site's collection settings.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapSiteSettings(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/settings", ReadAsync)
            .WithName("SiteSettings")
            .WithSummary("Returns what a website collects.");

        routes.MapPut("/api/sites/{siteId:guid}/settings", ApplyAsync)
            .WithName("UpdateSiteSettings")
            .WithSummary("Changes what a website collects.")
            .WithDescription(
                "Every setting is optional and one that is left out is left as it was. A change "
                + "takes effect on the next report, not on what has already been collected.")
            .RequireProofOfOrigin();
    }

    private static async Task<Results<Ok<SiteSettingsResponse>, NotFound>> ReadAsync(
        Guid siteId,
        ITenantScopeProvider scopes,
        ISiteSettings settings,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(siteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var found = await settings.DescribeAsync(siteId, cancellationToken).ConfigureAwait(false);

        return found is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SiteSettingsResponse(found.Value.CaptureClicks));
    }

    private static async Task<Results<Ok<SiteSettingsResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ApplyAsync(
        Guid siteId,
        UpdateSiteSettingsRequest? request,
        ITenantScopeProvider scopes,
        ISiteSettings settings,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(siteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        if (!MaySettle(scope))
        {
            return TypedResults.Forbid();
        }

        var current = await settings.DescribeAsync(siteId, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return TypedResults.NotFound();
        }

        if (request is null)
        {
            return Unusable();
        }

        var wanted = new CollectionSettings(request.CaptureClicks ?? current.Value.CaptureClicks);
        var applied = await settings.ApplyAsync(siteId, wanted, cancellationToken).ConfigureAwait(false);

        return applied is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SiteSettingsResponse(applied.Value.CaptureClicks));
    }

    /// <summary>
    /// Whether the caller may change how a site is measured.
    /// </summary>
    private static bool MaySettle(TenantScope scope) => scope.Role >= SiteRole.Editor;

    private static ProblemHttpResult Unusable() =>
        TypedResults.Problem(
            title: "Those settings could not be saved.",
            detail: "Send a JSON object naming at least one setting to change.",
            statusCode: StatusCodes.Status400BadRequest);
}

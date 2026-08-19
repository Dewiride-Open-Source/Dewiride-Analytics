using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Security;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Reading and changing a website's own settings.
/// </summary>
/// <remarks>
/// <para>
/// What is settled here decides how a website is named, when its days begin and what is written
/// into the telemetry store for it, so changing any of it is restricted to somebody who may
/// already change how the site is measured. Reading is not: how a site is set up is something
/// anybody who can see its numbers ought to be able to check.
/// </para>
/// <para>
/// A site that does not exist and a site the caller has no role on are answered identically, as
/// everywhere else, so this cannot be used to discover which sites on an install are real.
/// </para>
/// </remarks>
internal static class SiteSettingsEndpoints
{
    /// <summary>Names the reason a name is not one a website can be shown under.</summary>
    private const string NameRejectedCode = "SiteNameRejected";

    /// <summary>Names the reason a time zone is not one this installation knows.</summary>
    private const string TimeZoneRejectedCode = "SiteTimeZoneRejected";

    /// <summary>
    /// Maps reading and changing a site's settings.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapSiteSettings(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/settings", ReadAsync)
            .WithName("SiteSettings")
            .WithSummary("Returns how a website is set up and what it collects.");

        routes.MapPut("/api/sites/{siteId:guid}/settings", ApplyAsync)
            .WithName("UpdateSiteSettings")
            .WithSummary("Changes how a website is set up and what it collects.")
            .WithDescription(
                "Every setting is optional and one that is left out is left as it was. A change "
                + "to what is collected takes effect on the next report, not on what has already "
                + "been collected.")
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
            : TypedResults.Ok(Describe(found.Value));
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

        if (request is null)
        {
            return Unusable();
        }

        var amendment = new SiteAmendment(request.DisplayName, request.TimeZoneId, request.CaptureClicks);
        var change = await settings.ApplyAsync(siteId, amendment, cancellationToken).ConfigureAwait(false);

        return change.Outcome switch
        {
            SiteChangeOutcome.Applied when change.Profile is { } profile => TypedResults.Ok(Describe(profile)),
            SiteChangeOutcome.NameRejected => NameRejected(),
            SiteChangeOutcome.TimeZoneRejected => TimeZoneRejected(),
            _ => TypedResults.NotFound(),
        };
    }

    private static SiteSettingsResponse Describe(SiteProfile profile) =>
        new(profile.DisplayName, profile.TimeZoneId, profile.CaptureClicks);

    /// <summary>
    /// Whether the caller may change how a site is measured.
    /// </summary>
    private static bool MaySettle(TenantScope scope) => scope.Role >= SiteRole.Editor;

    private static ProblemHttpResult Unusable() =>
        TypedResults.Problem(
            title: "Those settings could not be saved.",
            detail: "Send a JSON object naming the settings to change.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NameRejected() =>
        Refused(
            "That name could not be saved.",
            new RefusedReason(
                NameRejectedCode,
                $"Give the website a name of up to {Site.MaxDisplayNameLength} characters."));

    private static ProblemHttpResult TimeZoneRejected() =>
        Refused(
            "That time zone could not be saved.",
            new RefusedReason(
                TimeZoneRejectedCode,
                "Choose the time zone the website's days should be counted in."));

    /// <summary>
    /// Refuses with a reason the dashboard can write its own sentence for.
    /// </summary>
    /// <param name="title">What went wrong, in one line.</param>
    /// <param name="reason">The specific reason.</param>
    /// <returns>The refusal.</returns>
    private static ProblemHttpResult Refused(string title, RefusedReason reason) =>
        TypedResults.Problem(
            title: title,
            detail: reason.Description,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["problems"] = new[] { reason },
            });
}

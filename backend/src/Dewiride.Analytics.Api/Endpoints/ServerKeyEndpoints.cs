using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Security;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Tenancy;
using Dewiride.Analytics.Domain.Sites;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Managing the keys a site's own server reports with.
/// </summary>
/// <remarks>
/// <para>
/// A key authorises writing traffic into one site's telemetry with an asserted visitor address,
/// so creating one is a change to what the numbers mean and is restricted to somebody who may
/// already change the site's settings. Reading the list is not: it holds no secret, only the
/// names and the last few characters, which is what somebody needs to work out whether an
/// integration is still reporting.
/// </para>
/// <para>
/// A site that does not exist and a site the caller has no role on are answered identically, as
/// everywhere else, so this cannot be used to discover which sites on an install are real.
/// </para>
/// </remarks>
internal static class ServerKeyEndpoints
{
    /// <summary>
    /// Maps listing, creating and withdrawing a site's server keys.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapServerKeys(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/sites/{siteId:guid}/server-keys", ListAsync)
            .WithName("ListServerKeys")
            .WithSummary("Lists the keys that may report traffic for a website.");

        routes.MapPost("/api/sites/{siteId:guid}/server-keys", IssueAsync)
            .WithName("IssueServerKey")
            .WithSummary("Creates a key for a website and returns it once.")
            .WithDescription(
                "The secret is in this response and nowhere else. It is stored only as a hash, so "
                + "it cannot be shown again.")
            .RequireProofOfOrigin();

        routes.MapDelete("/api/sites/{siteId:guid}/server-keys/{keyId:guid}", RevokeAsync)
            .WithName("RevokeServerKey")
            .WithSummary("Withdraws a key, so nothing can report with it again.")
            .RequireProofOfOrigin();
    }

    private static async Task<Results<Ok<IReadOnlyList<ServerKeySummary>>, NotFound>> ListAsync(
        Guid siteId,
        ITenantScopeProvider scopes,
        IIngestKeyDirectory keys,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(siteId, cancellationToken).ConfigureAwait(false);

        if (scope is null)
        {
            return TypedResults.NotFound();
        }

        var found = await keys.ListAsync(siteId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ServerKeySummary> summaries = [.. found.Select(Describe)];

        return TypedResults.Ok(summaries);
    }

    private static async Task<Results<Ok<IssuedServerKey>, NotFound, ForbidHttpResult, ProblemHttpResult>> IssueAsync(
        Guid siteId,
        CreateServerKeyRequest? request,
        ITenantScopeProvider scopes,
        IIngestKeyDirectory keys,
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

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Unusable();
        }

        var issued = await keys.IssueAsync(siteId, request.Name, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new IssuedServerKey(Describe(issued.Description), issued.Secret));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> RevokeAsync(
        Guid siteId,
        Guid keyId,
        ITenantScopeProvider scopes,
        IIngestKeyDirectory keys,
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

        var withdrawn = await keys.RevokeAsync(siteId, keyId, cancellationToken).ConfigureAwait(false);

        return withdrawn ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    /// <summary>
    /// Whether the caller may change how a site is measured.
    /// </summary>
    private static bool MaySettle(TenantScope scope) => scope.Role >= SiteRole.Editor;

    private static ServerKeySummary Describe(IngestKeyDescription key) =>
        new(key.Id, key.Name, key.Preview, key.CreatedAt, key.LastUsedAt);

    private static ProblemHttpResult Unusable() =>
        TypedResults.Problem(
            title: "That key could not be created.",
            detail: "Give the key a name, so it can be told apart from the others later.",
            statusCode: StatusCodes.Status400BadRequest);
}

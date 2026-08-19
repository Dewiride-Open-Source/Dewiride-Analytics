using System.Text.Json;
using Dewiride.Analytics.Api.Configuration;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Ingest;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// The public collection endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Unauthenticated by design: it is called from a script running on the customer's own pages, so
/// there is no secret it could hold that a visitor could not read. What protects it instead is
/// that a site accepts reports only from the origins it has declared, that the body is capped,
/// and that one address can only send so many reports a minute.
/// </para>
/// <para>
/// The response says nothing. A report that names a site that does not exist gets the same empty
/// answer as one that is stored, so the endpoint cannot be used to find out which site
/// identifiers are real. A malformed body is the one exception and answers plainly, because that
/// is a mistake by whoever is writing an integration and telling them nothing helps nobody.
/// </para>
/// </remarks>
internal static class CollectEndpoint
{
    /// <summary>Name of the cross-origin policy this endpoint runs under.</summary>
    public const string CorsPolicyName = "collector";

    /// <summary>Name of the rate-limiting policy this endpoint runs under.</summary>
    public const string RateLimitPolicyName = "collector";

    /// <summary>
    /// Maps the collection endpoint.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapCollect(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/collect", HandleAsync)
            .WithName("Collect")
            .WithSummary("Records one observation of activity on a measured site.")
            .WithDescription(
                "Called by the tracker and by every server-side capture surface. Always answers "
                + "with an empty response unless the payload itself is malformed.")
            .RequireCors(CorsPolicyName)
            .RequireRateLimiting(RateLimitPolicyName)
            .AllowAnonymous();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        HttpContext context,
        EventIngestor ingestor,
        IOptions<CollectorOptions> options,
        CancellationToken cancellationToken)
    {
        var maxBytes = options.Value.MaxRequestBytes;

        if (context.Request.ContentLength > maxBytes)
        {
            return TooLarge();
        }

        CapBodySize(context, maxBytes);

        CollectRequest? request;

        try
        {
            request = await JsonSerializer.DeserializeAsync(
                    context.Request.Body,
                    ApiJsonContext.Default.CollectRequest,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Malformed();
        }
        catch (BadHttpRequestException)
        {
            // A body that ran past the cap while it was being read. Answered here rather than
            // left to escape, because an unhandled exception would be reported as a server fault
            // and would write a stack trace for every oversized request — a log flood anyone
            // could set off from a shell.
            return TooLarge();
        }

        if (request is null || !TryBuildCommand(request, out var command))
        {
            return Malformed();
        }

        var observation = RequestObservation.From(context, IngestSurface.BrowserTracker);
        var outcome = await ingestor.IngestAsync(command, observation, cancellationToken).ConfigureAwait(false);

        return outcome == IngestOutcome.Invalid
            ? Malformed()
            : TypedResults.NoContent();
    }

    /// <summary>
    /// Applies the body limit to this request alone.
    /// </summary>
    /// <remarks>
    /// Set per endpoint rather than on the server, because the log importer that arrives later
    /// legitimately posts far larger bodies and would have to unpick a global limit.
    /// </remarks>
    private static void CapBodySize(HttpContext context, long maxBytes)
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = maxBytes;
        }
    }

    private static bool TryBuildCommand(CollectRequest request, out IngestCommand command)
    {
        command = null!;

        if (request.SiteId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Url)
            || request.Kind is null
            || !WireVocabulary.Kinds.TryGetValue(request.Kind, out var kind))
        {
            return false;
        }

        command = new IngestCommand
        {
            SiteId = request.SiteId,
            Kind = kind,
            Url = request.Url,
            Referrer = request.Referrer,
            ClientTimestampUnixMs = request.ClientTimestamp,
            ViewportWidth = request.ViewportWidth,
            ViewportHeight = request.ViewportHeight,
            Language = request.Language,
            TimezoneOffsetMinutes = request.TimezoneOffsetMinutes,
            EngagedMs = request.EngagedMs,
            ScrollDepthPercent = request.ScrollDepthPercent,
            HadPointerInteraction = request.PointerInteraction,
            HadKeyboardInteraction = request.KeyboardInteraction,
            DeclaredWebDriver = request.WebDriver,
            ActionControl = WireVocabulary.ResolveControl(request.Element),
            ActionLabel = request.Label,
            ActionTarget = request.Target,
            ActionTargetKind = WireVocabulary.ResolveTarget(request.TargetKind),
            CorrelationId = request.CorrelationId,
        };

        return true;
    }

    private static ProblemHttpResult Malformed() =>
        TypedResults.Problem(
            title: "The report could not be read.",
            detail: "Send a JSON object with a siteId, a kind of pageview, engagement, exit or "
                + "action, and an absolute http or https url.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TooLarge() =>
        TypedResults.Problem(
            title: "The report is too large.",
            detail: "A report describes one page view and is a few hundred bytes. Send several "
                + "reports rather than one large one.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
}

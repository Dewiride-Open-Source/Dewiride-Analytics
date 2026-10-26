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
/// The response says one thing, and only to a page view that was stored: that its website records
/// what visitors press, which is what the tracker waits for before it listens for a press at all.
/// Every other report that is read is answered alike, with nothing — a page view for a website that
/// does not exist, that is no longer measured, that records no presses or from an origin it does not
/// accept, and every other report however it fared — so no refusal says which of those it was. A
/// body that is malformed or too large is answered plainly, because that is a mistake by whoever is
/// writing an integration and telling them nothing helps nobody, and an address over its allowance
/// is refused before its report is read; neither says anything about a website.
/// </para>
/// <para>
/// What the yes shows is that a page view was stored: for a website that records presses, that its
/// account is still measured and that the host the request was judged by — its origin, failing that
/// its referrer, failing both the host of the page it names — is one the website accepts. It shows
/// nothing that was hidden before. A stored report is answered only once the store has taken it and
/// a refused one at once, so how long an answer takes has always said as much of every website; a
/// host can be confirmed one guess at a time, never listed; and site identifiers are random, so none
/// is found by guessing.
/// </para>
/// <para>
/// A request whose fetch metadata says it was sent in the mode whose reply cannot be read — a beacon,
/// or an older tracker's fallback — is answered with nothing even then. Nothing could read the body,
/// and a browser that blocks it on arrival says so in the console of the page that sent it, which is
/// the customer's. A request from a browser too old to say how it was sent is answered as any other
/// is; if it was a beacon, nothing reads that body either.
/// </para>
/// </remarks>
internal static class CollectEndpoint
{
    /// <summary>Name of the cross-origin policy this endpoint runs under.</summary>
    public const string CorsPolicyName = "collector";

    /// <summary>Name of the rate-limiting policy this endpoint runs under.</summary>
    public const string RateLimitPolicyName = "collector";

    /// <summary>The fetch metadata in which a browser says how it sent a request.</summary>
    private const string FetchModeHeader = "Sec-Fetch-Mode";

    /// <summary>The mode a request is sent in when nothing will read its reply.</summary>
    private const string Unreadable = "no-cors";

    /// <summary>The one answer the endpoint gives, to a page view from a website recording presses.</summary>
    private static readonly CollectAnswer PressesRecorded = new(Presses: true);

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
                "Called by the tracker. A page view stored for a website that records presses is "
                + "answered {\"presses\":true}, unless its Sec-Fetch-Mode header says no-cors; a "
                + "malformed or oversized report is answered with a problem; anything else with an "
                + "empty response. An address that sends too many reports in a minute is refused "
                + "with 429.")
            .RequireCors(CorsPolicyName)
            .RequireRateLimiting(RateLimitPolicyName)
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<CollectAnswer>, NoContent, ProblemHttpResult>> HandleAsync(
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
        var receipt = await ingestor.IngestWithReceiptAsync(command, observation, cancellationToken)
            .ConfigureAwait(false);

        if (receipt.Outcome == IngestOutcome.Invalid)
        {
            return Malformed();
        }

        // A page view is the first thing every page sends, and the tracker listens for presses from
        // its answer onwards; no other report is answered, so asking twice tells nobody anything new.
        return command.Kind == EventKind.PageView && receipt.RecordsPresses && CanReadAnswer(context.Request)
            ? TypedResults.Ok(PressesRecorded)
            : TypedResults.NoContent();
    }

    /// <summary>
    /// Whether the page that sent a request can read what it is answered.
    /// </summary>
    /// <remarks>
    /// A browser says in its fetch metadata which mode it sent a request in, and only one sent in
    /// no-cors mode — a beacon, or an older tracker's fallback — cannot be read. A request that says
    /// nothing of its mode — from an older browser, a server or a test — is answered as one that can.
    /// </remarks>
    /// <param name="request">The request being answered.</param>
    /// <returns>False only for a request sent in the mode whose reply nothing reads.</returns>
    private static bool CanReadAnswer(HttpRequest request) =>
        !string.Equals(request.Headers[FetchModeHeader].ToString(), Unreadable, StringComparison.Ordinal);

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
            ActionLabel = request.Element is not null && WireVocabulary.WrittenIn.Contains(request.Element)
                ? null
                : request.Label,
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

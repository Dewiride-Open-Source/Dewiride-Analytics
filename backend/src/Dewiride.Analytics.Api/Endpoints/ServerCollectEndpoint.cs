using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dewiride.Analytics.Api.Configuration;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Api.Ingest;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// Collection from a reporter running on the customer's own server.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the browser tracker cannot see the traffic this product is mostly about.
/// A crawler asks for the page, reads the markup and stops; it never runs the script, so as far
/// as the tracker is concerned it was never there. Only something sitting in the request path —
/// an edge worker, a plugin, the site's own middleware — observes it, and that is what reports
/// here.
/// </para>
/// <para>
/// Unlike the browser collector this one is authenticated, and the difference is not incidental.
/// A reporter forwards the visitor's address and user agent in place of its own, and those are
/// exactly the values a classification is built from. Accepting that substitution from anyone
/// would let a stranger write whatever traffic they liked into somebody else's account, so the
/// key is what authorises it — and the key decides which site is being reported for, so the body
/// never names one.
/// </para>
/// </remarks>
internal static class ServerCollectEndpoint
{
    /// <summary>Name of the rate-limiting policy this endpoint runs under.</summary>
    public const string RateLimitPolicyName = "collector-server";

    /// <summary>The authentication scheme a key is presented under.</summary>
    private const string Scheme = "Bearer";

    /// <summary>
    /// Maps the server-side collection endpoint.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapServerCollect(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/collect/server", HandleAsync)
            .WithName("CollectFromServer")
            .WithSummary("Records requests observed by the measured site's own server.")
            .WithDescription(
                "Authenticated with a server key issued for one website, presented as "
                + "'Authorization: Bearer'. The key decides which website the batch is recorded "
                + "against, so the body does not name one.")
            .RequireRateLimiting(RateLimitPolicyName)
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<ServerCollectResponse>, ProblemHttpResult>> HandleAsync(
        HttpContext context,
        EventIngestor ingestor,
        IIngestKeyCatalog keys,
        IOptions<CollectorOptions> options,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var settings = options.Value;
        var secret = ReadPresentedSecret(context.Request);

        if (secret is null)
        {
            return Unauthenticated(context);
        }

        var authorization = await keys.AuthorizeAsync(secret, cancellationToken).ConfigureAwait(false);

        if (authorization is null)
        {
            return Unauthenticated(context);
        }

        var (batch, refusal) = await ReadBatchAsync(context, settings, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        var surface = WireVocabulary.ResolveSurface(batch!.Surface);
        var observations = batch.Events!;

        if (observations.Count > settings.MaxEventsPerBatch)
        {
            return TooMany(settings.MaxEventsPerBatch);
        }

        var accepted = 0;

        foreach (var observation in observations)
        {
            if (await StoreAsync(observation, authorization, surface, ingestor, cancellationToken)
                .ConfigureAwait(false))
            {
                accepted++;
            }
        }

        return TypedResults.Ok(new ServerCollectResponse(accepted, observations.Count - accepted));
    }

    private static async Task<bool> StoreAsync(
        ServerObservation observation,
        IngestAuthorization authorization,
        IngestSurface surface,
        EventIngestor ingestor,
        CancellationToken cancellationToken)
    {
        if (!TryBuild(observation, authorization.SiteId, surface, out var command, out var reported))
        {
            return false;
        }

        var outcome = await ingestor.IngestAsync(command, reported, cancellationToken).ConfigureAwait(false);

        return outcome == IngestOutcome.Accepted;
    }

    /// <summary>
    /// Turns one asserted observation into the pair the ingestor takes.
    /// </summary>
    /// <remarks>
    /// The two halves stay separate even here, where both came from the same body. What the
    /// reporter observed about the visitor is not the same kind of claim as what the visitor's own
    /// page reported, and the surface recorded alongside it is what lets a classification read
    /// each for what it is worth.
    /// </remarks>
    private static bool TryBuild(
        ServerObservation observation,
        Guid siteId,
        IngestSurface surface,
        out IngestCommand command,
        out IngestContext reported)
    {
        command = null!;
        reported = null!;

        if (observation is null
            || observation.Kind is null
            || !WireVocabulary.Kinds.TryGetValue(observation.Kind, out var kind)
            || string.IsNullOrWhiteSpace(observation.Url)
            || !TryReadAddress(observation.IpAddress, out var address))
        {
            return false;
        }

        command = new IngestCommand
        {
            SiteId = siteId,
            Kind = kind,
            Url = observation.Url,
            Referrer = observation.Referrer,
            ClientTimestampUnixMs = observation.ObservedAt,
            Language = observation.Language,
            CorrelationId = observation.CorrelationId,
        };

        reported = new IngestContext
        {
            Surface = surface,
            UserAgent = observation.UserAgent,

            // Forwarded rather than read off this request, for the same reason the user agent is:
            // the request that reaches here is the reporter's, and what a reporter's own server
            // says about itself is not what the visitor said about theirs.
            Hints = HintsFrom(observation),

            IpAddress = address,

            // Left unset on purpose. A reporter has no browser origin to offer, so the site's own
            // rules are applied to the hostname of the page it says was requested — which is the
            // claim actually being made.
            RequestOrigin = null,

            StatusCode = observation.StatusCode,
            ContentType = observation.ContentType,
            ResponseBytes = observation.ResponseBytes,
        };

        return true;
    }

    /// <summary>
    /// Reads the three hints a reporter may have forwarded from the visitor's own request.
    /// </summary>
    /// <remarks>
    /// Taken as the browser wrote them, so a reporter forwards headers rather than interpreting
    /// them. A spelling this does not recognise means what an absent header means: the visitor
    /// said nothing, which is the ordinary case outside one family of browsers.
    /// </remarks>
    private static ClientHints HintsFrom(ServerObservation observation) => new()
    {
        Mobile = observation.Mobile switch
        {
            "?1" => true,
            "?0" => false,
            _ => null,
        },
        Platform = observation.Platform,
        Brands = observation.Brands,
    };

    /// <summary>
    /// Reads an asserted visitor address.
    /// </summary>
    /// <remarks>
    /// Absent is fine and means the reporter could not determine one. Present but unparseable is
    /// refused, because it is a fault in whoever wrote the reporter and silently storing nothing
    /// would leave them looking for a missing address in a place it never reached.
    /// </remarks>
    private static bool TryReadAddress(string? value, out string? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!IPAddress.TryParse(value.Trim(), out var parsed))
        {
            return false;
        }

        address = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4().ToString() : parsed.ToString();

        return true;
    }

    private static async Task<(ServerCollectRequest? Batch, ProblemHttpResult? Refusal)> ReadBatchAsync(
        HttpContext context,
        CollectorOptions settings,
        CancellationToken cancellationToken)
    {
        var maxBytes = settings.MaxServerBatchBytes;

        if (context.Request.ContentLength > maxBytes)
        {
            return (null, TooLarge());
        }

        CapBodySize(context, maxBytes);

        try
        {
            var batch = await JsonSerializer.DeserializeAsync(
                    context.Request.Body,
                    ApiJsonContext.Default.ServerCollectRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            return batch?.Events is null ? (null, Malformed()) : (batch, null);
        }
        catch (JsonException)
        {
            return (null, Malformed());
        }
        catch (BadHttpRequestException)
        {
            return (null, TooLarge());
        }
    }

    /// <summary>
    /// Reads the key the caller presented.
    /// </summary>
    /// <returns>The secret, or <see langword="null"/> when none was offered in a usable form.</returns>
    private static string? ReadPresentedSecret(HttpRequest request)
    {
        var header = request.Headers[HeaderNames.Authorization].ToString();

        if (!AuthenticationHeaderValue.TryParse(header, out var presented)
            || !string.Equals(presented.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(presented.Parameter) ? null : presented.Parameter;
    }

    /// <summary>
    /// Applies the batch limit to this request alone, rather than to the whole server.
    /// </summary>
    private static void CapBodySize(HttpContext context, long maxBytes)
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = maxBytes;
        }
    }

    /// <summary>
    /// Refuses a caller that presented no usable key, and says how one is presented.
    /// </summary>
    /// <remarks>
    /// A missing key and a key that matches nothing are answered identically, so this cannot be
    /// used to find out which keys on an install are real.
    /// </remarks>
    private static ProblemHttpResult Unauthenticated(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = Scheme;

        return TypedResults.Problem(
            title: "This batch was not accepted.",
            detail: "Present a server key for the website being measured, as an Authorization "
                + "header of the form 'Bearer dwk_...'. Keys are created on the website's page in "
                + "the dashboard.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static ProblemHttpResult Malformed() =>
        TypedResults.Problem(
            title: "The batch could not be read.",
            detail: "Send a JSON object with an events array. Each entry needs a kind of "
                + "pageview, engagement or exit, and an absolute http or https url.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TooMany(int limit) =>
        TypedResults.Problem(
            title: "That batch holds too many observations.",
            detail: $"Send at most {limit} in one batch, and send several batches beyond that.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TooLarge() =>
        TypedResults.Problem(
            title: "The batch is too large.",
            detail: "Send fewer observations in each batch.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
}

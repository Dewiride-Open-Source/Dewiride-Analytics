using System.Net.Mime;
using Dewiride.Analytics.Api.Ingest;
using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dewiride.Analytics.Api.Endpoints;

/// <summary>
/// The image a page asks for when it cannot run the tracker.
/// </summary>
/// <remarks>
/// <para>
/// This is a fallback for readers, not a way of catching anything. A crawler that does not run
/// scripts does not fetch images either — it asks for the page, reads the markup and stops — so
/// nothing here sees traffic the tracker missed for that reason. What it does see is a person
/// whose browser has scripting turned off or blocked, who would otherwise not be counted at all.
/// </para>
/// <para>
/// It observes far less than the tracker does: no engagement, no scroll depth, and nothing about
/// whether anybody touched the page. Those are left unset rather than sent as nought, so that
/// what this surface cannot see stays distinguishable from what it saw and found to be nothing.
/// </para>
/// <para>
/// The image is returned whatever happens — for a site that does not exist, for a report that
/// cannot be read, for one refused as coming from somewhere else. A page with a broken image on
/// it would tell a visitor something about somebody else's installation, and telling apart a real
/// site identifier from an invented one is exactly what the collector refuses to do.
/// </para>
/// </remarks>
internal static class NoScriptPixelEndpoint
{
    /// <summary>
    /// A one-pixel transparent image, in full.
    /// </summary>
    /// <remarks>
    /// Forty-three bytes, and every one of them is needed. The forty-two byte version that
    /// circulates online drops the end-of-information code from its compressed data, which most
    /// decoders forgive and some do not — and the ones that do not show a broken image on the
    /// customer's page.
    /// </remarks>
    private static readonly ReadOnlyMemory<byte> TransparentPixel = new byte[]
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
        0x01, 0x00, 0x01, 0x00,             // one pixel wide, one pixel tall
        0x80, 0x00, 0x00,                   // a colour table of two, no background, square pixels
        0x00, 0x00, 0x00,                   // colour nought
        0xFF, 0xFF, 0xFF,                   // colour one
        0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, // colour nought is the transparent one
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // the image itself
        0x02, 0x02, 0x44, 0x01, 0x00,       // its compressed contents
        0x3B,                               // end of file
    };

    /// <summary>
    /// Maps the no-script image.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    public static void MapNoScriptPixel(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/collect/pixel.gif", HandleAsync)
            .WithName("NoScriptPixel")
            .WithSummary("Records one page view for a reader whose browser runs no scripts.")
            .WithDescription(
                "Requested by the image inside the tracking snippet's noscript block. Always "
                + "answers with a one-pixel transparent image, whatever it made of the request.")
            .Produces<Stream>(StatusCodes.Status200OK, MediaTypeNames.Image.Gif)
            .RequireRateLimiting(CollectEndpoint.RateLimitPolicyName)
            .AllowAnonymous();
    }

    private static async Task<FileContentHttpResult> HandleAsync(
        HttpContext context,
        [AsParameters] PixelParameters parameters,
        EventIngestor ingestor,
        CancellationToken cancellationToken)
    {
        // Never held anywhere. A cached image is a page view that happens once and is then
        // counted no more, which would quietly turn returning readers into a single visit.
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        var observation = RequestObservation.From(context, IngestSurface.NoScriptPixel);
        var page = PageAddress(parameters.Url, observation.RequestOrigin);

        if (page is not null)
        {
            await ingestor
                .IngestAsync(
                    new IngestCommand
                    {
                        SiteId = parameters.SiteId,
                        Kind = EventKind.PageView,
                        Url = page,
                    },
                    observation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return TypedResults.Bytes(TransparentPixel, MediaTypeNames.Image.Gif);
    }

    /// <summary>
    /// Works out which page was being read.
    /// </summary>
    /// <remarks>
    /// A surface that renders the page itself knows the address and puts it in the query string.
    /// A snippet somebody pasted by hand cannot, so it asks the browser to name the page it is
    /// on instead — which is what the referrer policy on the image tag is for. Neither source is
    /// trusted: both are checked for being an absolute web address here, and the site they claim
    /// to belong to is checked against that site's own permitted origins afterwards.
    /// </remarks>
    /// <param name="claimed">The address the request put in the query string, if any.</param>
    /// <param name="referring">The page the browser said the image was requested from.</param>
    /// <returns>The address, or <see langword="null"/> when neither source gave a usable one.</returns>
    private static string? PageAddress(string? claimed, string? referring) =>
        WebAddress(claimed) ?? WebAddress(referring);

    private static string? WebAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            ? value
            : null;
}

/// <summary>
/// What the no-script image reads from the query string.
/// </summary>
/// <remarks>
/// The site is read as text rather than as an identifier so that a mistyped one is answered with
/// an image like every other request, instead of with the framework's own complaint about the
/// shape of a parameter — which would be one more way to tell a real installation's settings from
/// an invented one's.
/// </remarks>
/// <param name="Site">The website's identifier, as it appears in the snippet.</param>
/// <param name="Url">Absolute address of the page, where the surface knew it.</param>
internal readonly record struct PixelParameters(
    [FromQuery(Name = "site")] string? Site,
    [FromQuery(Name = "u")] string? Url)
{
    /// <summary>The site being reported for, or empty when the query string did not name one.</summary>
    public Guid SiteId => Guid.TryParse(Site, out var parsed) ? parsed : Guid.Empty;
}

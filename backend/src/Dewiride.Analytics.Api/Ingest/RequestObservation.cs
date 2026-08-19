using Dewiride.Analytics.Application.Ingest;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;
using Microsoft.Net.Http.Headers;

namespace Dewiride.Analytics.Api.Ingest;

/// <summary>
/// Reads what the server itself can observe about a collection request.
/// </summary>
/// <remarks>
/// Kept apart from the payload on purpose. These values come from the transport rather than from
/// the body, so they are the ones a client cannot simply assert, and mixing the two into one bag
/// is how that distinction quietly disappears.
/// </remarks>
internal static class RequestObservation
{
    /// <summary>
    /// Longest user agent stored. Real ones are a couple of hundred characters; the header itself
    /// can carry tens of kilobytes, and every one of those bytes would be stored on every event.
    /// </summary>
    private const int MaxUserAgentLength = 1024;

    /// <summary>
    /// Longest client hint examined. These are single tokens or a short bracketed list, and
    /// nothing beyond this length could be either.
    /// </summary>
    private const int MaxHintLength = 256;

    /// <summary>Whether the client is on a handheld device.</summary>
    private const string MobileHeader = "Sec-CH-UA-Mobile";

    /// <summary>Which platform the client is on.</summary>
    private const string PlatformHeader = "Sec-CH-UA-Platform";

    /// <summary>Which browser brands the client answers to.</summary>
    private const string BrandsHeader = "Sec-CH-UA";

    /// <summary>How a structured-header boolean spells true.</summary>
    private const string HintTrue = "?1";

    /// <summary>And false.</summary>
    private const string HintFalse = "?0";

    /// <summary>
    /// Builds the server-side half of an ingest.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <param name="surface">The capture surface this endpoint represents.</param>
    /// <returns>What the server observed.</returns>
    public static IngestContext From(HttpContext context, IngestSurface surface) => new()
    {
        Surface = surface,
        UserAgent = Header(context, HeaderNames.UserAgent, MaxUserAgentLength),
        Hints = ReadHints(context),
        IpAddress = ClientAddress(context),
        RequestOrigin = Header(context, HeaderNames.Origin) ?? Header(context, HeaderNames.Referer),
    };

    /// <summary>
    /// Reads what the browser volunteered about itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the three low-entropy hints, which a browser sends unasked and to any origin. Nothing
    /// is requested: asking would mean sending back a header inviting the browser to describe
    /// itself more precisely on the next request, and the extra precision is exactly the part that
    /// would help identify a person.
    /// </para>
    /// <para>
    /// Absent on most of the web, and that is expected rather than a fault. Only one family of
    /// browsers implements these, and they are sent only over a secure connection — so an
    /// installation being tried out over plain HTTP sees none of them and falls back to the user
    /// agent, which is what the rest of the world does anyway.
    /// </para>
    /// </remarks>
    private static ClientHints ReadHints(HttpContext context)
    {
        var mobile = Header(context, MobileHeader, MaxHintLength);

        return new ClientHints
        {
            Mobile = mobile switch
            {
                HintTrue => true,
                HintFalse => false,
                _ => null,
            },
            Platform = Header(context, PlatformHeader, MaxHintLength),
            Brands = Header(context, BrandsHeader, MaxHintLength),
        };
    }

    /// <summary>
    /// Returns the address the request came from, in its most readable form.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>The address, or <see langword="null"/> when the connection has none.</returns>
    /// <remarks>
    /// Addresses reach a dual-stack listener in their IPv6-mapped form, so the same visitor would
    /// otherwise be written two different ways depending on how the socket was opened — which
    /// would split one person's activity into two visitors and make every network lookup miss.
    /// </remarks>
    public static string? ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;

        if (address is null)
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }

    private static string? Header(HttpContext context, string name, int maxLength = 2048)
    {
        var value = context.Request.Headers[name].ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

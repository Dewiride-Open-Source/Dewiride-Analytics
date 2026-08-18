using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Ingest;

/// <summary>
/// Turns an untrusted report from a capture surface into a stored event.
/// </summary>
/// <remarks>
/// The collector is a public endpoint with no authentication, so this is where the trust
/// boundary is drawn. Three things happen in order and none of them may be skipped: the site is
/// resolved, the request's origin is checked against what that site permits, and the payload is
/// parsed into a shape the rest of the system can rely on.
/// </remarks>
/// <param name="siteCatalog">Resolves the site a report claims to belong to.</param>
/// <param name="visitorKeyFactory">Derives the daily-rotated visitor key.</param>
/// <param name="eventSink">Durable storage for accepted events.</param>
/// <param name="timeProvider">Source of the authoritative server timestamp.</param>
public sealed class EventIngestor(
    ISiteCatalog siteCatalog,
    IVisitorKeyFactory visitorKeyFactory,
    IEventSink eventSink,
    TimeProvider timeProvider)
{
    /// <summary>Longest URL accepted. Anything beyond this is a payload to be rejected, not a page.</summary>
    private const int MaxUrlLength = 2048;

    /// <summary>Longest referrer accepted.</summary>
    private const int MaxReferrerLength = 2048;

    /// <summary>
    /// Longest declared language accepted. A language tag is a handful of characters; this is
    /// generous enough for the longest well-formed one and short enough that the field cannot be
    /// used to carry a payload.
    /// </summary>
    private const int MaxLanguageLength = 35;

    /// <summary>Longest correlation identifier accepted.</summary>
    private const int MaxCorrelationIdLength = 64;

    /// <summary>Largest meaningful scroll depth.</summary>
    private const byte MaxScrollDepthPercent = 100;

    /// <summary>
    /// Validates a report and, if it is acceptable, writes it.
    /// </summary>
    /// <param name="command">The untrusted payload.</param>
    /// <param name="context">What the server observed about the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome. The endpoint returns the same empty response whatever this is.</returns>
    public async Task<IngestOutcome> IngestAsync(
        IngestCommand command,
        IngestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryParseUrl(command.Url, out var url) || !HasPlausibleMeasurements(command))
        {
            return IngestOutcome.Invalid;
        }

        var site = await siteCatalog.FindAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (site is null || !IsOriginPermitted(site, context.RequestOrigin, url.Host))
        {
            return IngestOutcome.Rejected;
        }

        var receivedAt = timeProvider.GetUtcNow();
        var rawEvent = BuildEvent(command, context, site, url, receivedAt);

        await eventSink.WriteAsync(rawEvent, cancellationToken).ConfigureAwait(false);
        return IngestOutcome.Accepted;
    }

    private RawEvent BuildEvent(
        IngestCommand command,
        IngestContext context,
        SiteSnapshot site,
        Uri url,
        DateTimeOffset receivedAt)
    {
        var clientTimestamp = ToTimestamp(command.ClientTimestampUnixMs);
        var referrer = Truncate(command.Referrer, MaxReferrerLength);

        return new RawEvent
        {
            EventId = Guid.CreateVersion7(receivedAt),
            SiteId = site.Id,
            Kind = command.Kind,
            Surface = context.Surface,
            ServerTimestamp = receivedAt,
            ClientTimestamp = clientTimestamp,
            ClockSkewMs = CalculateClockSkewMs(clientTimestamp, receivedAt),
            VisitorKey = visitorKeyFactory.Derive(site.Id, context.IpAddress, context.UserAgent, receivedAt),
            Host = url.Host,
            Path = url.AbsolutePath,
            QueryString = site.RetainQueryStrings ? NullIfEmpty(url.Query) : null,
            Referrer = referrer,
            ReferrerDomain = ExtractHost(referrer),
            UserAgent = context.UserAgent,
            StatusCode = context.StatusCode,
            ContentType = context.ContentType,
            ResponseBytes = context.ResponseBytes,
            IpAddress = context.IpAddress,
            ViewportWidth = command.ViewportWidth,
            ViewportHeight = command.ViewportHeight,
            Language = Truncate(command.Language, MaxLanguageLength),
            TimezoneOffsetMinutes = command.TimezoneOffsetMinutes,
            EngagedMs = command.EngagedMs,
            ScrollDepthPercent = command.ScrollDepthPercent,
            HadPointerInteraction = command.HadPointerInteraction,
            HadKeyboardInteraction = command.HadKeyboardInteraction,
            DeclaredWebDriver = command.DeclaredWebDriver,
            CorrelationId = Truncate(command.CorrelationId, MaxCorrelationIdLength),
        };
    }

    /// <summary>
    /// Rejects measurements that cannot describe anything that happened.
    /// </summary>
    /// <remarks>
    /// Only impossibilities. Merely surprising values — an implausibly large viewport, a page held
    /// open for a week — are kept, because a report that does not add up is evidence about what
    /// produced it, and discarding it here would hide the very thing this product exists to find.
    /// </remarks>
    private static bool HasPlausibleMeasurements(IngestCommand command) =>
        IsNonNegative(command.ViewportWidth)
        && IsNonNegative(command.ViewportHeight)
        && IsNonNegative(command.EngagedMs)
        && command.ScrollDepthPercent is null or <= MaxScrollDepthPercent;

    private static bool IsNonNegative(int? value) => value is null or >= 0;

    /// <summary>
    /// Decides whether a request may report for a site.
    /// </summary>
    /// <remarks>
    /// With no explicit allow-list a site accepts its own hostname and any subdomain of it, which
    /// is what a normal installation needs. The check falls back to the reported page's own host
    /// when no Origin header is present, because several capture surfaces are server-side and
    /// legitimately have no browser origin.
    /// </remarks>
    private static bool IsOriginPermitted(SiteSnapshot site, string? requestOrigin, string urlHost)
    {
        var candidate = NormalizeHost(requestOrigin) ?? NormalizeHost(urlHost);
        if (candidate is null)
        {
            return false;
        }

        if (site.AllowedOrigins.Length > 0)
        {
            return site.AllowedOrigins.Any(allowed => HostMatches(candidate, allowed));
        }

        return HostMatches(candidate, site.Domain);
    }

    private static bool HostMatches(string candidate, string allowed) =>
        string.Equals(candidate, allowed, StringComparison.Ordinal)
        || candidate.EndsWith('.' + allowed, StringComparison.Ordinal);

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return absolute.Host.ToLowerInvariant();
        }

        return trimmed.TrimEnd('.').ToLowerInvariant();
    }

    private static bool TryParseUrl(string value, out Uri url)
    {
        url = null!;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUrlLength)
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        url = parsed;
        return true;
    }

    private static string? ExtractHost(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var parsed) ? parsed.Host.ToLowerInvariant() : null;

    private static DateTimeOffset? ToTimestamp(long? unixMilliseconds) =>
        unixMilliseconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value);

    /// <summary>
    /// Difference between the client's claimed time and the server's, clamped to what fits a
    /// day either way. A client claiming a time years adrift is itself the signal; the exact
    /// magnitude beyond a day carries no additional meaning.
    /// </summary>
    private static int CalculateClockSkewMs(DateTimeOffset? clientTimestamp, DateTimeOffset receivedAt)
    {
        if (clientTimestamp is null)
        {
            return 0;
        }

        var skew = (clientTimestamp.Value - receivedAt).TotalMilliseconds;
        const double limit = 24 * 60 * 60 * 1000d;

        return (int)Math.Clamp(skew, -limit, limit);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

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
/// <param name="networkLookup">Resolves where the visitor's address is and whose network it is on.</param>
/// <param name="eventSink">Durable storage for accepted events.</param>
/// <param name="timeProvider">Source of the authoritative server timestamp.</param>
public sealed class EventIngestor(
    ISiteCatalog siteCatalog,
    IVisitorKeyFactory visitorKeyFactory,
    INetworkLookup networkLookup,
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

    /// <summary>
    /// Longest control name stored. A control is named in a few words; the tracker cuts it to
    /// this and the cut is applied again here, because the tracker runs on somebody else's page
    /// and nothing it sends is a promise.
    /// </summary>
    private const int MaxActionLabelLength = 64;

    /// <summary>
    /// Longest control destination stored. Generous for a path on the site and far past the
    /// longest possible hostname.
    /// </summary>
    private const int MaxActionTargetLength = 512;

    /// <summary>Longest correlation identifier accepted.</summary>
    private const int MaxCorrelationIdLength = 64;

    /// <summary>
    /// Longest user agent stored. Real ones run to a couple of hundred characters; the header can
    /// carry tens of kilobytes, and every one of those bytes would be stored on every event.
    /// </summary>
    private const int MaxUserAgentLength = 1024;

    /// <summary>
    /// Longest content type stored. The column is held as a small set of repeated values, and one
    /// arbitrarily long entry would spoil that for the whole site.
    /// </summary>
    private const int MaxContentTypeLength = 128;

    /// <summary>Largest meaningful scroll depth.</summary>
    private const byte MaxScrollDepthPercent = 100;

    /// <summary>
    /// Longest town name stored. Generous enough for the longest real one and short enough that
    /// nothing arriving from the reference data can become a payload.
    /// </summary>
    private const int MaxPlaceNameLength = 96;

    /// <summary>Longest network owner stored. The column holds a small set of repeated values.</summary>
    private const int MaxNetworkOwnerLength = 96;

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

        // Turned off, so the report is dropped where it arrives rather than stored and filtered
        // out of every later question. A setting that governs what is kept has to govern what is
        // written, or turning it off leaves everything already collected in place and collects
        // more.
        if (command.Kind == EventKind.Action && !site.CaptureClicks)
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

        // Only a report about an operated control may describe one. Anything else claiming to is
        // a caller filling in fields that do not belong to what it says it is reporting, and the
        // claim is dropped rather than stored beside a page view nobody would expect to carry it.
        var operated = command.Kind == EventKind.Action;

        // Resolved here rather than by a later job, because the address it is resolved from is
        // erased 72 hours after this row is written. An attribute missed on the way in cannot be
        // recovered afterwards: there would be nothing left to recover it from.
        var network = networkLookup.Resolve(context.IpAddress);
        var client = ClientProfiler.Profile(context.UserAgent, context.Hints);

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
            UserAgent = Truncate(context.UserAgent, MaxUserAgentLength),
            StatusCode = context.StatusCode,
            ContentType = Truncate(context.ContentType, MaxContentTypeLength),
            ResponseBytes = context.ResponseBytes,
            IpAddress = context.IpAddress,
            CountryCode = NullIfEmpty(network.CountryCode),
            Subdivision = Truncate(network.Subdivision, MaxPlaceNameLength),
            City = Truncate(network.City, MaxPlaceNameLength),
            AutonomousSystem = network.AutonomousSystem,
            NetworkOwner = Truncate(network.NetworkOwner, MaxNetworkOwnerLength),
            ViewportWidth = command.ViewportWidth,
            ViewportHeight = command.ViewportHeight,
            Language = Truncate(command.Language, MaxLanguageLength),
            TimezoneOffsetMinutes = command.TimezoneOffsetMinutes,
            DeviceClass = client.Device,
            BrowserFamily = NullIfEmpty(client.BrowserFamily),
            OperatingSystem = NullIfEmpty(client.OperatingSystem),
            DeclaredMobile = context.Hints.Mobile,
            EngagedMs = command.EngagedMs,
            ScrollDepthPercent = command.ScrollDepthPercent,
            HadPointerInteraction = command.HadPointerInteraction,
            HadKeyboardInteraction = command.HadKeyboardInteraction,
            DeclaredWebDriver = command.DeclaredWebDriver,
            ActionControl = operated ? command.ActionControl : ControlKind.Unknown,
            ActionLabel = operated ? Truncate(command.ActionLabel, MaxActionLabelLength) : null,
            ActionTarget = operated ? Truncate(command.ActionTarget, MaxActionTargetLength) : null,
            ActionTargetKind = operated ? command.ActionTargetKind : TargetKind.None,
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

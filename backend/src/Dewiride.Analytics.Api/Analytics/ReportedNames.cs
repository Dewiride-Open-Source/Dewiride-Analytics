using System.Collections.Frozen;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Api.Analytics;

/// <summary>
/// What each closed set of values is called on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Written out rather than derived from the members, on the same terms as the names the telemetry
/// store uses: this is a published contract that the dashboard and anybody else's integration read,
/// so renaming a member in C# must not be able to change it silently.
/// </para>
/// <para>
/// These are identifiers, not labels. Nothing here is ever shown to a reader — the dashboard looks
/// each one up in its message catalogue and renders the sentence in the reader's own language.
/// </para>
/// </remarks>
internal static class ReportedNames
{
    /// <summary>Wire name for each traffic category.</summary>
    public static FrozenDictionary<TrafficCategory, string> Categories { get; } =
        new Dictionary<TrafficCategory, string>
        {
            [TrafficCategory.InsufficientEvidence] = "insufficient-evidence",
            [TrafficCategory.LikelyHuman] = "likely-human",
            [TrafficCategory.KnownSearchCrawler] = "known-search-crawler",
            [TrafficCategory.KnownAiCrawler] = "known-ai-crawler",
            [TrafficCategory.SuspectedAiCrawler] = "suspected-ai-crawler",
            [TrafficCategory.KnownAutomatedService] = "known-automated-service",
            [TrafficCategory.BrowserAutomation] = "browser-automation",
            [TrafficCategory.GenericWebCrawler] = "generic-web-crawler",
            [TrafficCategory.ContentScraper] = "content-scraper",
            [TrafficCategory.MonitoringOrSynthetic] = "monitoring-or-synthetic",
            [TrafficCategory.SecurityScanner] = "security-scanner",
            [TrafficCategory.SuspiciousAutomation] = "suspicious-automation",
            [TrafficCategory.LikelyAnalyticsSpam] = "likely-analytics-spam",
            [TrafficCategory.Unknown] = "unknown",
        }.ToFrozenDictionary();

    /// <summary>Wire name for each evidence strength.</summary>
    public static FrozenDictionary<EvidenceStrength, string> Strengths { get; } =
        new Dictionary<EvidenceStrength, string>
        {
            [EvidenceStrength.None] = "none",
            [EvidenceStrength.Weak] = "weak",
            [EvidenceStrength.Moderate] = "moderate",
            [EvidenceStrength.Strong] = "strong",
            [EvidenceStrength.Verified] = "verified",
        }.ToFrozenDictionary();

    /// <summary>Wire name for the direction a piece of evidence points.</summary>
    public static FrozenDictionary<SignalDirection, string> Directions { get; } =
        new Dictionary<SignalDirection, string>
        {
            [SignalDirection.TowardHuman] = "toward-human",
            [SignalDirection.Neutral] = "neutral",
            [SignalDirection.TowardAutomation] = "toward-automation",
        }.ToFrozenDictionary();

    /// <summary>
    /// Wire name for each capture surface.
    /// </summary>
    /// <remarks>
    /// The same spellings a server-side reporter names itself by, so one vocabulary covers both
    /// directions. The three that no reporter can declare are here as well, because they are
    /// reported back: a visit says which surfaces saw it, and the browser tracker is the commonest
    /// answer.
    /// </remarks>
    public static FrozenDictionary<IngestSurface, string> Surfaces { get; } =
        new Dictionary<IngestSurface, string>
        {
            [IngestSurface.Unknown] = "unknown",
            [IngestSurface.BrowserTracker] = "browser-tracker",
            [IngestSurface.NoScriptPixel] = "no-script-pixel",
            [IngestSurface.CloudflareWorker] = "cloudflare-worker",
            [IngestSurface.WordPressPlugin] = "wordpress-plugin",
            [IngestSurface.NetlifyEdge] = "netlify-edge",
            [IngestSurface.VercelEdge] = "vercel-edge",
            [IngestSurface.AspNetCoreMiddleware] = "aspnetcore-middleware",
            [IngestSurface.NextJsMiddleware] = "nextjs-middleware",
            [IngestSurface.LogImport] = "log-import",
            [IngestSurface.ServerSide] = "server-side",
        }.ToFrozenDictionary();
}

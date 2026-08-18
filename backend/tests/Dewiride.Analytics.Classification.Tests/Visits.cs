using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Tests;

/// <summary>
/// Builds the sessions the tests reason about.
/// </summary>
/// <remarks>
/// Written as the kinds of visitor a site actually gets rather than as field assignments, so a
/// test reads as a claim about the product — "a reader is not called automation" — and the
/// shape of the evidence stays in one place when it changes.
/// </remarks>
internal static class Visits
{
    /// <summary>A fixed moment, because a verdict that moved with the clock would not be a verdict.</summary>
    public static readonly DateTimeOffset Noon = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    /// <summary>
    /// Somebody reading, with a browser reporting what they did.
    /// </summary>
    public static SessionEvidence AReader(int pages = 3, int engagedMs = 45_000) => new()
    {
        SessionKey = "reader",
        StartedAt = Noon,
        EndedAt = Noon.AddMinutes(4),
        Requests = Pages(pages, TimeSpan.FromMinutes(4)),
        Surfaces = [IngestSurface.BrowserTracker],
        UserAgent = ChromeOnWindows,
        Language = "en-GB",
        ViewportWidth = 1440,
        EngagedMs = engagedMs,
        MaxScrollDepthPercent = 80,
        HadPointerInteraction = true,
        HadKeyboardInteraction = false,
        DeclaredWebDriver = false,
    };

    /// <summary>
    /// A crawler naming itself, seen only by something in the request path.
    /// </summary>
    /// <remarks>
    /// Every behavioural reading is absent rather than false, which is the situation the engine
    /// has to handle correctly: nothing was watching, so nothing may be concluded from silence.
    /// </remarks>
    public static SessionEvidence ANamedCrawler(string userAgent, int pages = 6) => new()
    {
        SessionKey = "crawler",
        StartedAt = Noon,
        EndedAt = Noon.AddSeconds(12),
        Requests = Pages(pages, TimeSpan.FromSeconds(12)),
        Surfaces = [IngestSurface.CloudflareWorker],
        UserAgent = userAgent,
    };

    /// <summary>Something sweeping the site for a way in.</summary>
    public static SessionEvidence AScanner() => new()
    {
        SessionKey = "scanner",
        StartedAt = Noon,
        EndedAt = Noon.AddSeconds(6),
        Requests =
        [
            new ObservedRequest(Noon, "/.env", 404),
            new ObservedRequest(Noon.AddSeconds(1), "/wp-admin/setup-config.php", 404),
            new ObservedRequest(Noon.AddSeconds(2), "/.git/config", 404),
            new ObservedRequest(Noon.AddSeconds(3), "/phpmyadmin/index.php", 404),
        ],
        Surfaces = [IngestSurface.CloudflareWorker],
        UserAgent = "python-requests/2.32.3",
    };

    /// <summary>A session about which nothing whatever is known.</summary>
    public static SessionEvidence Anonymous() => new()
    {
        SessionKey = "quiet",
        StartedAt = Noon,
        EndedAt = Noon.AddSeconds(1),
        Requests = Pages(1, TimeSpan.FromSeconds(1)),
        Surfaces = [IngestSurface.CloudflareWorker],
        UserAgent = ChromeOnWindows,
        Language = "en-GB",
    };

    /// <summary>A run of ordinary pages, spread evenly across a span.</summary>
    public static ImmutableArray<ObservedRequest> Pages(int count, TimeSpan across, short? status = 200)
    {
        var gap = count <= 1 ? TimeSpan.Zero : across / (count - 1);

        return
        [
            .. Enumerable.Range(0, count).Select(index =>
                new ObservedRequest(Noon + gap * index, $"/posts/{index}", status)),
        ];
    }
}

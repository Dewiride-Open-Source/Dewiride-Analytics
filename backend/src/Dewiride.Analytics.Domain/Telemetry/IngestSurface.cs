namespace Dewiride.Analytics.Domain.Telemetry;

/// <summary>
/// Identifies which capture surface produced an event.
/// </summary>
/// <remarks>
/// This is stored on every event and is load-bearing rather than diagnostic. Different
/// surfaces observe genuinely different evidence: a browser tracker sees pointer and
/// scroll presence but never sees a crawler that does not execute JavaScript, while a
/// server-side surface sees every request including 404 probes but observes no client
/// behaviour at all. Classification confidence therefore has to be interpreted against
/// the surface that produced the evidence, and the product must be able to tell a user
/// which distinctions it simply cannot make with what they have installed.
/// </remarks>
public enum IngestSurface
{
    /// <summary>Unknown or not yet attributed. Never valid on a persisted event.</summary>
    Unknown = 0,

    /// <summary>The JavaScript beacon running in a real browser.</summary>
    BrowserTracker = 1,

    /// <summary>
    /// The no-JavaScript image fallback. Captures humans with scripting disabled and
    /// script-blocked page views; it does not capture non-rendering crawlers, which
    /// request HTML and stop without fetching subresources.
    /// </summary>
    NoScriptPixel = 2,

    /// <summary>A Cloudflare Worker sitting in front of the customer's site.</summary>
    CloudflareWorker = 3,

    /// <summary>The WordPress plugin.</summary>
    WordPressPlugin = 4,

    /// <summary>A Netlify edge function.</summary>
    NetlifyEdge = 5,

    /// <summary>A Vercel edge function.</summary>
    VercelEdge = 6,

    /// <summary>First-party ASP.NET Core middleware.</summary>
    AspNetCoreMiddleware = 7,

    /// <summary>First-party Next.js middleware.</summary>
    NextJsMiddleware = 8,

    /// <summary>Batch import of web-server access logs.</summary>
    LogImport = 9,
}

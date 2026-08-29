using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Sessions;

/// <summary>
/// Builds the visitors a live reading holds, and the shorter versions of them it held earlier.
/// </summary>
/// <remarks>
/// Written as the kinds of visitor a site actually gets rather than as field assignments, so a test
/// reads as a claim about the product — "a reader is never called machinery while they are still
/// reading" — and so that what a live reading carries stays in one place when it changes.
/// </remarks>
internal static class Watching
{
    /// <summary>A fixed moment, because a reading that moved with the clock could not be replayed.</summary>
    public static readonly DateTimeOffset Noon = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    /// <summary>Somebody reading, with a browser reporting what they did.</summary>
    public static SessionEvidence AReader() => new()
    {
        SessionKey = "reader",
        StartedAt = Noon,
        EndedAt = Noon.AddMinutes(4),
        Requests = Pages(3, "/posts/"),
        Surfaces = [IngestSurface.BrowserTracker],
        UserAgent = ChromeOnWindows,
        Language = "en-GB",
        ViewportWidth = 1440,
        EngagedMs = 45_000,
        MaxScrollDepthPercent = 80,
        HadPointerInteraction = true,
        HadKeyboardInteraction = false,
        DeclaredWebDriver = false,
    };

    /// <summary>
    /// A reader on a site that also reports from its own server, in the moment before their browser
    /// has said anything.
    /// </summary>
    /// <remarks>
    /// The case the whole rule exists for. Nothing here is unusual — it is an ordinary person on an
    /// ordinary site, one page in — and the engine has no choice but to read "executed nothing" as
    /// evidence of machinery, because that is what it is evidence of when a script had time to run.
    /// </remarks>
    public static SessionEvidence AReaderWhoseBrowserHasNotSpokenYet() => new()
    {
        SessionKey = "arriving",
        StartedAt = Noon,
        EndedAt = Noon,
        Requests = Pages(1, "/posts/"),
        Surfaces = [IngestSurface.WordPressPlugin],
        UserAgent = ChromeOnWindows,
    };

    /// <summary>A crawler naming itself, seen only by something in the request path.</summary>
    public static SessionEvidence ACrawlerThatNamedItself() => new()
    {
        SessionKey = "named",
        StartedAt = Noon,
        EndedAt = Noon.AddSeconds(12),
        Requests = Pages(6, "/posts/"),
        Surfaces = [IngestSurface.CloudflareWorker],
        UserAgent = "Mozilla/5.0 (compatible; GPTBot/1.2; +https://openai.com/gptbot)",
    };

    /// <summary>A visit whose address was inside the ranges a company publishes for its crawlers.</summary>
    /// <param name="operatorName">The company whose published addresses it arrived from.</param>
    public static SessionEvidence ACrawlerWhoseOwnerVouchesForIt(string operatorName = "Google") =>
        ACrawlerThatNamedItself() with
        {
            SessionKey = "vouched",
            UserAgent = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
            ConfirmedOperator = operatorName,
        };

    /// <summary>A browser volunteering that something else is driving it.</summary>
    public static SessionEvidence ADrivenBrowser() =>
        AReader() with { SessionKey = "driven", DeclaredWebDriver = true };

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

    /// <summary>
    /// A visitor whose opening requests were all for pages that are not there, and who then went on
    /// to read the site.
    /// </summary>
    /// <remarks>
    /// A broken link followed from an old index, or a reader working through addresses from a
    /// bookmark file. The share of the visit spent failing is what makes it look like a sweep, and
    /// that share falls as the visit goes on — which is exactly why the observation behind it is not
    /// one a live reading is allowed to name anybody by.
    /// </remarks>
    public static SessionEvidence AVisitorWhoseFirstPagesWereMissing() => new()
    {
        SessionKey = "missing",
        StartedAt = Noon,
        EndedAt = Noon.AddMinutes(4),
        Requests =
        [
            new ObservedRequest(Noon, "/old/one", 404),
            new ObservedRequest(Noon.AddSeconds(2), "/old/two", 404),
            new ObservedRequest(Noon.AddSeconds(4), "/old/three", 404),
            .. Pages(6, "/posts/", Noon.AddSeconds(20)),
        ],
        Surfaces = [IngestSurface.BrowserTracker],
        UserAgent = ChromeOnWindows,
        Language = "en-GB",
        ViewportWidth = 1440,
        EngagedMs = 60_000,
        MaxScrollDepthPercent = 70,
        HadPointerInteraction = true,
        DeclaredWebDriver = false,
    };

    /// <summary>
    /// The same visitor as they looked earlier, holding only their first few requests.
    /// </summary>
    /// <remarks>
    /// What a live reading of them would have carried a moment ago. Everything the browser reports
    /// about how a page was read arrives with the reports themselves, so a visitor holding one
    /// request has none of it yet — which is what makes replaying the prefixes a fair test of
    /// whether a conclusion survives the visit going on.
    /// </remarks>
    /// <param name="whole">The visitor as they ended up.</param>
    /// <param name="requests">How many of their requests had arrived.</param>
    /// <returns>The visitor as they were.</returns>
    public static SessionEvidence AsTheyWere(SessionEvidence whole, int requests)
    {
        ArgumentNullException.ThrowIfNull(whole);

        var so = whole.Requests.Take(requests).ToImmutableArray();
        var watched = requests > 1;

        return whole with
        {
            EndedAt = so.Length == 0 ? whole.StartedAt : so[^1].At,
            Requests = so,
            PageCount = so.Length,
            EngagedMs = watched ? whole.EngagedMs : null,
            MaxScrollDepthPercent = watched ? whole.MaxScrollDepthPercent : null,
            HadPointerInteraction = watched ? whole.HadPointerInteraction : null,
            HadKeyboardInteraction = watched ? whole.HadKeyboardInteraction : null,
        };
    }

    /// <summary>A run of ordinary pages, two seconds apart.</summary>
    private static ImmutableArray<ObservedRequest> Pages(int count, string prefix, DateTimeOffset? from = null)
    {
        var opened = from ?? Noon;

        return
        [
            .. Enumerable.Range(0, count).Select(index =>
                new ObservedRequest(opened.AddSeconds(index * 2), $"{prefix}{index}", (short)200)),
        ];
    }
}

namespace Dewiride.Analytics.Classification;

/// <summary>
/// What generated a session.
/// </summary>
/// <remarks>
/// <para>
/// Sessions are not forced into a human/bot binary. <see cref="Unknown"/> and
/// <see cref="InsufficientEvidence"/> are first-class outcomes, and a product that reports
/// them honestly is more useful than one that guesses: a wrong confident answer about
/// whether someone's audience is real is worse than no answer.
/// </para>
/// <para>
/// The distinction between <see cref="KnownAiCrawler"/> and <see cref="SuspectedAiCrawler"/>
/// is deliberate and must never be collapsed in the user interface. The first means the
/// operator published the address range and it was verified; the second means behaviour
/// resembled a content crawler. Presenting an inference as an identity would attribute
/// traffic to a named company on the strength of a guess.
/// </para>
/// </remarks>
public enum TrafficCategory
{
    /// <summary>
    /// Not enough evidence has been gathered yet to say anything. Distinct from
    /// <see cref="Unknown"/>: this means the question has not been answered, not that it
    /// was answered inconclusively.
    /// </summary>
    InsufficientEvidence = 0,

    /// <summary>
    /// Behaviour is consistent with a person. Never described to a user as "verified human" —
    /// behavioural evidence cannot prove a human was present, and claiming otherwise is the
    /// exact overreach this product exists to correct.
    /// </summary>
    LikelyHuman = 1,

    /// <summary>A search-engine crawler whose identity was verified against published ranges or forward-confirmed DNS.</summary>
    KnownSearchCrawler = 2,

    /// <summary>An AI crawler whose identity was verified against its operator's published address ranges.</summary>
    KnownAiCrawler = 3,

    /// <summary>
    /// Behaves like a content or AI crawler, but identity could not be verified. An inference,
    /// and labelled as one.
    /// </summary>
    SuspectedAiCrawler = 4,

    /// <summary>A recognised, legitimate automated service: uptime monitors, link previews, feed readers.</summary>
    KnownAutomatedService = 5,

    /// <summary>A real browser under programmatic control.</summary>
    BrowserAutomation = 6,

    /// <summary>An unremarkable crawler that identifies itself and behaves politely.</summary>
    GenericWebCrawler = 7,

    /// <summary>Systematic retrieval of content, typically with high coverage and no engagement.</summary>
    ContentScraper = 8,

    /// <summary>Uptime checks and synthetic transaction monitoring.</summary>
    MonitoringOrSynthetic = 9,

    /// <summary>Vulnerability probing, recognised largely by requests for paths that do not exist.</summary>
    SecurityScanner = 10,

    /// <summary>Automated, unidentified, and behaving in a way that warrants a look.</summary>
    SuspiciousAutomation = 11,

    /// <summary>
    /// Activity that appears in telemetry without corresponding to a plausible page visit —
    /// fabricated or replayed reporting rather than a real visitor.
    /// </summary>
    LikelyAnalyticsSpam = 12,

    /// <summary>
    /// Evidence was gathered and weighed, and it does not support any category confidently.
    /// A real answer, not a failure.
    /// </summary>
    Unknown = 13,
}

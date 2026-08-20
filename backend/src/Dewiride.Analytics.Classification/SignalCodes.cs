namespace Dewiride.Analytics.Classification;

/// <summary>
/// Every observation a detector may report, by its stable code.
/// </summary>
/// <remarks>
/// <para>
/// These strings outlive the code that produces them. A stored classification references them,
/// the message catalogue is keyed by them, and a golden-fixture snapshot compares them — so a
/// code that ships must never be reused for a different meaning. Adding one is ordinary; changing
/// what one means is a ruleset major version.
/// </para>
/// <para>
/// Named in dotted lower case, grouped by what the observation is about rather than by which
/// detector happened to make it, because a reader of a stored verdict has the code and not the
/// detector.
/// </para>
/// </remarks>
public static class SignalCodes
{
    /// <summary>The visitor named itself as a crawler this product has a catalogue entry for.</summary>
    public const string DeclaredCrawler = "identity.declared_crawler";

    /// <summary>
    /// The name it gave could not be confirmed against the operator's published addresses.
    /// </summary>
    /// <remarks>
    /// Carried on every unverified identity claim, and carried deliberately: a user-agent string
    /// is one line of text the visitor writes itself, so a claim without this signal removed is
    /// an inference and the interface has to say so.
    /// </remarks>
    public const string UnverifiedClaim = "identity.unverified_claim";

    /// <summary>The visitor named itself as a general-purpose automation tool rather than a crawler.</summary>
    public const string DeclaredTool = "identity.declared_tool";

    /// <summary>The visitor sent no user agent at all, which an ordinary browser never does.</summary>
    public const string NoUserAgent = "identity.no_user_agent";

    /// <summary>Pages were in front of somebody for a measurable time.</summary>
    public const string ReadTime = "engagement.read_time";

    /// <summary>A page was scrolled through.</summary>
    public const string Scrolled = "engagement.scrolled";

    /// <summary>The pointer was used at least once. Presence only; never where.</summary>
    public const string PointerUsed = "engagement.pointer_used";

    /// <summary>A key was pressed at least once. Presence only; never which.</summary>
    public const string KeyboardUsed = "engagement.keyboard_used";

    /// <summary>Somebody was watching, and nothing was touched, scrolled or read.</summary>
    public const string NoEngagement = "engagement.none_observed";

    /// <summary>Pages were retrieved faster than a person reads them.</summary>
    public const string RetrievalRate = "retrieval.rate";

    /// <summary>A great many pages were taken in one visit.</summary>
    public const string RetrievalBreadth = "retrieval.breadth";

    /// <summary>Repeated requests for pages that were never there.</summary>
    public const string MissingPaths = "probing.missing_paths";

    /// <summary>Requests for the specific places a break-in attempt looks for.</summary>
    public const string SensitivePaths = "probing.sensitive_paths";

    /// <summary>The browser said outright that software was driving it.</summary>
    public const string DeclaredWebDriver = "automation.declared_web_driver";

    /// <summary>Something in the request path saw the visit, and no script ever ran.</summary>
    public const string NoScriptExecution = "automation.no_script_execution";

    /// <summary>A browser ran the tracker, so the page was rendered by something that executes.</summary>
    public const string ScriptExecuted = "browser.script_executed";

    /// <summary>Images were fetched but no script ran — a browser with scripting turned off.</summary>
    public const string ImagesOnly = "browser.images_only";

    /// <summary>No language was asked for, which an ordinary browser always does.</summary>
    public const string NoLanguageDeclared = "browser.no_language_declared";

    /// <summary>
    /// The visit arrived over a network whose business is renting computers.
    /// </summary>
    /// <remarks>
    /// Where a browser is running is the one thing about it that cannot be rewritten by whoever is
    /// driving it, short of renting a household connection — which is why this carries more weight
    /// than anything the visit says about itself.
    /// </remarks>
    public const string HostingNetwork = "network.hosting";
}

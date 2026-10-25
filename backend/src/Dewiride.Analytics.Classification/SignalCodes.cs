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

    /// <summary>
    /// The request came from an address its operator vouches for as its own crawlers'.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only observation in this list that establishes who a visitor is rather than what it said
    /// or what it did, and the only one that may carry a verdict to
    /// <see cref="EvidenceStrength.Verified"/>. It replaces <see cref="UnverifiedClaim"/> rather
    /// than joining it: the claim has stopped being a claim.
    /// </para>
    /// <para>
    /// One code for both of the ways a company can vouch — a file of addresses it publishes, and a
    /// domain its machines answer to that points back at them — because what they establish is the
    /// same fact to the same certainty, and which of the two settled a particular visit is a
    /// detail of this product rather than something a customer has any use for.
    /// </para>
    /// </remarks>
    public const string ConfirmedCrawler = "identity.confirmed_crawler";

    /// <summary>
    /// The visitor named one company's crawler and arrived from an address belonging to another.
    /// </summary>
    /// <remarks>
    /// Reported only on a positive contradiction — the address settled as one company's while the
    /// visitor named a different one. An address that settles as nobody's is not an impostor; it is
    /// an address nothing has been established about, which is the ordinary case.
    /// </remarks>
    public const string FalseCrawlerClaim = "identity.false_claim";

    /// <summary>The visitor named itself as a general-purpose automation tool rather than a crawler.</summary>
    public const string DeclaredTool = "identity.declared_tool";

    /// <summary>
    /// The visitor described itself as a crawler, in a name this product has no catalogue entry for.
    /// </summary>
    /// <remarks>
    /// Carries no parameters, deliberately. The name is text the visitor wrote and has no business
    /// reaching a screen; what is known is only that the visitor called itself a crawler, and the
    /// sentence a reader is shown says exactly that and nothing more.
    /// </remarks>
    public const string DeclaredGenericCrawler = "identity.declared_generic_crawler";

    /// <summary>The visitor sent no user agent at all, which an ordinary browser never does.</summary>
    public const string NoUserAgent = "identity.no_user_agent";

    /// <summary>Pages were in front of somebody for a measurable time.</summary>
    public const string ReadTime = "engagement.read_time";

    /// <summary>A page came into view at least a quarter of the way down.</summary>
    /// <remarks>
    /// How far down counts the first screen, so a page no taller than four screens reaches a quarter
    /// by being shown. It says how much of a page was seen, and on its own it is no sign that
    /// anybody scrolled.
    /// </remarks>
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
    /// than anything the visit says about itself. Where it is the only thing pointing toward
    /// automation, what the visit did and whether another site sent it decide the verdict instead —
    /// see <see cref="ArrivedFromAnotherSite"/>.
    /// </remarks>
    public const string HostingNetwork = "network.hosting";

    /// <summary>
    /// Another site sent the visit: a search engine, an assistant, a social network or a link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A faint sign of somebody reading, and weighted as one: too light to count toward any
    /// conclusion, so on an ordinary network it changes no verdict about a visit anything else was
    /// observed about. Programs that fetch pages almost never arrive through another site's link,
    /// while people mostly do, but the referring address is written by the visitor's own browser
    /// and can be made up.
    /// </para>
    /// <para>
    /// A visit it is the only observation of is answered as one the product could not tell rather
    /// than as too little to go on. Nothing this build collects produces one — the image a browser
    /// fetches without scripts carries no referring address, and every other way of reporting a
    /// visit adds an observation about the browser — so it takes reports whose way in the running
    /// build does not recognise, as reports a later release stored would be after going back to
    /// this one.
    /// </para>
    /// <para>
    /// Beside anything else observed, it matters only where a rented network is the one thing
    /// pointing the other way. A company's staff sent out through a provider's security edge, or
    /// somebody on a desktop a company rents in a cloud, reach a page the way everybody else does —
    /// from a search, an answer or a link — and what they then do with it is what tells them apart
    /// from a program on the same network.
    /// </para>
    /// <para>
    /// Carries the kind of site and never the site itself, because the address it was read from
    /// is text the visitor's browser wrote.
    /// </para>
    /// </remarks>
    public const string ArrivedFromAnotherSite = "arrival.from_another_site";
}

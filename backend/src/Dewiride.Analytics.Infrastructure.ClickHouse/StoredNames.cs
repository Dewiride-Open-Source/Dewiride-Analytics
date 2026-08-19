using System.Collections.Frozen;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse;

/// <summary>
/// What each closed set of values is called in the telemetry store.
/// </summary>
/// <remarks>
/// <para>
/// Every name is written out rather than derived from the member it belongs to. A stored
/// vocabulary outlives the code that wrote it: renaming a member in C# would silently reinterpret
/// rows nobody is going to look at again, and a self-hosted installation upgrades whenever its
/// owner decides to, so there is no window in which anyone would notice.
/// </para>
/// <para>
/// Each set is held both ways round, because rows are read back as well as written, and a pair of
/// tables built from one literal cannot drift apart.
/// </para>
/// </remarks>
internal static class StoredNames
{
    /// <summary>Stored name for a reading no surface could take.</summary>
    public const string Unobserved = "Unobserved";

    /// <summary>Stored name for each event kind.</summary>
    public static FrozenDictionary<EventKind, string> KindNames { get; } =
        new Dictionary<EventKind, string>
        {
            [EventKind.PageView] = "PageView",
            [EventKind.Engagement] = "Engagement",
            [EventKind.Exit] = "Exit",
            [EventKind.Action] = "Action",
        }.ToFrozenDictionary();

    /// <summary>Stored name for each kind of control.</summary>
    public static FrozenDictionary<ControlKind, string> ControlKindNames { get; } =
        new Dictionary<ControlKind, string>
        {
            [ControlKind.Unknown] = "Unknown",
            [ControlKind.Link] = "Link",
            [ControlKind.Button] = "Button",
            [ControlKind.Field] = "Field",
        }.ToFrozenDictionary();

    /// <summary>Kind of control for each stored name.</summary>
    public static FrozenDictionary<string, ControlKind> ControlKinds { get; } = Invert(ControlKindNames);

    /// <summary>Stored name for each kind of place a control pointed at.</summary>
    public static FrozenDictionary<TargetKind, string> TargetKindNames { get; } =
        new Dictionary<TargetKind, string>
        {
            [TargetKind.None] = "None",
            [TargetKind.Internal] = "Internal",
            [TargetKind.External] = "External",
            [TargetKind.Contact] = "Contact",
        }.ToFrozenDictionary();

    /// <summary>Kind of place for each stored name.</summary>
    public static FrozenDictionary<string, TargetKind> TargetKinds { get; } = Invert(TargetKindNames);

    /// <summary>Stored name for each capture surface.</summary>
    public static FrozenDictionary<IngestSurface, string> SurfaceNames { get; } =
        new Dictionary<IngestSurface, string>
        {
            [IngestSurface.Unknown] = "Unknown",
            [IngestSurface.BrowserTracker] = "BrowserTracker",
            [IngestSurface.NoScriptPixel] = "NoScriptPixel",
            [IngestSurface.CloudflareWorker] = "CloudflareWorker",
            [IngestSurface.WordPressPlugin] = "WordPressPlugin",
            [IngestSurface.NetlifyEdge] = "NetlifyEdge",
            [IngestSurface.VercelEdge] = "VercelEdge",
            [IngestSurface.AspNetCoreMiddleware] = "AspNetCoreMiddleware",
            [IngestSurface.NextJsMiddleware] = "NextJsMiddleware",
            [IngestSurface.LogImport] = "LogImport",
            [IngestSurface.ServerSide] = "ServerSide",
        }.ToFrozenDictionary();

    /// <summary>Capture surface for each stored name.</summary>
    public static FrozenDictionary<string, IngestSurface> Surfaces { get; } = Invert(SurfaceNames);

    /// <summary>Stored name for each kind of device.</summary>
    public static FrozenDictionary<DeviceClass, string> DeviceClassNames { get; } =
        new Dictionary<DeviceClass, string>
        {
            [DeviceClass.Unknown] = "Unknown",
            [DeviceClass.Phone] = "Phone",
            [DeviceClass.Tablet] = "Tablet",
            [DeviceClass.Desktop] = "Desktop",
            [DeviceClass.Other] = "Other",
        }.ToFrozenDictionary();

    /// <summary>Kind of device for each stored name.</summary>
    public static FrozenDictionary<string, DeviceClass> DeviceClasses { get; } = Invert(DeviceClassNames);

    /// <summary>
    /// The surfaces reporting from the visitor's own browser, written as a list a statement can
    /// test membership of.
    /// </summary>
    /// <remarks>
    /// Assembled from the table above rather than typed out a second time, so a surface added to
    /// the product cannot be classified one way in code and the other way in a statement. Every
    /// part of it is a literal from this file, which is what keeps the rule that no statement
    /// contains anything a caller supplied.
    /// </remarks>
    public static string BrowserSurfaceList { get; } = string.Join(
        ", ",
        SurfaceNames
            .Where(entry => IngestSurfaces.RunsInVisitorBrowser(entry.Key))
            .Select(entry => $"'{entry.Value}'")
            .Order(StringComparer.Ordinal));

    /// <summary>Stored name for each traffic category.</summary>
    public static FrozenDictionary<TrafficCategory, string> CategoryNames { get; } =
        new Dictionary<TrafficCategory, string>
        {
            [TrafficCategory.InsufficientEvidence] = "InsufficientEvidence",
            [TrafficCategory.LikelyHuman] = "LikelyHuman",
            [TrafficCategory.KnownSearchCrawler] = "KnownSearchCrawler",
            [TrafficCategory.KnownAiCrawler] = "KnownAiCrawler",
            [TrafficCategory.SuspectedAiCrawler] = "SuspectedAiCrawler",
            [TrafficCategory.KnownAutomatedService] = "KnownAutomatedService",
            [TrafficCategory.BrowserAutomation] = "BrowserAutomation",
            [TrafficCategory.GenericWebCrawler] = "GenericWebCrawler",
            [TrafficCategory.ContentScraper] = "ContentScraper",
            [TrafficCategory.MonitoringOrSynthetic] = "MonitoringOrSynthetic",
            [TrafficCategory.SecurityScanner] = "SecurityScanner",
            [TrafficCategory.SuspiciousAutomation] = "SuspiciousAutomation",
            [TrafficCategory.LikelyAnalyticsSpam] = "LikelyAnalyticsSpam",
            [TrafficCategory.Unknown] = "Unknown",
        }.ToFrozenDictionary();

    /// <summary>Traffic category for each stored name.</summary>
    public static FrozenDictionary<string, TrafficCategory> Categories { get; } = Invert(CategoryNames);

    /// <summary>Stored name for each evidence strength.</summary>
    public static FrozenDictionary<EvidenceStrength, string> StrengthNames { get; } =
        new Dictionary<EvidenceStrength, string>
        {
            [EvidenceStrength.None] = "None",
            [EvidenceStrength.Weak] = "Weak",
            [EvidenceStrength.Moderate] = "Moderate",
            [EvidenceStrength.Strong] = "Strong",
            [EvidenceStrength.Verified] = "Verified",
        }.ToFrozenDictionary();

    /// <summary>Evidence strength for each stored name.</summary>
    public static FrozenDictionary<string, EvidenceStrength> Strengths { get; } = Invert(StrengthNames);

    /// <summary>Stored name for the direction a piece of evidence points.</summary>
    public static FrozenDictionary<SignalDirection, string> DirectionNames { get; } =
        new Dictionary<SignalDirection, string>
        {
            [SignalDirection.TowardHuman] = "TowardHuman",
            [SignalDirection.Neutral] = "Neutral",
            [SignalDirection.TowardAutomation] = "TowardAutomation",
        }.ToFrozenDictionary();

    /// <summary>Direction for each stored name.</summary>
    public static FrozenDictionary<string, SignalDirection> Directions { get; } = Invert(DirectionNames);

    /// <summary>
    /// Renders a three-state observation.
    /// </summary>
    /// <remarks>
    /// A surface that cannot see an interaction records that it could not see it, which is not the
    /// same claim as recording that none happened.
    /// </remarks>
    /// <param name="value">What was observed, if anything could be.</param>
    /// <returns>The stored name.</returns>
    public static string Observed(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        _ => Unobserved,
    };

    private static FrozenDictionary<string, TValue> Invert<TValue>(FrozenDictionary<TValue, string> names)
        where TValue : notnull =>
        names.ToFrozenDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
}

using System.Collections.Immutable;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Works out what a visit was made on, from what the client said about itself.
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape all of it, and both come from the same place: everything read here was written
/// by the visitor.
/// </para>
/// <para>
/// <b>Nothing is matched with a regular expression.</b> The user-agent string is attacker-supplied
/// and arbitrarily long, and a pattern set rich enough to name every browser is a pattern set rich
/// enough to be made to run for a very long time on one carefully written line of text. Every test
/// below is an ordered plain substring search, which cannot backtrack and costs the same whatever
/// is thrown at it.
/// </para>
/// <para>
/// <b>Nothing the client wrote is ever stored.</b> A match returns the catalogue's own word, so the
/// columns these fill hold a closed set however many visitors invent a browser. Anything not in the
/// catalogue is left empty rather than passed through, which is the honest answer as well as the
/// safe one.
/// </para>
/// <para>
/// The order of each catalogue is the whole of its correctness. Nearly every browser claims to be
/// several others further along its user-agent string — Edge names Chrome, Chrome names Safari,
/// and Safari names Mozilla — so the most specific claim has to be tested first, and a member
/// moved is a member misread.
/// </para>
/// </remarks>
public static class ClientProfiler
{
    /// <summary>
    /// Longest user agent examined.
    /// </summary>
    /// <remarks>
    /// The searches are linear, so a very long string costs time proportional to its length once
    /// per catalogue entry. Real user agents are a couple of hundred characters and the store
    /// keeps a thousand; nothing beyond that would be read even if it were matched.
    /// </remarks>
    private const int MostCharactersExamined = 1024;

    /// <summary>
    /// Browser families, most specific claim first.
    /// </summary>
    /// <remarks>
    /// Brave is absent from this list on purpose. Its user agent is byte-identical to Chrome's by
    /// design, so it can only be recognised where it names itself in the brand list, and inventing
    /// a token for it here would match nothing.
    /// </remarks>
    private static readonly ImmutableArray<(string Token, string Family)> BrowserTokens =
    [
        ("edg/", "Edge"),
        ("edge/", "Edge"),
        ("edga/", "Edge"),
        ("edgios/", "Edge"),
        ("opr/", "Opera"),
        ("opera", "Opera"),
        ("samsungbrowser/", "Samsung Internet"),
        ("vivaldi/", "Vivaldi"),
        ("yabrowser/", "Yandex Browser"),
        ("duckduckgo/", "DuckDuckGo"),
        ("fxios/", "Firefox"),
        ("firefox/", "Firefox"),
        ("crios/", "Chrome"),
        ("chrome/", "Chrome"),
        ("chromium/", "Chrome"),
        ("safari/", "Safari"),
    ];

    /// <summary>
    /// Browser families as they appear in the brand list, most specific first.
    /// </summary>
    /// <remarks>
    /// The list a Chromium browser sends holds its real name, the engine it is built on, and a
    /// deliberately meaningless entry that exists to stop anyone assuming the shape is fixed. So
    /// the specific names are tested before <c>Chromium</c>, which every one of them also carries.
    /// </remarks>
    private static readonly ImmutableArray<(string Token, string Family)> BrandTokens =
    [
        ("microsoft edge", "Edge"),
        ("opera", "Opera"),
        ("samsung internet", "Samsung Internet"),
        ("vivaldi", "Vivaldi"),
        ("yandex", "Yandex Browser"),
        ("duckduckgo", "DuckDuckGo"),
        ("brave", "Brave"),
        ("google chrome", "Chrome"),
        ("chromium", "Chrome"),
    ];

    /// <summary>
    /// Operating systems, most specific claim first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apple's handheld devices name macOS further along the same string, so they are tested
    /// first or every iPhone in the world is filed as a Mac. Android names Linux for the same
    /// reason and is tested before it.
    /// </para>
    /// <para>
    /// Chrome OS is spelled with a trailing space deliberately. Without it the four letters sit
    /// inside the word <c>Microsoft</c>, which appears in more user agents than Chrome OS does.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<(string Token, string System)> SystemTokens =
    [
        ("iphone", "iOS"),
        ("ipad", "iPadOS"),
        ("ipod", "iOS"),
        ("android", "Android"),
        ("windows", "Windows"),
        ("cros ", "ChromeOS"),
        ("mac os", "macOS"),
        ("macintosh", "macOS"),
        ("freebsd", "BSD"),
        ("openbsd", "BSD"),
        ("linux", "Linux"),
        ("x11", "Linux"),
    ];

    /// <summary>Platforms as the hint spells them, mapped onto the catalogue's own words.</summary>
    private static readonly ImmutableArray<(string Token, string System)> PlatformTokens =
    [
        ("windows", "Windows"),
        ("android", "Android"),
        ("chrome os", "ChromeOS"),
        ("chromeos", "ChromeOS"),
        ("macos", "macOS"),
        ("ios", "iOS"),
        ("linux", "Linux"),
    ];

    /// <summary>Devices that are neither a computer nor something carried, most specific first.</summary>
    private static readonly ImmutableArray<string> ApplianceTokens =
    [
        "smart-tv",
        "smarttv",
        "appletv",
        "googletv",
        "hbbtv",
        "crkey",
        "playstation",
        "xbox",
        "nintendo",
        "watch",
    ];

    /// <summary>Systems that only ever run on something sat at rather than carried.</summary>
    private static readonly ImmutableArray<string> DesktopSystems = ["Windows", "macOS", "ChromeOS", "Linux", "BSD"];

    /// <summary>
    /// Works out the device, browser and operating system behind a visit.
    /// </summary>
    /// <param name="userAgent">What the client called itself, if it called itself anything.</param>
    /// <param name="hints">What the client volunteered besides.</param>
    /// <returns>
    /// What could be established. Anything that could not is left at its unknown value rather
    /// than filled with a best guess: a visit whose device nobody could determine is a fact worth
    /// reporting, and a made-up one is worse than none.
    /// </returns>
    public static ClientProfile Profile(string? userAgent, ClientHints? hints)
    {
        var declared = hints ?? ClientHints.None;
        var agent = Examinable(userAgent);
        var system = ResolveSystem(agent, declared);

        return new ClientProfile(
            ResolveDevice(agent, system, declared),
            ResolveBrowser(agent, declared),
            system);
    }

    /// <summary>
    /// Picks the operating system, preferring what the client stated outright.
    /// </summary>
    /// <remarks>
    /// The hint is a quoted token the browser chose from a fixed list, which is a better source
    /// than a string that exists mainly to reassure websites written twenty years ago.
    /// </remarks>
    private static string ResolveSystem(string agent, ClientHints hints)
    {
        var stated = FirstMatch(Examinable(hints.Platform), PlatformTokens);

        return stated.Length > 0 ? stated : FirstMatch(agent, SystemTokens);
    }

    private static string ResolveBrowser(string agent, ClientHints hints)
    {
        var stated = FirstMatch(Examinable(hints.Brands), BrandTokens);

        return stated.Length > 0 ? stated : FirstMatch(agent, BrowserTokens);
    }

    /// <summary>
    /// Picks the kind of device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stated answer is preferred where there is one, because a browser saying whether it is
    /// on a handheld device is a browser answering the question directly. It is overridden in
    /// exactly one direction: a client that says it is not handheld while naming a tablet is a
    /// tablet, which is what Android tablets and iPads both look like.
    /// </para>
    /// <para>
    /// The screen's width is deliberately not consulted. It reads as a better signal than it is —
    /// a phone held sideways is wider than a tablet held upright, and a narrowed window on a
    /// desktop is narrower than either — so it would turn a known answer into a wrong one and
    /// invent an answer where honestly there is none.
    /// </para>
    /// </remarks>
    private static DeviceClass ResolveDevice(string agent, string system, ClientHints hints)
    {
        if (IsAppliance(agent))
        {
            return DeviceClass.Other;
        }

        if (hints.Mobile is bool handheld)
        {
            if (handheld)
            {
                return DeviceClass.Phone;
            }

            return IsTablet(agent, system) ? DeviceClass.Tablet : DeviceClass.Desktop;
        }

        if (IsTablet(agent, system))
        {
            return DeviceClass.Tablet;
        }

        return IsPhone(agent)
            ? DeviceClass.Phone
            : Settled(system);
    }

    private static bool IsAppliance(string agent) =>
        ApplianceTokens.Any(token => agent.Contains(token, StringComparison.Ordinal));

    /// <summary>
    /// Whether this is a tablet.
    /// </summary>
    /// <remarks>
    /// Android names itself on phones and tablets alike and distinguishes them by adding
    /// <c>mobile</c> to the one you can hold in a hand — an inversion of the obvious reading, and
    /// the reason this cannot simply look for a word meaning tablet.
    /// </remarks>
    private static bool IsTablet(string agent, string system) =>
        system == "iPadOS"
        || agent.Contains("tablet", StringComparison.Ordinal)
        || (system == "Android" && !agent.Contains("mobile", StringComparison.Ordinal) && agent.Length > 0);

    private static bool IsPhone(string agent) =>
        agent.Contains("mobile", StringComparison.Ordinal)
        || agent.Contains("iphone", StringComparison.Ordinal)
        || agent.Contains("ipod", StringComparison.Ordinal);

    /// <summary>
    /// The answer for a client that named a system but nothing about the shape of the thing
    /// running it.
    /// </summary>
    /// <remarks>
    /// Only the systems that run on nothing portable settle it. Everything else stays unknown,
    /// which is a real answer here rather than a gap: a great deal of the traffic this product
    /// exists to identify is not a device at all.
    /// </remarks>
    private static DeviceClass Settled(string system) =>
        DesktopSystems.Contains(system) ? DeviceClass.Desktop : DeviceClass.Unknown;

    private static string FirstMatch(string subject, ImmutableArray<(string Token, string Name)> catalogue)
    {
        if (subject.Length == 0)
        {
            return string.Empty;
        }

        foreach (var (token, name) in catalogue)
        {
            if (subject.Contains(token, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Reduces a claim to something the catalogues can be compared against.
    /// </summary>
    /// <remarks>
    /// Lower-cased once here rather than compared case-insensitively at every entry, and capped so
    /// that a client cannot decide how long the search takes.
    /// </remarks>
    private static string Examinable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Length <= MostCharactersExamined ? value : value[..MostCharactersExamined];

        return trimmed.ToLowerInvariant();
    }
}

/// <summary>
/// What a visit was made on.
/// </summary>
/// <param name="Device">The kind of device.</param>
/// <param name="BrowserFamily">Browser family without a version, or empty when unrecognised.</param>
/// <param name="OperatingSystem">Operating system family without a version, or empty.</param>
public readonly record struct ClientProfile(DeviceClass Device, string BrowserFamily, string OperatingSystem);

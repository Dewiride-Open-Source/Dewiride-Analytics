using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Identity;

/// <summary>
/// One network whose business is renting computers.
/// </summary>
/// <param name="AutonomousSystem">
/// The routing number the network is known by. Numbers are assigned by the regional registries and
/// outlive the names their holders trade under, which is why the match is on the number and the
/// name is only there to be shown.
/// </param>
/// <param name="Operator">The company that runs it, as it names itself.</param>
public readonly record struct HostingNetwork(uint AutonomousSystem, string Operator);

/// <summary>
/// The networks this build recognises as places servers live rather than places people do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A visit arriving from a rented server is the single cheapest thing to
/// observe about automation that is trying not to be observed. Everything else the engine looks at
/// — reading time, scrolling, a pointer moving — can be produced by a real browser driven by a
/// script, and increasingly is. Where that browser is running cannot be, without renting a
/// household connection.
/// </para>
/// <para>
/// <b>Every number here was read from the routing registry data this product already downloads</b>
/// (<c>ip2asn-combined</c>, PDDL-1.0), not from a third-party list of "bad" networks and not from
/// memory. Each was looked up by number and its holder confirmed before being written down.
/// </para>
/// <para>
/// <b>Absence is not a claim.</b> There is no complete list of every network that rents servers,
/// and a network missing from this one produces no signal at all rather than a signal that the
/// visit was human. That asymmetry is deliberate: this catalogue can say "this came from a
/// datacentre" and can never say "this did not".
/// </para>
/// <para>
/// <b>Three kinds of network are kept out on purpose, and the reason is the same each time: real
/// people browse from them.</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Content delivery networks</b> — Cloudflare, Akamai, Fastly. A site sitting behind one sees
/// every one of its own readers arrive from it, so treating these as datacentres would classify a
/// whole customer's audience as automation on the day they turned a CDN on.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Corporate security proxies</b> — Zscaler and its kind. Everybody behind one is an employee
/// at a desk, and every one of them would be called a robot.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Networks that mostly carry consumer privacy services.</b> Somebody reading a blog through a
/// subscription VPN is a reader who wanted privacy, not a scraper, and this product is the last
/// one that should punish them for it. Where a network is genuinely both, it is left out — the
/// cost of missing some automation is far lower than the cost of calling a private reader a robot.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Keeping this current is an accepted ongoing cost</b>, on the same terms as the crawler
/// catalogue beside it. It is consulted when a visit is judged, so adding an entry is a code change
/// and a re-judging, with no migration and nothing to backfill.
/// </para>
/// </remarks>
public static class HostingNetworks
{
    /// <summary>Every network this build recognises, grouped by the company that runs it.</summary>
    public static ImmutableArray<HostingNetwork> All { get; } =
    [
        new(16509, "Amazon Web Services"),
        new(14618, "Amazon Web Services"),

        new(15169, "Google"),
        new(19527, "Google"),
        new(396982, "Google Cloud"),

        new(8075, "Microsoft"),

        new(45102, "Alibaba Cloud"),
        new(37963, "Alibaba Cloud"),
        new(134963, "Alibaba Cloud"),
        new(203513, "Alibaba Cloud"),

        new(45090, "Tencent Cloud"),
        new(132203, "Tencent Cloud"),
        new(133478, "Tencent Cloud"),
        new(137876, "Tencent Cloud"),

        new(55990, "Huawei Cloud"),
        new(136907, "Huawei Cloud"),
        new(131444, "Huawei Cloud"),
        new(141180, "Huawei Cloud"),
        new(151610, "Huawei Cloud"),

        new(14061, "DigitalOcean"),
        new(63949, "Linode"),
        new(20473, "Vultr"),

        new(24940, "Hetzner"),
        new(212317, "Hetzner"),
        new(213230, "Hetzner"),
        new(215859, "Hetzner"),

        new(16276, "OVHcloud"),
        new(51167, "Contabo"),
        new(40021, "Contabo"),
        new(141995, "Contabo"),

        new(12876, "Scaleway"),
        new(31898, "Oracle Cloud"),
        new(36351, "IBM Cloud"),

        new(60781, "Leaseweb"),
        new(19148, "Leaseweb"),
        new(30633, "Leaseweb"),
        new(133752, "Leaseweb"),
        new(134351, "Leaseweb"),
        new(136988, "Leaseweb"),

        new(8560, "IONOS"),
        new(197540, "netcup"),
        new(49505, "Selectel"),
        new(62240, "Clouvider"),
        new(42708, "GleSYS"),
        new(396356, "Latitude.sh"),
        new(32475, "SingleHop"),
        new(20860, "iomart"),

        new(20773, "GoDaddy"),
        new(26496, "GoDaddy"),

        new(12200, "Rackspace"),
        new(19994, "Rackspace"),
        new(15395, "Rackspace"),
    ];

    /// <summary>Routing numbers, in the order <see cref="Operators"/> holds their companies.</summary>
    /// <remarks>
    /// Paired arrays rather than a map, because the place they are needed second is a statement
    /// that maps one to the other — a company's several networks are one row on a screen, and a
    /// registry handle like <c>MICROSOFT-CORP-MSN-AS-BLOCK</c> is a wire format rather than a name
    /// anybody should be shown.
    /// </remarks>
    public static ImmutableArray<uint> Numbers { get; } = [.. All.Select(network => network.AutonomousSystem)];

    /// <summary>Companies, in the order <see cref="Numbers"/> holds their routing numbers.</summary>
    public static ImmutableArray<string> Operators { get; } = [.. All.Select(network => network.Operator)];

    /// <summary>Which company runs each recognised network.</summary>
    private static readonly FrozenDictionary<uint, string> ByNumber =
        All.ToFrozenDictionary(network => network.AutonomousSystem, network => network.Operator);

    /// <summary>
    /// Looks up the company running a network.
    /// </summary>
    /// <param name="autonomousSystem">
    /// The routing number the visit arrived over. Nought means nothing resolved it, which is not
    /// the same as a network this catalogue does not hold.
    /// </param>
    /// <param name="operatorName">The company, where the network is one this build recognises.</param>
    /// <returns><see langword="true"/> when the visit came from a rented server.</returns>
    public static bool TryFind(uint autonomousSystem, out string operatorName)
    {
        if (autonomousSystem == 0)
        {
            operatorName = string.Empty;

            return false;
        }

        return ByNumber.TryGetValue(autonomousSystem, out operatorName!);
    }
}

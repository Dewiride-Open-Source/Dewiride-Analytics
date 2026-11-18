using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Dewiride.Analytics.Classification.Identity;

/// <summary>
/// One network a company keeps for its own computers.
/// </summary>
/// <param name="AutonomousSystem">
/// The routing number the network is known by. Numbers are assigned by the regional registries and
/// outlive the names their holders trade under, which is why the match is on the number and the
/// name is only there to be shown.
/// </param>
/// <param name="Operator">The company that runs it, spelt as <see cref="CrawlerCatalogue"/> spells it.</param>
public readonly record struct CompanyNetwork(uint AutonomousSystem, string Operator);

/// <summary>
/// The networks this build recognises as a company's own: where it runs computers of its own, and
/// rents none to anybody.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Measured on real traffic, browsers on Meta's own network opened each newly
/// published post of one blog seven to eleven seconds after the blog shared it to Facebook, two to
/// five at once, with Facebook named as the page that sent them and nothing in their own description
/// saying what they were. Somebody who follows a link they were shown arrives from their own
/// connection — a household, an office, a phone — and not from the network of the company whose
/// page they followed it from.
/// </para>
/// <para>
/// <b>Not a rented network.</b> <see cref="HostingNetworks"/> is where servers anybody can rent
/// live, and a visit there that another site sent and that was then used the way a person uses a
/// page is decided by what it did, because a company's staff on a desktop rented in a cloud reach
/// pages that way. Nobody rents a computer on these networks, so nothing here is decided that way,
/// and a visitor arriving over one is still told apart by the address it came from rather than
/// gathered under the network.
/// </para>
/// <para>
/// <b>Never an identity.</b> The company publishes no list of the addresses its machines use and no
/// name they answer to, so arriving over its network establishes where a visit ran and not which of
/// its services ran it. Nothing here confirms a crawler, names a service, or reaches
/// <see cref="EvidenceStrength.Verified"/>.
/// </para>
/// <para>
/// <b>Every number here was read from the routing registry data this product already downloads</b>
/// (<c>ip2asn-combined</c>, PDDL-1.0) and its holder confirmed, on the same terms as
/// <see cref="HostingNetworks"/>, and is listed only where real traffic showed browsers arriving
/// over it as described above. A number missing from here produces no signal at all.
/// </para>
/// </remarks>
public static class CompanyNetworks
{
    /// <summary>Every network this build recognises, grouped by the company that runs it.</summary>
    public static ImmutableArray<CompanyNetwork> All { get; } =
    [
        new(32934, "Meta"),
    ];

    /// <summary>Which company runs each recognised network.</summary>
    private static readonly FrozenDictionary<uint, string> ByNumber =
        All.ToFrozenDictionary(network => network.AutonomousSystem, network => network.Operator);

    /// <summary>
    /// Looks up the company keeping a network for its own computers.
    /// </summary>
    /// <param name="autonomousSystem">
    /// The routing number the visit arrived over. Nought means nothing resolved it, which is not
    /// the same as a network this catalogue does not hold.
    /// </param>
    /// <param name="operatorName">The company, where the network is one this build recognises.</param>
    /// <returns><see langword="true"/> when the visit came over a company's own network.</returns>
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

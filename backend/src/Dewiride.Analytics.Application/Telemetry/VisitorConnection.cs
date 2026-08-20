using System.Globalization;
using Dewiride.Analytics.Classification.Identity;

namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// What about a visitor's connection is worth recognising them by for a day.
/// </summary>
/// <remarks>
/// <para>
/// Normally the address the request arrived from. A household, an office or a phone holds one at a
/// time, so activity arriving over it is one visitor's activity.
/// </para>
/// <para>
/// <b>On a network that rents servers it is the network instead.</b> There an address is a lease
/// held for as long as somebody is paying for it, and pools of them are sold for the express
/// purpose of not being recognised. Read literally, one program reading a site becomes as many
/// visitors as it held addresses — and worse, its account of a page arrives under a different
/// visitor from the page itself, leaving one visit holding a page nobody read and another holding
/// a reading of no page.
/// </para>
/// <para>
/// The cost is stated rather than hidden: where several unrelated programs run on one rented
/// network and describe themselves identically, they are counted as one. That is much the smaller
/// error of the two. It understates how many machines were reading, and it can never turn
/// machinery into people.
/// </para>
/// <para>
/// Which networks those are is <see cref="HostingNetworks"/> — the same catalogue the engine weighs
/// when it decides whether a visit was a person, so the two can never disagree about what a rented
/// network is. An address on any other network is used exactly as it arrived.
/// </para>
/// </remarks>
public static class VisitorConnection
{
    /// <summary>
    /// Marks a network standing in for an address.
    /// </summary>
    /// <remarks>
    /// No address can be spelt this way: a colon cannot appear in an IPv4 address, and an IPv6 one
    /// is written in hexadecimal, which has no <c>s</c> in it. So a network never collides with an
    /// address somebody genuinely arrived from.
    /// </remarks>
    private const string NetworkPrefix = "as:";

    /// <summary>
    /// Reduces a connection to the part a visitor is worth recognising by.
    /// </summary>
    /// <param name="ipAddress">The address the request arrived from, where one was observed.</param>
    /// <param name="autonomousSystem">
    /// The routing number that address belongs to, or nought where nothing resolved it. Nought is
    /// not a network this catalogue holds, so an unresolved address is used as it arrived.
    /// </param>
    /// <returns>The network for a rented address, and the address itself for every other.</returns>
    public static string? Identifying(string? ipAddress, uint autonomousSystem) =>
        HostingNetworks.TryFind(autonomousSystem, out _)
            ? NetworkPrefix + autonomousSystem.ToString(CultureInfo.InvariantCulture)
            : ipAddress;
}

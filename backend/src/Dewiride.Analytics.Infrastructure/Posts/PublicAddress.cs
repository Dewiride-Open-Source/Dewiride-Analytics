using System.Net;
using System.Net.Sockets;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Whether an address is on the public internet, which is the only place a website's pages are read
/// from.
/// </summary>
/// <remarks>
/// <para>
/// Whoever runs a website chooses what its name resolves to. Without this, naming a sitemap would be
/// a way of pointing the engine at whatever the engine itself can reach: the machine it runs on, the
/// stores behind it, the address a cloud hands out its machines' credentials from.
/// </para>
/// <para>
/// The older family is refused by list: every block in IANA's special-purpose registry, whatever
/// that registry says about whether it can be reached, and multicast from the registry of its own.
/// The newer family is permitted by list, because only one block of it has ever been handed out, and
/// a name answering with anything outside that block is not a website that has merely been unlucky.
/// Both were read from the registries themselves on 2026-11-06.
/// </para>
/// <para>
/// It is not the check the visitor lookups use. That one asks whether an address could belong to
/// somebody and lets through plenty that is no use as somewhere to connect to.
/// </para>
/// </remarks>
internal static class PublicAddress
{
    /// <summary>The older family's blocks that no website is on.</summary>
    private static readonly IPNetwork[] RefusedOlderBlocks =
    [
        IPNetwork.Parse("0.0.0.0/8"), // "This network"
        IPNetwork.Parse("10.0.0.0/8"), // Private use
        IPNetwork.Parse("100.64.0.0/10"), // Shared between the customers of one carrier
        IPNetwork.Parse("127.0.0.0/8"), // This machine
        IPNetwork.Parse("169.254.0.0/16"), // Link local, where clouds answer with credentials
        IPNetwork.Parse("172.16.0.0/12"), // Private use
        IPNetwork.Parse("192.0.0.0/24"), // Protocol assignments, some cloud metadata among them
        IPNetwork.Parse("192.0.2.0/24"), // Documentation
        IPNetwork.Parse("192.31.196.0/24"), // AS112
        IPNetwork.Parse("192.52.193.0/24"), // Automatic multicast tunnelling
        IPNetwork.Parse("192.88.99.0/24"), // The relay of a withdrawn tunnelling scheme
        IPNetwork.Parse("192.168.0.0/16"), // Private use
        IPNetwork.Parse("192.175.48.0/24"), // AS112
        IPNetwork.Parse("198.18.0.0/15"), // Benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // Documentation
        IPNetwork.Parse("203.0.113.0/24"), // Documentation
        IPNetwork.Parse("224.0.0.0/4"), // Multicast
        IPNetwork.Parse("240.0.0.0/4"), // Reserved, through to the limited broadcast address

        // In nobody's registry and looks public. It is the address every Azure machine reaches the
        // platform on, on port 80 among others, with no firewall of the customer's in the way.
        IPNetwork.Parse("168.63.129.16/32"),
    ];

    /// <summary>The one block of the newer family handed out to anybody.</summary>
    private static readonly IPNetwork GlobalUnicast = IPNetwork.Parse("2000::/3");

    /// <summary>What IANA has set aside from inside that block.</summary>
    private static readonly IPNetwork[] RefusedNewerBlocks =
    [
        IPNetwork.Parse("2001::/23"), // Protocol assignments, tunnelling through a third party among them
        IPNetwork.Parse("2001:db8::/32"), // Documentation
        IPNetwork.Parse("2002::/16"), // Tunnelling to an address of the older family
        IPNetwork.Parse("2620:4f:8000::/48"), // AS112
        IPNetwork.Parse("3fff::/20"), // Documentation
    ];

    /// <summary>
    /// Where a network with no older addresses of its own carries one in its last four bytes.
    /// </summary>
    private static readonly IPNetwork TranslatedOlderAddresses = IPNetwork.Parse("64:ff9b::/96");

    /// <summary>
    /// Whether an address is on the public internet.
    /// </summary>
    /// <remarks>
    /// An older address written in the newer family's form reaches the same machine as the older
    /// address does, so it is judged as that older address. That holds for the mapped form and for
    /// the translated form, which is what a name resolves to on a network that only has the newer
    /// family: refusing that form outright would leave such a network reading nothing at all.
    /// <para>
    /// The mapped form has to be taken apart before any block is asked about it. The framework's check
    /// of a block in the newer family answers for a mapped address by what it maps to, which puts
    /// <c>::ffff:10.0.0.1</c> inside the one block handed out to anybody.
    /// </para>
    /// </remarks>
    /// <param name="address">The address.</param>
    /// <returns>Whether a connection to it stays on the public internet.</returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicOlderAddress(address.MapToIPv4());
        }

        return address.AddressFamily == AddressFamily.InterNetwork
            ? IsPublicOlderAddress(address)
            : IsPublicNewerAddress(address);
    }

    private static bool IsPublicOlderAddress(IPAddress address) =>
        !Array.Exists(RefusedOlderBlocks, block => block.Contains(address));

    private static bool IsPublicNewerAddress(IPAddress address)
    {
        if (TranslatedOlderAddresses.Contains(address))
        {
            return IsPublicOlderAddress(new IPAddress(address.GetAddressBytes()[^4..]));
        }

        return GlobalUnicast.Contains(address)
            && !Array.Exists(RefusedNewerBlocks, block => block.Contains(address));
    }
}

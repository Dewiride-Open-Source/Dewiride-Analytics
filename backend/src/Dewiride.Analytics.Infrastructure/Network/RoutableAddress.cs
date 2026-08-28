using System.Net;
using System.Net.Sockets;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Reads the addresses a lookup could mean anything for, and rejects the rest.
/// </summary>
/// <remarks>
/// A private, loopback or link-local address is not a place, is not on anybody's network, and is
/// nobody's crawler. It is what arrives when the product is being run locally, or when a proxy in
/// front of it has not been told to pass the visitor's own address through — and every lookup this
/// product makes would be inventing a fact rather than reporting one if it answered for one.
/// </remarks>
internal static class RoutableAddress
{
    /// <summary>
    /// Parses an address, keeping only the ones that could belong to somebody on the internet.
    /// </summary>
    /// <param name="value">The address in its textual form, as the collector observed it.</param>
    /// <param name="address">
    /// The parsed address, reduced to the older family where it was written in the mapped form.
    /// </param>
    /// <returns>
    /// <see langword="true"/> where it parsed and is routable, and <see langword="false"/> for
    /// absent, unparseable and private alike — all three being the same answer to a caller, which
    /// is that there is nothing to look up.
    /// </returns>
    public static bool TryRead(string? value, out IPAddress address)
    {
        address = IPAddress.None;

        if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value, out var parsed))
        {
            return false;
        }

        if (parsed.IsIPv4MappedToIPv6)
        {
            parsed = parsed.MapToIPv4();
        }

        if (IsPrivate(parsed))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        return address.AddressFamily == AddressFamily.InterNetwork
            ? IsPrivateHouseAddress(address.GetAddressBytes())
            : IsUniqueLocal(address.GetAddressBytes());
    }

    /// <summary>The ranges set aside for private networks in the older address family.</summary>
    private static bool IsPrivateHouseAddress(byte[] octets) => octets[0] switch
    {
        10 or 127 => true,
        169 => octets[1] == 254,
        172 => octets[1] is >= 16 and <= 31,
        192 => octets[1] == 168,
        _ => false,
    };

    /// <summary>Unique local addresses, the newer family's answer to a private network.</summary>
    private static bool IsUniqueLocal(byte[] octets) => octets[0] is 0xFC or 0xFD;
}

using System.Net;
using System.Net.Sockets;
using Dewiride.Analytics.Application.Telemetry;
using MaxMind.Db;

namespace Dewiride.Analytics.Infrastructure.Network;

/// <summary>
/// Resolves a visitor's address against the downloaded reference data.
/// </summary>
/// <remarks>
/// <para>
/// Reads from memory and never from the network. It runs on the ingest path, so anything it
/// waited on would be something a customer's measurements could be lost to.
/// </para>
/// <para>
/// Every failure is the same answer: nothing is known. A missing database, an address that will
/// not parse, an address that parses but belongs to nobody, a database that turns out to be
/// damaged — a caller can do nothing different about any of them, and an exception here would
/// discard a page view over a question that was only ever supplementary.
/// </para>
/// </remarks>
/// <param name="store">Holds whichever data is currently loaded.</param>
internal sealed class ReferenceDataNetworkLookup(ReferenceDataStore store) : INetworkLookup
{
    /// <inheritdoc />
    public NetworkAttributes Resolve(string? ipAddress)
    {
        if (!TryReadRoutable(ipAddress, out var address))
        {
            return NetworkAttributes.Unresolved;
        }

        var place = FindPlace(address);
        var (number, owner) = store.Networks?.Find(address) ?? (0, string.Empty);

        return new NetworkAttributes(
            place.Country,
            place.Subdivision,
            place.City,
            number,
            owner);
    }

    /// <summary>
    /// Parses an address, and reports only the ones a lookup could mean anything for.
    /// </summary>
    /// <remarks>
    /// A private, loopback or link-local address is not a place. It is what arrives when the
    /// product is being run locally, or when a proxy in front of it has not been told to pass the
    /// visitor's own address through — and answering "the Netherlands" for one of those would be
    /// inventing a fact rather than reporting one.
    /// </remarks>
    private static bool TryReadRoutable(string? value, out IPAddress address)
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

    private (string Country, string Subdivision, string City) FindPlace(IPAddress address)
    {
        var places = store.Places;

        if (places is null)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        PlaceRecord? record;

        try
        {
            record = places.Find<PlaceRecord>(address);
        }
        catch (InvalidDatabaseException)
        {
            // A file that passed its opening check and is damaged further in. The next refresh
            // replaces it; until then this address, and possibly every address, resolves to
            // nothing — which is a state the product already reports honestly.
            return (string.Empty, string.Empty, string.Empty);
        }

        if (record is null)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        return (
            record.Country?.IsoCode ?? string.Empty,
            Region(record),
            record.City?.EnglishName ?? string.Empty);
    }

    /// <summary>
    /// The outermost region, preferring its code where it has one.
    /// </summary>
    /// <remarks>
    /// A code where one exists, because it is stable and short; the English name otherwise,
    /// because a region with no code and no name is a region that cannot be shown at all.
    /// </remarks>
    private static string Region(PlaceRecord record)
    {
        var region = record.Subdivisions?.Count > 0 ? record.Subdivisions[0] : null;

        if (region is null)
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(region.IsoCode) ? region.EnglishName : region.IsoCode;
    }
}

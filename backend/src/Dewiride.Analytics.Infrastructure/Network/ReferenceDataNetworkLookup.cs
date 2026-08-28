using System.Net;
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
        if (!RoutableAddress.TryRead(ipAddress, out var address))
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

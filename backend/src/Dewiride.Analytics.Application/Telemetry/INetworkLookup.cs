namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Turns a visitor's network address into where they are and whose network they are on.
/// </summary>
/// <remarks>
/// <para>
/// Called once per accepted event, on the ingest path, so an implementation must answer from
/// memory. Nothing here may reach the network: a lookup that waited on a remote service would put
/// somebody else's availability between a customer's visitors and their own measurements.
/// </para>
/// <para>
/// The reference data is downloaded rather than shipped, so it is legitimately absent on a first
/// run and for as long as an install has no way out to the internet. An implementation reports
/// that by answering <see cref="NetworkAttributes.Unresolved"/>, never by throwing and never by
/// guessing.
/// </para>
/// </remarks>
public interface INetworkLookup
{
    /// <summary>
    /// Resolves what is known about an address.
    /// </summary>
    /// <param name="ipAddress">
    /// The address, in its textual form, or <see langword="null"/> when the surface could not
    /// observe one.
    /// </param>
    /// <returns>
    /// What resolved. <see cref="NetworkAttributes.Unresolved"/> when the address is absent,
    /// unparseable, private, or simply not in the data — all four are the same answer to the
    /// caller, which is that nothing is known.
    /// </returns>
    NetworkAttributes Resolve(string? ipAddress);
}

/// <summary>
/// Where an address is, and whose network it belongs to.
/// </summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2 code, or empty.</param>
/// <param name="Subdivision">State, province or region, or empty.</param>
/// <param name="City">
/// Town or city. An estimate: address ranges belong to networks rather than to places, so this is
/// often the nearest sizeable town rather than where the visitor actually is.
/// </param>
/// <param name="AutonomousSystem">Autonomous system number, or zero when unresolved.</param>
/// <param name="NetworkOwner">Who runs that autonomous system, or empty.</param>
public readonly record struct NetworkAttributes(
    string CountryCode,
    string Subdivision,
    string City,
    uint AutonomousSystem,
    string NetworkOwner)
{
    /// <summary>Nothing is known about this address.</summary>
    public static NetworkAttributes Unresolved { get; } =
        new(string.Empty, string.Empty, string.Empty, 0, string.Empty);
}

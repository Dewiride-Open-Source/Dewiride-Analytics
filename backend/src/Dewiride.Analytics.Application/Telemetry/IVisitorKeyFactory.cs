namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Derives the opaque per-visitor key that groups a visitor's activity within a single day.
/// </summary>
/// <remarks>
/// <para>
/// The key is a hash over coarse request attributes combined with a salt that rotates every
/// day. That gives the two properties the product needs at once: activity can be grouped into
/// sessions and near-identical sessions can be recognised as a family, while nobody — including
/// the operator — can follow a visitor from one day to the next, because yesterday's salt is
/// gone and the hash cannot be recomputed.
/// </para>
/// <para>
/// No cookie is set and no identifier is stored on the visitor's device.
/// </para>
/// </remarks>
public interface IVisitorKeyFactory
{
    /// <summary>
    /// Derives the visitor key for a request.
    /// </summary>
    /// <param name="siteId">Site being visited, so keys never collide across sites.</param>
    /// <param name="connection">
    /// What the visitor's connection is worth recognising them by — usually the address the request
    /// arrived from, and on a network that rents servers the network itself. Reduced by
    /// <see cref="VisitorConnection"/>, which is where that difference is explained. Null when
    /// nothing about the connection was observed.
    /// </param>
    /// <param name="userAgent">Visitor's declared user agent, or null when unavailable.</param>
    /// <param name="observedAt">When the request was observed, which selects the day's salt.</param>
    /// <returns>
    /// The key, or <see langword="null"/> when there is too little to derive one from. A null key
    /// is honest: it means activity cannot be grouped, not that it should be grouped arbitrarily.
    /// </returns>
    string? Derive(Guid siteId, string? connection, string? userAgent, DateTimeOffset observedAt);
}

using System.Net;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Where a connection to a website may actually go.
/// </summary>
/// <remarks>
/// <para>
/// Every connection opened on a website's behalf goes to an endpoint from here and to nothing else,
/// so a name is resolved once, judged, and connected to by the addresses that were judged. A name
/// that resolved to something public when it was checked and something else when it was connected to
/// has no second lookup to take advantage of.
/// </para>
/// <para>
/// It is a port for one reason. The tests read from a website on this machine, which the real answer
/// refuses, and no setting can make it do otherwise: only code compiled into a test can stand in for
/// it, and an architecture test holds the product to its one implementation.
/// </para>
/// </remarks>
internal interface IPublicEndpoints
{
    /// <summary>
    /// Settles where a connection to a host may go.
    /// </summary>
    /// <param name="requested">The host and port asked for.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The endpoints, in the order to try them, of which there is at least one.</returns>
    /// <exception cref="AddressRefusedException">The host or the port is not one a website is read from.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">The name resolves to nothing.</exception>
    ValueTask<IReadOnlyList<IPEndPoint>> ResolveAsync(DnsEndPoint requested, CancellationToken cancellationToken);
}

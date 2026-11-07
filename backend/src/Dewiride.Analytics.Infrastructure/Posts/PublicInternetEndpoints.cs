using System.Net;
using System.Net.Sockets;

namespace Dewiride.Analytics.Infrastructure.Posts;

/// <summary>
/// Lets a connection go to the public internet, on the web's own two ports, and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// A name is refused whole if any address it answers with is not public, rather than connected to by
/// the ones that are. Nobody's website legitimately answers with both, and a name that does is either
/// misconfigured or somebody finding out what happens when it tries.
/// </para>
/// <para>
/// An address written as a number is judged like an answer. What decides whether a website may be
/// read refuses those before they get here, and this is the second lock on the same door.
/// </para>
/// <para>
/// There is no setting here, and there must never be one. An installation that wants its own private
/// network read cannot have that by configuration, because the same configuration on the hosted
/// service would let any account read that service's own network.
/// </para>
/// </remarks>
/// <param name="resolve">Asks a name server what a name resolves to.</param>
internal sealed class PublicInternetEndpoints(Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    : IPublicEndpoints
{
    private const int HttpPort = 80;
    private const int HttpsPort = 443;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IPEndPoint>> ResolveAsync(
        DnsEndPoint requested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested.Port is not (HttpPort or HttpsPort))
        {
            throw new AddressRefusedException("A website is only read on the web's own two ports.");
        }

        var answers = IPAddress.TryParse(requested.Host, out var written)
            ? [written]
            : await resolve(requested.Host, cancellationToken).ConfigureAwait(false);

        if (answers.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        if (!Array.TrueForAll(answers, PublicAddress.IsPublic))
        {
            throw new AddressRefusedException("That name leads somewhere other than the public internet.");
        }

        return Array.ConvertAll(answers, answer => new IPEndPoint(answer, requested.Port));
    }
}

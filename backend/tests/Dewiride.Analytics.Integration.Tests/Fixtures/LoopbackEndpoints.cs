using System.Net;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Puts the names set aside for testing on a website on this machine.
/// </summary>
/// <remarks>
/// <para>
/// This is the only thing in the repository that lets the reader reach this machine, and it exists
/// because the real answer rightly never will. It is compiled into this test project and nothing else,
/// which is the whole of what stands between it and the product: there is no setting that selects it,
/// and an architecture test holds the product to its one implementation.
/// </para>
/// <para>
/// Every <c>.test</c> name comes here, on either of the web's ports, as far as the connection goes.
/// Any other name is refused, so a test that lets a reader wander off gets a refusal and not a request
/// to the internet.
/// </para>
/// </remarks>
/// <param name="port">The port the website on this machine listens on.</param>
internal sealed class LoopbackEndpoints(int port) : IPublicEndpoints
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<IPEndPoint>> ResolveAsync(
        DnsEndPoint requested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requested);

        return requested.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            ? ValueTask.FromResult<IReadOnlyList<IPEndPoint>>([new IPEndPoint(IPAddress.Loopback, port)])
            : throw new AddressRefusedException("Only the names set aside for testing lead anywhere from here.");
    }
}

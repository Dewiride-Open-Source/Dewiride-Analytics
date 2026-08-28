using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;

namespace Dewiride.Analytics.Infrastructure.Crawlers;

/// <summary>
/// What a name server says about one address.
/// </summary>
/// <param name="HostName">
/// The name the address answers to, or empty where it answers to none.
/// </param>
/// <param name="Addresses">
/// The addresses that name points back at. Empty where the name could not be resolved, which is a
/// different thing from a name that resolves elsewhere and has to stay distinguishable from it.
/// </param>
internal readonly record struct ResolvedName(string HostName, ImmutableArray<IPAddress> Addresses)
{
    /// <summary>What an address nobody publishes a name for comes back as.</summary>
    public static ResolvedName None { get; } = new(string.Empty, []);
}

/// <summary>
/// Asks a name server what an address is called, and what that name points at.
/// </summary>
/// <remarks>
/// A seam rather than an abstraction for its own sake. The rule that turns two name-server answers
/// into an identity is the part worth pinning — that a name has to match on a label boundary, that
/// it has to point back at the address that produced it, that a company's name may not be inferred
/// from half of the pair — and none of it can be tested against the real thing, because the real
/// thing is other people's name servers answering differently on different days.
/// </remarks>
internal interface IReverseNameLookup
{
    /// <summary>
    /// Resolves the name an address answers to, and the addresses that name resolves to.
    /// </summary>
    /// <param name="address">The address the visit arrived from.</param>
    /// <param name="cancellationToken">Cancellation token, carrying the caller's allowance.</param>
    /// <returns>
    /// Both halves of the answer, or <see cref="ResolvedName.None"/> where the address answers to
    /// nothing, where the name it answers to resolves to nothing, or where nothing answered at all.
    /// </returns>
    Task<ResolvedName> ResolveAsync(IPAddress address, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the machine's own resolver.
/// </summary>
/// <remarks>
/// <para>
/// One call does both halves. Handed an address in its textual form, the framework's own resolver
/// looks up the name it answers to, then looks that name up again to find every address it points
/// at, and returns the pair — which is exactly the procedure every one of these companies
/// documents, carried out by the platform's resolver rather than reimplemented here.
/// </para>
/// <para>
/// An address that answers to no name comes back as itself, and that is the case this has to
/// notice rather than pass on: a name that is the address is not a name, and letting it through
/// would make the matching rule the only thing standing between a visitor and a company's name.
/// </para>
/// <para>
/// Every failure is the same answer. A name server that is down, an address with a name but no way
/// back, a resolver that took too long: a caller can do nothing different about any of them, and
/// the honest reading of all three is that the address establishes nobody.
/// </para>
/// </remarks>
internal sealed class SystemNameLookup : IReverseNameLookup
{
    /// <inheritdoc />
    public async Task<ResolvedName> ResolveAsync(IPAddress address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var literal = address.ToString();

        try
        {
            var entry = await Dns.GetHostEntryAsync(literal, cancellationToken).ConfigureAwait(false);

            return string.Equals(entry.HostName, literal, StringComparison.OrdinalIgnoreCase)
                ? ResolvedName.None
                : new ResolvedName(entry.HostName, [.. entry.AddressList]);
        }
        catch (SocketException)
        {
            return ResolvedName.None;
        }
        catch (ArgumentException)
        {
            return ResolvedName.None;
        }
    }
}

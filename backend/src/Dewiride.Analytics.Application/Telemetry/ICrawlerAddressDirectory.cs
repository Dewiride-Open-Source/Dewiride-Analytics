namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Answers whether a visitor's address is one a company vouches for as its own crawlers'.
/// </summary>
/// <remarks>
/// <para>
/// The route this product has to an identity rather than a claim, and the only one that may carry
/// a verdict to its firmest band. A user agent is a line of text the visitor writes; where the
/// request came from is not, and a company that says which addresses its crawlers connect from has
/// made a statement anyone can check against the connection itself.
/// </para>
/// <para>
/// Two kinds of statement reach an implementation, and it answers from both without distinguishing
/// them. Most companies publish a file listing their addresses. The rest publish a domain their
/// machines answer to, which cannot be listed in advance and is settled one address at a time by
/// <see cref="ICrawlerNameLookup"/> — off this path, because settling it means waiting on a name
/// server. What that settles is handed back here, so the next request from the same machine is
/// recognised as it arrives.
/// </para>
/// <para>
/// Called once per accepted event, on the ingest path, so an implementation must answer from
/// memory on the same terms as <see cref="INetworkLookup"/>. Nothing here may reach the network: a
/// lookup that waited on somebody else's web server would put their availability between a
/// customer's visitors and their own measurements, and the addresses are attacker-chosen, so a
/// remote call per address is a way of asking to be flooded.
/// </para>
/// <para>
/// Resolved here rather than when a visit is judged, because the address it is resolved from is
/// erased 72 hours after the row is written. An answer missed on the way in cannot be recovered
/// afterwards, and it is what lets a visit re-judged under a later ruleset reach the same
/// conclusion instead of quietly losing an identity it once had.
/// </para>
/// </remarks>
public interface ICrawlerAddressDirectory
{
    /// <summary>
    /// Finds whose crawlers an address belongs to.
    /// </summary>
    /// <param name="ipAddress">
    /// The address, in its textual form, or <see langword="null"/> when the surface could not
    /// observe one.
    /// </param>
    /// <returns>
    /// The company, spelt as <c>CrawlerCatalogue</c> spells it, or <see langword="null"/> where the
    /// address is absent, unparseable, or simply one nobody vouches for — which is the answer for
    /// virtually every visit and is never a finding. An implementation with nothing loaded answers
    /// this way too, and never throws and never guesses.
    /// </returns>
    string? OperatorOf(string? ipAddress);
}

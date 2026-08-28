namespace Dewiride.Analytics.Application.Telemetry;

/// <summary>
/// Establishes whose crawlers an address belongs to from the name it answers to.
/// </summary>
/// <remarks>
/// <para>
/// The second of the two routes this product has to an identity rather than a claim, and it exists
/// because several companies offer no other. They publish no list of their crawlers' addresses —
/// the largest of them says outright that it will not, because the addresses change — and instead
/// document the domain their machines answer to and invite anyone to check. A name is a statement
/// only the company can make, since only the holder of an address block can set what it answers
/// to, and only the holder of a domain can decide which addresses it points at. Asking both
/// questions and requiring the answers to agree is what makes it proof rather than a lookup.
/// </para>
/// <para>
/// <b>This is the opposite of <see cref="ICrawlerAddressDirectory"/> in the one respect that
/// matters: it reaches the network, so it must never be called while a visitor is waiting.</b> It
/// is asked when a finished visit is judged, which happens once the visit has been silent for an
/// idle timeout and is therefore minutes to hours after the fact, on a background worker where a
/// slow answer costs nothing anybody can see.
/// </para>
/// <para>
/// Asked in a batch because that is how the work arrives — a pass over a stretch of a site's
/// history has all of its candidates at once — and because the pacing of the lookups is then the
/// implementation's own business rather than something every caller has to get right.
/// </para>
/// <para>
/// The addresses handed over are personal data under the same retention rule as everywhere else,
/// and the caller is expected to have narrowed them to visits that already look like machinery.
/// Nothing derived from a person's address should be sent to a name server to satisfy a curiosity.
/// </para>
/// </remarks>
public interface ICrawlerNameLookup
{
    /// <summary>
    /// Finds which of a set of addresses answer to a name their operator documents.
    /// </summary>
    /// <param name="ipAddresses">
    /// The addresses, in their textual form. Duplicates and unparseable entries are the caller's
    /// convenience rather than its responsibility, and cost nothing.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per address that was settled, keyed by the address exactly as it was passed in
    /// and holding the company spelt as <c>CrawlerCatalogue</c> spells it. An address that answers
    /// to nothing, answers to somebody else's name, or could not be asked about at all is simply
    /// absent — which is the answer for virtually every address and is never a finding. An
    /// implementation that cannot reach a name server returns an empty result, never throws, and
    /// never guesses.
    /// </returns>
    Task<IReadOnlyDictionary<string, string>> OperatorsOfAsync(
        IReadOnlyCollection<string> ipAddresses,
        CancellationToken cancellationToken);
}

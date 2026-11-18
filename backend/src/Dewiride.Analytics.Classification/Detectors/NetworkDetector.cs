using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Identity;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports where the visit was coming from, in the sense of what kind of place rather than which
/// country.
/// </summary>
/// <remarks>
/// <para>
/// The only observation the engine makes that the visitor cannot author. A user agent is a string
/// whoever is driving the browser chooses; reading time, scrolling and a pointer moving are all
/// producible by a real browser under a script, and automation that wants to be counted as a
/// person increasingly produces them. None of that changes the fact that the request arrived from
/// a rented server, and renting household connections instead is expensive enough to be a
/// different problem.
/// </para>
/// <para>
/// It is still evidence rather than proof, and is weighted as evidence. Somebody working on a
/// desktop a company rents in a cloud, staff whose browsing their company sends out through a
/// provider's security edge, and a reader behind a privacy service that leaves from a hosting
/// network are all real readers arriving from a datacentre. So this outweighs what the visit did
/// without erasing it. Where it is the only thing pointing toward automation, a visit another site
/// sent on which a key or the pointer was then pressed or the page read is weighed as the reader it
/// behaved like, with this shown against it and holding the conclusion back.
/// </para>
/// <para>
/// A network a company keeps for its own computers is reported apart from a rented one. Nobody
/// rents a computer there, so nobody reads from a desktop rented on it, and what a visit over it
/// did never decides the visit instead: a browser a company runs on its own network stays, scrolls
/// and presses as it is told to. A visitor naming one of that company's own crawlers is left to what
/// it called itself, which nothing here can confirm.
/// </para>
/// <para>
/// Silent about everything else. A network nobody catalogued, and a visit nothing could place at
/// all, both produce nothing — never a signal that the visit was therefore a person. The
/// catalogue can say where a visit came from and can never say where it did not.
/// </para>
/// </remarks>
public sealed class NetworkDetector : IDetector
{
    /// <summary>
    /// What arriving from a rented server is worth.
    /// </summary>
    /// <remarks>
    /// Set above the heaviest observation of a person reading, because it has to be: a session
    /// that came straight to a page, read for a minute and scrolled to the bottom from an Alibaba
    /// datacentre is a scraper running a real browser, and calling it a reader is the one mistake
    /// this product cannot afford. Set no higher, because it is one observation and this product
    /// does not let a single observation reach its firmest band on its own.
    /// </remarks>
    private const int RentedServerWeight = 65;

    /// <summary>
    /// What arriving over a network a company keeps for its own computers is worth.
    /// </summary>
    /// <remarks>
    /// The same as a rented server and for the same reasons: above the heaviest observation of a
    /// person reading, and no higher, because it is one observation.
    /// </remarks>
    private const int CompanyMachineWeight = 65;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (HostingNetworks.TryFind(session.AutonomousSystem, out var operatorName))
        {
            return
            [
                Observed.Signal(
                    SignalCodes.HostingNetwork,
                    SignalDirection.TowardAutomation,
                    RentedServerWeight,
                    ("operator", operatorName)),
            ];
        }

        if (!CompanyNetworks.TryFind(session.AutonomousSystem, out var company)
            || NamedOneOfItsOwnCrawlers(session, company))
        {
            return [];
        }

        return
        [
            Observed.Signal(
                SignalCodes.CompanyNetwork,
                SignalDirection.TowardAutomation,
                CompanyMachineWeight,
                ("operator", company)),
        ];
    }

    /// <summary>
    /// Whether the visitor named one of the company's own crawlers.
    /// </summary>
    /// <remarks>
    /// That visit is already said to be what it called itself, a claim the company publishes no way
    /// to confirm. Saying beside it that it came over the company's own network would read as that
    /// confirmation, and would add weight to a conclusion the name alone reached.
    /// </remarks>
    /// <param name="session">The visit.</param>
    /// <param name="company">The company whose network it came over.</param>
    /// <returns><see langword="true"/> when the visitor named one of that company's crawlers.</returns>
    private static bool NamedOneOfItsOwnCrawlers(SessionEvidence session, string company) =>
        string.Equals(CrawlerCatalogue.Match(session.UserAgent)?.Operator, company, StringComparison.Ordinal);
}

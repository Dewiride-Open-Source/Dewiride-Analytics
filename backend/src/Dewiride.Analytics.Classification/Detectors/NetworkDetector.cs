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
/// without erasing it. Where it is the only thing pointing toward automation, a visit on which a
/// key was pressed, or one another site sent on which the pointer was then pressed or the page
/// read, is weighed as the reader it behaved like, with this shown against it and holding the
/// conclusion back.
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

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!HostingNetworks.TryFind(session.AutonomousSystem, out var operatorName))
        {
            return [];
        }

        return
        [
            Observed.Signal(
                SignalCodes.HostingNetwork,
                SignalDirection.TowardAutomation,
                RentedServerWeight,
                ("operator", operatorName)),
        ];
    }
}

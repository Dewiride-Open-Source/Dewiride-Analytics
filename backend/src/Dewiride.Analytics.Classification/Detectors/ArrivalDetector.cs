using System.Collections.Frozen;
using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports that another site sent the visit, and what kind of site that was.
/// </summary>
/// <remarks>
/// <para>
/// People mostly reach a page from somewhere — a search, an assistant's answer, a post, a link on
/// another site — and programs that fetch pages on their own almost never do: they are handed the
/// address directly. That makes an arrival a sign of somebody reading, and a faint one, because the
/// referring address it is read from is written by the visitor's own browser and can be made up.
/// </para>
/// <para>
/// So it is weighted below anything that counts. Beside anything else an ordinary network shows it
/// moves no verdict, and the one visit it would decide alone — one nothing else was observed about —
/// is one this build's own collection never produces (see
/// <see cref="SignalCodes.ArrivedFromAnotherSite"/>). The one place it decides anything is a visit
/// over a rented network with nothing else pointing toward automation, and there it is read
/// together with what the visit then did.
/// </para>
/// <para>
/// Silent where nothing named a sender. A visit that came straight in is not thereby automation,
/// and the absence of an arrival is never reported as anything.
/// </para>
/// </remarks>
public sealed class ArrivalDetector : IDetector
{
    /// <summary>
    /// What another site having sent the visit is worth.
    /// </summary>
    /// <remarks>
    /// Below the weight at which an observation counts toward a conclusion, on purpose. A
    /// referring address costs nothing to forge, so it may tip a visit only where everything else
    /// about it has already been weighed and found to say nothing against a person but the network
    /// it came over; it never makes a person on its own and never outweighs anything that counts.
    /// </remarks>
    private const int SentWeight = 20;

    /// <summary>
    /// The stored spelling of every kind of sender.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from the member name, so renaming one in C# cannot change the
    /// meaning of a verdict that was stored months ago. A direct arrival has no spelling because it
    /// is never reported.
    /// </remarks>
    private static readonly FrozenDictionary<SourceChannel, string> Kinds =
        new Dictionary<SourceChannel, string>
        {
            [SourceChannel.Search] = "search",
            [SourceChannel.Assistant] = "assistant",
            [SourceChannel.Social] = "social",
            [SourceChannel.Link] = "link",
        }.ToFrozenDictionary();

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // The kind of site, never the site: the address it was read from is the visitor's text.
        return Kinds.TryGetValue(session.SentBy, out var kind)
            ? [Observed.Signal(SignalCodes.ArrivedFromAnotherSite, SignalDirection.TowardHuman, SentWeight, ("kind", kind))]
            : [];
    }
}

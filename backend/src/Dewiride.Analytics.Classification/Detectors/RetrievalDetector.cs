using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports how much was taken, and how quickly.
/// </summary>
/// <remarks>
/// <para>
/// Works on the requests alone, so it says something about every session whatever surface saw it.
/// That matters: a crawler that runs no script is invisible to every behavioural reading except
/// this one.
/// </para>
/// <para>
/// Both thresholds are set where a person could not plausibly be on the other side of them, not
/// where an average person sits. A fast reader skimming a documentation site should never trip
/// this, so the rate is judged against how quickly pages can be <em>read</em> rather than how
/// quickly they can be clicked.
/// </para>
/// </remarks>
public sealed class RetrievalDetector : IDetector
{
    /// <summary>Pages a session needs before a rate means anything at all.</summary>
    private const int PagesBeforeRateCounts = 5;

    /// <summary>
    /// Pages per minute beyond which nobody is reading.
    /// </summary>
    /// <remarks>
    /// Twenty is one page every three seconds, sustained. A person skimming for a link manages
    /// that in bursts; sustaining it across a whole session is retrieval, not reading.
    /// </remarks>
    private const double ImplausiblePagesPerMinute = 20d;

    /// <summary>Pages in one visit beyond which the visit is covering a site rather than using it.</summary>
    private const int BroadCoverage = 25;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        AddRate(found, session);

        if (session.PageCount >= BroadCoverage)
        {
            found.Add(Observed.Signal(
                SignalCodes.RetrievalBreadth,
                SignalDirection.TowardAutomation,
                40,
                ("pageCount", Observed.Number(session.PageCount))));
        }

        return found.ToImmutable();
    }

    private static void AddRate(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        var seconds = session.Duration.TotalSeconds;

        // A session whose requests all landed in the same instant has no rate to speak of — the
        // arithmetic would divide by nothing and the answer would be an artefact rather than an
        // observation.
        if (session.PageCount < PagesBeforeRateCounts || seconds <= 0)
        {
            return;
        }

        var perMinute = session.PageCount / (seconds / 60d);

        if (perMinute < ImplausiblePagesPerMinute)
        {
            return;
        }

        // Twice the threshold is as decisive as it gets. Beyond that the number keeps climbing
        // while the conclusion does not change, and letting weight climb with it would let one
        // observation overwhelm everything else in the scorecard.
        var weight = perMinute >= ImplausiblePagesPerMinute * 2 ? 65 : 50;

        found.Add(Observed.Signal(
            SignalCodes.RetrievalRate,
            SignalDirection.TowardAutomation,
            weight,
            ("pageCount", Observed.Number(session.PageCount)),
            ("seconds", Observed.Number((long)Math.Round(seconds))),
            ("perMinute", Observed.Number((long)Math.Round(perMinute)))));
    }
}

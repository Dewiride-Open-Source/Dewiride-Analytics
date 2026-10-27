using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports a page whose wait was cut short: its timers ran ahead of its own clocks.
/// </summary>
/// <remarks>
/// <para>
/// The tracker waits fifteen seconds on a page before its first report of how the page is going,
/// and it waits on the browser's own clock. That report gives two accounts of the wait — the time
/// the page says it was in front of somebody, and the page's own time of day read against the time
/// of day its page view gave — and a browser whose timers keep to its clocks cannot send it sooner
/// on both. Measured on real traffic, the pages that did came straight in, almost all of them over
/// one cloud provider's network or one carrier's, with the wait over within a second or two on both
/// accounts and the next report a second or two after that; not one visit another site had sent
/// was among them.
/// </para>
/// <para>
/// Both accounts have to be short, and that is what keeps real readers out. Some browsers report
/// the time a page has been in front of somebody at a fraction of the real rate while their timers
/// keep real time, and their pages say so on one account and not the other.
/// </para>
/// <para>
/// Weighed above anything a person does, because a person cannot do it: whatever reading, scrolling
/// and pressing such a page reports happened on a clock its own timers did not keep to. What this
/// catches is timers hurried past the clocks a page can read. A program that moves those clocks
/// forward together with its timers, as a virtual source of time does, reports the whole wait and
/// is not caught here, nor is one that leaves the page's timing alone.
/// </para>
/// </remarks>
public sealed class TimingDetector : IDetector
{
    /// <summary>
    /// How long the tracker waits on a page before its first report of how the page is going.
    /// </summary>
    /// <remarks>
    /// The tracker's own figure, and a test holds the two together: a tracker that reported sooner
    /// would make every honest browser look like one whose wait was cut short.
    /// </remarks>
    private const int FirstReportAfterMs = 15000;

    /// <summary>
    /// How much sooner than that a report has to come to say more than a clock read coarsely.
    /// </summary>
    /// <remarks>
    /// Browsers that guard against being identified by their timing hand pages a coarser clock, a
    /// tenth of a second at the coarsest. A whole second is ten times that.
    /// </remarks>
    private const int ToleranceMs = 1000;

    /// <summary>What a wait cut short is worth.</summary>
    /// <remarks>
    /// Above the heaviest thing a person does, so a page whose wait was cut short is never called a
    /// person however much else it reported.
    /// </remarks>
    private const int CutShortWeight = 75;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.SoonestProgressMs is not (int soonest and < FirstReportAfterMs - ToleranceMs))
        {
            return [];
        }

        // The whole seconds the wait was over within, which is never nought: a sentence saying a
        // wait was over within no time at all would claim a precision the clock never had.
        return
        [
            Observed.Signal(
                SignalCodes.WaitCutShort,
                SignalDirection.TowardAutomation,
                CutShortWeight,
                ("seconds", Observed.Number((soonest / 1000) + 1))),
        ];
    }
}

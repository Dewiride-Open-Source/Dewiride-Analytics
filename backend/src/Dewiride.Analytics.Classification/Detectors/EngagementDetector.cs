using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports what a person did with the page, where anything could see it.
/// </summary>
/// <remarks>
/// <para>
/// The detector whose signals carry most of what the product can say about a person. Scrolling,
/// touching and reading are cheap to fake in principle and rarely faked in practice, because
/// automation that wanted to be counted as human would have to run a real browser, spend real
/// seconds on a page and then let it go the way a reader does — at which point it is paying a
/// person's price for a page.
/// </para>
/// <para>
/// Every reading here is skipped when it was not observed. A session no browser surface reported
/// on produces nothing from this detector, rather than producing evidence of absence — which is
/// the single most important line in the engine. Treating "we were not watching" as "nothing
/// happened" would classify every server-side observation of a real person as automation.
/// </para>
/// <para>
/// Time counts as reading only where the pages said it as they were being left. Fleets of browsers
/// measured on real traffic opened a page, waited and were switched off, and every one of them
/// stopped on one of the tracker's running accounts of a page still open — at fifteen seconds, at
/// forty-five, at a hundred and five — while about ninety-seven in a hundred of the people another
/// site sent ended on a page being left. What the pages reported beyond that is reported as left
/// open, pointing the other way and too light to count, so the seconds a reader is shown for the
/// visit are never silently dropped.
/// </para>
/// </remarks>
public sealed class EngagementDetector : IDetector
{
    /// <summary>Shortest time on a page that suggests it was being read rather than fetched.</summary>
    private const int MeaningfulReadMs = 2000;

    /// <summary>
    /// What time a page was left open for is worth, pointing away from a person.
    /// </summary>
    /// <remarks>
    /// Below the weight at which an observation counts toward a conclusion, on purpose. A reader
    /// whose laptop is shut mid-page leaves a page open as surely as a program switched off does, and
    /// a report that a page was being left can be lost on its way, so it never makes anything
    /// automation on its own and never outweighs anything that counts; the work is done by not
    /// counting the time as reading.
    /// </remarks>
    private const int LeftOpenWeight = 20;

    /// <summary>Time beyond which the reading counts for as much as it is going to.</summary>
    private const int SubstantialReadMs = 15000;

    /// <summary>
    /// How far down a page has to have come into view, first screen included, to be reported. A
    /// page no taller than four screens gets there by being shown, so this alone is not reading.
    /// </summary>
    private const byte MeaningfulScrollPercent = 25;

    /// <summary>Pages a session must reach before the absence of any interaction is worth reporting.</summary>
    private const int PagesBeforeSilenceCounts = 2;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        AddReading(found, session);
        AddLeftOpen(found, session);
        AddScrolling(found, session);
        AddTouching(found, session);
        AddSilence(found, session);

        return found.ToImmutable();
    }

    private static void AddReading(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (Read(session) is not (int read and >= MeaningfulReadMs))
        {
            return;
        }

        // Reaching a substantial read is worth more than scraping past the threshold, and nothing
        // beyond it is worth more still: a tab left open all afternoon says no more about whether
        // somebody was reading than one open for a minute.
        var weight = read >= SubstantialReadMs ? 60 : 40;

        found.Add(Observed.Signal(
            SignalCodes.ReadTime,
            SignalDirection.TowardHuman,
            weight,
            ("seconds", Observed.Number(read / 1000))));
    }

    /// <summary>
    /// Time the session's pages were in front of somebody up to the last time each said it was
    /// being left.
    /// </summary>
    /// <remarks>
    /// All of it where nothing could say how much was left open, which is a visitor still on the
    /// site: their pages are open because they are still there.
    /// </remarks>
    private static int? Read(SessionEvidence session) =>
        session.EngagedMs is int engaged ? Math.Max(0, engaged - (session.LeftOpenMs ?? 0)) : null;

    private static void AddLeftOpen(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session.LeftOpenMs is not (int leftOpen and >= MeaningfulReadMs))
        {
            return;
        }

        found.Add(Observed.Signal(
            SignalCodes.LeftOpen,
            SignalDirection.TowardAutomation,
            LeftOpenWeight,
            ("seconds", Observed.Number(leftOpen / 1000))));
    }

    private static void AddScrolling(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session.MaxScrollDepthPercent is not (byte depth and >= MeaningfulScrollPercent))
        {
            return;
        }

        found.Add(Observed.Signal(
            SignalCodes.Scrolled,
            SignalDirection.TowardHuman,
            45,
            ("percent", Observed.Number(depth))));
    }

    private static void AddTouching(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session.HadPointerInteraction == true)
        {
            found.Add(Observed.Signal(SignalCodes.PointerUsed, SignalDirection.TowardHuman, 55));
        }

        if (session.HadKeyboardInteraction == true)
        {
            found.Add(Observed.Signal(SignalCodes.KeyboardUsed, SignalDirection.TowardHuman, 50));
        }
    }

    /// <summary>
    /// Reports a session that something was watching and in which nothing whatever happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only reportable when every reading was actually observed. One unobserved reading is enough
    /// to make this silence meaningless, because the thing that would have spoken was not
    /// listened for.
    /// </para>
    /// <para>
    /// The time weighed here is all of it, left open or not. A page kept open in front of a browser
    /// is something happening even where it is not somebody reading, and calling it silence would
    /// make a reader whose laptop was shut mid-page into a visit nobody was ever at.
    /// </para>
    /// </remarks>
    private static void AddSilence(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        var watched = session is
        {
            HadPointerInteraction: not null,
            HadKeyboardInteraction: not null,
            EngagedMs: not null,
            MaxScrollDepthPercent: not null,
        };

        if (!watched || session.PageCount < PagesBeforeSilenceCounts)
        {
            return;
        }

        var nothingHappened = session is
        {
            HadPointerInteraction: false,
            HadKeyboardInteraction: false,
            EngagedMs: < MeaningfulReadMs,
            MaxScrollDepthPercent: < MeaningfulScrollPercent,
        };

        if (nothingHappened)
        {
            found.Add(Observed.Signal(
                SignalCodes.NoEngagement,
                SignalDirection.TowardAutomation,
                50,
                ("pageCount", Observed.Number(session.PageCount))));
        }
    }
}

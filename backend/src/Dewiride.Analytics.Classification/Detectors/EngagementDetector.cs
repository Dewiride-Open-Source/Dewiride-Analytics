using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports what a person did with the page, where anything could see it.
/// </summary>
/// <remarks>
/// <para>
/// The detector whose signals carry most of what the product can say about a person. Staying on a
/// page is cheap to fake in principle and rarely faked in practice, because automation that wanted
/// to be counted as human would have to run a real browser, spend real seconds on a page and then
/// let it go the way a reader does — at which point it is paying a person's price for a page.
/// </para>
/// <para>
/// A visit that came straight in has only that to speak for it. How far down a page came into view
/// counts the first screen, and a program can press a key as easily as anybody. A fleet of browsers
/// measured on real traffic opened one page in a window three thousand pixels tall and closed it,
/// half of them within half a second, with a third of the page counted as seen, which a window that
/// tall reaches by being opened. So where nothing named a sender, how far down a page was seen counts
/// only once the pages have stayed fifteen seconds, and a press does not count at any length of stay.
/// A visit another site sent has that arrival beside what it did, and what it did counts.
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

    /// <summary>
    /// What a short stay and how far down a page was seen are worth on a visit that came straight
    /// in and did not stay.
    /// </summary>
    /// <remarks>
    /// Below the weight at which an observation counts toward a conclusion, and still reported,
    /// because a reader of the verdict is owed what was seen as well as why it did not settle it.
    /// </remarks>
    private const int TooLightToCount = 20;

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
        AddBriefStay(found, session);
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
        // somebody was reading than one open for a minute. Short of that, a moment on a page counts
        // only where another site sent the visit there.
        var weight = read switch
        {
            >= SubstantialReadMs => 60,
            _ when session.SentByAnotherSite => 40,
            _ => TooLightToCount,
        };

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

    /// <summary>Whether the session's pages stayed long enough for the reading to count by itself.</summary>
    private static bool Stayed(SessionEvidence session) => Read(session) is >= SubstantialReadMs;

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

        // The first screen counts toward the depth, so on a visit that came straight in it says
        // nothing about anybody until the pages have stayed: a window tall enough reaches a quarter
        // of a page by being opened.
        found.Add(Observed.Signal(
            SignalCodes.Scrolled,
            SignalDirection.TowardHuman,
            session.SentByAnotherSite || Stayed(session) ? 45 : TooLightToCount,
            ("percent", Observed.Number(depth))));
    }

    private static void AddTouching(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session.HadPointerInteraction == true)
        {
            found.Add(Press(SignalCodes.PointerUsed, 55, session));
        }

        if (session.HadKeyboardInteraction == true)
        {
            found.Add(Press(SignalCodes.KeyboardUsed, 50, session));
        }
    }

    /// <summary>
    /// What a press is worth, by whether anything named a sender.
    /// </summary>
    /// <remarks>
    /// A program can press as easily as anybody, so on a visit that came straight in a press is not
    /// taken as a sign of a person. It stays on the record as something seen, pointing nowhere.
    /// </remarks>
    private static Signal Press(string code, int weightWhereSent, SessionEvidence session) =>
        session.SentByAnotherSite
            ? Observed.Signal(code, SignalDirection.TowardHuman, weightWhereSent)
            : Observed.Signal(code, SignalDirection.Neutral, 0);

    /// <summary>
    /// Says why a visit that came straight in was not taken for somebody reading, where anything
    /// about it looked like reading.
    /// </summary>
    /// <remarks>
    /// Without it a reader of the verdict is shown what was seen and nothing that says why it did
    /// not settle the question.
    /// </remarks>
    private static void AddBriefStay(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        var shortOfAStay = !session.SentByAnotherSite && Read(session) is < SubstantialReadMs;

        if (shortOfAStay && LeftOpenIsNotTheReason(session) && ShowedMoreThanTheTracker(session))
        {
            found.Add(Observed.Signal(SignalCodes.BriefStay, SignalDirection.Neutral, 0));
        }
    }

    /// <summary>
    /// Whether the visit is over, and no sentence about time left open already accounts for its
    /// falling short of a stay.
    /// </summary>
    /// <remarks>
    /// A visitor still on the site has no time left open to read, and their stay is not over, so
    /// nothing is said about it. Where two seconds or more were left open and would have made the
    /// fifteen seconds, the sentence about that time gives the reason: it says that time was not
    /// counted, though nothing then says why how far down a page was seen or a press did not count.
    /// Less than two seconds left open has no sentence of its own, so the reason is still owed.
    /// </remarks>
    private static bool LeftOpenIsNotTheReason(SessionEvidence session) =>
        session.LeftOpenMs is int leftOpen
        && (leftOpen < MeaningfulReadMs || session.EngagedMs < SubstantialReadMs);

    /// <summary>Whether anything beyond the tracker having run was seen.</summary>
    private static bool ShowedMoreThanTheTracker(SessionEvidence session) =>
        Read(session) >= MeaningfulReadMs
        || session.MaxScrollDepthPercent >= MeaningfulScrollPercent
        || session.HadPointerInteraction == true
        || session.HadKeyboardInteraction == true;

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
    /// <para>
    /// A press, or a page seen a quarter of the way down, stops this being reported whatever they
    /// count for elsewhere, because "nothing was touched" cannot be said of a visit on which something
    /// was. So on a visit that came straight in a press can keep a visit from being called silent,
    /// and never makes one a person.
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

using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports how much of the page the visitor actually asked for.
/// </summary>
/// <remarks>
/// <para>
/// The difference between reading a page and taking its text. A browser fetches the markup and
/// then everything the markup refers to — scripts, images, stylesheets. A crawler asks for the
/// markup and stops.
/// </para>
/// <para>
/// This only means anything when something in the request path saw the visit, because that is
/// the only way to know a session existed at all when no script ran. Without it, a session that
/// executed nothing is simply a session nothing reported, and the correct conclusion is silence.
/// </para>
/// </remarks>
public sealed class RenderingDetector : IDetector
{
    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.ScriptRan)
        {
            // Moderate rather than decisive. Software that drives a real browser executes
            // everything a person's browser does, so this rules out the crudest automation and
            // nothing more.
            return [Observed.Signal(SignalCodes.ScriptExecuted, SignalDirection.TowardHuman, 35)];
        }

        if (!session.ServerObserved)
        {
            return [];
        }

        return session.ImagesFetched

            // Something rendered the markup far enough to fetch what it referred to, which a
            // crawler taking text does not do. Weak, because a person with scripting switched off
            // and a well-built crawler are hard to tell apart on this alone.
            ? [Observed.Signal(SignalCodes.ImagesOnly, SignalDirection.TowardHuman, 25)]
            : [Observed.Signal(SignalCodes.NoScriptExecution, SignalDirection.TowardAutomation, 50)];
    }
}

/// <summary>
/// Reports what a client says about itself that gives it away, and what a browser always says and
/// other software usually forgets.
/// </summary>
public sealed class ClientDeclarationDetector : IDetector
{
    /// <summary>
    /// The tallest window read as an ordinary one: the long side of a large desktop screen.
    /// </summary>
    private const int TallestOrdinaryWindow = 2560;

    /// <summary>
    /// What a window taller than a screen is worth, pointing toward automation.
    /// </summary>
    /// <remarks>
    /// Enough to decide a visit on its own against the tracker having run and anything too light to
    /// count, and less than the sixty fifteen seconds are worth, so somebody who stayed is not
    /// overruled by the size of a window.
    /// </remarks>
    private const int OversizedWindowWeight = 55;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        if (session.DeclaredWebDriver == true)
        {
            // The browser volunteered it. Trivially switched off by anybody who would rather it
            // were not there, which is why this is strong when present and means nothing at all
            // when absent.
            found.Add(Observed.Signal(SignalCodes.DeclaredWebDriver, SignalDirection.TowardAutomation, 75));
        }

        if (string.IsNullOrWhiteSpace(session.Language))
        {
            // Every browser asks for a language, because every person has one. A fetching library
            // has nothing to ask for and says nothing.
            found.Add(Observed.Signal(SignalCodes.NoLanguageDeclared, SignalDirection.TowardAutomation, 25));
        }

        AddOversizedWindow(found, session);

        return found.ToImmutable();
    }

    /// <summary>
    /// Reports a window taller and narrower than an ordinary screen gives, on a visit nothing named a
    /// sender for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window is opened at whatever size a program tells it, and every window this tall measured on
    /// real traffic came straight in with nothing pressed. A visit another site sent has that arrival
    /// beside it and is judged as it would be whatever size its window was.
    /// </para>
    /// <para>
    /// Height alone is not enough. Zoomed out, a screen held upright reports more pixels than it has.
    /// A browser filling an upright screen of 16:9 is at most 1.78 times as tall as it is wide at any
    /// zoom, and a side panel open beside the page narrows that: 320 pixels on an upright 4K screen at
    /// 125% make it 2.10. So the line is 2.2, which leaves room for a panel of about a fifth of the
    /// screen's width and lies below every window this tall measured on real traffic that did not say
    /// it was a phone. The margin costs little. A visit this misses under fifteen seconds still reads
    /// as one the product could not tell about, while a reader it wrongly caught would be called
    /// automated and have their address asked about.
    /// </para>
    /// <para>
    /// A phone is left out altogether: on a page too wide for it the height it reports is a figure from
    /// how the page was laid out rather than from its screen, in the shape of what the screen shows,
    /// and that shape can pass 2.2 on a long narrow phone.
    /// </para>
    /// </remarks>
    private static void AddOversizedWindow(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session is { ViewportHeight: int height and > TallestOrdinaryWindow, ViewportWidth: int width }
            && IsNarrowerThanAnUprightScreen(height, width)
            && !session.SentByAnotherSite
            && !SaysItIsAPhone(session))
        {
            found.Add(Observed.Signal(
                SignalCodes.OversizedWindow,
                SignalDirection.TowardAutomation,
                OversizedWindowWeight,
                ("height", Observed.Number(height))));
        }
    }

    /// <summary>
    /// Whether a window is more than 2.2 times as tall as it is wide, in whole numbers. Long, because
    /// the collector keeps any size a browser reports.
    /// </summary>
    private static bool IsNarrowerThanAnUprightScreen(int height, int width) => height * 5L > width * 11L;

    /// <summary>
    /// Whether the visitor's own description of itself marks it as a phone, by the mark MDN gives for a
    /// mobile device. Text the visitor wrote, so a program can claim it; a person's phone does not leave
    /// it out unless asked for the desktop version of a site.
    /// </summary>
    private static bool SaysItIsAPhone(SessionEvidence session) =>
        session.UserAgent?.Contains("Mobi", StringComparison.Ordinal) == true;
}

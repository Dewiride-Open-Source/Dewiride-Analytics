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

    /// <summary>The clock the first fleet's browsers reported: eight hours ahead of UTC, in minutes.</summary>
    private const short FleetClockOffsetMinutes = 480;

    /// <summary>The clock the second fleet's browsers reported: no minutes either side of UTC.</summary>
    private const short FixedWindowFleetClockOffsetMinutes = 0;

    /// <summary>How wide the second fleet's browsers said their window was, in pixels.</summary>
    private const int FixedWindowFleetWidth = 1280;

    /// <summary>How tall the second fleet's browsers said their window was, in pixels.</summary>
    private const int FixedWindowFleetHeight = 800;

    /// <summary>The operating system both fleets' browsers named, in the collector's own words.</summary>
    private const string FleetOperatingSystem = "Linux";

    /// <summary>The browser both fleets' browsers named, in the collector's own words.</summary>
    private const string FleetBrowserFamily = "Chrome";

    /// <summary>The language both fleets' browsers asked for, exactly as they asked for it.</summary>
    private const string FleetLanguage = "en-US";

    /// <summary>
    /// What either fleet's description is worth, pointing toward automation.
    /// </summary>
    /// <remarks>
    /// Above the sixty that fifteen seconds said as the pages were left are worth, which is what made
    /// the fleets' visits people. At sixty the two would tie and the visit would be one nothing could
    /// tell about; anything less would leave it a person.
    /// </remarks>
    private const int FleetDescriptionWeight = 65;

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
        AddFleetDescription(found, session);

        return found.ToImmutable();
    }

    /// <summary>
    /// Reports a browser that described itself word for word as one of two fleets measured on real
    /// traffic described itself, on a visit nothing named a sender for and on which nothing was pressed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first fleet opened archive pages one at a time, first over a rented network and then over
    /// household and mobile ones, and stayed past fifteen seconds without a press, which with the
    /// tracker having run made each visit a person. Its browsers all said the same four things about
    /// themselves: Chrome, on Linux, asking for American English, with a clock eight hours ahead of
    /// UTC.
    /// </para>
    /// <para>
    /// The second opened one page at a time, mostly over small networks that carried nothing else and
    /// once over a rented one, and left within a minute without a press. Its browsers said the same
    /// first three things, with a clock on UTC and a window exactly 1,280 pixels wide and 800 tall.
    /// The window is part of the description because the rest of it was also said by visits nothing
    /// else ties to the fleet.
    /// </para>
    /// <para>
    /// No reader in the record gave either description whole, came straight in and pressed nothing.
    /// Every part of each is something a browser says about itself, so this establishes a description
    /// and nothing about who gave it, and changing any one part defeats it. A press, where pointer and
    /// keys were both watched for, and another site having sent the visit each put it out of reach:
    /// neither fleet did either, and a reader who does is weighed as any other.
    /// </para>
    /// </remarks>
    private static void AddFleetDescription(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (!SaidWhatBothFleetsSaid(session) || !CameStraightInAndPressedNothing(session))
        {
            return;
        }

        if (FleetItDescribedItselfAs(session) is { } code)
        {
            found.Add(Observed.Signal(code, SignalDirection.TowardAutomation, FleetDescriptionWeight));
        }
    }

    /// <summary>Whether the three things both fleets said about themselves were said, exactly.</summary>
    private static bool SaidWhatBothFleetsSaid(SessionEvidence session) =>
        string.Equals(session.OperatingSystem, FleetOperatingSystem, StringComparison.Ordinal)
        && string.Equals(session.BrowserFamily, FleetBrowserFamily, StringComparison.Ordinal)
        && string.Equals(session.Language, FleetLanguage, StringComparison.Ordinal);

    /// <summary>
    /// The observation reporting the fleet whose whole description the rest of what the browser said
    /// completes, or nothing where it completes neither.
    /// </summary>
    private static string? FleetItDescribedItselfAs(SessionEvidence session) => session switch
    {
        { TimezoneOffsetMinutes: FleetClockOffsetMinutes } => SignalCodes.FleetDescription,
        {
            TimezoneOffsetMinutes: FixedWindowFleetClockOffsetMinutes,
            ViewportWidth: FixedWindowFleetWidth,
            ViewportHeight: FixedWindowFleetHeight,
        } => SignalCodes.FixedWindowFleetDescription,
        _ => null,
    };

    /// <summary>
    /// Whether nothing named a sender, pointer and keys were both watched for and neither was pressed,
    /// and the visitor did not say it was a phone.
    /// </summary>
    private static bool CameStraightInAndPressedNothing(SessionEvidence session) =>
        !session.SentByAnotherSite
        && session is { HadPointerInteraction: false, HadKeyboardInteraction: false }
        && !SaysItIsAPhone(session);

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

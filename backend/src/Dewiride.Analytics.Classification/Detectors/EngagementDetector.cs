using System.Collections.Immutable;
using Dewiride.Analytics.Classification.Sessions;

namespace Dewiride.Analytics.Classification.Detectors;

/// <summary>
/// Reports what a person did with the page, where anything could see it.
/// </summary>
/// <remarks>
/// <para>
/// The only detector whose signals point toward a person, and the reason the product can say
/// anything positive at all. Scrolling, touching and dwelling are cheap to fake in principle and
/// almost never faked in practice, because automation that wanted to be counted as human would
/// have to run a real browser and spend real seconds — at which point it is paying a person's
/// price for a page.
/// </para>
/// <para>
/// Every reading here is skipped when it was not observed. A session no browser surface reported
/// on produces nothing from this detector, rather than producing evidence of absence — which is
/// the single most important line in the engine. Treating "we were not watching" as "nothing
/// happened" would classify every server-side observation of a real person as automation.
/// </para>
/// </remarks>
public sealed class EngagementDetector : IDetector
{
    /// <summary>Shortest time on a page that suggests it was being read rather than fetched.</summary>
    private const int MeaningfulReadMs = 2000;

    /// <summary>Time beyond which the reading counts for as much as it is going to.</summary>
    private const int SubstantialReadMs = 15000;

    /// <summary>Scroll depth below which a page may simply have been rendered rather than read.</summary>
    private const byte MeaningfulScrollPercent = 25;

    /// <summary>Pages a session must reach before the absence of any interaction is worth reporting.</summary>
    private const int PagesBeforeSilenceCounts = 2;

    /// <inheritdoc />
    public ImmutableArray<Signal> Examine(SessionEvidence session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ImmutableArray.CreateBuilder<Signal>();

        AddReading(found, session);
        AddScrolling(found, session);
        AddTouching(found, session);
        AddSilence(found, session);

        return found.ToImmutable();
    }

    private static void AddReading(ImmutableArray<Signal>.Builder found, SessionEvidence session)
    {
        if (session.EngagedMs is not (int engaged and >= MeaningfulReadMs))
        {
            return;
        }

        // Reaching a substantial read is worth more than scraping past the threshold, and nothing
        // beyond it is worth more still: a tab left open all afternoon says no more about whether
        // somebody was reading than one open for a minute.
        var weight = engaged >= SubstantialReadMs ? 60 : 40;

        found.Add(Observed.Signal(
            SignalCodes.ReadTime,
            SignalDirection.TowardHuman,
            weight,
            ("seconds", Observed.Number(engaged / 1000))));
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
    /// Only reportable when every reading was actually observed. One unobserved reading is enough
    /// to make this silence meaningless, because the thing that would have spoken was not
    /// listened for.
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

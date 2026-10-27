using System.Collections.Immutable;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Classification.Sessions;

/// <summary>
/// Everything the engine is allowed to reason about when judging one session.
/// </summary>
/// <remarks>
/// <para>
/// A closed set of values rather than a handle on a store. The engine cannot fetch anything it
/// was not given, which is what makes a verdict reproducible from a fixture and what stops a
/// detector quietly acquiring a dependency on the network or the clock.
/// </para>
/// <para>
/// The three-state readings are three-state for the reason the whole product exists: a surface
/// that could not observe an interaction has not observed the absence of one.
/// <see langword="null"/> means nobody was watching and must never be weighed as evidence of
/// anything.
/// </para>
/// </remarks>
public sealed record SessionEvidence
{
    private readonly int? _pageCount;

    /// <summary>Identity of the session being judged.</summary>
    public required string SessionKey { get; init; }

    /// <summary>When the first request in the session was received.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the last was.</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>Every page the session asked for, oldest first.</summary>
    public required ImmutableArray<ObservedRequest> Requests { get; init; }

    /// <summary>Which capture surfaces contributed to this session.</summary>
    /// <remarks>
    /// Load-bearing rather than diagnostic. What a surface is able to see decides how the
    /// absence of a reading should be read, and a session seen only by a server is a session
    /// about which nothing behavioural can be concluded.
    /// </remarks>
    public required ImmutableArray<IngestSurface> Surfaces { get; init; }

    /// <summary>What the visitor said it was. Attacker-controlled; never treated as identity on its own.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// The company that vouches for the address the visit arrived from, where one does. Null for
    /// almost every visit, and that is not a finding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing on this record that is an identity rather than a claim or a behaviour. It was
    /// settled by where the request came from — against a file of addresses the company publishes,
    /// or against the domain it documents its machines as answering to — so it is not something a
    /// visitor can write for itself the way a user agent is, and it is the only route to
    /// <see cref="EvidenceStrength.Verified"/>.
    /// </para>
    /// <para>
    /// Settled before the engine is asked anything, never worked out here. The address it was
    /// settled from is erased 72 hours after the activity was collected, so it is the answer that
    /// is kept rather than the address — on the activity itself wherever the collector recognised
    /// the address as a report arrived, and beside the visit where the name its address answers to
    /// was settled only once the visit was over — and that is what lets a visit re-judged next year
    /// reach the same conclusion instead of quietly losing an identity it once had.
    /// </para>
    /// <para>
    /// Absence proves nothing and must never be read as a denial. A company may offer no way to
    /// check at all, an install may have no way out to the internet, and what a company publishes
    /// is a snapshot that a newly commissioned crawler is not in until it publishes again.
    /// </para>
    /// </remarks>
    public string? ConfirmedOperator { get; init; }

    /// <summary>Language the visitor declared, where any surface could observe one.</summary>
    public string? Language { get; init; }

    /// <summary>Viewport width the browser reported, where a browser surface ran.</summary>
    public int? ViewportWidth { get; init; }

    /// <summary>Milliseconds the pages were actually in front of somebody, summed across the session.</summary>
    public int? EngagedMs { get; init; }

    /// <summary>
    /// Of <see cref="EngagedMs"/>, the milliseconds the pages reported without saying they were
    /// being left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tracker reports the time a page has been in front of somebody twice over: while the page
    /// stays open, so that a browser shut down outright is still measured, and again as the page is
    /// left — closed, navigated away from, or put behind another tab. Only the second is said by a
    /// page somebody let go of. Time a page reported after the last time it said it was being left,
    /// or at all where it never said so, is time a browser kept the page open as far as anything
    /// heard: what somebody reading does until a laptop is shut on them, what a page does whose
    /// last report was lost on the way, and what a program does that opens a page, waits and is
    /// switched off without closing it.
    /// </para>
    /// <para>
    /// Counted only where the reports a page was left with could have been heard. A page whose
    /// address was arrived at again later in the visit may have had its leaving filed with the
    /// next arrival, and a page still open at midnight goes on reporting under the next day's key;
    /// neither is taken for a page left open.
    /// </para>
    /// <para>
    /// Null where nothing could tell: where nothing measured any time at all, and where the visitor
    /// is still on the site, whose pages are open because they are still there.
    /// </para>
    /// </remarks>
    public int? LeftOpenMs { get; init; }

    /// <summary>
    /// How soon a page sent its first report of how it was going, in milliseconds, on whichever of
    /// the two clocks that report carries says longer — the soonest across the session's pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tracker waits fifteen seconds on a page before that report, and waits on the browser's
    /// own clock. The report gives two accounts of the wait: the time the page says it was in front
    /// of somebody, and the page's own time of day, read against the time of day its page view gave.
    /// A browser whose timers keep to its own clocks cannot send it sooner on both; one whose timers
    /// are hurried past them — a page opened in a sandbox to see what it does, a renderer hurrying
    /// through a page it was handed — sends it within a second or two. One that moves its clocks
    /// forward together with its timers reports the whole wait, and is not told apart here.
    /// </para>
    /// <para>
    /// The longer of the two, because either alone can be short in an honest browser. Some report
    /// the time a page has been in front of somebody at a fraction of the real rate while keeping
    /// real time otherwise, and a report from one tab can be read against a second tab's page view
    /// of the same page; neither shortens both at once.
    /// </para>
    /// <para>
    /// Null where no page sent such a report, or where nothing gave the time of day it opened at.
    /// </para>
    /// </remarks>
    public int? SoonestProgressMs { get; init; }

    /// <summary>
    /// Deepest point of any page that came into view, as a percentage of its height. The first
    /// screen counts, so a page that was only shown reports the share of it that screen held.
    /// </summary>
    public byte? MaxScrollDepthPercent { get; init; }

    /// <summary>Whether any pointer interaction was observed. Null means no surface could see.</summary>
    public bool? HadPointerInteraction { get; init; }

    /// <summary>Whether any keyboard interaction was observed. Null means no surface could see.</summary>
    public bool? HadKeyboardInteraction { get; init; }

    /// <summary>Whether the client declared itself under automation control.</summary>
    public bool? DeclaredWebDriver { get; init; }

    /// <summary>
    /// The routing number of the network the visit arrived over. Nought means nothing resolved one.
    /// </summary>
    /// <remarks>
    /// The one thing here that whoever is driving the browser did not choose. Everything else on
    /// this record is either something the client said about itself or something it did, and both
    /// can be produced deliberately; where the request came from cannot be, short of renting a
    /// household connection. Nought is common and is not a finding — an installation whose proxy
    /// does not pass the visitor's address through resolves nothing at all.
    /// </remarks>
    public uint AutonomousSystem { get; init; }

    /// <summary>
    /// Who runs that network, as the routing registry names them. Empty where nothing resolved it.
    /// </summary>
    /// <remarks>
    /// Carried for the reader rather than for the rules: the engine matches on the number, which
    /// outlives the names its holders trade under.
    /// </remarks>
    public string? NetworkOwner { get; init; }

    /// <summary>
    /// What kind of site sent the visit, where one was named. <see cref="SourceChannel.Direct"/>
    /// where nothing said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settled exactly as the visit list settles it: the first report of the visit whose referring
    /// address names a site other than the one being measured, reduced to a kind through the
    /// catalogue of sending sites. A site's own pages never send it a visit, so a reader moving
    /// from one page to the next is not an arrival. A reading of who is here now settles it over
    /// the reports its window holds rather than the whole visit's, so a visitor who arrived before
    /// the window opened reads there as sent by nobody.
    /// </para>
    /// <para>
    /// Only the kind reaches the engine, never the address or the site it names. The address is
    /// text the visitor's browser wrote, and the kind is all a rule needs: that another site sent
    /// the visit, and which sort of site that was. Direct means nothing named a sender, which is
    /// not the same as nobody having sent them, and no rule reads it as evidence of anything.
    /// </para>
    /// </remarks>
    public SourceChannel SentBy { get; init; } = SourceChannel.Direct;

    /// <summary>How long the session lasted.</summary>
    public TimeSpan Duration => EndedAt - StartedAt;

    /// <summary>
    /// How many pages the session asked for.
    /// </summary>
    /// <remarks>
    /// Counted rather than measured from <see cref="Requests"/>, because that array is capped when
    /// a session is rebuilt from stored activity: one sweep can ask for tens of thousands of pages
    /// in a single visit, and holding all of them in order to count them would let a visitor decide
    /// how much memory the engine uses. The count stays exact and the array is the earliest of
    /// them. Left unset — as a hand-written fixture leaves it — it is the length of the array.
    /// </remarks>
    public int PageCount
    {
        get => _pageCount ?? Requests.Length;
        init => _pageCount = value;
    }

    /// <summary>Whether a browser ran the tracker during this session.</summary>
    public bool ScriptRan => Surfaces.Contains(IngestSurface.BrowserTracker);

    /// <summary>Whether the no-script image was fetched, which means something rendered the page.</summary>
    public bool ImagesFetched => Surfaces.Contains(IngestSurface.NoScriptPixel);

    /// <summary>
    /// Whether anything in the request path reported this session.
    /// </summary>
    /// <remarks>
    /// Only these surfaces observe a visitor that never executes anything, so only they can
    /// establish that a session existed at all when no script ever ran.
    /// </remarks>
    public bool ServerObserved => Surfaces.Any(surface => surface is not (
        IngestSurface.Unknown or IngestSurface.BrowserTracker or IngestSurface.NoScriptPixel));
}

/// <summary>
/// One page a session asked for.
/// </summary>
/// <param name="At">When the request was received, by the collector's own clock.</param>
/// <param name="Path">
/// Path component of the requested URL. Written by whoever is visiting the site: never
/// interpolated into a statement, never rendered as markup, and never placed in a signal
/// parameter that reaches a screen.
/// </param>
/// <param name="StatusCode">What the site answered, where a surface could observe it.</param>
public readonly record struct ObservedRequest(DateTimeOffset At, string Path, short? StatusCode);

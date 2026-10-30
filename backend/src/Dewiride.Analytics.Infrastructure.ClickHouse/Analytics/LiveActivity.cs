using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reads the last stretch of minutes of a site's activity, ready to be asked about.
/// </summary>
/// <remarks>
/// <para>
/// Written once here because the four statements that answer about the present moment need the
/// same activity and the same visitor: who has been here, how much was read minute by minute,
/// which pages they are on, and what one of them has been doing. Four answers on one screen that
/// disagreed about who a visitor is or which page they are on would be worse than any one of them
/// being absent.
/// </para>
/// <para>
/// <strong>Nothing is read from outside the window</strong>, and that is the difference between this
/// and every other reconstruction in this project. The others rebuild visits, and a visit has to be
/// read from where it began, so they reach a full day either side. This one describes a stretch of
/// minutes: a report outside it is not part of that description, whatever it says about a page
/// somebody is still on. Two things follow. The reading is a prefix of the primary key and touches
/// nothing else, which is what makes it cheap enough to ask over and over. And a page arrived at
/// before the window opened still counts for a visitor the window holds opening another, because
/// they were on it — and how they reached the site is then known only as far as the window says. A
/// site that sent them is named wherever a page in the window names one; that none was named is
/// said only where the window saw them arrive, which <see cref="SeenArriving"/> decides.
/// </para>
/// <para>
/// Who is here at all is somebody the window holds opening a page, which
/// <see cref="OpenedAPage"/> settles once for every statement that counts visitors, so the list of
/// who is here and the pages they are on cannot disagree about whom they hold.
/// </para>
/// <para>
/// A page a visitor was on is counted once here, however many reports described it and however many
/// surfaces sent them. It is narrower than the page <see cref="VisitGrouping"/> counts, which knows
/// where each arrival was: a visitor who leaves a page and comes back to it inside the window is on
/// one page here and on two there. That is the honest reading of a question with no visit
/// boundaries in it — there is nothing here for "again" to mean.
/// </para>
/// <para>
/// A visitor is on one page: the one their most recent report named. It is the page printed beside
/// their row and the page they are counted under in the list of pages being read, and it is written
/// once, as <see cref="CurrentPage"/>, so the two cannot disagree.
/// </para>
/// <para>
/// The two halves of the measurement are folded onto one key first, by every statement that uses
/// this. Without it, a site reported by both its own server and the browser shows every visitor
/// twice for the second or so before the browser echoes what it was given — which on a screen that
/// renews itself is not a rounding error but a number visibly disagreeing with itself.
/// </para>
/// </remarks>
internal static class LiveActivity
{
    /// <summary>Where a carried-through column sits in the statement this writes.</summary>
    private const string ColumnIndent = "\n            ";

    /// <summary>
    /// The activity the window itself holds, and nothing on either side of it.
    /// </summary>
    /// <remarks>
    /// The same condition as <see cref="SendingSites.ThePeriod"/> and for the same reason: a report
    /// either falls inside the stretch being described or it does not, and no report outside it
    /// changes what one inside it says.
    /// </remarks>
    public static string TheseMinutes { get; } = SendingSites.ThePeriod;

    /// <summary>
    /// Every visitor the window holds.
    /// </summary>
    /// <remarks>
    /// Activity carrying no visitor key takes no part, on the same terms as
    /// <see cref="VisitGrouping.EveryVisitor"/>: a surface that could not derive one has observed
    /// nothing about who was there, and gathering all of those under one empty key would put a
    /// single impossibly busy visitor at the top of the list.
    /// </remarks>
    public const string EveryVisitor = VisitGrouping.EveryVisitor;

    /// <summary>
    /// Whether the window holds a page the visitor opened, which is what makes them somebody here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A progress report and a departure are accounts of a page somebody already had, sent from the
    /// page itself, and a page left on a screen goes on sending them for as long as it stays there:
    /// one a minute through the evening, the night and on into the next day. Counted as somebody
    /// here, it would sit at the top of the list, active a moment ago, every minute of that. And
    /// once the day or the network it came over changes, its reports arrive under a key with no
    /// arrival behind it, which no visit is ever made of — so it would stay on the list as a visitor
    /// the finished visits never mention, waiting for a verdict that is never coming.
    /// </para>
    /// <para>
    /// So a visitor is somebody here while a page they opened is inside the window, and what they
    /// reported about a page opened before it counts for them once they are. Nothing is taken from
    /// what a finished visit is judged on: this decides only whom the list of who is here holds.
    /// The cost is a reader who stays on one page for longer than the window without opening
    /// another, who leaves the list until they do.
    /// </para>
    /// <para>
    /// A condition on a selection grouped by visitor and carrying <c>kind</c>.
    /// </para>
    /// </remarks>
    public const string OpenedAPage = "countIf(kind = 'PageView') > 0";

    /// <summary>
    /// The page a visitor is on: the one their most recent report named.
    /// </summary>
    /// <remarks>
    /// Written once because two statements place a visitor — the row that lists them, and the list
    /// of pages being read, which counts visitors by it. A visitor printed beside one page and
    /// counted under another would be the disagreement this file exists to prevent. Over a
    /// selection carrying <c>server_ts</c>, <c>event_id</c> and <c>path</c>, grouped by visitor.
    /// </remarks>
    public const string CurrentPage = "argMax(path, (server_ts, event_id))";

    /// <summary>
    /// Orders the tracker's own reports ahead of every other surface's, the store sorting false
    /// before true.
    /// </summary>
    /// <remarks>
    /// Spelled from the stored name rather than typed out, so the surface tested is the one the
    /// collector writes. The tracker alone rather than everything in the visitor's browser: the
    /// image a page with no script running asks for reports no referrer at all.
    /// </remarks>
    private static readonly string TheTrackerFirst =
        $"surface != '{StoredNames.SurfaceNames[IngestSurface.BrowserTracker]}'";

    /// <summary>
    /// Orders the image a page with no script running asks for behind every other surface's
    /// reports, the store sorting false before true.
    /// </summary>
    /// <remarks>
    /// The image never says where its page was reached from, so its report of a page is silence
    /// about that, every time. Ordered among the others by when each reached the collector, it
    /// would let that silence speak over a report of the same page that did say — a reporter on the
    /// site's own server may send its reports in batches, well after the image's request has
    /// landed — and which of the two answered would turn on network timing. Read last, it decides
    /// only where nothing else reported a page at all.
    /// </remarks>
    private static readonly string TheImageLast =
        $"surface = '{StoredNames.SurfaceNames[IngestSurface.NoScriptPixel]}'";

    /// <summary>
    /// How long a reading about the present moment may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated on the statement rather than left to the store's own default, which is no limit at
    /// all. A screen that renews itself asks again whether or not the last answer has arrived, so a
    /// reading that has become slow enough to be useless must stop occupying the store rather than
    /// wait for somebody to close the tab.
    /// </para>
    /// <para>
    /// A limit on time rather than a rule about the caller going away, because the store's setting
    /// for the latter does not do it: a query whose client has disconnected keeps running to the
    /// end. Ten seconds is the beat these are asked on, so a reading that misses one is already
    /// answering about a moment that has passed.
    /// </para>
    /// </remarks>
    public const string WithinTheBeat = "SETTINGS max_execution_time = 10";

    /// <summary>
    /// Whether the window saw a visitor arrive, rather than only saw them already here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What lets a reading say that nothing named a site as having sent a visitor. A window is a
    /// stretch of minutes rather than a visit, so somebody who arrived before it opened and is still
    /// reading reaches it with no sending site, and "came straight here" would then be a claim the
    /// window cannot carry. A visitor counts as seen arriving unless the window holds none of their
    /// page views, holds something of theirs from before the earliest of them — whichever surface
    /// reported it — or the page view that decides, chosen as set out below, names the site itself
    /// as where it was reached from. A progress report, a departure or a press ahead of any page view
    /// is somebody already reading a page; a page reached from another page of the same site is
    /// somebody who was already here.
    /// </para>
    /// <para>
    /// The page view that decides is the tracker's own first, because the tracker says where its
    /// page was reached from whenever the browser does — where a reporter on the site's own server
    /// may not, and the image a page with no script running asks for never does. Where the tracker
    /// reported no page view, the earliest from a surface able to say decides, and the image's only
    /// where nothing else reported one. Of what its referrer can say, only one that positively names
    /// the site counts against an arrival. One naming nothing, or naming no host at all, is what a
    /// finished visit reads as nobody having named a sender, and the two readings say the same thing
    /// about it.
    /// </para>
    /// <para>
    /// Over a selection carrying <c>kind</c>, <c>surface</c>, <c>server_ts</c>, <c>event_id</c>,
    /// <c>referrer_domain</c> and the <c>sending_host</c> <see cref="SendingSites"/> reduces it to,
    /// grouped by visitor.
    /// </para>
    /// </remarks>
    /// <param name="indent">
    /// The column the calling statement places the expression at, which its arguments are laid out
    /// one step inside.
    /// </param>
    /// <returns>The expression, which is one for a visitor seen arriving and nought otherwise.</returns>
    public static string SeenArriving(int indent)
    {
        var pad = new string(' ', indent);

        return $"""
            toUInt8(
            {pad}    countIf(kind = 'PageView') > 0
            {pad}    AND min(server_ts) = minIf(server_ts, kind = 'PageView')
            {pad}    AND NOT argMinIf(
            {pad}        referrer_domain != '' AND sending_host = '',
            {pad}        ({TheTrackerFirst}, {TheImageLast}, server_ts, event_id),
            {pad}        kind = 'PageView'))
            """;
    }

    /// <summary>
    /// Reads the window straight from the store, ending in a <c>windowed</c> selection.
    /// </summary>
    /// <param name="carried">
    /// The columns of <c>events</c> the calling statement needs. Each is a fixed identifier written
    /// by a compiler in this assembly and never by a caller.
    /// </param>
    /// <returns>The expression, ready to follow a <c>WITH</c> keyword.</returns>
    public static string Window(params string[] carried) => $$"""
        windowed AS
            (
                SELECT
                    {{string.Join($",{ColumnIndent}", carried)}}
                FROM events
                WHERE site_id = {site_id:UUID}
                  AND {{TheseMinutes}}
            )
        """;

    /// <summary>
    /// Reads the window with each arrival reduced to the site that sent it, and marks one report per
    /// page the visitor was on. Ends in a <c>present</c> selection.
    /// </summary>
    /// <remarks>
    /// The report that opens a page is chosen as <see cref="VisitGrouping"/> chooses it for a
    /// finished visit: the request path's sighting ahead of the browser's, then a page view ahead of
    /// anything else, then the earliest. A page both halves saw therefore carries the status the
    /// site answered with in the live reading exactly as it does once the visit is over, and a rule
    /// that reads that status reaches the same conclusion in both places.
    /// </remarks>
    /// <param name="visitors">
    /// Which visitors take part, as a condition over <c>identified</c>. Written by a compiler in
    /// this assembly and never by a caller: where it narrows to a single visitor, that visitor's key
    /// travels as a bound value and only the parameter's name appears here.
    /// </param>
    /// <param name="carried">
    /// The columns of <c>events</c> the calling statement needs carried through. Each is a fixed
    /// identifier written by a compiler in this assembly and never by a caller.
    /// </param>
    /// <returns>
    /// The expressions, ending in <c>present</c>, which carries <paramref name="carried"/> plus what
    /// <see cref="SendingSites"/> adds, <c>page_engaged_ms</c> and <c>opens_page</c>.
    /// </returns>
    public static string WithSources(string visitors, params string[] carried) => $$"""
        {{SendingSites.Of(TheseMinutes, carried)}},
            {{ReconciledEvents.Reconciliation}},
            present AS
            (
                SELECT
                    *,
                    max(engaged_ms) OVER (PARTITION BY visitor_key, path) AS page_engaged_ms,
                    row_number() OVER (
                        PARTITION BY visitor_key, path
                        ORDER BY {{ReconciledEvents.FromVisitorBrowser}}, kind != 'PageView', server_ts, event_id) = 1 AS opens_page
                FROM identified
                WHERE {{visitors}}
            )
        """;
}

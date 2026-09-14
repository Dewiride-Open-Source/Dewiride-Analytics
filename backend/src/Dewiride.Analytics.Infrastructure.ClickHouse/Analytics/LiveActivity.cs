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
/// before the window opened still counts, because the visitor was on it — it is only unknown where
/// they arrived.
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

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Groups a visitor's reconciled activity into visits and into the pages those visits went to,
/// and marks every report that is a second account of a page another surface already reported.
/// </summary>
/// <remarks>
/// <para>
/// A visit begins when somebody arrives, and runs until the first silence longer than the idle
/// timeout. The store has no session function, so the running total below is the implementation —
/// and it is written once here because more than one statement needs it: the one that rebuilds
/// visits for the detection engine, and the ones that answer where visits began, where they ended,
/// and which pages a single visit went through. A visit is therefore defined in exactly one place
/// however many questions are asked about one.
/// </para>
/// <para>
/// Only an arrival begins one. A tracker reports how a page is going, and reports it being left,
/// from the page itself — so a report of either kind is an account of a page somebody was already
/// on, and carries no beginning of its own. A departure that reaches the collector long after the
/// reader stopped touching the page, because the tab was dismissed the next morning or the machine
/// was woken up, is the end of the visit it names rather than the whole of a new one. The silence
/// is still measured across every report whatever its kind, because any report at all is somebody
/// being there.
/// </para>
/// <para>
/// Every report belongs to the visit its own page was arrived at in. That is what the report is an
/// account of, and it is knowable from the report itself: the visitor and the page are both on it.
/// A report naming a page this visitor was never seen arriving at is left out altogether. The page
/// was delivered to somebody, but nothing on the report says which visit it belonged to, and the
/// nearest one is not a safe guess — a visitor's key is derived from the network the report came
/// over and the day it arrived, so it changes under a reader who moves between networks and again
/// at midnight, and the tail of a visit routinely arrives under a key that announced nothing.
/// Counted as a visit of its own, one reader becomes two, the second of them a person who arrived,
/// read for a quarter of an hour and left.
/// </para>
/// <para>
/// Where an arrival and an account of it fall in the same instant, the arrival is read first, so a
/// page is never reported on before the visit it belongs to has been settled.
/// </para>
/// <para>
/// A visit watched by both a tracker in the browser and a reporter on the site's own server holds
/// two accounts of every page it asked for. The second is marked rather than dropped, so each
/// statement decides what to do with it. It is the browser's copy that is marked, because the
/// report from the request path carries the status the site answered with, which is most of what
/// identifies something probing for a way in.
/// </para>
/// <para>
/// A page the visit went to is every report about one arrival at it, folded into one — so a page
/// read for half an hour and reported on thirty times is the single delivery it was, and a page
/// asked for twice is two. Where both halves announced the same arrival, the request path's report
/// is the one that stands for it.
/// </para>
/// <para>
/// Expects a preceding <c>identified</c> selection — see
/// <see cref="ReconciledEvents.Reconciliation"/> — carrying at least <c>event_id</c>,
/// <c>server_ts</c>, <c>kind</c>, <c>path</c>, <c>surface</c> and <c>visitor_key</c>, and an
/// <c>idle_seconds</c> value bound by the caller.
/// </para>
/// </remarks>
internal static class VisitGrouping
{
    /// <summary>
    /// Reads a visitor's reports in the order the visit happened in.
    /// </summary>
    /// <remarks>
    /// The instant, then the arrival ahead of the accounts of it, then the report's own identity so
    /// that two written in the same millisecond are read the same way every time.
    /// </remarks>
    private const string InOrder = "ORDER BY server_ts, kind != 'PageView', event_id";

    /// <summary>
    /// Writes the grouping, over the visitors the calling statement is asking about.
    /// </summary>
    /// <param name="visitors">
    /// Which visitors take part, as a condition over <c>identified</c>. Written by a compiler in
    /// this assembly and never by a caller: where it narrows to a single visitor, that visitor's
    /// key travels as a bound value and only the parameter's name appears here.
    /// </param>
    /// <returns>The eight expressions, ending in <c>opened</c>.</returns>
    public static string Of(string visitors) => $$"""
        reported AS
            (
                SELECT
                    *,
                    max(if(kind = 'PageView', toUnixTimestamp64Milli(server_ts), 0)) OVER (
                        PARTITION BY visitor_key, path
                        {{InOrder}}
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS arrival_ms
                FROM identified
                WHERE {{visitors}}
            ),
            ordered AS
            (
                SELECT
                    *,
                    dateDiff('second', lagInFrame(server_ts, 1, server_ts) OVER visit, server_ts) AS since_previous
                FROM reported
                WHERE arrival_ms > 0
                WINDOW visit AS (PARTITION BY visitor_key {{InOrder}} ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)
            ),
            numbered AS
            (
                SELECT
                    *,
                    sum(toUInt8(kind = 'PageView' AND since_previous > {idle_seconds:Int64})) OVER (
                        PARTITION BY visitor_key
                        {{InOrder}}
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS arrival_ordinal
                FROM ordered
            ),
            grouped AS
            (
                SELECT
                    *,
                    max(if(kind = 'PageView', arrival_ordinal, 0)) OVER (
                        PARTITION BY visitor_key, path
                        {{InOrder}}
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS visit_ordinal
                FROM numbered
            ),
            sighted AS
            (
                SELECT
                    *,
                    row_number() OVER (
                        PARTITION BY visitor_key, visit_ordinal, path, kind, {{ReconciledEvents.FromVisitorBrowser}}
                        ORDER BY server_ts, event_id) AS sighting,
                    sum(toUInt8(kind = 'PageView' AND {{ReconciledEvents.FromRequestPath}})) OVER (
                        PARTITION BY visitor_key, visit_ordinal, path) AS sightings_from_path
                FROM grouped
            ),
            counted AS
            (
                SELECT
                    *,
                    kind = 'PageView'
                        AND {{ReconciledEvents.FromVisitorBrowser}}
                        AND sighting <= sightings_from_path AS is_second_sighting
                FROM sighted
            ),
            paged AS
            (
                SELECT
                    *,
                    greatest(
                        toUInt64(1),
                        sum(toUInt8(kind = 'PageView' AND NOT is_second_sighting)) OVER (
                            PARTITION BY visitor_key, visit_ordinal, path
                            ORDER BY server_ts, event_id
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)) AS page_ordinal
                FROM counted
            ),
            opened AS
            (
                SELECT
                    *,
                    row_number() OVER (
                        PARTITION BY visitor_key, visit_ordinal, path, page_ordinal
                        ORDER BY is_second_sighting, kind != 'PageView', server_ts, event_id) = 1 AS opens_page
                FROM paged
            )
        """;

    /// <summary>
    /// Every visitor a window holds.
    /// </summary>
    /// <remarks>
    /// Activity carrying no visitor key takes no part. A surface that could not derive one has not
    /// observed an anonymous visitor; it has observed nothing about who was there, and gathering
    /// all of those under one empty key would build a single impossibly busy visitor out of
    /// everybody the product could not identify.
    /// </remarks>
    public const string EveryVisitor = "visitor_key != ''";
}

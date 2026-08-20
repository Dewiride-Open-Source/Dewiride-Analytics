namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Groups a visitor's reconciled activity into visits and into the pages those visits went to,
/// and marks every report that is a second account of a page another surface already reported.
/// </summary>
/// <remarks>
/// <para>
/// A visit is one visitor's activity up to the first silence longer than the idle timeout. The
/// store has no session function, so the running total of silences crossed below is the
/// implementation — and it is written once here because more than one statement needs it now: the
/// one that rebuilds visits for the detection engine, and the ones that answer where visits began,
/// where they ended, and which pages a single visit went through. A visit is therefore defined in
/// exactly one place however many questions are asked about one.
/// </para>
/// <para>
/// A visit watched by both a tracker in the browser and a reporter on the site's own server holds
/// two accounts of every page it asked for. The second is marked rather than dropped, so each
/// statement decides what to do with it. It is the browser's copy that is marked, because the
/// report from the request path carries the status the site answered with, which is most of what
/// identifies something probing for a way in.
/// </para>
/// <para>
/// A page the visit went to is every report about one arrival at it, folded into one. A report is
/// about a page whether or not it announces one: the tracker sends progress and departure reports
/// naming the page they were measured on, and one of those is evidence that the page was delivered
/// — the script that sent it was running on the page. Reports travel by a transport that
/// acknowledges nothing, from pages frequently in the act of being closed, and the report
/// announcing the arrival is the first of them and the one most easily lost. So a page a visit only
/// ever reported reading is a page the visit read, and counting nothing for it discards the
/// strongest evidence this product ever holds that somebody was there.
/// </para>
/// <para>
/// A page cannot be counted twice this way. A path the visit announced arriving at counts once per
/// announcement whatever else was reported about it, and a path it announced no arrival at counts
/// once altogether. Where both halves announced the same arrival it is the request path's report
/// that stands for it, because that one carries the status the site answered with.
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
    /// Writes the grouping, over the visitors the calling statement is asking about.
    /// </summary>
    /// <param name="visitors">
    /// Which visitors take part, as a condition over <c>identified</c>. Written by a compiler in
    /// this assembly and never by a caller: where it narrows to a single visitor, that visitor's
    /// key travels as a bound value and only the parameter's name appears here.
    /// </param>
    /// <returns>The six expressions, ending in <c>opened</c>.</returns>
    public static string Of(string visitors) => $$"""
        ordered AS
            (
                SELECT
                    *,
                    dateDiff('second', lagInFrame(server_ts, 1, server_ts) OVER visit, server_ts) AS since_previous
                FROM identified
                WHERE {{visitors}}
                WINDOW visit AS (PARTITION BY visitor_key ORDER BY server_ts ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)
            ),
            grouped AS
            (
                SELECT
                    *,
                    sum(toUInt8(since_previous > {idle_seconds:Int64})) OVER (
                        PARTITION BY visitor_key
                        ORDER BY server_ts
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS visit_ordinal
                FROM ordered
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

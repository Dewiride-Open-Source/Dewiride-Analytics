using System.Globalization;
using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Reconciles the two halves of the measurement, so that one visitor asking for one page is
/// counted as one visitor asking for one page.
/// </summary>
/// <remarks>
/// <para>
/// The intended arrangement on a measured site is both halves at once: a tracker in the browser,
/// which sees whether anybody was actually there, and a reporter on the site's own server, which
/// sees the visitors that never run a script at all. Every page a person reads is therefore
/// reported twice, and taken at face value that tells every customer running the product properly
/// that their traffic is twice what it is.
/// </para>
/// <para>
/// Two things have to be true before it can be counted correctly, and they are separate problems.
/// </para>
/// <para>
/// <b>The two halves must agree on who the visitor was.</b> Each half derives its own key from the
/// address the request came from, and those addresses are not reliably the same one: the page and
/// the collector are different hosts, so a visitor whose network offers both kinds of address can
/// reach one over each. Where a reporter named the page it served and one browser sent the name
/// back, that settles it, and the browser's key is the one kept — it was measured from the
/// visitor's own connection rather than asserted on their behalf. Where a cache in between served
/// the same page, name and all, to several browsers, the name settles nothing, and is read as
/// nothing.
/// </para>
/// <para>
/// <b>One page delivered is one page view, however many surfaces watched it happen.</b> Applied by
/// counting each half separately and keeping the larger, which is exact rather than approximate:
/// the two halves genuinely see different things, and the one that saw more pages saw them. A
/// crawler that runs no script is counted entirely from the server's half; a page reached without
/// a fresh request — moving back, or a site that redraws itself rather than reloading — entirely
/// from the browser's; and a page asked for twice is two, because both halves saw it twice. It
/// holds whether or not a site can stamp an identifier, which matters because several of the
/// places a reporter runs cannot.
/// </para>
/// <para>
/// Both statements this product sends against raw activity are built from the fragments below, so
/// which surfaces count as the visitor's own browser is stated once.
/// </para>
/// </remarks>
internal static class ReconciledEvents
{
    /// <summary>Tests whether a report came from the visitor's own browser.</summary>
    public static string FromVisitorBrowser { get; } = $"surface IN ({StoredNames.BrowserSurfaceList})";

    /// <summary>Tests whether a report came from somewhere between the visitor and the site.</summary>
    public static string FromRequestPath { get; } = $"surface NOT IN ({StoredNames.BrowserSurfaceList})";

    /// <summary>The selection the reconciliation ends in, with identity settled.</summary>
    public const string Identified = "identified";

    /// <summary>
    /// Settles who each report was about, given a preceding <c>windowed</c> selection carrying at
    /// least <c>surface</c>, <c>visitor_key</c>, <c>correlation_id</c> and <c>server_ts</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name the reporter gave a page is the browser's <i>answer</i> when exactly one browser
    /// sent it back within <see cref="EchoedDeliveries.AnswerWindow"/> of the reporter's report. A
    /// name two or more browsers sent back in that time was handed out by a cache, and counts for
    /// nobody; a browser sending it back later than that is reading a copy, and is not counted at
    /// all. The same holds of the reporting side: a report another reporter key matched with the
    /// same name in that time cannot be one delivery to one visitor. Each report is read on its own,
    /// against what arrived within the answer window of it, so the same name given again later is
    /// read as a report of its own and changes nothing about the first. A name nobody sent back is a
    /// page whose tracker never ran.
    /// </para>
    /// <para>
    /// What an answer establishes is not merely that one report was a page the browser also saw; it
    /// is that the two keys are one person. So each report the reporter's key made is given to a
    /// browser when, within <see cref="EchoedDeliveries.Reach"/> either side of it, that browser is
    /// the only one to have answered the reporter, and has answered at least half of the pages the
    /// reporter named. That keeps a redirect, a missing page, or a page whose tracker never ran
    /// inside the visit it belongs to, instead of stranding it as a visitor of its own. The half is
    /// what stops a key that named hundreds of pages and was answered once — a crawler whose copy
    /// of a page a cache passed to one reader — being folded into that reader; being the only one
    /// is what stops one address shared by several people being folded into any of them.
    /// </para>
    /// <para>
    /// Whose a report was depends on nothing further from it than the reach and the answer window
    /// together, which is <see cref="EchoedDeliveries.Settled"/>, and the idle timeout is never
    /// shorter than that. Everything that decides a report has therefore arrived before any visit
    /// holding it is judged, and nothing that arrives later can give it to somebody else under a
    /// verdict already reached.
    /// </para>
    /// <para>
    /// A visitor no answer ever settled keeps the key their own half derived. That is the only
    /// answer available for a crawler that ran no script, and the right one: there was no browser
    /// there to disagree.
    /// </para>
    /// </remarks>
    public static string Reconciliation { get; } = Reconciling(null);

    /// <summary>
    /// The same, for a <c>windowed</c> selection that also carries <c>site_id</c> and covers more
    /// than one site.
    /// </summary>
    /// <remarks>
    /// A correlation identifier is minted by the reporting site's own server, so two sites can
    /// perfectly well mint the same one. Settling identity across all of them at once would read
    /// that coincidence as one visitor seen twice and move one site's activity onto another site's
    /// key. Settling it inside each site leaves the coincidence harmless.
    /// </remarks>
    public static string ReconciliationPerSite { get; } = Reconciling("site_id");

    /// <summary>
    /// Daily visitors: each visitor key once, and none for the activity that carries no key.
    /// </summary>
    /// <remarks>
    /// A surface that could not derive a key observed nothing about who was there, and counting all
    /// of that activity together would invent one busy phantom. A key covers one UTC day, so over a
    /// longer stretch this counts somebody once for every day they came. Written once because the
    /// headline totals, the series and the months kept after their reports have to count the same
    /// visitors, and a test holds the version of the kept months' arithmetic to this text.
    /// </remarks>
    public const string Visitors = "toInt64(uniqExactIf(visitor_key, visitor_key != ''))";

    /// <summary>
    /// Pages delivered, for a grouping already narrowed to one visitor and one page.
    /// </summary>
    /// <param name="indent">
    /// Spaces the fragment's own lines are laid out against, matching the depth the calling
    /// statement nests it at. The approved statements beside these compilers are read by people
    /// deciding whether a change to them was intended, and a fragment that keeps one statement's
    /// indentation wherever it is dropped makes the rest of them ragged.
    /// </param>
    /// <returns>The two expressions, ready to place at that depth.</returns>
    /// <remarks>
    /// <para>
    /// A delivery is counted from the reports that announce one, and never from the reports about a
    /// page already being read. A progress or departure report is an account of a page somebody was
    /// already on, so the arrival happened whether or not this half saw it — but the report reaches
    /// the collector under whatever key the network and the day produced, which is not the key that
    /// announced the page if either has changed since. Counting a page named only by readings would
    /// therefore count a second time, under a second visitor, the delivery already counted under the
    /// key that announced it.
    /// </para>
    /// <para>
    /// Activity carrying no visitor key is counted as it arrives. Nothing about it says which
    /// visitor asked for the page, so there is no second sighting to recognise, and folding those
    /// together would discard views rather than duplicates.
    /// </para>
    /// </remarks>
    public static string DeliveredPageViews(int indent)
    {
        var pad = new string(' ', indent);

        return $"""
            greatest(
            {pad}    countIf(kind = 'PageView' AND {FromVisitorBrowser}),
            {pad}    countIf(kind = 'PageView' AND {FromRequestPath})) AS delivered,
            {pad}if(visitor_key = '', countIf(kind = 'PageView'), delivered) AS page_views
            """;
    }

    /// <summary>
    /// How far either side of a server's report a browser's echo is its answer, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored, as are the two below, because the statements above are written
    /// while this type's fields are still being initialised in the order they appear.
    /// </remarks>
    private static string AnswerWindow => Milliseconds(EchoedDeliveries.AnswerWindow);

    /// <summary>How far either side of a report its deciding answers are read from, in milliseconds.</summary>
    private static string Reach => Milliseconds(EchoedDeliveries.Reach);

    /// <summary>
    /// The nearest a name can be before a report and still lie outside its reach, in milliseconds.
    /// </summary>
    /// <remarks>
    /// What lies within the reach is counted as everything up to its far edge less everything
    /// before its near one, so the second frame ends a millisecond short of where the first would
    /// begin. Both frames start where the reporter's activity in the window does, which is what lets
    /// the store add each row once as the frame grows rather than start again for every row it moves
    /// past.
    /// </remarks>
    private static string BeforeReach => Milliseconds(EchoedDeliveries.Reach + TimeSpan.FromMilliseconds(1));

    /// <summary>
    /// Writes the reconciliation, settling who each report was about either across the whole
    /// window or inside one column of it.
    /// </summary>
    /// <param name="within">
    /// The column identity is settled inside, or <see langword="null"/> to settle it across
    /// everything the window holds. Written in this file and never supplied by a caller, on the
    /// same terms as every other identifier these statements are built from.
    /// </param>
    /// <returns>
    /// The common table expressions, laid out to follow a <c>windowed</c> one and ending in
    /// <see cref="Identified"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>stamped</c> reads every report that carries a name, and <c>bundled</c> gathers the reports
    /// of each name some reporter gave. <c>echoed</c> then takes each report a reporter made of a name
    /// and reads what else carried it within the answer window of that report, and nothing further:
    /// which reporter keys gave it, and which browsers sent it back. It is answered by a browser only
    /// when that browser is the only one and the report's own key the only reporter; otherwise —
    /// another reporter key matched it, or a cache handed it to two browsers — it is a name the
    /// reporter gave that nobody in particular answered, which is what it counts as. Each report is
    /// read on its own, so the same name given again an hour later — a cached response reported a
    /// second time, a batch sent twice — is read as what it is then and there, and nothing about it
    /// depends on whether the reading reached back to the first.
    /// </para>
    /// <para>
    /// Gathered by grouping rather than by joining the reports of a name to one another, because a
    /// grouping is what the store can spill to disk once a statement reaches its memory limit, where
    /// the table a join holds has to fit; and because a stream of browser reports repeating a name a
    /// site really gave then costs the stream's length once for each report the site made of that
    /// name, never the square of the stream.
    /// </para>
    /// <para>
    /// <c>timeline</c> lays out the names each reporter gave, answered or not, and — for a reporter
    /// that gave any — the instants of its reports that carry no name. A report that carries one is
    /// decided where its name stands, at the same instant, so it is laid out once. Which reporters
    /// gave a name is read from <c>stamped</c> rather than from the answers, so the names are gathered
    /// once per reading and not a second time to learn who was answered.
    /// <c>tallied</c> numbers each browser's answers to a reporter in order, and <c>weighed</c> reads,
    /// at each instant, how many of the reporter's names lie within the reach, how many were
    /// answered, and the first and last answer among them: the same browser at both ends, with as
    /// many answers between them as there are in the reach, is the only browser that answered, and
    /// that settles the instant.
    /// </para>
    /// <para>
    /// <c>linked</c> keeps only the instants at which the answer changes for a reporter — usually one
    /// or two per key — and <see cref="Identified"/> gives each report the answer of the latest change
    /// at or before it. Every report of the reporter's is an instant laid out above, so the answer
    /// between two changes is the earlier one's. Looked up nearest-earlier like this, what is held
    /// while every report is read is that short list rather than an entry per report, which matters
    /// because a statement that reads the reconciliation more than once holds it once per reading.
    /// </para>
    /// <para>
    /// The reach is counted with frames that all open where the reporter's activity does — a
    /// count up to the far edge less a count before the near one, and the first answer read
    /// backwards from the end — so the store adds each row to a frame once, however busy the key.
    /// A frame that slid would be counted again from its start at every row, which for a crawler
    /// answered once is the square of everything it fetched that day.
    /// </para>
    /// </remarks>
    private static string Reconciling(string? within)
    {
        var also = within is null ? string.Empty : $"{within}, ";
        var joined = within is null ? string.Empty : $"windowed.{within} = linked.{within} AND ";

        return $$"""
            stamped AS
                (
                    SELECT
                        {{also}}correlation_id,
                        visitor_key,
                        {{FromVisitorBrowser}} AS from_browser,
                        toUnixTimestamp64Milli(server_ts) AS at_ms
                    FROM windowed
                    WHERE correlation_id != ''
                      AND visitor_key != ''
                ),
                bundled AS
                (
                    SELECT
                        {{also}}correlation_id AS name,
                        groupArray((visitor_key, at_ms, from_browser)) AS reports
                    FROM stamped
                    GROUP BY {{also}}correlation_id
                    HAVING countIf(NOT from_browser) > 0
                ),
                echoed AS
                (
                    SELECT
                        {{also}}name,
                        delivered.1 AS reported_key,
                        delivered.2 AS delivery_ms,
                        arrayFilter(report -> abs(report.2 - delivery_ms) <= {{AnswerWindow}}, reports) AS around,
                        arrayDistinct(arrayMap(report -> report.1, arrayFilter(report -> NOT report.3, around))) AS reporters,
                        arrayDistinct(arrayMap(report -> report.1, arrayFilter(report -> report.3, around))) AS answerers,
                        if(length(reporters) = 1 AND length(answerers) = 1, answerers[1], '') AS answered_by
                    FROM bundled
                    ARRAY JOIN arrayDistinct(arrayMap(report -> (report.1, report.2), arrayFilter(report -> NOT report.3, reports))) AS delivered
                ),
                timeline AS
                (
                    SELECT
                        {{also}}visitor_key AS reporter,
                        toUnixTimestamp64Milli(server_ts) AS at_ms,
                        '' AS identifier,
                        toUInt8(0) AS is_name,
                        '' AS answerer
                    FROM windowed
                    WHERE {{FromRequestPath}}
                      AND correlation_id = ''
                      AND ({{also}}visitor_key) IN
                      (
                          SELECT {{also}}visitor_key
                          FROM stamped
                          WHERE NOT from_browser
                      )
                    GROUP BY {{also}}reporter, at_ms
                    UNION ALL
                    SELECT
                        {{also}}reported_key AS reporter,
                        delivery_ms AS at_ms,
                        name AS identifier,
                        toUInt8(1) AS is_name,
                        answered_by AS answerer
                    FROM echoed
                ),
                tallied AS
                (
                    SELECT
                        *,
                        -at_ms AS backwards_ms,
                        if(answerer = '', 0, row_number() OVER (
                            PARTITION BY {{also}}reporter, answerer
                            ORDER BY at_ms, identifier)) AS answer_number
                    FROM timeline
                ),
                weighed AS
                (
                    SELECT
                        {{also}}reporter,
                        at_ms,
                        identifier,
                        sum(is_name) OVER ahead - sum(is_name) OVER behind AS names_near,
                        countIf(answerer != '') OVER ahead - countIf(answerer != '') OVER behind AS answered_near,
                        argMaxIf((answerer, answer_number), (at_ms, identifier), answerer != '') OVER ahead AS last_answer,
                        argMinIf((answerer, answer_number), (at_ms, identifier), answerer != '') OVER since AS first_answer,
                        if(answered_near > 0
                               AND first_answer.1 = last_answer.1
                               AND last_answer.2 - first_answer.2 + 1 = answered_near
                               AND 2 * answered_near >= names_near
                               AND last_answer.1 != reporter,
                           last_answer.1,
                           '') AS browser_key
                    FROM tallied
                    WINDOW
                        ahead AS (PARTITION BY {{also}}reporter ORDER BY at_ms RANGE BETWEEN UNBOUNDED PRECEDING AND {{Reach}} FOLLOWING),
                        behind AS (PARTITION BY {{also}}reporter ORDER BY at_ms RANGE BETWEEN UNBOUNDED PRECEDING AND {{BeforeReach}} PRECEDING),
                        since AS (PARTITION BY {{also}}reporter ORDER BY backwards_ms RANGE BETWEEN UNBOUNDED PRECEDING AND {{Reach}} FOLLOWING)
                ),
                linked AS
                (
                    SELECT
                        {{also}}reporter AS reported_key,
                        at_ms AS changed_ms,
                        browser_key
                    FROM
                    (
                        SELECT
                            {{also}}reporter,
                            at_ms,
                            browser_key,
                            lagInFrame(browser_key, 1, '') OVER (
                                PARTITION BY {{also}}reporter
                                ORDER BY at_ms, identifier
                                ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS before
                        FROM weighed
                    )
                    WHERE browser_key != before
                ),
                {{Identified}} AS
                (
                    SELECT
                        windowed.* EXCEPT (visitor_key),
                        if(linked.browser_key != '' AND {{FromRequestPath}}, linked.browser_key, windowed.visitor_key) AS visitor_key
                    FROM windowed
                    ASOF LEFT JOIN linked ON {{joined}}windowed.visitor_key = linked.reported_key
                        AND toUnixTimestamp64Milli(windowed.server_ts) >= linked.changed_ms
                )
            """;
    }

    /// <summary>Writes a stretch of time as the whole milliseconds a statement compares it in.</summary>
    /// <param name="span">The stretch.</param>
    /// <returns>Its length, as an integer literal.</returns>
    private static string Milliseconds(TimeSpan span) =>
        ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}

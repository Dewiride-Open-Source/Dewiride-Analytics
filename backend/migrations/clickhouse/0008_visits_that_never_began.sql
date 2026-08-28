-- ---------------------------------------------------------------------------
-- 0008_visits_that_never_began — removes the stored verdicts on activity that
-- is no longer reconstructed as a visit.
--
-- A tracker reports how a page is going, and reports it being left, from the
-- page itself, and neither report announces an arrival. A visitor key is
-- derived from the network a report came over and the day it arrived, so it
-- changes under a reader who moves between networks and again at midnight — and
-- the tail of a visit therefore arrives, routinely, under a key that announced
-- nothing. Opening a visit for such a tail counts one reader twice and records
-- the second of them as somebody who arrived, read for a quarter of an hour and
-- left.
--
-- A visit now begins where somebody arrived, and every other report belongs to
-- the visit its own page was arrived at in. Two shapes of stored verdict
-- therefore describe something that will never be reconstructed again:
--
--   * a visit holding no arrival at all, and
--   * a visit whose first arrival is later than the instant it is recorded as
--     beginning, because a visit is named by its beginning and this one is now
--     named differently — so it gains a row rather than having one replaced.
--
-- Neither can be superseded. Reads take the highest ruleset present for each
-- visit, and nothing will ever judge a visit that is not reconstructed, so a row
-- of either shape is shown for ever and counted in every number it appears in.
--
-- Both are recognised by asking the activity itself. A visit is named by its
-- visitor and the instant it began, so the visitor is read back out of the name
-- and the reports it made inside the visit's own span are counted. One condition
-- catches both shapes: no arrival at the instant the visit is recorded as
-- beginning. The instants compare exactly, because the beginning is one of the
-- visit's own reports and both are stamped by the same clock.
--
-- Two limits keep it from removing anything real.
--
-- Only visits every surface of which is the visitor's own browser. Nothing in
-- the request path reports a page being read, so a visit any of those watched
-- announced an arrival — and those are the keys the reconciliation rewrites, so
-- for such a visit the name is not the key its reports carry.
--
-- Only visits whose activity is still here. Raw activity is dropped at twelve
-- months, as verdicts are, and a visit whose reports have already gone cannot be
-- judged either way: the join produces nothing for it and it is left alone.
--
-- The cost is one mutation over a single join between the two tables, run once,
-- and it is bounded by the retention window rather than by how long the
-- installation has been running.
-- ---------------------------------------------------------------------------

DELETE FROM session_classifications
WHERE (site_id, session_key) IN
(
    SELECT
        visits.visit_site,
        visits.visit_key
    FROM events AS reports
    INNER JOIN
    (
        SELECT
            site_id AS visit_site,
            session_key AS visit_key,
            splitByChar(':', session_key)[1] AS visitor,
            min(started_at) AS started_at,
            max(ended_at) AS ended_at
        FROM session_classifications
        GROUP BY site_id, session_key
        HAVING arrayAll(
            surface -> toString(surface) IN ('BrowserTracker', 'NoScriptPixel'),
            groupUniqArrayArray(surfaces))
    ) AS visits
        ON reports.site_id = visits.visit_site
       AND reports.visitor_key = visits.visitor
    WHERE reports.server_ts >= visits.started_at
      AND reports.server_ts <= visits.ended_at
    GROUP BY visits.visit_site, visits.visit_key
    HAVING countIf(reports.kind = 'PageView' AND reports.server_ts = visits.started_at) = 0
);

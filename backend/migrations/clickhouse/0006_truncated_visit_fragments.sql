-- ---------------------------------------------------------------------------
-- 0006_truncated_visit_fragments — removes the second, shorter copy of a visit
-- that the engine could previously write for a visit it had already judged.
--
-- The engine works forward through a site in windows and keeps a bookmark, and
-- that bookmark stops at the earliest visit still in progress rather than at the
-- end of the window it has just read. A later window therefore routinely opens
-- part-way through a visit that has already been judged. Reading activity from
-- the window's own start made the remainder of that visit look like a whole one
-- beginning at whichever report fell first inside the window, so it was judged
-- again under a second identity — usually with a page or none left in it, and so
-- usually as "not enough to say".
--
-- The statement that reconstructs visits now reaches an idle timeout back before
-- the window it was asked about, which gives such a remainder its true beginning
-- and leaves it out. That stops new ones being written. The ones already stored
-- have to be removed, because their identity does not name a visit that ever
-- happened, so nothing will ever supersede them and every count they appear in is
-- one too many.
--
-- A remainder is recognisable without knowing anything about the windows that
-- produced it: it is a visit by a visitor who already had a visit that started
-- earlier and ended no sooner. Two genuine visits by one visitor cannot overlap —
-- they are separated by a silence longer than the idle timeout, which is what
-- makes them two — so an overlap of that shape is always this defect.
--
-- Rows are grouped by identity first because the table replaces on merge rather
-- than on write, so one visit can be present as several rows until its parts are
-- merged, and each of them has to be judged as the one visit it belongs to.
-- ---------------------------------------------------------------------------

DELETE FROM session_classifications
WHERE (site_id, session_key) IN
(
    SELECT
        site_id,
        session_key
    FROM
    (
        SELECT
            site_id,
            session_key,
            ended_at,
            max(ended_at) OVER (
                PARTITION BY site_id, visitor
                ORDER BY started_at
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS covered_through
        FROM
        (
            SELECT
                site_id,
                session_key,
                splitByChar(':', session_key)[1] AS visitor,
                min(started_at) AS started_at,
                max(ended_at) AS ended_at
            FROM session_classifications
            GROUP BY site_id, session_key
        )
    )
    WHERE ended_at <= covered_through
);

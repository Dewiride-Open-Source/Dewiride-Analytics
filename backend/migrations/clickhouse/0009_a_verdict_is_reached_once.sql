-- ---------------------------------------------------------------------------
-- 0009_a_verdict_is_reached_once — drops the flag for a verdict reached early.
--
-- 0003 added is_provisional against a second way of judging that was going to
-- exist: a verdict reached the moment activity arrived, shown as not yet final,
-- and replaced once the visit was over. That way of judging was never built,
-- and the engine that was built rules it out. A visit is read only once it has
-- been quiet long enough to be over, and a verdict is reached only for a visit
-- that is — so no value other than false has ever been written here, on any
-- installation, and none could be.
--
-- A column meaning "reached before the visit finished" therefore describes a
-- state the design forbids. It was carried the whole way to the screen: stored,
-- selected, read, put into the answer the dashboard receives, and parsed there.
-- Nothing ever showed it, because there was never anything to show.
--
-- What replaces it is nothing. A verdict is the engine's answer on the evidence
-- there was, and where more of it arrives later the answer is reached again
-- when history is re-judged. Marking every recent visit as possibly-not-final
-- would put a caveat on the freshest thing on the screen to describe something
-- that moves about one visit in a hundred and only ever towards more confidence.
--
-- Dropping a column from a MergeTree deletes whole files rather than rewriting
-- parts, so it completes almost instantly and is not a mutation. It is also not
-- reversible: rolling a release back leaves the column gone. That costs nothing
-- here, because everything in it was false.
-- ---------------------------------------------------------------------------

ALTER TABLE session_classifications
  DROP COLUMN IF EXISTS is_provisional;

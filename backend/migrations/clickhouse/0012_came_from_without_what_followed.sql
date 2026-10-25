-- ---------------------------------------------------------------------------
-- 0012_came_from_without_what_followed — cuts what followed the path of every
-- referrer already stored, at its first question mark or hash.
--
-- A referrer used to be kept as it was reported. What follows a page's path is
-- where a search box puts what somebody searched for: on another site, or on
-- this one, where going from the results to a page carried the search onto that
-- page's reading. A page that moves itself had the address it left passed on
-- whole, hash included, and some search boxes keep the search after the hash.
-- Nothing here ever read either part — a referrer is asked only for the site it
-- names and the page on it — and the collector now cuts both on the way in.
-- This cuts them from what was kept before, where the collector cuts them.
--
-- An update of a column outside the sorting key, so only that column of the
-- parts holding a match is rewritten. It runs in the background: the engine
-- starts without waiting for it, and the store finishes it on its own, across
-- restarts. Running it again finds nothing to change.
-- ---------------------------------------------------------------------------

ALTER TABLE events
  UPDATE referrer = substring(referrer, 1, multiSearchFirstPosition(referrer, ['?', '#']) - 1)
  WHERE multiSearchFirstPosition(referrer, ['?', '#']) > 0;

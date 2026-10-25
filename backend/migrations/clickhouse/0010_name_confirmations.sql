-- ---------------------------------------------------------------------------
-- 0010_name_confirmations — whose crawler a visit was, where that was settled
-- from the name its address answers to.
--
-- A company that publishes a list of its crawlers' addresses is recognised as
-- each request arrives, and the answer is written onto the activity itself
-- (0007), where it outlives the address it was worked out from. A company that
-- documents only a domain its machines answer to is settled when the visit is
-- judged, by asking a name server about the address. The collector is told,
-- and marks the next reports from that machine as they arrive — but only for a
-- few hours, and never the reports of the visit that prompted the question,
-- which had already arrived. 0001 clears the address 72 hours after it was
-- collected, so that visit, judged again after that — which is what improving
-- the rules does to the whole of a site's history — would find nothing left to
-- ask about and fall back to the claim it had once been shown to be more than.
--
-- This table keeps the answer instead: the company, beside the visit it was
-- established for, written by the engine before the verdict that rests on it.
-- It is the one part of what the engine reasons about that is stored rather
-- than reconstructed, and only because the evidence it was worked out from does
-- not survive. It holds no address and no user agent — the conclusion, never
-- what the conclusion was drawn from.
--
-- Keyed by the visit, never by the visitor. On a rented network a visitor key
-- can stand for a whole network's day, and an answer spread to every visit
-- under that key would vouch for activity whose own address was never checked.
--
-- ReplacingMergeTree on settled_at, so writing an answer twice leaves one row
-- and a later answer for the same visit replaces an earlier one; readers take
-- the latest themselves rather than wait for a merge. Sorted by the site and
-- then by when the visit began, because every reading asks for one site's
-- visits over a stretch of time. Kept as long as the verdicts it stands behind,
-- and removed with the site.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS name_confirmations
(
  site_id      UUID,
  session_key  String,
  started_at   DateTime64(3, 'UTC'),
  operator     LowCardinality(String),
  settled_at   DateTime64(3, 'UTC')
)
ENGINE = ReplacingMergeTree(settled_at)
PARTITION BY toYYYYMM(started_at)
ORDER BY (site_id, started_at, session_key)
TTL toDateTime(started_at) + INTERVAL 12 MONTH
SETTINGS index_granularity = 8192, merge_with_ttl_timeout = 3600;

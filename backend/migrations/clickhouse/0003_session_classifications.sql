-- ---------------------------------------------------------------------------
-- 0003_session_classifications — one row per session judged, per ruleset.
--
-- Sessions themselves are not stored. They are reconstructed from events by the
-- statement in SessionSqlCompiler, which makes the reconstruction a property of
-- the code rather than of a table somebody has to keep in step with it: change
-- the idle timeout and every session is re-derived, with no migration and no
-- half-rebuilt table in between.
--
-- The verdict is stored, because it cannot be re-derived. It is the output of a
-- particular ruleset, and the ruleset changes.
--
-- ruleset_major and ruleset_minor are part of the sorting key, so improving the
-- rules adds rows beside the old ones instead of overwriting them. That is what
-- makes it possible to say which ruleset produced a number, to re-judge a window
-- and compare, and to tell a rebuild apart from a regression. Reads take the
-- highest ruleset present for each session.
--
-- ReplacingMergeTree on classified_at makes re-judging the same session under the
-- same ruleset idempotent, which is what lets two instances of the engine work the
-- same site without coordinating: they do the work twice and store it once.
--
-- The user agent is deliberately absent. It is written by the visitor, and the
-- only part of it worth showing — the crawler name, where it matched an entry in
-- the catalogue this product maintains — already travels in a signal parameter.
-- Storing the raw string here would put attacker-written text on a screen for no
-- gain over reading it from events on the rare occasion somebody needs it.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS session_classifications
(
  site_id            UUID,

  -- The visitor key and the session's first instant, which is what makes it
  -- derivable from the events rather than allocated. Re-running the engine over
  -- the same events produces the same key.
  session_key        String,

  ruleset_major      UInt16,
  ruleset_minor      UInt16,

  started_at         DateTime64(3, 'UTC'),
  ended_at           DateTime64(3, 'UTC'),

  -- Exact, and not the length of the request array below: that array is capped,
  -- because one sweep can ask for tens of thousands of pages.
  page_count         UInt32,

  -- Which capture surfaces saw the session. Load-bearing rather than diagnostic:
  -- what a surface can observe decides how the absence of a reading is read.
  -- Held as text rather than as a second copy of the events enumeration, so that
  -- adding a capture surface is one migration and not two.
  surfaces           Array(LowCardinality(String)),

  category           Enum8('InsufficientEvidence' = 0, 'LikelyHuman' = 1, 'KnownSearchCrawler' = 2, 'KnownAiCrawler' = 3, 'SuspectedAiCrawler' = 4, 'KnownAutomatedService' = 5, 'BrowserAutomation' = 6, 'GenericWebCrawler' = 7, 'ContentScraper' = 8, 'MonitoringOrSynthetic' = 9, 'SecurityScanner' = 10, 'SuspiciousAutomation' = 11, 'LikelyAnalyticsSpam' = 12, 'Unknown' = 13),
  strength           Enum8('None' = 0, 'Weak' = 1, 'Moderate' = 2, 'Strong' = 3, 'Verified' = 4),

  -- Whether the verdict was reached before the session closed. Nothing writes a
  -- provisional verdict yet; the live view will, and it renders them as not-final.
  is_provisional     Bool,

  -- The evidence, as parallel arrays in one order: the reason the verdict was
  -- reached, and the reasons against it. Codes rather than sentences, because the
  -- sentence is produced from the message catalogue in the reader's language and a
  -- stored English string could not be translated later.
  signal_codes       Array(LowCardinality(String)),
  signal_directions  Array(Enum8('TowardHuman' = -1, 'Neutral' = 0, 'TowardAutomation' = 1)),
  signal_weights     Array(UInt8),

  -- False for the evidence that pointed the other way. Contradicting evidence is
  -- stored and shown, never discarded — a verdict that only carries what agrees
  -- with it is an argument rather than an assessment.
  signal_supporting  Array(Bool),

  -- Values the sentence templates substitute, for example the number of pages.
  -- Never anything the visitor wrote.
  signal_parameters  Array(Map(LowCardinality(String), String)),

  classified_at      DateTime64(3, 'UTC'),

  -- Every screen asks over a window of time while the sorting key leads with the
  -- session, so the parts that cannot hold the window are skipped on this instead
  -- of read and discarded.
  INDEX idx_started_at started_at TYPE minmax GRANULARITY 4
)
ENGINE = ReplacingMergeTree(classified_at)
PARTITION BY toYYYYMM(started_at)
ORDER BY (site_id, session_key, ruleset_major, ruleset_minor)
TTL toDateTime(started_at) + INTERVAL 12 MONTH
SETTINGS index_granularity = 8192, merge_with_ttl_timeout = 3600;

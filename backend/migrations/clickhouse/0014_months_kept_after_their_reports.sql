-- ---------------------------------------------------------------------------
-- 0014_months_kept_after_their_reports — each website's totals for each
-- calendar month, kept until the website is removed.
--
-- Every report, verdict and name answer is deleted twelve months after it
-- arrived (0001, 0003, 0010), and nothing here changes that. What outlives
-- them is one row per website per finished month: the month's totals, counted
-- by the dashboard's own arithmetic while every report of the month was still
-- held. This widens what the product keeps — these rows have no TTL and go
-- only when the website does — and it is the whole of the widening: a month
-- holds how much was read, by how many daily visitors, on which day, weekday
-- and hour, how many visits of each kind there were and the pages they read,
-- and, on the month it falls in, the website's first recorded day. Nothing
-- about any one visit: no address, no page address and no user agent.
--
-- The same figures are kept a second time for the visits judged to be people,
-- on the same row, so the two can never describe different counts.
--
-- A row says how it was counted: in which zone, by which rules, how far
-- judging had reached and which version of the arithmetic ran. That is what
-- lets the engine count a month again while its reports remain — after a
-- change of zone, a newer ruleset or a correction to the arithmetic — and
-- leave it alone once they are gone.
--
-- ReplacingMergeTree on counted_at, the instant the count was taken rather
-- than the instant it was written, so the newest count wins whichever order
-- two engines' writes arrive in; readers take the newest themselves rather
-- than wait for a merge. Partitioned by year, which every row of a website and
-- month falls inside once, so replacing a row never spans two partitions.
--
-- Touches no existing row, and is safe to run again. There is no backfill
-- here: counting needs each website's zone, which only the control plane
-- holds, so the engine counts every month still held itself, from its first
-- run on, the oldest first and a few months per website per run.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS consolidated_months
(
  site_id                       UUID,

  -- The first of the month on the website's calendar, and the zone that
  -- calendar was read in when the month was counted.
  month                         Date,
  time_zone                     LowCardinality(String),

  -- The stretch counted: from the month's first instant to its last.
  begins_at                     DateTime64(3, 'UTC'),
  ends_at                       DateTime64(3, 'UTC'),

  -- The website's first recorded day, on the row of the month it falls in.
  first_day                     Nullable(Date),
  days_measured                 UInt8,

  page_views                    UInt64,
  visitors                      UInt64,
  best_day                      Nullable(Date),
  best_day_page_views           UInt64,
  page_views_by_weekday         Array(UInt64),
  page_views_by_hour            Array(UInt64),

  people_page_views             UInt64,
  people_visitors               UInt64,
  people_best_day               Nullable(Date),
  people_best_day_page_views    UInt64,
  people_page_views_by_weekday  Array(UInt64),
  people_page_views_by_hour     Array(UInt64),

  -- How many visits of each kind began in the month, by the newest verdict
  -- each had when it was counted, spelled as 0003 spells a category.
  verdict_categories            Array(LowCardinality(String)),
  verdict_visits                Array(UInt64),
  verdict_page_views            Array(UInt64),

  -- How it was counted.
  ruleset_major                 UInt16,
  ruleset_minor                 UInt16,
  ruleset_judged_to             DateTime64(3, 'UTC'),
  counting_rules                UInt16,
  counted_to                    DateTime64(3, 'UTC'),
  judged_to                     DateTime64(3, 'UTC'),
  counted_at                    DateTime64(3, 'UTC')
)
ENGINE = ReplacingMergeTree(counted_at)
PARTITION BY toYear(month)
ORDER BY (site_id, month)
SETTINGS index_granularity = 8192;

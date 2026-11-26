-- ---------------------------------------------------------------------------
-- 0017_pages_read_month_by_month — how many times each of a website's busiest
-- pages was viewed in each calendar month, kept until the website is removed.
--
-- Every report is deleted twelve months after it arrived (0001), and nothing
-- here changes that. What outlives them is one row per website per finished
-- month: the paths of the month's busiest pages, each with the pages
-- delivered there that month, counted by the arithmetic of the dashboard's
-- list of pages while every report of the month was still held.
--
-- The bound is what keeps a row small however many addresses were asked
-- for. A path is kept only when at least two different visitor keys were
-- delivered a page there that month — a page only one visitor opened, often
-- their own search, order or account page, is never kept — and when it is no
-- longer than 2,048 characters. Of those, a month keeps the 5,000 paths most
-- viewed by everybody and the 5,000 most viewed by the visits judged to be
-- people, ties broken by the path's bytes. Every other path is kept only as
-- three totals: how many there were, and the pages delivered at them to
-- everybody and to people. A visitor key is rebuilt each day, so one person
-- who opened a page on two days counts as two visitors here.
--
-- This widens what the product keeps — these rows have no TTL and go only
-- when the website does — and it is the whole of the widening: the month,
-- the paths, two counts beside each and the three totals. A path is kept
-- as the collector stored it, with nothing after it: no query string, and
-- nothing about any one visit — no address, no user agent, no visitor key.
--
-- A row says how it was counted, as a month's totals say it (0014). That
-- is what lets the engine count a month's pages again while its reports
-- remain — after a change of zone, a newer ruleset or a correction to the
-- arithmetic — and leave it alone once they are gone. The decision and its
-- measured cost are ADR 0080.
--
-- ReplacingMergeTree on counted_at, partitioned by year and sorted by website
-- and month, for the reasons 0014 gives; readers take the newest row of each
-- month themselves rather than wait for a merge.
--
-- Touches no existing row, and is safe to run again. There is no backfill
-- here: the engine counts the pages of every month still held itself, from
-- its first run on, the oldest first and a few months per website per run.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS consolidated_page_months
(
  site_id                     UUID,

  -- The first of the month on the website's calendar, and the zone that
  -- calendar was read in when the month was counted.
  month                       Date,
  time_zone                   LowCardinality(String),

  -- The stretch counted: from the month's first instant to its last.
  begins_at                   DateTime64(3, 'UTC'),
  ends_at                     DateTime64(3, 'UTC'),

  -- The paths kept, in ordinal order, each with the pages delivered there
  -- to everybody and to the visits judged to be people, nought where none
  -- went to a person.
  paths                       Array(String),
  page_views                  Array(UInt64),
  people_page_views           Array(UInt64),

  -- Every other path a page was delivered at in the month: how many there
  -- were, and the pages delivered at them to everybody and to people.
  left_out_paths              UInt64,
  left_out_page_views         UInt64,
  left_out_people_page_views  UInt64,

  -- How it was counted.
  ruleset_major               UInt16,
  ruleset_minor               UInt16,
  ruleset_judged_to           DateTime64(3, 'UTC'),
  counting_rules              UInt16,
  counted_to                  DateTime64(3, 'UTC'),
  judged_to                   DateTime64(3, 'UTC'),
  counted_at                  DateTime64(3, 'UTC')
)
ENGINE = ReplacingMergeTree(counted_at)
PARTITION BY toYear(month)
ORDER BY (site_id, month)
SETTINGS index_granularity = 8192;

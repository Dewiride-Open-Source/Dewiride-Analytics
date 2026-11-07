-- ---------------------------------------------------------------------------
-- 0015_posts_read_month_by_month — how many times each of a website's posts
-- was read in each calendar month, kept until the website is removed.
--
-- Every report is deleted twelve months after it arrived (0001), and nothing
-- here changes that. What outlives them is one row per website per finished
-- month: the paths of the website's posts, as its sitemap lists them, each
-- with the pages delivered there that month, counted by the arithmetic of the
-- dashboard's list of pages while every report of the month was still held.
-- This widens what the product keeps — these rows have no TTL and go only
-- when the website does — and it is the whole of the widening: the month,
-- the website's own post paths, and two counts beside each, for everybody
-- and for the visits judged to be people. Nothing about any one visit: no
-- address, no user agent, no visitor key, and no path but the website's own
-- posts, and of those only the ones somebody read that month.
--
-- A row says how it was counted, as a month's totals say it (0014), and
-- which list of posts it was counted for, as a fingerprint of their paths.
-- That is what lets the engine count a month's posts again while its reports
-- remain — after a change of zone, a newer ruleset, a correction to the
-- arithmetic or a post found later — and leave it alone once they are gone.
--
-- ReplacingMergeTree on counted_at, partitioned by year and sorted by website
-- and month, for the reasons 0014 gives; readers take the newest row of each
-- month themselves rather than wait for a merge.
--
-- Touches no existing row, and is safe to run again. There is no backfill
-- here: the engine counts the posts of every month still held itself, from
-- its first run on, the oldest first and a few months per website per run.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS consolidated_post_months
(
  site_id            UUID,

  -- The first of the month on the website's calendar, and the zone that
  -- calendar was read in when the month was counted.
  month              Date,
  time_zone          LowCardinality(String),

  -- The stretch counted: from the month's first instant to its last.
  begins_at          DateTime64(3, 'UTC'),
  ends_at            DateTime64(3, 'UTC'),

  -- The paths a page was delivered at, in ordinal order, each with the
  -- pages delivered there to everybody and to the visits judged to be
  -- people, nought where none went to a person.
  paths              Array(String),
  page_views         Array(UInt64),
  people_page_views  Array(UInt64),

  -- Which list of posts was counted.
  posts_fingerprint  String,

  -- How it was counted.
  ruleset_major      UInt16,
  ruleset_minor      UInt16,
  ruleset_judged_to  DateTime64(3, 'UTC'),
  counting_rules     UInt16,
  counted_to         DateTime64(3, 'UTC'),
  judged_to          DateTime64(3, 'UTC'),
  counted_at         DateTime64(3, 'UTC')
)
ENGINE = ReplacingMergeTree(counted_at)
PARTITION BY toYear(month)
ORDER BY (site_id, month)
SETTINGS index_granularity = 8192;

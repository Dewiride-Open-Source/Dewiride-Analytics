-- ---------------------------------------------------------------------------
-- 0016_the_best_hour_of_each_month — the clock hour each kept month had the
-- most pages delivered in, beside the day it had the most.
--
-- A kept month (0014) already holds its busiest day and how its pages fell
-- across the hours of the day. These columns hold its busiest hour on the
-- clock: the day that hour fell on and which hour of that day, both on the
-- website's clock as the month was counted, and the pages delivered in it —
-- once for everybody and once, on the same row and from the same count, for
-- the visits judged to be people. The earliest of a tie, as for the best day;
-- the hour the clocks go back through is two hours, each with its own pages.
--
-- This widens what a website's months keep for as long as the website exists
-- by that one hour a month, and it is the whole of the widening: no visit, no
-- address, no page address and no user agent. Recorded in
-- docs/adr/0079-a-month-keeps-its-best-hour-beside-its-best-day.md.
--
-- Rows kept before this change read as no day, hour nought and no pages,
-- which names no hour. An engine that keeps these columns counts by version 3
-- of its arithmetic, so every month kept under an earlier version is counted
-- again while its reports remain, and gains its hour — the oldest first and a
-- few months per website per run. A month whose reports have gone keeps what
-- it was counted with, and never gains one.
--
-- Adding a column to a MergeTree is a metadata change. No part is rewritten
-- and no data is read, so this is safe to run against a self-hoster's
-- existing rows however many of them there are, and safe to run again.
-- ---------------------------------------------------------------------------

ALTER TABLE consolidated_months
  ADD COLUMN IF NOT EXISTS best_hour_day Nullable(Date) AFTER best_day_page_views,
  ADD COLUMN IF NOT EXISTS best_hour UInt8 AFTER best_hour_day,
  ADD COLUMN IF NOT EXISTS best_hour_page_views UInt64 AFTER best_hour,
  ADD COLUMN IF NOT EXISTS people_best_hour_day Nullable(Date) AFTER people_best_day_page_views,
  ADD COLUMN IF NOT EXISTS people_best_hour UInt8 AFTER people_best_hour_day,
  ADD COLUMN IF NOT EXISTS people_best_hour_page_views UInt64 AFTER people_best_hour;

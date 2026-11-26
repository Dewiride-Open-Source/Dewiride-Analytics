# 0079 — A month keeps its best hour beside its best day

- **Status**: accepted
- **Date**: 2026-11-26
- **Applies to**: both editions, and the whole of it is in the free product. Adds three columns for each
  population to `consolidated_months` (ClickHouse migration 0016), raises `CountingRules.Current` to 3
  with every counting statement unchanged, and adds one tile to Insights; **widens the retention
  envelope** by one hour a month, stated below. Collects nothing new: the hour kept is the busiest bucket
  of the hourly cut a month is already counted in. Nothing in `integrations/` or in the tracker changes,
  because nothing about what is captured changes. In `ee/` only the hosted service's privacy copy, the
  date its legal documents were last changed and the release runbook change.
- **Extends**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose kept months each
  gain their busiest hour, counted, counted again and removed on its terms.
- **Revises**: [0066](0066-insights-are-read-from-the-months-that-were-kept.md), in two sentences. "The
  best day takes a row of its own" stops holding: it shares its row with the best hour ever. And "a
  month's best day, and on the first month the day the website was first measured, are the only days a
  kept month names" gains a third: the day its busiest hour fell on.

## Context

The owner asked on 2026-11-25 for one more figure on Insights: the most page views a website was ever
delivered in a single clock hour, with that hour's day and time — views, not visitors — "saved once, and
replaced when it is beaten".

Nothing kept could answer it. A kept month holds its best day and twenty-four figures for the hours of
the day (0066), and those fold every day of the month into one: they say when in the day a website is
read, not which hour of which day was its busiest. The hours themselves are in the reports, which go
after twelve months (0064), so a record "ever" needs each month to keep its own busiest hour while its
reports are still held, exactly as it keeps its busiest day.

## Decision

**Each month keeps its best hour.** `consolidated_months` gains `best_hour_day`, `best_hour` and
`best_hour_page_views`, and the same three prefixed `people_` for the visits judged to be people: the
day the month's busiest clock hour fell on, which hour of that day on the website's clock, and the
pages delivered in it. It is the tallest bar of the month's hourly series. The hourly cut `Month_Count`
already returns — one row for each hour on the clock, carrying its local day and hour — was used only to
fold into the hours of the day; the engine now also keeps the busiest of those rows. No statement
changes. Of two hours as busy the earliest is kept, by the rule the best day is kept by, and one class
(`Peak<TWhen>`) decides both, so the two cannot drift apart. Nought never names an hour.

**The best hour ever is worked out, not recorded.** As with the best day (0066): a record that can only
rise would go on naming an hour the rules no longer support once a month still held is counted again — a
visit judged anew moves pages from one population to the other, and a correction removes some. So each
month's hour is saved once, when the month is kept, and replaced only when that month is counted again,
and the record is worked out on every reading from those: the busiest of the kept months' best hours and
the hours of the months answered from their reports, and of two as busy, the earliest. It can fall, and
when it does that is the correct answer.

**Months still held gain their hour, and frozen ones never do.** Raising `CountingRules.Current` from 2
to 3 counts again every month whose reports are still held, at most four months per website per run,
the oldest first (0064). Every statement is byte-identical, so version 3 shares version 2's fingerprint:
the version is raised because a month comes to keep a figure its older rows lack, not because a figure
is counted differently. The months of posts keep their own version (0069) and are not counted again. A
month whose reports have gone stays as it was kept, without an hour.

**A record says where it can begin.** Where a kept month holds pages but no hour, no hour before the
month after it can be compared with the ones after, so the answer names that month (`bestHourFrom`),
offers only hours from it on — a month older still that has an hour is not offered either — and the tile
reads "Best hour since" that month. While an upgraded installation counts its months again this lasts one
run for every four months it holds, a quarter of an hour each; it stays only where a month froze before it
was counted again, which is an installation that kept months from late 2025 and upgrades late, or one
with counting switched off.

**An hour is an hour on the website's clock.** The day and the hour are the ones the website's clock
showed as the month was counted, so a website in Kolkata or Kathmandu names an hour beginning at its own
top of the hour. Where the clocks go back, the two hours they show alike are two hours, each with its own
figure, never added together — unlike the hours of the day, which add them (0066). Where they go
forward, the hour skipped has no figure. An hour read from the reports carries the limit 0063 states for
the hourly picture on a day Lord Howe Island moves its clocks by half an hour. A month counted again after
a change of zone moves with it; a frozen month keeps the hour it was counted on.

**The hour running now can hold the record.** Its figure grows while it runs, and the tile says no "so
far", as Best day ever beside it does not.

**People only.** The answer about people names their best hour, counted by the same statement and kept
beside everybody's, and a month whose people were never settled adds nothing to it, as for the best day.

**The answer.** `GET /api/sites/{siteId}/insights` gains `bestHour` — `{"day": "2026-09-02", "hour": 20,
"pageViews": 123}`, or null where no hour has any — and `bestHourFrom` — `"2026-01"`, or null where every
month offers its hour. The day is a day on the website's calendar and the hour counts from its midnight.

**The screen.** "Most popular times" lays its four tiles out two by two from 768 pixels — Best day ever
beside Best hour ever, then the busiest day of the week beside the busiest hour of the day — and one above
the other on a phone, in that order. The best hour writes its day as Best day ever writes one, and its
hour as the busiest hour of the day writes one, on a line of its own that never breaks, with its page
views beneath. One component draws both dated records: the figure, the mark for nothing read, and the
stand-ins while the answer is on its way. The tile that named the busiest hour is called "Busiest hour of
the day", so the card's two hours are never read as one.

## The retention envelope, changed

0064's envelope stands, and its line for each website's totals for each finished month gains, beside the
busiest day and that day's pages, the busiest hour — the day it fell on and which hour of it on the
website's clock — and that hour's pages, for everybody and for the visits judged to be people: until the
website is removed, even after it stops being measured, or until its account's records are deleted thirty
days after the account is closed.

The hour holds no address, no visitor key, no page address and nothing about any one visit. What it adds
is precision. For a month with one visitor, the best day and the hours of the day already say which hour
that visitor read in; for a busier month, which hour of which day drew the most reading was not kept
before, and is now.

## What it does not do

- **Give a frozen month an hour.** Its reports are gone; the record is said from the month after it.
- **Name every hour tied for the most.** The earliest is named, as the earliest of two best days is.
- **Count visitors by the hour.** Views only, as the owner chose.
- **Say "so far"** of an hour still running.
- **Move a frozen month to a new zone.** As 0064 says of a month's totals.

## Consequences

- Every month still held is counted once more after the release, four a website a run, every fifteen
  minutes. On 2026-11-26 the hosted service held three finished months a website, so its first run counts
  them all.
- Reading the record reads three more columns of the kept months, for the population asked about. No
  statement is added.
- The dashboard reads `bestHour` from every answer, so it goes out with the engine that writes it, or after
  it. An engine that predates this reads none of the columns and leaves them in place, and the months it
  counts while it runs are kept without an hour; the newer engine counts them again while their reports are
  held. The release carries a migration, so it takes a backup first.
- The README names the best hour ever among what Insights shows; the hosted service's privacy copy names
  the busiest hour among what each month keeps, and the date its legal documents were last changed moves
  with it.

## Tests that make this true

- **One rule for the busiest day and hour**: `PeakTests`, every case, among them
  `The_Earliest_Of_Two_Equally_Busy_Hours_Is_The_Busiest_Whatever_Order_They_Come_In` and
  `The_Same_Hour_Offered_Twice_Is_The_Busier_Of_The_Two_And_Never_Their_Sum`.
- **What a month keeps**, against the store: `MonthConsolidationTests`
  `A_Kept_Months_Busiest_Day_And_Hour_Weekdays_And_Hours_Are_Its_Daily_And_Hourly_Series`, whose hour is
  the tallest bar of the month's hourly series,
  `A_Kept_Months_Busiest_Day_And_Hour_For_People_Are_Counted_From_The_People_Alone`,
  `A_Kept_Months_Busiest_Day_And_Hour_Are_The_Earliest_Of_Two_As_Busy`, and
  `A_Kept_Month_Begins_Where_The_Pictures_Month_Begins` on Kolkata's, Kathmandu's and London's clocks. That
  the statements are unchanged and version 3 shares version 2's fingerprint:
  `ConsolidationSqlCompilerTests` `The_Version_Of_The_Arithmetic_Moves_With_The_Statements_That_Count`, with
  the approved `Month_Count`, `Month_Count_Of_People`, `Month_Verdicts` and `Month_Calendar` files as they
  were.
- **Months still held gain their hour**: `A_Month_Kept_Without_Its_Best_Hour_Is_Counted_Again_While_Its_Reports_Remain`.
- **Reading what was kept**: the approved `AnalyticsSqlCompilerTests.Kept_Months.verified.txt` and
  `AnalyticsSqlCompilerTests.Kept_Months_Of_People.verified.txt`, and
  `Kept_Months_Of_People_Read_The_Columns_Kept_For_People`.
- **The record, worked out**: `KeptInsightsTests`
  `The_Best_Hour_Ever_Is_The_Busiest_Kept_Hour_And_The_Earliest_Of_A_Tie`,
  `A_Record_Can_Fall_When_Its_Month_Is_Counted_Again`,
  `A_Day_Or_An_Hour_From_The_Reports_Takes_The_Record_Only_By_Beating_It`, which includes the hour running
  now, `A_Day_Or_An_Hour_Of_A_Kept_Month_Is_Not_Counted_Again_From_The_Reports`,
  `A_Month_Answered_From_Its_Reports_Is_Folded_On_The_Websites_Clock`,
  `Two_Hours_The_Clocks_Show_Alike_Are_One_Hour_Of_The_Day_And_Two_Hours_On_The_Clock`,
  `A_Month_Whose_People_Were_Never_Settled_Adds_Nothing_To_An_Answer_About_People`,
  `Nothing_Read_Has_No_Best_Day_And_No_Busiest_Day_Or_Hour` and `A_Record_With_Nothing_In_It_Says_Nothing`.
- **Where a record can begin**: `A_Month_Kept_With_Pages_And_No_Hour_Names_The_Best_Hour_From_The_Month_After_It`,
  which offers neither an older kept hour nor an older month's hours from its reports, and
  `A_Month_Kept_With_Nothing_Read_Leaves_The_Best_Hour_Whole`; against the stores, `InsightsReadTests`
  `A_Month_Kept_Without_A_Best_Hour_Names_The_Month_The_Best_Hour_Is_Known_From`.
- **The answer**: `InsightsReadTests` `Insights_Read_The_Kept_Months`,
  `Insights_Say_The_Same_Whether_A_Month_Was_Kept_Or_Not`, `Insights_Of_People_Count_The_Reader_Alone`,
  `A_Month_That_Was_Never_Counted_Has_No_Figure_And_Is_Named` and `Insights_Before_Anything_Was_Measured_Say_So`;
  on the dashboard's side, `endpoints.test.ts` "reads the best hour, and the month it is known from where an
  older month names none" and "refuses a best hour outside the day".
- **The screen**: `insights.test.tsx` "names the best hour ever with its day, its time and its page views",
  "keeps the best hour's time whole on a line of its own", "names the best hour since the month hours are
  known from, where an older month names none", "sets the best day and the best hour first, then the busiest
  day of the week and hour of the day", "names the best day ever with its page views", "names no day and no
  time where every month is left out", "asks for people's figures and says so once" and "holds the record’s
  place, in its own words, while it is on its way".

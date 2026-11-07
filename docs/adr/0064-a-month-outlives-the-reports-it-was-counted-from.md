# 0064 — A month outlives the reports it was counted from

- **Status**: accepted
- **Date**: 2026-10-30
- **Applies to**: both editions, and the whole of it is in the free product. Adds the table
  `consolidated_months` (ClickHouse migration 0014) with no expiry, a background service that fills
  it, and three endpoints that read it; **widens the retention envelope**, which is stated in full
  below. Collects nothing new: every figure kept is worked out from reports the dashboard already reads, by its own arithmetic. Nothing in
  `integrations/` or in the tracker changes, because nothing about what is captured changes. In
  `ee/` only the hosted service's privacy copy and its release runbook change.
- **Extends**: [0013](0013-removing-a-website.md), whose removal of a website now also removes its
  kept months — the only thing that ever deletes one.
- **Revises**: [0042](0042-a-day-in-the-picture-is-a-period.md), whose "the period is the whole
  screen's" stops at the lists under All time: the cards and the picture cover the whole record,
  and the lists beneath them its last twelve months, which is as far back as anything about a
  single visit is kept. The screen says so in one sentence above the lists.
- **Follows**: [0025](0025-one-number-for-the-screen-and-the-bill.md), whose rule that one figure is
  counted by one arithmetic a kept month is held to; [0041](0041-one-population-for-the-whole-screen.md),
  whose two shapes per population a month is counted in; [0062](0062-a-day-s-visitor-key-is-ready-before-the-day-begins.md),
  whose salt lifecycle is part of the envelope stated below.
- **Revises**: [0063](0063-the-picture-is-drawn-by-the-day-the-week-or-the-month.md), whose "as columns,
  a first or last bucket the period cuts short is faded" now holds only where a column between the
  ends stays at full strength, for years as well as weeks and months, and whose drawing choice is not
  offered for a picture of fewer than three buckets, which is drawn as columns. Its month buckets are
  still what a kept month is cut by, and its first day still kept is still where a kept month can be
  opened in detail.
- **Revises**: [0054](0054-a-visit-still-in-progress-holds-back-only-itself.md), whose "the whole
  period is still being judged where not one bucket has settled" makes an exception for a picture of
  one bucket — today's first hour, or all time in a website's first month. That bucket runs to the
  present and never settles, so the whole period is named only where judging has reached none of it.
- **Followed by**: [0066](0066-insights-are-read-from-the-months-that-were-kept.md), which reads each
  kept month's best day and its weekday and hour figures on a screen of their own.
- **Extended by**: [0067](0067-work-in-the-background-ends-when-the-host-does-and-not-before.md), which
  holds every timed service to what the one that keeps months does: a failed run ends there, and the
  loop goes on.
- **Extended by**: [0069](0069-a-post-s-views-are-kept-a-month-at-a-time.md), which keeps how many times
  each of a website's posts was read beside each month's totals — counted, counted again and removed on
  these terms — and widens the retention envelope by it.

## Context

The owner asked on 2026-10-30 to see a website's figures for "all time", and for an Insights screen
built on them: the best day ever, the busiest weekday and hour, and a table of every month of every
year. None of it could be answered. Every report, every verdict and every answer about a name is
deleted by the store twelve months after it arrived (0001, 0003, 0010), so the oldest anything could
reach was a year ago, and a website measured for three years would have shown one.

The owner's direction was explicit: keep monthly figures only, not daily ones — "consolidate
monthly data only… just keep one record and update it". A day-by-day history kept for ever would
have kept, for every website, a far finer account of when somebody read it than any figure on the
screen needs, and it was refused.

## Decision

**Reports keep their twelve months.** Nothing about how long a report, a verdict or a name answer is
kept changes. What outlives them is one row per website per finished month, in
`consolidated_months`, with no expiry.

**A month is counted the way the dashboard counts it.** The statements that count a month
(`ConsolidationSqlCompiler`) are written from the fragments the dashboard's own statements are
written from — the reconciliation of the two halves of the measurement, pages delivered, the people
the verdicts cover, and the website's calendar buckets — so a kept month's page views and daily
visitors are the overview of that month, its busiest day is the tallest bar of that month's daily
series, its weekdays are that series folded, its hours are the hourly series folded, and its visits
of each kind are that month's bar of who came. A test holds `CountingRules.Current`, the version of
the arithmetic every row records, to a hash of the counting statements' text, so a change to a
shared fragment fails until the version is raised — and a raised version recounts every month still
held.

**A month is one row, replaced whole.** Both populations and the verdicts are on the same row,
written in one insert once every figure has been counted. The table replaces on the instant the
count was taken, not the instant it was written, so of two counts the one that read later wins
whichever arrives first; every read takes the newest row of each month itself rather than waiting
for the store to merge.

**A month says how it was counted**: in which zone, by which rules and how far those rules had
judged the website, how far judging had reached, which version of the arithmetic ran, and when.

**It is counted again only while its reports remain.** A month is counted when it finishes, and
again whenever what was kept no longer describes it — it was read in another zone, counted by older
arithmetic, counted before judging had reached its end and judging has since moved into it, or
counted by older rules that the rules now running have since judged the whole of. Never once the
oldest report it rests on could be gone: from a day before that, the month keeps whatever was last
kept of it. The stretch that decides a month's people begins a whole visit before the month, so
judging moving before that recounts nothing, which keeps a backlog of judging from recounting the
same months on every run.

**Only finished months are kept.** The month running now, and any finished month nothing was kept
of yet, is counted from its reports whenever somebody looks, by the same arithmetic. That keeps All
time level with every period read from reports alone — all of it, for a website measured only this
year, is this year — and a website measured for the first time shows its visits at once rather than
after the next count.

**The first day is the day of the first visit, where that can be known.** It is kept with the month
it falls in, and an average per day is divided by the days from it. A report is kept twelve months,
so the oldest report still held is the website's first only where nothing it sent earlier can have
gone uncounted: where this installation began keeping months while the month the website was added
in could still be counted in full, every month it has sent anything in since was kept, or is held
still. Keeping began when the store gained the table months are kept in, which its own record of
applied changes holds once and never rewrites. The month the website was added in is taken on the
calendar that puts it earliest — its added instant read on the clock furthest behind UTC, its month
begun on the clock furthest ahead — because months were counted on whatever calendar the website
kept at the time, and so neither a change of zone nor anything else ever moves the answer. A website
added longer before keeping began than that — so that the month it was added in could no longer be
counted in full by then — may have sent reports that went before any month of it was kept, so its
oldest report only says how far back the store reaches; it keeps no first day, and its record begins
on the first of its first month, counted as measured on every day. A first day once kept stands.
When the months are next counted after a change of zone it is read again on the new calendar,
while the report it was taken from is still held, and moves to the day it falls on there — by two
days at most — and to the month that holds that day; where that month can no longer be counted the
day could be kept nowhere, so it stays as it was kept. All time reads a kept first day as it was
kept, with the months it was kept with, so a change of zone moves both at the next count, minutes
later — except where the month that count will move the day into is already the record's first
month, answered from its reports, which begins on that day at once. Where no first day is kept —
nothing kept yet, or a first month kept without one on a calendar the website no longer keeps,
waiting to be counted again — it names the day the engine will keep, by the same rule
(`FirstDays`), so the day a screen names is the day the month is kept with.

**Records are worked out from months, never kept.** A best day ever, or a most-read anything, is
the largest of the months' own figures, worked out when it is read. A single kept record could not
fall when a month still held is counted again under newer rules and comes out lower, and it would
then name a day as the best ever on figures no month any longer has.

**Visitors stay daily.** A month's visitors are counted as the overview counts a period — each
visitor once for every day their key covers — and a year is the sum of its months. Away from UTC a
key that spans the night a month changes is counted in both months, which is the same small excess
a week or a month already carries on the picture (0063).

**Two engines count twice and keep once.** A month counted by two engines at once is two rows until
the store replaces one, and every read takes the newer.

**Removing a website removes its months, and nothing else ever deletes one.** `consolidated_months`
joins the tables `ClickHouseTelemetryPurge` empties when a website is removed or a closed account's
records are deleted. The counting itself deletes nothing: a sweep that removed kept months of
websites the control plane no longer lists would delete every website's history on an engine started
against an empty, restored or mistaken database. Instead a month is written only while the website
still exists, under a lock the removal takes before it deletes anything and holds until the website
is gone (`ISiteRoster.WhileItExistsAsync`, `AdvisoryLocks.SiteTelemetryNamespace`): a write that
comes first is deleted by the removal, and one that comes second finds no website and is never made.
Kept months never expire, so unlike a report caught between a removal's two steps (0013), one left
behind would stay for ever.

**All time reads what was kept, and what can still be counted.** `GET /api/sites/{id}/all-time`,
`…/all-time/series` (by month or by year) and `…/all-time/traffic/series` put the record together
month by month (`KeptHistory`): a finished month from what was kept of it, every month nothing was
kept of from its reports while they are all held. A month of the record that is neither — never kept, and with
reports that may already have begun to go — is **missing**: it has no figure, never a nought, the
answer lists it, and the screen draws it as a gap. Asked about people, or about who came, a month
kept before judging had finished with it and no longer countable again is missing too, because how
many of its visits were people was never settled; the figures for everybody still stand. Each answer
names the two apart — `missing` for a month never counted, `unjudged` for one kept before its visits
were all judged — so a screen never works out why a month is missing by setting one answer against
another read at a different moment. Only a month still held can hold back where the figures stop
being complete, so a month that froze unjudged never washes the months after it. A month in front
of the first day that holds nothing is no part of any answer: one kept as measured on none of its
days, or one never counted and no longer countable into which a change to a calendar behind the
old one has put the first visit's hours, so that they are in no month. Figures are never dropped for
a first day: a month in front of it that holds figures and no first day of its own begins the record
— on the day its next count will keep, where it is still answered from its reports and a change to a
calendar behind the old one has moved the first visit into it, and otherwise from the first of it.

**On the overview, All time is a period of its own.** It is filed with the calendar periods, after
this year, and says beside the chooser since when it runs. Its cards and its picture cover the whole
record, by the month, or by the year as well once the record runs across the first of January, and
measure themselves against nothing, because nothing comes before it — so it offers no comparison and
its cards say no rise or fall. The picture waits for the record before it asks for anything or draws
anything, so it is never cut or named by a span it does not cover. A shorter period offers years only
where All time would, and choosing them moves the screen there as choosing weeks on a short period
moves it to a longer one (0063). A month still held in full can be pressed to look at its days; one
older than that cannot, because only its totals remain. The lists beneath, and the visits on the
journey screen, cover the days still kept, from the first day still held in full to today, which each
says once where the record reaches further back. Where nothing reached the website in that time the
lists give way to one card that says so; kept to people, where visits came but none judged was a
person the card says there are no people yet, and where none of them has been judged yet it says
that instead. The lists wait for the record, and for the figures over those months — the screen's
own and everybody's — before choosing any of these, so a screen that has no lists to show never asks
them anything, and where those figures cannot be had they say so in the lists' own place. A
month never counted is a gap in the picture, "Not counted" in its table and in the card a hover
opens, and one clause beneath it that names it. A month kept before its visits were all judged is
missing only from the people's figures and from who came: there it is the same gap, "Not fully
judged", with a clause of its own, while everybody's figures for it stand. A year that leaves out
either kind names those months in its row; a year with no figure at all reads "Not fully judged"
where any month it leaves out was counted, and "Not counted" otherwise. Each picture names the
months it leaves out, and why, from its own answer. The record's answers
are filed under the website's zone, so a change of zone asks for them again rather than reading old
months by new days.

**A picture of fewer than three buckets is drawn as columns.** A line through one or two points
draws a slope out of nothing, so a record of a month or two — or the first hours of today — is
drawn as columns, and the choice of drawing is not offered until there is a shape to draw. Under All
time how many buckets there will be is known only once the record is in, so until then the places of
the choice of drawing and of how to cut the picture are held, offering nothing. Each control then
appears in its place where the record turns out to offer it and gives the place up where it does
not: how to cut the picture is offered where the record runs across a new year, and the choice of
drawing where the picture it is cut into has three buckets or more. A record across a new year drawn
in three months or more offers both, and nothing moves; one within a single calendar year gives up
the list; one drawn in fewer than three buckets — by the year, or across a new year by the month for
a month or two — gives up the choice of drawing; and a record that cannot be had holds no place at
all. The
record is asked for on any period once somebody reaches for the period — pressing it, or working it
from the keyboard, never just tabbing past — so the date chooser can say where All time reaches
further back whatever was looked at before. The ends a
period cuts short, now years as well as weeks and months, are faded only where a full column stands
between them.

**One first day still held in full.** The first midnight where the website is that falls a whole
visit after the oldest report kept (`Retention.DetailFrom`) is the day the dashboard stops letting a
period reach back to and the day a bucket of the record can be opened in detail from; the engine and
the dashboard work it out by one rule, held to the same examples by a test on each side.

## The retention envelope, in full

- A visitor's network address: cleared by the store 72 hours after the report arrived.
- The visitor key: derived from the salt of the UTC day a report arrives on. A salt is made a day
  before its own day, never reused, and deleted once the day after its own is over (0062), so a key
  joins nothing across days and nobody, the operator included, can recover one.
- Every report, every verdict and its reasons, and every answer about a name: twelve months.
- Each website's totals for each finished month — pages read, daily visitors, its busiest day and
  that day's pages, pages read on each weekday and in each hour of the day, how many visits of each
  kind began in it and the pages they read, and the same pages, visitors, day, weekdays and hours for
  the visits judged to be people, and, on the month it falls in, the day the website was first
  measured, which says how many of that month's days it was measured on: until the website is
  removed, even after it stops being measured, or until its account's records are deleted thirty days
  after the account is closed.
- A closed account: thirty days in the running stores (0045), after which everything above goes
  with it.

A month's totals hold no address, no page address, no user agent and nothing about any one visit. For a
quiet month, though, they can describe a handful of visits: a month with one visitor says which
day, which weekday and which hour that visitor read on. That is said here rather than discovered.

## Measured

Read-only on the hosted installation on 2026-10-30, by month on the clock reports are stamped by:

| Month                   | Websites | Reports | Busiest website's reports |
| ----------------------- | -------- | ------- | ------------------------- |
| 2026-08 (from the 19th) | 5        | 7,281   | 6,574                     |
| 2026-09                 | 5        | 24,329  | 18,822                    |
| 2026-10 (to the 30th)   | 6        | 32,258  | 24,904                    |

The two counting statements, run there for the busiest website's September in Kolkata's calendar
with their results discarded, took 0.12 seconds for everybody and 0.22 seconds for people, each on
the one thread they ask for. The first run after this record ships counts every finished month of
every website in well under a second of the store's time per month, and the ten-minute limit each
statement carries is three orders of magnitude away.

## What it does not do

- It keeps no day-by-day figures beyond the twelve months a report is kept.
- It cannot recover a month whose reports expired before it was counted. From the first month kept
  onwards such a month is missing — listed, and drawn as a gap. A month before every kept month is
  missing only while some of its own reports are still held; once the last of them goes it is no part
  of the record, and All time begins after it. That is what an installation loses when it first runs
  this record more than about eleven months after a website was first measured.
  The hosted installation's history begins on 19 August 2026, and August 2026 stays countable until
  about 29 July 2027 — a day before the oldest verdict it rests on, on a visit begun the day before it,
  could begin to go, and two days before its own first reports — so its whole history is kept within
  minutes of this record shipping, and every one of its websites keeps its first day.
- It cannot know the first day of a website added before this installation began keeping months,
  wherever the month it was added in — read on the calendar that puts it earliest — could no longer
  be counted by then: its record begins on the first of its first month, counted as measured on every
  day, rather than on a day that only says how far back the store reaches. Reading that month on the
  earliest calendar is stricter than the website's own calendar in two ways. A website added in the
  first hours of a month on its own calendar — twelve, and as many more as its clock runs ahead of
  UTC, or fewer as it runs behind — is taken as added in the month before, so keeping begun up to
  about a month before its own month stopped being countable leaves it without a first day. And any
  website is refused where keeping began within fourteen hours, less its clock's lead on UTC, of its
  own month's limit. A website added since keeping began, however long before its first visit, keeps
  the day of that visit.
- Nothing records how long counting was switched off. Months still held when it is switched back on
  are counted then; switched off for longer than about eleven months, it loses the months that
  expired in between, as above, and a first day taken afterwards can be later than the website's
  first visit. The engine warns at every start-up while counting is off.
- It never judges a kept month again. A month that froze keeps the verdicts it was counted with,
  however the rules improve afterwards.
- A month frozen before the website's zone was changed keeps the old zone's boundaries; months
  still held are counted again in the new zone. Where the new zone is ahead of the old one, the hours
  between the new zone's midnight and the old one's at that line are in both months, so All time, its
  years and who came count them twice; where it is behind, they are in neither. Either way the
  stretch is at most the difference between the zones. A month of the calendar that a change of zone
  leaves wholly before the website's first day is measured on none of its days, and is no part of
  the record. A website moved across more than a day of zones and back again, where the count after
  the second move fails part-way, can have All time name the first of its first month, a day or two
  before its first visit, until the month that will keep the first day is counted.

## Consequences

- An engine that predates this record reads nothing from the table and writes nothing to it, but it
  also does not delete a removed website's kept months, and those never expire. The release runbook
  says what to delete by hand after going back.
- Counting a month reads every report of it, three cuts for each population, on one thread and for
  at most ten minutes, over a client of its own allowed fifteen. It runs a minute after start-up and
  every fifteen minutes after that, at most four months per website per run, the oldest first, and
  says when a run outlasts the time between runs. A month already kept and still accurate costs one
  small read of the calendar per run.
- The privacy copy of the hosted service names everything a month keeps, for both populations, and
  the date the legal documents were last changed moves with it.
- The README says every report of a visit is deleted after twelve months and each month's totals are
  kept until the website is removed, instead of "retention is 12 months".

**Tests that make this true.** That a month is counted by the dashboard's arithmetic is
`ConsolidationSqlCompilerTests` `A_Month_Is_Counted_By_The_Dashboards_Arithmetic` and
`A_Months_Days_And_Hours_Are_Cut_Where_The_Dashboards_Series_Cut_Them`, with the approved
`Month_Calendar`, `Month_Count`, `Month_Count_Of_People` and `Month_Verdicts`, and, run against the
store, `MonthConsolidationTests` `A_Kept_Month_Is_The_Overview_Of_That_Month` (both populations),
`A_Kept_Months_Busiest_Day_Weekdays_And_Hours_Are_Its_Daily_And_Hourly_Series_Folded`,
`A_Kept_Months_Busiest_Day_For_People_Is_Counted_From_The_People_Alone`,
`A_Kept_Months_Verdicts_Are_Who_Came_That_Month` and `A_Kept_Month_Begins_Where_The_Pictures_Month_Begins`;
that the version of the arithmetic moves with the statements is
`The_Version_Of_The_Arithmetic_Moves_With_The_Statements_That_Count`. That only finished months are
kept, every one still held, the oldest first and a run's allowance at a time, is
`Every_Finished_Month_Still_Held_Is_Kept_Oldest_First_A_Runs_Allowance_At_A_Time`,
`The_Month_Running_Now_Is_Never_Kept`, `Nothing_Is_Kept_For_A_Website_With_No_Reports`, and
`MonthsToCountTests` `The_Month_Running_Now_Is_Never_Counted`, `The_Oldest_Months_Are_Counted_First`
and `A_Run_Counts_No_More_Than_Its_Allowance`; that a month is counted again only while its reports
remain, and only when what was kept no longer describes it, is the rest of `MonthsToCountTests` —
`A_Month_Whose_Reports_May_Have_Begun_To_Expire_Is_Never_Counted`,
`A_Month_Judging_Has_Not_Reached_Is_Left_Until_It_Has`, `A_Month_Whose_Judging_Has_Moved_On_Is_Counted_Again`,
`A_Month_Is_Counted_Again_Once_Newer_Rules_Have_Judged_All_Of_It`,
`A_Month_Newer_Rules_Are_Still_Walking_Is_Left_As_It_Was`, `An_Older_Engine_Never_Recounts_A_Newer_Ones_Month`,
`A_Month_Is_Counted_Again_In_A_New_Time_Zone` and `A_Month_Counted_By_Older_Arithmetic_Is_Counted_Again` —
with `A_Month_Already_Kept_Is_Not_Counted_Again_Until_Something_About_It_Changes` against the store.
That nothing but removal deletes a month is `MonthlyConsolidationTests`
`Nothing_Is_Ever_Deleted_By_Counting` and `A_Count_Is_Not_Kept_For_A_Website_Removed_While_It_Ran`,
`TelemetryPurgeTests` `The_Months_Kept_For_A_Site_Go_With_It_And_Nobody_Elses_Do`, and `SiteRosterTests`
`Work_Is_Done_For_A_Website_Of_A_Closed_Account_And_Never_For_A_Removed_One`, with `MonthConsolidationTests` `A_Website_Removed_While_Its_Months_Are_Being_Kept_Keeps_None_Of_Them`; that a month that fails stops nothing
else is `A_Month_That_Fails_To_Count_Is_Not_Kept_And_Does_Not_Stop_The_Others` and `A_Month_Whose_Count_Times_Out_Does_Not_Stop_The_Others`. That the work runs a minute after start-up and keeps running through a failed website or a failed run is `ConsolidationWorkerTests` `The_First_Run_Comes_After_The_Start_Delay_And_Before_A_Whole_Interval`, `A_Website_That_Fails_Does_Not_Stop_The_Next_Or_The_Loop`, `A_Run_That_Cannot_List_The_Websites_Does_Not_Stop_The_Engine` and `Nothing_Is_Counted_While_Switched_Off`, with `The_Engine_Runs_The_Work_That_Keeps_Months`, and that it is counted over a client of its own is `TelemetrySchemaTests` `Months_Are_Counted_Over_A_Client_Given_Longer_Than_The_Store_Gives_A_Count`. That a month judging stalled inside is not counted again on every run is `A_Month_Kept_Where_Judging_Stalled_Is_Not_Counted_Again_While_It_Stays_There`; that the busiest day is the earliest of a tie is `BusiestDayTests` and `A_Kept_Months_Busiest_Day_Is_The_Earliest_Of_Two_As_Busy`. What a change of zone does is `A_Change_Of_Zone_Counts_Again_The_Months_Still_Held_And_Keeps_The_Frozen_Ones` against the store, with `MonthsToCountTests` `A_Month_That_Froze_Keeps_The_Zone_It_Was_Counted_In_And_The_Next_Is_Counted_In_The_New_One`, `KeptHistoryTests` `A_Zone_Change_Counts_The_Overlap_At_The_Freeze_Line_In_Both_Months` and `A_Month_A_Change_Of_Zone_Left_Before_The_First_Day_Is_No_Part_Of_The_Record`, and `MonthlyConsolidationTests` `A_Month_A_Change_Of_Zone_Leaves_Before_The_First_Day_Is_Measured_On_None_Of_Its_Days`; the first day once the record's first month is gone is `The_First_Day_Is_The_First_Of_A_First_Month_That_Can_No_Longer_Be_Counted` and `The_First_Day_Of_A_First_Month_Kept_Without_One_Is_The_First_Of_It`, with `MonthlyConsolidationTests` `A_First_Day_Kept_Before_A_Change_Of_Zone_Moves_To_The_Day_It_Falls_On_Now` and `A_Month_Kept_Before_The_First_Day_Stays_Measured_On_None_Of_Its_Days_When_Counted_Again`. That the first day is the first visit only where nothing sent earlier can have gone uncounted, decided by one rule on both sides, is `FirstDaysTests`, `MonthlyConsolidationTests` `A_First_Day_Is_Kept_For_A_Website_First_Visited_Long_After_It_Was_Added_Where_Months_Were_Kept_All_Along` and `No_First_Day_Is_Kept_Where_Months_Began_To_Be_Kept_After_The_Website_Could_Lose_Some`, `KeptHistoryTests` `The_First_Day_Is_Not_Named_From_The_Oldest_Report_Where_Earlier_Ones_May_Have_Gone_Uncounted`; that a kept first day stands, moving with a change of zone only into a month that can still be counted, is `FirstDaysTests` `A_Kept_First_Day_Stays_As_It_Was_Kept_Where_The_Month_It_Would_Move_Into_Can_No_Longer_Be_Counted` and `MonthlyConsolidationTests` `A_Kept_First_Day_Stays_Where_The_Month_It_Would_Move_Into_Can_No_Longer_Be_Counted`; that All time reads it as it was kept, never drops figures for it and drops a month in front of it only where that month holds nothing is `KeptHistoryTests` `A_Kept_First_Day_Is_Read_As_It_Was_Kept_Until_Its_Months_Are_Counted_Again`, `A_Month_With_Figures_Before_A_Kept_First_Day_Begins_The_Record`, `A_Month_Answered_From_Its_Reports_Before_A_Kept_First_Day_Begins_On_The_Day_Its_Next_Count_Keeps`, `A_First_Month_Kept_On_Another_Calendar_Is_Named_From_The_Day_Its_Next_Count_Keeps`, `A_Month_Answered_From_Its_Reports_Before_A_Kept_First_Day_Begins_The_Record` and `A_Month_Never_Counted_Before_The_Kept_First_Day_Is_No_Part_Of_The_Record`; that the month a website was added in is read on the calendar that puts it earliest is `The_Month_A_Website_Was_Added_In_Is_Taken_On_The_Calendar_That_Puts_It_Earliest` and `A_Website_Is_Vouched_For_Only_Where_The_Month_It_Was_Added_In_Could_Still_Be_Counted_When_Keeping_Began`, and, against the stores, `TelemetrySchemaTests` `Keeping_Months_Began_When_The_Store_Gained_Somewhere_To_Keep_Them` and `AuthorisationTests` `A_Member_Is_Given_A_Scope_Carrying_Their_Role_The_Site_Time_Zone_And_When_It_Was_Added`. That the table never
expires and the engine's twelve months are the store's is `TelemetrySchemaTests`
`Kept_Months_Are_One_Row_Per_Month_And_Never_Expire` and
`The_Months_The_Engine_Assumes_A_Report_Is_Kept_Are_The_Months_The_Store_Keeps_It`. That All time is
the kept months and the months answered from their reports, and never behind, is `AllTimeReadTests`
`All_Time_Adds_Up_The_Kept_Months_And_The_Month_Running_Now`,
`A_Month_Nothing_Has_Been_Kept_Of_Yet_Is_Answered_From_Its_Reports`,
`All_Time_Is_Never_Less_Than_This_Year_For_A_Website_Measured_Only_This_Year`,
`All_Time_By_Year_Adds_Up_Its_Months`, `All_Time_Of_People_Counts_The_People_Alone` and
`Who_Came_All_Time_Lines_Every_Conclusion_Up_On_One_List_Of_Months`; that a missing month is a gap
and never a nought is `A_Month_That_Was_Never_Kept_And_Can_No_Longer_Be_Counted_Is_Missing`, and that
the people's picture names a month kept before its visits were all judged where everybody's does not
is `The_Peoples_Picture_Names_A_Month_Kept_Before_Its_Visits_Were_All_Judged`, with
`KeptHistoryTests`, including `A_Month_Never_Counted_And_A_Month_Kept_Before_Judging_Finished_Are_Named_Apart`
and `A_Month_That_Froze_Unjudged_Does_Not_Hold_Back_The_Months_After_It`;
and that the engine's first day still held is the dashboard's is `RetentionTests`
`Detail_Begins_On_The_Day_The_Dashboard_Keeps_From`, which runs the dashboard's own examples, with
`All_Time_Says_How_Far_Its_Detail_Reaches` against the store. That nothing answering a request can
count for the system is `AuthorisationBoundaryTests` `Nothing_Answering_A_Request_Counts_For_The_System`.
On the dashboard, "reads the cards from the record, and compares them with nothing", "offers no
comparison", "says since when all time runs, beside the period", "draws by the month, and by the year
once asked", "offers no choice of how to cut it while the record lies within one year", "lists years from a shorter
period where all time has them, and moves there for them", "lists no years from a shorter period
while the record lies within one year", "offers only the months still held day by day to be pressed",
"narrows the whole screen to the days of a pressed month", "says the lists cover the last 12 months,
and asks every list about them", "shows a month never counted as not counted, and names it as left
out", "says who came leaves out a month whose visits were never all judged", "shows the
visits of a website first measured this month" and "waits for the first visit only when nothing of the website is held" hold the screen to this record, with "files all time with the calendar
periods, after this year" and "says the list covers the last 12 months under all time". That a month
never counted and a month not fully judged are named apart is "names a month never counted apart from
one not fully judged", "says the people’s figures leave out a month whose visits were never all
judged" and "never calls the people absent while months are left out of their figures", with "names a
month not fully judged apart from a month never counted" and "calls a month not fully judged that in
the card a hover opens, as the table does" on the picture itself. What stands in for the lists is
"says nothing reached it in those months where the screen is kept to people", "says no people yet in
those months where visits came and none judged was a person", "says nothing was judged yet in those
months where visits came and none was judged", "says nothing about quiet lists before the record is
in", "says what went wrong where whether the lists hold anything cannot be had", "says nothing went wrong where a website never measured has nothing to list", "says nothing went wrong before a website never measured is known to have nothing to list", "says nothing went wrong before a screen kept to people is known to have none", "stops asking about the lists’ twelve
months where no people at all stand in for them" and "asks the lists nothing on a cold load where no
people at all stand in for them"; on the journey screen, "says the list covers the last 12 months
together with the rows, above them", "keeps the rows and the visit opened while a refused record is asked again", "waits for its own answer where a refusal was left from before it opened", "waits for its own answer where a refusal lands as it opens" and "waits again for the record of another website chosen". That each picture names the months it leaves out, and why, from its own answer is
"names a month the people’s figures leave out where the cards cannot be read" and "says why a month
is left out from the picture’s own answer, never from the record’s", and that a year with no figure
and a counted month in it is never called uncounted is "calls a year
not fully judged where a month of it was counted, beside one never counted". That the choice of
drawing is never offered and then withdrawn, and that a record across a new year offers its controls
in the places held for them, is
"never offers a way of drawing a first month while its picture is on its way", "offers the ways of
drawing a long record while its picture is on its way", "holds the places of the ways of drawing and
cutting until the record is in, and offers them there", "gives up the held places for a first month,
having offered nothing in them", "keeps the place of the ways of drawing when all time is chosen from
today", "holds the place of the ways to draw it, offering none, while how many buckets it is due is
not known", "holds no place where it is due too few buckets for a shape", "holds no place for ways of drawing or cutting a picture that could not be had", "offers the list in its place and gives up the switch’s for a record from December into January" and "offers the list in its place and gives up the switch’s for a record of two years by the year"; that the record is asked for once somebody
reaches for the period is "asks for the record once somebody reaches for the period, and not before",
"offers all time for older days from the chooser on a period that never asked for the record", "says nothing when it is tabbed past", "says so when it is opened from the keyboard", "says so when it is opened with a modifier held", "says nothing when a key that moves through it is pressed with a modifier", "says so when a letter is typed into it" and "says nothing when focus is sent from it to the browser". That a picture of one bucket names the whole period only where judging has reached none
of it is "is not a period whose only bucket is still running and judged most of the way", "is a
period of one bucket that judging has reached none of" and "says the whole period is still being
judged when judging stopped inside its first day".

# 0063 — The picture is drawn by the day, the week or the month

- **Status**: accepted
- **Date**: 2026-10-30
- **Applies to**: both editions, and the whole of it is in the free product. The two series
  statements gain a week and a month bucket and are filled by the website's dates, and the
  endpoints that answer them accept the two words; adds no column, no migration and no index;
  collects nothing new and widens no retention envelope. Nothing in `integrations/` or in the
  tracker changes, because nothing about what is captured changes, and nothing in `ee/` moves.
- **Revises**: [0042](0042-a-day-in-the-picture-is-a-period.md), whose "a press on a bucket of the
  picture narrows the period to the calendar day that bucket falls in" becomes the days the bucket
  covers, and whose table names every bucket "{bucket}, look at this day". A bucket of one day is
  still pressed, named and written exactly as 0042 decided.
- **Revises**: [0049](0049-a-day-is-called-what-the-reader-calls-it.md), whose naming of a chosen
  stretch stopped at today and yesterday: a stretch that is exactly this month, last month or this
  year, pressed on the picture or typed into the chooser, is called that too — at the moment of
  choosing, never when an address is read.
- **Extends**: [0028](0028-what-a-link-carries.md) and [0040](0040-what-a-screen-remembers.md), with
  one more choice that travels in the address and is remembered by the browser, and
  [0041](0041-one-population-for-the-whole-screen.md), whose population and wash apply to a week or a
  month exactly as to a day, and whose way of quietening a bucket also quietens a week or a month
  the period cuts short.
- **Revised by**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md). As columns, a week,
  a month or a year the period cuts short is faded only where a column between the ends stays at
  full strength, and a picture of fewer than three buckets is drawn as columns, with no choice of
  drawing. Its kept months are cut by this record's month buckets and can be opened in detail from
  its first day still kept.

## Context

The owner asked on 2026-10-30 to read the traffic picture "monthly, weekly, yearly", because it only
ever drew days. It drew whatever the period's length decided — an hour at a time for two days or
fewer, a day at a time for everything else — so "This year" in late October was three hundred
daily points, and the shape of a quarter could only be read by squinting at ninety. Years belong to
a period longer than any this screen offers today, and arrive with it in a later record.

Looking at what the picture compared itself with turned up a second fault. "This year" was set
beside "the same days last year", but a visit's reports are kept for twelve months, so part of last
year's same days is always already gone. The cards read "up" against days that were deleted rather
than quiet. Nothing on the screen said so, and nothing could have: a figure counted from a store
that no longer holds the days it names is not a figure.

Cutting weeks and months turned up a third. The days nothing happened in were filled in as
instants, a day's step from the midnight before. Where the clocks go forward at midnight —
Santiago on the first Sunday after the first of September, Cairo on the last Friday of April,
Havana, Beirut and the Azores in March — that day has no midnight, and the store begins it at one
in the morning; a day's step lands an hour short of that, and every day after it came back twice,
once empty and an hour early. The picture of a website in Chile drew two bars for each day after
its clocks changed, and the series of verdicts, whose days are read once for every kind of visit,
could put a kind's visits on the wrong day. The dashboard had the opposite fault where the clocks
go back to midnight, as Havana's and the Azores' do in autumn: it began such a day at its second
midnight, an hour after the engine does, so a period ending the day before reached an hour into
it.

## Decision

**A week runs Monday to Sunday, and a month from the first, where the website is.** Weeks are ISO
weeks. The engine cuts both in the website's own zone, by `CalendarBuckets` — the local date of the
instant, its Monday or its first — so a week in Kolkata begins on Sunday at 18:30 UTC and a month in
London begins at the midnight its own clocks keep, either side of a change. A bucket is named by its
calendar start, so the first and last may begin before or end after the period; each counts only
what the period holds.

**Buckets are filled by the website's dates, and become instants afterwards.** A day, a week or a
month is counted and filled by the local date it begins on, a calendar day, week or month at a time,
in a query of its own; only then is every bucket, counted or invented, put at the instant it begins,
by the one conversion both share. A day whose midnight the clocks skip is therefore one bucket
beginning at the first instant its clocks show, and every day after it begins at its own midnight.
An hour has no date to be filled by and is still filled as an instant, sixty minutes at a time. Both
series — the one counted from activity and the one counted from verdicts — are cut and filled this
way. The dashboard begins a day at the same instant the engine does — its first midnight where the
clocks go back to midnight, and the first instant they show where they skip it — so a period's
window and the buckets it is drawn in begin and end together.

**The picture is cut by the day until the period is long enough for anything coarser.** Weeks are
offered from periods of 28 days and months from 90: a picture needs three bars before it has a
shape, and a choice that drew one bar and two halves would be offered only to be regretted. Two days
or fewer stay hourly, and the choice is not shown there. Days are every longer period's own way,
which it opens on unless the address or the browser names another.

**The choice travels in the address and the browser remembers it.** It is written `by=week` or
`by=month`, pushed onto the history as every choice about what is looked at is, remembered as
`dewiride.chart-by`, and left out of the address when it is the period's own. A remembered choice
the period cannot offer is not forgotten: the picture is drawn the period's own way — by the hour on
two days or fewer, by the day otherwise — and the choice returns when a long enough period does.

**A way the period cannot show is offered as the period that can.** The control is the browser's
own list, as the period control beside the website's name is, and lists every cut on every period
longer than two days. One the period cannot show reads as the period it leads to — "By week · Last
30 days", "By month · Last 90 days" — and choosing it chooses that period and keeps the cut. The
reader stays on the list: a closed list chooses at every arrow key on some systems, so a list that
sent its reader to the period control would send them there on the first key, before they had heard
the rest of it. The change is said instead, politely, while the screen is on the period it moved to
— "Period changed to Last 30 days." Nothing is greyed out and nothing is explained in a title nobody
on a phone can read.

**A bucket is written as the days it covers.** A week is written as its first and last day inside
the period, and a month by its name, or by its days where the period cuts it. The card a hover opens
names the same days in full in the reader's language and in the website's zone, as 0049 settled for
a day.

**A week or a month the period cuts short is drawn quietly.** The first week of "Last 90 days" may
hold a single Sunday, and drawn at full strength its figure reads as traffic collapsing rather than
as a seventh of a week. As columns, a first or last bucket the period cuts short is faded, as a
bucket still being judged is. As a line or an area, a wash is laid from its point to its
neighbour's — but only where something between the quietened ends is still drawn at full strength:
three months cut at both ends, washed from both sides, would leave every figure faded, which reads
as none of them faded at all. The wash over buckets still being judged is always laid, and
stretches that meet are laid as one, so nothing is washed twice. The caption says "Weeks the period
cuts short are drawn fainter." — or months — exactly where the drawing does so. An hour or a day is
never cut short.

**Visitors stay daily visitors.** A visitor key covers one day of UTC, so a week counts each key
once and a reader who came on three days counts three times — the cards' own "someone who returns
tomorrow counts again". Away from UTC a week can hold fewer than its days added up, because a key
straddling local midnight is one key in the week and one in each of two days. The caption says
somebody who comes back on another day counts again; it never says the days are added up.

**The dashed line is set week beside week and month beside month.** By the day, the period before is
the stretch the cards are measured against. By the week it steps back by whole weeks, and by the
month by whole months, so each earlier bar shares its weekdays or its days of the month with the one
it is drawn behind; set by the cards' stretch, the bars at either end would compare four days with
six and report a fall that never happened. The earlier stretch is counted to the same moment of its
last day as the period has reached into its own, and in full once the period has finished, so a
month still running is never set against a whole one. It is worked out with the period, from the
moment the period was chosen, so cutting the picture another way later in the day never sets it a
day apart from the days on screen. The cards keep their own stretch, and the line says which days
it is.

**A press lands on the bucket's days.** Pressing a week or a month narrows the period to the days it
covers within the period, through the same chooser, history entry and hand-over to the period
control 0042 settled for a day. The table offers the same way in: "{bucket}, look at these days".

**No comparison reaches into days no longer kept, and no chosen stretch starts before them.** The
kept window begins at the first midnight, where the website is, at or after twelve months ago plus a
day — now less twelve calendar months, plus twenty-four hours — the day being the look-back verdicts
are read with, so a figure for people starting there is whole. Where the stretch a card or the
dashed line would be measured against begins before it, the card shows no change and the picture
offers no comparison. The chooser refuses an earlier day, and says so before it says a stretch is
too long, since every stretch longer than the kept window also begins before it; an address naming
one is pulled forward to the kept window — both ends of it, when the whole stretch is older — rather
than refused, as 0028 asks of any out-of-range stretch. A named period is held to the same window:
on the last day of a year, "This year" begins on the second of January, the first day still kept,
and keeps its name. Today this silences "This year" every day of the year, which is the honest
answer.

**A stretch that is exactly this month, last month or this year is called that.** The rule 0049 set
for today and yesterday is the same rule, and runs first: a pressed or typed single day is still
named before a month is.

## What this deliberately does not do

- **No years.** A year cut of a period that is at most a year long is two halves; years arrive with a
  period that reaches back further, in a later record.
- **No week that starts on Sunday.** The zone is the website's, and so is the week; a reader's locale
  does not move the buckets everybody else on the account is reading.
- **No "this week" or "last week" period.** The picture lines weeks up; a named calendar-week period
  is a separate addition.
- **No correction for a change of the clocks by half an hour.** Lord Howe Island moves its clocks by
  thirty minutes, which neither the store's rounding to the hour nor a sixty-minute step follows, so
  an hourly picture there that begins on the day of a change, or reaches across one, fills hours
  half an hour away from the hours it counts. It is the one zone that changes by less than an hour,
  and a picture is drawn by the hour only over two days or fewer.

## Consequences

**"This year" has no rise or fall.** Its earlier stretch always reaches past what is kept. Every other
named period keeps its comparison until a website is a year old, and keeps it afterwards wherever the
stretch it is measured against is still held.

**A link to an old stretch opens on the oldest days still kept.** It used to open on a screen of
noughts for days that had been deleted.

**Tests that make this true.** That a week begins on Monday and a month on the first where the
website is, in both series, is `AnalyticsSqlCompilerTests` `A_Week_Begins_On_A_Monday` with the
approved `Page_Views_By_Week`, `Page_Views_By_Month`, `Visitors_By_Week`, `Visitors_By_Month`,
`Page_Views_By_Month_Of_People`, `Traffic_Series_By_Week` and `Traffic_Series_By_Month`, and, run
against the store, `TelemetryQueryTests` `A_Week_Begins_On_Monday_Where_The_Website_Is` (Kolkata at
18:30 and Kathmandu at 18:15 UTC) and
`A_Month_Begins_At_Midnight_On_The_First_Either_Side_Of_A_Clock_Change`, with `TrafficReadTests`
`Weeks_Begin_On_Monday_Where_The_Website_Is`; that the first bucket counts
only the period is `The_First_Week_Holds_Only_What_The_Window_Holds`. That buckets are filled by the
website's dates and put at their instants afterwards is
`Every_Bucket_Is_Cut_And_Filled_Where_The_Website_Is`, `A_Fill_Steps_By_The_Calendar` and
`A_Bucket_Is_Put_At_The_Instant_It_Begins_Only_Once_It_Is_Filled`, which pins what each bucket is
named by — a local date for a day, a week or a month — and fails if that name is an instant; against
the store, in London and Kolkata, it is
`A_Week_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight_On_Monday`,
`A_Month_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight_On_The_First`,
`A_Month_Nobody_Visited_Still_Begins_At_Midnight_On_The_First_Where_The_Website_Is`,
`A_Day_Nobody_Visited_After_The_Clocks_Change_Still_Begins_At_Midnight` and `TrafficReadTests`
`A_Week_With_No_Visits_After_The_Clocks_Change_Still_Begins_On_Monday`; and, placed in Santiago or
Cairo, whichever skipped midnight more recently, and both failing against a fill of instants,
`TelemetryQueryTests`
`A_Day_The_Clocks_Begin_After_Midnight_Is_Still_One_Day_And_So_Is_Every_Day_After_It` and
`TrafficReadTests`
`Every_Kind_Of_Visit_Is_Counted_Beside_The_Same_Days_Where_The_Clocks_Skip_Midnight`.
That the dashboard begins a day where the engine does is `period.test.ts` "begins a day whose
midnight the clocks skip when they resume" and "begins a day the clocks go back to midnight on at
its first midnight". That a week's visitors are not its days added up is
`A_Week_Can_Hold_Fewer_Visitors_Than_Its_Days_Added_Up`, and that the caption never says they are is
`traffic-chart.test.tsx` "keeps calling a week's visitors daily visitors" and "says weeks run Monday
to Sunday, and that somebody who comes back counts again". That the engine answers the two words and
refuses others is `SiteReadTests` `A_Series_Is_Answered_In_Weekly_And_Monthly_Buckets`,
`A_Series_Of_Days_Weeks_Or_Months_Can_Cover_More_Than_A_Month` and
`A_Bucket_Size_This_Product_Does_Not_Cut_Is_Refused_By_Naming_The_Ones_It_Does`, with
`endpoints.test.ts` "asks for the period cut by the week, and reads the weeks back". That the cut
follows the period's length is `period.test.ts` "offers nothing to choose on a period of two days
or less", "offers only days below four weeks", "offers weeks from four weeks", "offers months from
ninety days", "is by the day from three days upwards, until somebody chooses otherwise" and "falls
back to the period's own way where the choice is not offered"; that it travels in the address and is
remembered is `use-chart-by.test.tsx` "travels in the address as a new entry in the history", "is
remembered once chosen", "leaves the address plain for the period's own way" and "lets a link name
the way over the one remembered", with `dashboard.test.tsx` "remembers how the chart was cut",
"falls back to days on a period too short for weeks, and keeps the choice" and "opens on the cut the
address names". That a way the period cannot show is offered as the period that can, and leaves the
reader where they are, is `traffic-chart.test.tsx` "offers every way, naming the period a way this
one cannot show moves to", `period.test.ts` "moves to the shortest running period that can be cut by
the %s" and `dashboard.test.tsx` "offers weeks from a short period by moving to the last 30 days,
without a pointer", which holds that focus stays on the list and that the change is said. That a
bucket is written as its days is `bucket-names.test.ts` and `buckets.test.ts` throughout, with
`traffic-chart.test.tsx` "writes a week cut by the period as only its days" and "heads the card
with the week's dates in full". That a week or a month cut short is drawn quietly, and said to be
only where it is, is `wash.test.ts` throughout, with `traffic-chart.test.tsx` "draws the weeks the
period cuts short fainter, at either end", "leaves three months cut short at both ends at full
strength as an area, and says nothing of them", "fades the months the period cuts short as columns,
and says so", "quietens weeks cut short and weeks still being judged without washing a stretch
twice" and "says nothing about weeks cut short where the period holds only whole ones". That the
dashed line is set week beside week and month beside month is `period.test.ts` "sets the dashed
lines on the same weekday when cut by the week", "moves a quarter back by thirteen whole weeks",
"sets the dashed lines on the same day of the month when cut by the month" and "never covers days
this period also covers", and that it is counted as far into its last day as the period is, "ends
on its last day at the hour this period has reached on its own", "covers no more of its last day
than this period has of its own" and "is the whole of the earlier stretch once this period has
finished", with `dashboard.test.tsx` "asks for the earlier weeks lined up week for week" and, for
the moment it is worked out from, "keeps the earlier weeks lined up with the picture when it is cut
another way later". That a press lands on the bucket's days is `traffic-chart.test.tsx` "narrows to
the days of a pressed week", "offers the same week from the row of the table" and "takes the
picture of who came into the days of a week as well", with `dashboard.test.tsx` "narrows the whole
screen to the days of a pressed week". That nothing reaches into days no longer kept is
`period.test.ts` "hides the comparison when last year's same days are no longer kept", "measures a
stretch against the days before it only while every one of them is kept", "never draws the earlier weeks from days no longer kept, while the cards
still compare", "is the first midnight a whole day after the oldest report still kept", "calls a
stretch too old before it calls it too long", "calls a stretch wholly older than the first day still
kept too old", "opens an old link on the first days still kept" and "begins this year on the first
day still kept on the last day of the year in %s", with `dashboard.test.tsx` "hides the comparison
when last year's same days are no longer kept" and `period-picker.test.tsx` "refuses a first day
older than what is kept, and says which day it can be", "names the day both ends have to move to
when the whole stretch is older than what is kept", "offers no day older than what is kept" and
"opens on this year without a refusal on the last day of the year"; that the window is as long as
the store keeps reports is
`vocabulary.parity.test.ts` "keeps %s exactly as long as the dashboard believes", read against the
expiry of each table a period is counted from. That a stretch is called this month, last month or
this year is `period.test.ts` "is This month when it runs from the first of the month to today", "is
Last month when it is the whole of the month before", "is This year when it runs from the first of
January to today" and "names a single day before a month", with `dashboard.test.tsx` "names a
pressed month that is last month as Last month" and `period-picker.test.tsx` "comes back as Last
month when the whole of it is typed".

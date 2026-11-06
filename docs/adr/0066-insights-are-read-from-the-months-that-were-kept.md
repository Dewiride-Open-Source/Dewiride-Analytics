# 0066 — Insights are read from the months that were kept

- **Status**: accepted
- **Date**: 2026-11-06
- **Applies to**: both editions, and the whole of it is in the free product. Adds one endpoint and one
  screen, and reads what [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md) already
  keeps: no migration, no new statement, nothing collected and nothing retained that was not already.
  Nothing in `integrations/` or in the tracker changes, because nothing about what is captured
  changes, and nothing in `ee/` moves.
- **Follows**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose months this
  reads through the same `KeptHistory`, and
  [0025](0025-one-number-for-the-screen-and-the-bill.md), whose rule that one figure is counted by one
  arithmetic holds this screen's totals to All time's.
- **Extends**: [0040](0040-what-a-screen-remembers.md), with a screen the population travels to and
  the period does not, since it has none, and
  [0041](0041-one-population-for-the-whole-screen.md), whose one population for the whole screen, and
  whose rule for saying why there are no people, apply here.

## Context

The overview and the User journey answer for a stretch somebody chose. The questions an owner asks about
a website's whole life have no stretch: which day was the best we ever had, when are we read, how did this March
go against last March. 0064 keeps one row per website per month until the website is removed, and each
row already holds its busiest day and how its reading fell across the week and the day, because a
month that froze before this screen shipped could never have gained them.

The name sits beside the roadmap's "Explanations and digests", which is about sentences written about a
change. This screen writes none. It lays out what the record holds and says nothing about why.

## Decision

**One reading, one answer.** `GET /api/sites/{id}/insights[?only=people]`, beside All time's three in
`KeptHistoryEndpoints`, answers everything on the screen from one reading, through the same `ReadAsync`
and `KeptHistory.Resolve`. Its page views and daily visitors are All time's to the unit, and its first
day, its `completeTo`, its `missing` and its `unjudged` mean what they mean there, so Insights and All
time never disagree about one website.

**The best day is worked out, not recorded.** The operator asked for one record, kept and updated. A
record that can only rise would go on naming a day the rules no longer support once a month still held
is counted again, so it is worked out on every reading instead: the busiest of the months' best days,
and of two as busy, the earliest. It can fall, and when it does that is the correct answer.

**How reading falls across the week and the day.** A kept month gives its stored weekday and hour
figures, added place by place. A month answered from its reports gives its daily and hourly series
folded on the website's clock, which are the cuts `Month_Count` keeps, by the same arithmetic. Those
series run from the first month answered from its reports to the
present, a stretch that can hold a month that was kept, and only the months answered from their reports
are read from them, so no day is counted twice. Two hours the clocks show alike, as they go back, are
one hour of the day.

A share is of its own figures. Pages delivered are the larger of what the tracker and the server saw,
worked out afresh in each cut, and the larger of two sums is not the sum of the larger. Where the two
halves of the measurement report a page on either side of a bucket's edge, it can count once in the month
and once in each of the days or hours, so a month's weekdays can add up to more than its page views, and
its hours too; a share of the month's total could pass the whole. Every weekday or hour sharing the most is busiest, in
order, and the screen names them all. Nothing read names none, rather than Monday at midnight, which is
where the index of the largest of nothing would land.

**A month's row.** `months` holds every month from the first to the one running now, with no gaps, each
with its page views and the days they cover. An average per day divides one by the other: from the
website's first day, up to and including today in the running month, so a website first measured on the
nineteenth is not averaged over days nobody was counting. A year's average is its views over its days,
taken across the months with a figure. A month with no figure carries none and covers no days, and
`missing` or `unjudged` says why: it is drawn as "Not counted" or "Not fully judged", never as nought,
and it makes no hole in a year's average. A year with no figures at all shows none.

**People.** The screen is kept to people by the switch the overview has and remembered as the overview
remembers it (0040, 0041). It asks about everybody as well, which is what tells a website nobody has
been to from one in which nobody was judged a person. Where people come to nought it asks who came
across the record, as the overview does, and says which of two things is true: nothing has been judged
yet (everything is still being judged, where not one month has settled), or no visit so far has been
judged to be a person. The overview has a third, nobody at all over a stretch judging has finished
with. A whole record never reaches it, because the month running now is still open to judging, so it
never has a sentence here. The words are this screen's own, because the overview's say "this period"
and this screen has none. People are not called absent while months are left out of their figures,
because nought then is only what was counted. Above the figures the screen says the newest visits are
still being judged only where the figures are more than a day behind. A visit is held open until it has
been quiet for the idle timeout, twelve hours at the longest an installation may set; one still going
holds back only itself (0054); and judging then runs within the hour. So a day is beyond anything those
settings produce: judging has fallen behind, or a month kept before judging had finished with it is
waiting for its next count, which an installation may space up to a day apart or switch off.

**The screen.** Insights sits between Overview and Live, and carries the population from one screen to
the next but not the period. The best day takes a row of its own, with the weekdays and the hours in
two strips of bars below it and their figures in a table a reader can open. The months and years are a
real table, its first column and its last sticky, scrolling inside its own card, newest year first; on
a phone each year is a card of twelve shaded tiles built from the same model. A month left out is
hatched and named in its cell, and its key appears only where one is on screen; the running month is
outlined and says "so far". Shading is one violet ramp in five steps on a square-root scale, so a
growing website's early months spread over the quieter steps, and a year's figure is neither shaded
nor counted in the scale, because it would set the scale for everything else. Every figure stands at
4.5 to 1 or better against its shade in both themes, which a test works out from the stylesheet. These
are HTML, not a chart: a table is its own accessible representation, nothing here has a category axis,
and no second chart runtime is involved.

While an answer is on its way the screen draws its own frame, with stand-ins in place of the figures. Its
section headings, switches and tiles and the line that unfolds the figures are the same elements
throughout, so the keyboard stays on them when the figures land, and a stand-in never holds it itself. The
cards keep their places but for what only the answer can say: a month left out, the date in the caption, a
longer day, several days or hours sharing the most, more years. The switch for people works in that frame,
before the website is known, and remembered people are written into the address only once it is: a choice
made sooner stands, and the way back from it is never written over.

**A refusal settles the screen.** Where an answer could not be had the screen says so, and nothing goes
on moving beneath the notice as though a figure were on its way. The overview's headline cards did, under
every period; they show the mark for no figure the same way, until asking again is answered.

## What it does not do

- **Say anything about posts.** Those are read from a sitemap under a later record.
- **Explain or compare.** It shows this March above last March and leaves the reading to the reader.
- **Keep anything by the day past twelve months.** A month's best day, and on the first month the day
  the website was first measured, are the only days a kept month names, which is why "best day ever" can
  be answered and "the third-best day" cannot (0064).
- **Give a month read beside another exactly what it will be kept as.** A month is counted alone when it
  is kept, and read in one stretch with the months after it while it is answered from its reports, and a
  server's reports are linked to a browser's only inside the stretch read. Where the two halves of the
  measurement report under different keys and the only pair linking them fell across the month's edge,
  the month can read a few units differently until it is kept. That is the quarter of an hour after a
  month ends, or for as long as counting is switched off; the month running now is otherwise alone in its
  stretch, and is counted as it will be kept.
- **Move a frozen month to a new zone.** A month counted again after a change of zone moves with it.
  One whose reports have gone keeps the weekdays and hours it was counted on, and they are added up as
  they are, within the limit 0064 states.

## Consequences

- A best day ever can change without anything new happening, when a month still held is counted again.
  That is 0064's bargain, and the first place a reader can see it.
- One reading asks the store for the kept months and four series over the stretch answered from its
  reports, for each population on screen. That stretch is normally the running month. Where counting has
  been switched off it can reach the whole twelve months held, the one place an hourly series runs past
  the 31 days the series endpoint allows. The framework gives up on a forwarded request after 30 seconds,
  too soon for a reading of that size on a busy website, so the dashboard waits two minutes
  (`experimental.proxyTimeout`); a reading that takes longer than that says it could not be had, and
  counting being switched on is what cures it.

## Tests that make this true

The dashboard:

- `insights.test.tsx`: "names the screen and how far back it reaches", "shows all-time page views and
  daily visitors", "names the best day ever with its page views", "names the busiest weekday and hour on
  the website's clock", "publishes those figures as a table", "shows every row of the figures when they
  are opened, with no box to scroll", "names each bar to whoever points at it", "names every weekday and
  hour sharing the most", "lets a line end between the hours it names, and never inside one", "names no
  day and no time where every month is left out", "lays the months out newest year first and totals each
  year", "switches the same table to an average per day", "writes an average too small to round as its
  first digit, and a true nought as nought", "shades a busier month more strongly", "tells nought from
  before measuring and from still to come", "marks this month as so far", "names the scroll region and the
  table", "says in words why a month has no figure, and leaves it out of its year", "tells a month kept
  before its visits were all judged from one never counted", "gives a year with no figures a mark rather
  than nought", "lays each year out as a card of twelve tiles for a phone", "keeps the words of the legend
  from a screen reader", "leaves out the keys to what forced colours do not draw, and keeps the rest",
  "asks for people's figures and says so once", "keeps how the months are read and the open figures
  through a first press of People only", "keeps the keyboard on %s when %s", "says once that the newest
  are still being judged where judging is two days behind", "says nothing of judging where it is %s
  behind", "asks once under everybody", "opens on the people when they were chosen last", "offers everyone
  back when nobody was a person", "says there are no people yet where every visit judged was something
  else", "says no people yet however far judging has reached", "says nothing has been judged yet where
  judging has reached none of the visits", "says everything is still being judged where not one month has
  settled", "does not call the people absent while months are left out of their figures", "says what went
  wrong where who came could not be had", "keeps saying no people yet where asking who came a second time
  fails", "offers the tracking code before any visit", "says the same kept to people, since a website
  nobody has visited has no people to miss", "says what went wrong when the engine refuses", "holds the
  record’s place, in its own words, while it is on its way", "keeps the keyboard out of the months while
  their figures are on their way", "stands in for the best day with a day about as long as most are
  written", "draws the same screen before the website is known".
- `use-remembered-address.test.tsx`: "forgets nothing where it only reads and its address falls silent",
  "adds no entry for %s", "seeds what was remembered when nobody chose", "lets a choice stand", "does not
  write a choice back over the way back from it", "does not bring back what was remembered after it was
  turned off".
- `all-time.test.ts`: "tells a month before the record from one measured, the one running and one to
  come", "names a month left out by why it was, as the answer says", "averages a month over the days
  measured, not the days it has", "gives no figure to a month left out, however it is read", "totals a
  year over the months with figures", "averages a year over the days its figures cover", "gives a year
  with no figures in it no figure of its own", "shades against the largest month, never against a year".
- `heat.test.ts`: "is nothing for nought", "is the strongest step for the busiest", "never falls below the
  first step above nought", "spreads the quieter figures over the quieter steps by their square root", "is
  defined for both themes at step %i", "keeps its ink readable against its shade at step %i, in both
  themes".
- `figures.test.tsx`: "shows every row where the screen says the table is to be read through whole",
  "opens as the screen says, and tells the screen when the reader folds it".
- `ranked-list.test.tsx`: "writes %i of %i, too small to round, as its first digit: %s", "writes %i of %i,
  just short of the whole, as just short of it: %s".
- `app-header.test.tsx`: "offers Insights between the screens", "hands the population, and not the period,
  to Insights (%s)", "marks Insights while it is the screen being looked at".
- `period.test.ts`: "writes only what it is given".
- `endpoints.test.ts`: "reads a month with no figure as nothing rather than as nought", "refuses an answer
  whose week or day is the wrong length".
- `dashboard.test.tsx`: "settles the cards on no figure when their figures are refused, until asking again
  is answered", "keeps the cards standing in while the people’s figures are on their way, where only
  everybody’s record was refused".
- `patience.parity.test.ts`: "waits longer than the engine waits for one statement to its store".
- `judging.parity.test.ts`: "is beyond the longest a visit may stay quiet and the longest wait between
  runs put together".

The engine:

- `KeptInsightsTests`: `The_Best_Day_Ever_Is_The_Busiest_Kept_Day_And_The_Earliest_Of_A_Tie`,
  `A_Record_Can_Fall_When_Its_Month_Is_Counted_Again`, `Every_Weekday_Sharing_The_Most_Is_Busiest`,
  `Every_Hour_Sharing_The_Most_Is_Busiest`, `Nothing_Read_Has_No_Best_Day_And_No_Busiest_Day_Or_Hour`,
  `A_Month_Answered_From_Its_Reports_Is_Folded_On_The_Websites_Clock`,
  `Two_Hours_The_Clocks_Show_Alike_Are_One_Hour_Of_The_Day`,
  `A_Day_Of_A_Kept_Month_Is_Not_Counted_Again_From_The_Reports`,
  `A_Day_From_The_Reports_Takes_The_Record_Only_By_Beating_It`,
  `A_Kept_Month_Covers_The_Days_It_Was_Measured_On`, `The_Month_Running_Now_Counts_Today_Among_Its_Days`,
  `Today_Is_The_Day_It_Is_On_The_Websites_Clock`,
  `A_Finished_Month_Answered_From_Its_Reports_Covers_Its_Days_From_The_First_Day`,
  `A_Month_Never_Counted_Adds_Nothing_And_Covers_No_Days`,
  `A_Month_Whose_People_Were_Never_Settled_Adds_Nothing_To_An_Answer_About_People`,
  `A_Record_With_Nothing_In_It_Says_Nothing`.
- `InsightsReadTests`, against real stores: `Insights_Read_The_Kept_Months`,
  `Insights_Say_The_Same_Whether_A_Month_Was_Kept_Or_Not`, `Insights_Agree_With_All_Time_To_The_Unit`,
  `Insights_Say_How_Far_They_Are_Complete`, `Insights_Of_People_Count_The_Reader_Alone`,
  `The_Grid_Carries_Days_Measured_For_Every_Month`, `A_Month_That_Was_Never_Counted_Has_No_Figure_And_Is_Named`,
  `A_Month_Kept_Before_Its_Visits_Were_All_Judged_Is_Named_Among_People`,
  `Insights_Before_Anything_Was_Measured_Say_So`, `Insights_Refuse_Anybody_But_Everybody_Or_People`,
  `Insights_Of_A_Website_Somebody_Cannot_See_Are_Not_Found`.

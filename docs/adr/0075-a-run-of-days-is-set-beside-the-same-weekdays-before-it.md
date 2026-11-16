# 0075 — A run of days is set beside the same weekdays before it

- **Status**: accepted
- **Date**: 2026-11-16
- **Applies to**: both editions, and the whole of it is in the free product. Dashboard only: the
  engine answers the same questions in the same shapes over other windows; no endpoint, column,
  migration or index is added, and nothing new is collected or kept. The catalogue's words under
  `dashboard.metrics.against`, its sentence for a rise from nothing and the name of the chart's
  Compare control change. Nothing in `integrations/`, the tracker or `ee/` changes, because nothing
  about what is captured changes.
- **Extended by**: [0076](0076-somebody-chooses-which-earlier-days-a-period-is-set-beside.md), which
  lets a run of days be set beside the day before or the same weekdays four weeks earlier as well as
  the stretch this record gives it, names every stretch a period is offered through
  `comparisonsFor` in place of `measuredAgainst`, and renames two of its tests.
- **Revises**: [0042](0042-a-day-in-the-picture-is-a-period.md), whose "**The period before
  follows.** A single day is measured against the day before it, and the cards say so in the words
  they already use for a chosen stretch of one day." is given up: a single day is measured against
  the same day a week before, and its cards say "the same day a week earlier". Its test "lands on
  this period's day rather than the earlier one's, with the period before drawn behind it" is now
  "…with the earlier stretch drawn behind it".
- **Revises**: [0049](0049-a-day-is-called-what-the-reader-calls-it.md). Four of its statements are
  given up: that "the cards on a pressed today use Today's own words and say they were measured
  against yesterday — a pressed yesterday's cards read "the day before" either way"; the reason that
  a chosen single day and the named period of the same date "are set beside the same stretch before
  them, because both step back by their own length"; that "a pressed yesterday covers the window its
  date did and is set beside the day before it; a pressed today covers today up to the top of the
  running hour and is set beside the same part of yesterday. The cards on Today say they were
  measured against yesterday, and the cards on Yesterday against the day before."; and, of the
  earlier figure a hover card carries, "On a week that is the same weekday a week earlier; on most
  other periods it is another weekday". Its first consequence holds: a pressed today and Today are
  both a run of one day, are set beside the same stretch by the same rule, and give the same
  figures. Its test "names a pressed day that is today where the website is as Today, measured
  against yesterday" is now "…measured against the same day last week".
- **Extends**: [0063](0063-the-picture-is-drawn-by-the-day-the-week-or-the-month.md). By the day its
  dashed line is still the stretch the cards are measured against, which on a run of days is now
  also the stretch its week cut draws. By the month the line moves back as many whole months as the
  period reaches into. Its "The cards keep their own stretch, and the line says which days it is" now
  separates the two only on a picture cut by the month and on a month cut by the week. Of its tests,
  "never draws the earlier weeks from days no longer kept, while the cards still compare" describes a
  case that can no longer arise and gives way to "never draws the earlier months from days no longer
  kept, while the cards still compare"; "hides the comparison when last year's same days are no
  longer kept", in `period.test.ts` and `dashboard.test.tsx`, is now "measures this year against
  nothing, since last year's same days are never all kept"; and "measures a stretch against the days
  before it only while every one of them is kept" is now "measures a stretch against the same
  weekdays before it only while every one of them is kept".

## Context

The owner said on 2026-11-16 that the comparison "doesn't have any intelligence". Every run of days
was set beside the same number of days immediately before it (`byLength` in
`frontend/apps/web/src/lib/analytics/period.ts`). Seven days, and a stretch somebody chose that was a
whole number of weeks long, landed on the same weekdays; nothing else did. On Monday 16 November
"Last 30 days" ran from Sunday 18 October and was set beside Friday 18 September to Saturday 17
October, so every weekend of the period was read against two weekdays of the earlier one, and a
website whose week has a shape — a blog that reads less at the weekend and more on a Monday —
showed that shape as a rise or a fall that never happened. Today could only be set beside
yesterday, which on a Monday is a Sunday.

Analytics products line days up by the day of the week for exactly this reason. Google Analytics
offers "Previous period (match day of week)", which "displays current range data versus previous
range data, starting the same weekday", and moves a two-day range back a whole week
(<https://support.google.com/analytics/answer/13412290>).

The owner also asked for a choice of what to compare with — yesterday, the same day last week, the
same day last month — and for the same period last year as an extra. The choice is a decision of its
own. The same period last year is not built: no hosted website has a year of history before 19 August
2027, and a day's figures are kept for twelve months (0064), so answering it needs each website's
day-by-day figures kept for longer, which is a change to that envelope and a decision of its own.

## Decision

1. **A run of days is set beside the same weekdays the fewest whole weeks earlier that clear it.**
   Today, yesterday, the last 7, 30 and 90 days and any stretch somebody chose — N days — are set
   beside the N days that begin 7·⌈N/7⌉ days earlier: a day beside the same day a week before, the
   last 7 days beside the 7 before them, the last 30 beside the 30 that end five weeks earlier, and
   the last 90 beside the 90 that end thirteen weeks earlier. Every earlier day falls on the weekday
   of its partner, and none is a day of the period itself. Moved back by the nearest whole week
   instead, the last 30 days would be set beside days that end two days into them, and a period is
   never measured against days it also counts. On Monday 16 November in Kolkata:

   | Period       | Its days                | Set beside              | Until now               |
   | ------------ | ----------------------- | ----------------------- | ----------------------- |
   | Today        | Mon 16 Nov              | Mon 9 Nov               | Sun 15 Nov              |
   | Yesterday    | Sun 15 Nov              | Sun 8 Nov               | Sat 14 Nov              |
   | Last 7 days  | Tue 10 – Mon 16 Nov     | Tue 3 – Mon 9 Nov       | the same                |
   | Last 30 days | Sun 18 Oct – Mon 16 Nov | Sun 13 Sep – Mon 12 Oct | Fri 18 Sep – Sat 17 Oct |
   | Last 90 days | Wed 19 Aug – Mon 16 Nov | Wed 20 May – Mon 17 Aug | Thu 21 May – Tue 18 Aug |

2. **A month is set beside the month before, date for date.** This month is set beside the same
   dates of last month, and last month beside the whole of the month before, as they were. A month
   is read as a month: "the first to the sixteenth of November against the first to the sixteenth of
   October" is what somebody looking at a month asks, and a month moved back by whole weeks would be
   neither last month nor its dates.

3. **This year has nothing to be measured against.** Its only fitting stretch, the same days a year
   earlier, always begins before the first day a website still holds, so 0063 already showed it no
   rise or fall. It now has no rule at all — `hasEarlier('this-year')` is false — and gains one only
   with the decision that keeps day-by-day figures past twelve months.

4. **Every earlier window is cut on its own last day.** A period still running is set beside its
   earlier stretch up to the same moment of the stretch's last day as the period has reached on its
   own; a finished one beside the whole of its earlier stretch. For a day still running this is the
   cut by elapsed hours it replaces, and a finished day is set beside the whole of the day it is set
   beside, twenty-three hours long or twenty-five where the clocks changed. It settles a month set
   beside a longer one: a finished November is set beside every day of October, where the cut by
   elapsed time stopped a day short of the thirty-first the days written for it name. And where the
   earlier stretch's last day was pulled back to the end of a shorter month, that day is set beside a
   day of the period already over — the twenty-eighth of February beside the twenty-eighth of March,
   while the thirty-first is running — so the whole earlier stretch is counted.

5. **The dashed lines follow the cards.** By the hour and by the day they are the cards' stretch,
   and on a run of days so they are by the week, which already steps back by whole weeks. By the
   month they move back as many whole calendar months as the period reaches into, so each earlier
   month runs over the same days of the month as the one it is drawn beside; ninety days from the
   first of March are measured against days from the thirtieth of November and drawn beside December
   to February, where counting from that first day would move the line four months back and leave
   February out. A line is drawn only where the cards have a stretch to be measured against. The
   control that draws it is named after what it draws: "Compare with" the cards' words where the line
   is their stretch, and with the line's own days where a picture cut by the month, or a month cut by
   the week, draws other days.

6. **The cards name the stretch in words that are true of it.** `measuredAgainst` in
   `lib/analytics/period.ts` names it by what the period is called and by how far back the stretch
   was moved, never by its dates, and the words are the catalogue's: Today "the same day last week",
   the last 7 days "the same days last week", this month "the same dates last month", last month "the
   month before", and every other run of days "the same day" or "the same days", "a week" or "N
   weeks", "earlier" — "the same days 5 weeks earlier" for the last 30 days. A stretch of today's date
   that somebody chose stays a stretch they chose, as 0049 decided, and reads "the same day a week
   earlier". A rise from nothing reads "Up from none vs" those words, which holds for every phrase
   where "Up from none in yesterday" did not, and is no longer than the sentence for a rise, whose
   room the cards keep while their figures are on their way.

## What this deliberately does not do

- **No same period last year.** It needs day-by-day figures kept past twelve months — a change to
  0064's envelope, and a decision of its own.
- **No weekday line-up for a month.** This month and last month keep their dates, so a month with
  one more weekend day than the month before reads a little lower at a website that is quieter at
  the weekend.
- **No change to a post's page, and no comparison on Insights, the journeys screen or Live.** A
  post's recent weeks are already set beside the week before, whole Monday-to-Sunday weeks on the
  same weekdays; the other three set no period beside an earlier one, and none starts to.
- **No correction for an hour the clocks add or take away.** A running day is set beside the same
  hours of its earlier day, which end an hour apart on the clock across a change — in London, ten
  hours of Sunday 1 November end at 10:00 GMT and the same ten hours of Sunday 25 October at 09:00 —
  and a finished day beside the whole of its earlier day, an hour longer or shorter.
- **No exclusion of the days before a website was first measured.** A website measured for less than
  a week reads "Up from none" on Today, as one measured for less than a week already did on the last
  7 days.

## Consequences

**A link that draws the comparison draws the new stretch.** `against=before` puts the stretch the
cards are measured against behind the picture, so a link written before this record now draws the
same weekdays rather than the days immediately before.

**Near the first day still kept, a stretch loses its comparison sooner.** A run whose length is not a
whole number of weeks reaches back up to six days further than its own length did, so a stretch
somebody chose close to the twelve-month edge loses its comparison up to six days sooner.

**The earlier figure on a hover card falls on the same weekday** on every run of days, and on the
same date on a month cut by the day.

**On a run of days the cards and the line name the same days** by the hour, the day and the week. A
picture cut by the month, and a month cut by the week, is drawn from days the cards are not measured
against; the caption says which, and so does the name of the control that draws it.

**Tests that make this true.** That a run of days is set beside the same weekdays the fewest whole
weeks earlier that clear it is `period.test.ts` "sets today beside the same day a week before",
"sets yesterday beside the same day a week before", "sets the last 7 days beside the 7 before them,
which fall on the same weekdays", "sets the last 30 days beside the same weekdays five weeks
earlier", "sets the last 90 days beside the same weekdays thirteen weeks earlier", "sets a stretch
somebody chose beside the same weekdays, moved back by the fewest whole weeks that clear it", "moves
a run of %i days back %i days" and "lands every run of days on the same weekdays, and never on a day
of its own", which runs every length from one day to ninety and a hundred and twenty; that a pressed
today is set beside the same days as Today is "sets a stretch of the day that is today beside the
same days as Today". That a month keeps its dates is "sets this month beside the same run of days in
the month before", "sets last month beside the whole of the month before it" and "pulls a date back
to the last day a shorter month has". That this year has nothing to be measured against is "measures
this year against nothing, since last year's same days are never all kept" and "is, with this year,
a named period with nothing before it", with `dashboard.test.tsx` "measures this year against
nothing, since last year's same days are never all kept". That nothing reaches into days no longer
kept is "measures a stretch against the same weekdays before it only while every one of them is
kept", "never draws the earlier months from days no longer kept, while the cards still compare" and
"draws no earlier months where the cards have nothing to be measured against, even where those
months are still kept". That every earlier window is cut on its own last day is "covers the whole of
the earlier stretch once this period has finished", "is cut to the same length as the part of this
period that has happened", "is the whole of the month before once last month has finished, however
long either is", "sets the last days of a month beside the whole of a shorter month before it", "cuts
the same date of the month before at the hour this month has reached", "sets a finished day beside
the whole of a day the clocks changed on" and "sets today in %s at %s beside the same hours of the
same day a week before", which holds London on both sides of its change and Havana a week after a
day whose midnight its clocks skipped. That the dashed lines follow is "sets the dashed lines on the
same weekday when cut by the week", "is settled apart from the days the cards are measured against on
a month", "moves a run of days back by the months it reaches into, and no further, when cut by the
month", "cuts the dashed days as the cards are cut, on a month longer than the one it is set beside",
"draws the whole of a last day pulled back to the end of a shorter month", "draws the whole of a
pulled-back last day on a stretch as long as the period, at %s" and "never covers days this period
also covers", which runs every named period and stretches of nine lengths from 1 to 120 days
by every cut, with `dashboard.test.tsx` "asks for the earlier weeks lined up week for week", where
the cards and the line ask about the same window, and "names the control by the days it draws where
the line is not the cards’ own stretch". That the words follow is `period.test.ts` "names today, the
last 7 days and the calendar months in words of their own", "names any other run of days by how many
days it covers and how many weeks earlier it is set", "names a stretch of the day that is today by
its days rather than as Today", "names nothing where there is nothing to measure against" and "names
some period by every word it has, and by no other"; on the screen it is `dashboard.test.tsx` "says
which way every headline number moved, and against what", "shows no percentage where the days it is
measured against held nothing", which also holds the arrow beside the first line of a sentence too
long for its card, "names a pressed day that is today where the website is as Today, measured against the same
day last week", "names a pressed day that is yesterday where the website is as Yesterday", "narrows
the whole screen to that day, and leaves the way back in the history" and "opens a link to today's
date as that date rather than as Today", with `traffic-chart.test.tsx` "is asked for by the control
beside the rest, which names what it draws". That every word the cards can say is written, and no
other, is `vocabulary.parity.test.ts` "says in words what a period is measured against as %s" and
"names nothing a period is measured against that the screen cannot say".

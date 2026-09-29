# 0049 — A day is called what the reader calls it

- **Status**: accepted
- **Date**: 2026-09-29
- **Applies to**: both editions, and the whole of it is in the free product. Dashboard only: the
  engine answers the same questions in the same shapes, no column, migration or catalogue entry is
  added, and nothing new is collected. Nothing in `integrations/` or `ee/` changes, because nothing
  about what is captured changes.
- **Revises**: [0042](0042-a-day-in-the-picture-is-a-period.md), for a day that is today or
  yesterday where the website is. Four of its statements are given up for those two days: that a
  pressed day is written `2026-08-12..2026-08-12` "even when the day is today", because "a pressed
  day is that day and no other"; that the control "now reads 'Your dates' with the day beneath
  it", repeated in its first consequence as "the picker — 'Your dates' and the day beneath —";
  that a pressed day "is remembered as the last period chosen, as any chosen stretch is under
  0040", where a pressed today or yesterday is remembered as the named period instead, and moves
  with the calendar; and that the cards on a single day "say so in the words they already use for
  a chosen stretch of one day", where the cards on a pressed today use Today's own words and say
  they were measured against yesterday — a pressed yesterday's cards read "the day before" either
  way. Every other day is pressed, written, named, remembered and compared as 0042 decided, and
  its first consequence — that a pressed day and a chosen day are the same period — holds.

## Context

The owner asked for two things about the picture at the top of the overview, and they meet in the
same place: what a day is called.

**A pressed day was always "Your dates".** 0042 made a press on the picture a period, written in
the spelling 0028 gives a chosen stretch, so every pressed day reached the period control as a
stretch of the reader's own choosing. For most days that is the only name there is. For the two
newest columns of a week it is the wrong one: the list behind the control has "Today" and
"Yesterday" for exactly those days. 0042 wrote a pressed today as its date deliberately — "today"
names the day that moves, and a pressed day is that day and no other. That is a statement about
what the address holds, and the reader does not read the address. They read the control, which
told somebody who had pressed the newest column of the week that they had chosen their own dates.

The figures were never wrong. A single chosen day and the named period of the same date cover the
same window and are set beside the same stretch before them, because both step back by their own
length, so the cards gave the same numbers under either name. Only the words differed.

**The card a hover opens was headed with the axis's name for the bucket.** "Aug 12" leaves the
reader to work out which day of the week that was, and a rise on a Tuesday is a question about
Tuesday. The owner asked for the weekday written out — "Sunday, September 27" — on the card and
not on the axis. The axis has no room for it at phone width, where a week's days and a day's hours
have to sit side by side. The card is read one bucket at a time, and has the room.

## Decision

1. **A pressed day that is today or yesterday where the website is becomes that named period.** It
   is written `today` or `yesterday` in the address, and the control reads "Today" or "Yesterday"
   with the date beneath it, exactly as though the reader had picked it from the list. Which day is
   today is settled in the website's zone at the moment of the press, so a week drawn before
   midnight and pressed after it names its newest column Yesterday. The day each name stands for
   is read from the same table that says which days a named period covers, so the name a press is
   given and the days that name is then read as cannot disagree. Any other day is written
   `first..last` and reads "Your dates", as 0042 decided. `namedPeriod` in `lib/analytics/period.ts`
   is the one place the choice is made.
2. **A single day chosen in the chooser is named the same way.** A day typed into both boxes that is
   today or yesterday where the website is comes back from the period control as that named period,
   so a day typed in and the same day pressed on the picture stay one period under one name. It is
   settled in the control rather than by each screen, because the journeys screen offers the same
   control and would otherwise call the same day two different things; the chooser inside it still
   reports exactly the dates typed. A stretch of more than one day is left as the dates typed, even
   one a name in the list would also cover: nothing presses a stretch, so there is no second way in
   for it to disagree with.
3. **A day is named when it is chosen, never when it is read.** An address or a remembered period
   that names `2026-08-17..2026-08-17` opens on that date and reads "Your dates", whatever today
   is. A link names the days its sender put in it, and a date sent on is that date rather than the
   reader's today. The naming happens where a reader chooses — the press and the chooser — and
   nowhere that reads a period back.
4. **The card a hover opens is headed with the weekday and the date in full.** A day reads
   "Wednesday, August 12", and "Tuesday, August 11, 2026" once the period runs across a year. An
   hour always carries its weekday and its date — "Tuesday, August 18 at 12 AM" — even on a period
   of one day whose axis writes the hours bare, because the card is read away from the axis that
   would otherwise say which day it is. The heading is written in the website's zone, as the axis
   is, so a bucket counted in Kolkata is headed with Kolkata's weekday wherever it is read. It is
   written in one piece by the reader's language, which puts the parts in its own order and joins
   them with its own words — the "at" of an hourly heading is the language's, not copy of this
   product's — so no catalogue entry is added and a translation has nothing to assemble. It is found
   by where the bucket sits rather than by its name on the axis: an hour is written twice on the day
   the clocks go back, and its name alone cannot say which of the two is meant. The card is kept
   inside the picture. Left to itself it jumps to the far side of the pointer when it would run off
   the right, and on a picture a phone wide the far side is often off the left of the screen.
5. **The axis and the table keep their short names.** The weekday is on the card and nowhere else.
   The axis has to fit a week's days and a day's hours side by side on a phone; the table's first
   column sets its width there, and its row buttons keep "Aug 12, look at this day". The heading is
   taken from the label the charting engine would otherwise carry on the axis beneath the pointer,
   which it keeps off the axis unless asked to show it, so the axis goes on reading the short names.

## Consequences

**The same figures under the name the reader uses.** Nothing is asked of the engine that was not
asked before. A pressed yesterday covers the window its date did and is set beside the day before
it; a pressed today covers today up to the top of the running hour and is set beside the same part
of yesterday. The cards on Today say they were measured against yesterday, and the cards on
Yesterday against the day before.

**Remembered as Today.** Under 0040 the last period chosen is remembered by the browser, and a
pressed today or yesterday is remembered as the named period rather than as its date. Opened the
next morning, the screen reads that morning's today, as it would had Today been picked from the
list, rather than the day that was pressed.

**Links move with the calendar.** A link copied after pressing today or yesterday carries `today`
or `yesterday`, and opens on the recipient's today or yesterday where the website is — a different
day as soon as the link is a day old. Nothing in the product writes a fixed link to either day:
only an address edited by hand does, and sending that date through the chooser again turns it back
into the named day. This is the trade-off accepted. 0042's reason for writing the date is given up
for these two days knowingly, because what the reader sees is the control and not the address.

**A day already remembered as its date stays one.** A period remembered or linked as dates, a
pressed today included, opens as those dates and reads "Your dates" until somebody chooses again,
because nothing is renamed when it is read.

**Nobody reading the table instead of the card is missing a fact.** On a period cut by the day,
the weekday is the one thing the card says that the table does not, and it follows from the date
each row carries. On a single day cut by the hour, the card also names the date, which the table's
rows leave out and the period control above the picture states once for all of them.

**Over a comparison, the heading names this period's bucket.** Where the period before is drawn
behind, the card also carries the earlier period's figure for the bucket in the same place, under
the name the card already gives it as the period before. On a week that is the same weekday a week
earlier; on most other periods it is another weekday, and the heading does not name it.

**A phone reads the card mostly on a single day.** A tap on a week's picture is a press and opens
that day (0042), so the card over a week is rarely what a phone is left looking at. On a period of
one day nothing can be pressed, and that is where a phone reads the heading.

**The live screen's card is kept inside its picture too**, because it shares the frame. Its heading
is its own label, the minute.

**The day the clocks go back heads two hours alike**, as their labels on the axis already are: the
full name of an hour carries no offset from Greenwich to tell the two apart. Each card still
carries its own hour's figures.

**A press in the last moment before midnight** where the website is, on today's column, is named
Today, and the screen that reads the period a moment later may already be in the next day. Picking
Today from the list at that moment lands the same way.

**Tests that make this true.** That a pressed day that is today or yesterday where the website is
becomes that named period and hands the reader to a control that says so is `dashboard.test.tsx`
"names a pressed day that is yesterday where the website is as Yesterday", which also holds that
every question the screen asks is asked again about that day, and "names a pressed day that is
today where the website is as Today, measured against yesterday", which holds the stretch it is
measured against; both run on a clock stood still on the week the engine answers with. That any
other day is pressed as 0042 decided is its "narrows the whole screen to that day, and leaves the
way back in the history", unchanged and run on the same clock. That the name is settled in the
website's own calendar, and only for a single day, is `period.test.ts` "is Today when the day is
today where the site is", "is Yesterday when it is the day before", "is the day itself on any
other day", "leaves a stretch of more than one day as it was, even one ending today", "leaves a
named period as it was", "counts today where the site is once its midnight has passed, whatever
the day is in UTC" and "finds yesterday across the end of a month"; that both places a day is
chosen hand it the website's calendar rather than the reader's is `dashboard.test.tsx` "names a
pressed day by the website’s calendar rather than the reader’s" and `period-picker.test.tsx` "is
named by the site’s calendar rather than the reader’s", each run half an hour past the website's
midnight while the day before is still running where the tests do. That the
chooser names a single day the same way is `period-picker.test.tsx` "comes back as Today when it is
today where the site is", "comes back as Yesterday when it is the day before" and "stays the dates
somebody chose on any other day". That a named period is remembered by its name is
`use-period.test.tsx` "is remembered once chosen, so the next screen opens on it", which a press
reaches through the same choice as the list. That nothing is renamed when it is read is
`dashboard.test.tsx` "opens a link to today's date as that date rather than as Today". That the
card is headed with the weekday and the date in the website's zone, with the year across one and
the day with every hour, while the axis and the table keep their short names, is
`traffic-chart.test.tsx` "names the weekday over a day in the website's own zone, and nowhere
else" — whose bucket is a Tuesday where the tests run and a Wednesday where the website is —
"writes the year there too once the period runs across one", "gives an hour its weekday and its
date, even on a period of one day" and "heads the picture of who came the same way". That the
heading is found by where the bucket sits, falls back to the name on the axis, leaves the axis to
its short names and keeps the card inside the picture is `frame.test.ts` "is headed with the full
name of the bucket under the pointer, found by where it sits", "is headed with the name on the axis
where no fuller one is given", "is headed with the name on the axis where the bucket has no fuller
name of its own", "falls back to the name on the axis where no series is under the pointer",
"leaves the axis to its short names" and "stays inside the picture", with "keeps the pointer each
drawing is read with" holding the pointer each drawing already had.

## Alternatives considered

- **The weekday on the axis, or in the table.** Neither has the room at phone width, and the owner
  asked for it on the card. The date each row of the table carries already settles the weekday for
  anybody who wants it there, and on a day cut by the hour the period control above the picture
  names the date the rows leave out.
- **A comma between an hour and its day.** Two ways give one: writing the day and the hour in two
  pieces joined by a catalogue template, which takes the order and the joining word away from the
  reader's language and hands every translation a sentence to assemble; or a short month, which
  heads an hour differently from a day on the same screen. The language's own joining word is kept.
- **Naming a single day whenever a period is read.** Every link and every remembered period naming
  today's date would open as Today, which renames a date its sender wrote into the reader's own
  today — a different day as soon as the link is a day old — and does it without saying so.
- **Naming a typed day in each screen, or inside the chooser.** Each screen naming it for itself
  leaves one screen free to forget, and the chooser naming it would report something other than
  what was typed. The press is named where it is made, and a typed day in the control both screens
  share.
- **Leaving the card free to move, and looking first.** The engine moves a card that would run off
  the right to the other side of the pointer and holds it inside nothing, so with a longer heading
  on a phone the card running off the left of the screen was certain rather than something to wait
  and see.

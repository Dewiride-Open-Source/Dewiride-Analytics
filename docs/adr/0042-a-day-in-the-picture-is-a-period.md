# 0042 — A day in the picture is a period

- **Status**: accepted
- **Date**: 2026-09-17
- **Applies to**: both editions, and the whole of it is in the free product. Dashboard only: the
  engine answers the same questions in the same shapes, no column or migration is added, and
  nothing new is collected. Nothing in `integrations/` changes.
- **Follows**: [0041](0041-one-population-for-the-whole-screen.md), which made a choice about the
  screen reach every panel on it; this record makes the picture a way of making such a choice.
- **Extends**: [0028](0028-what-a-link-carries.md), whose spelling for a chosen stretch is what a
  pressed day is written in, and whose "changing it leaves an entry in the history" is what makes
  the way back from one the button the reader already reaches for.

## Context

The picture at the top of the overview is where a question starts. A rise on a Tuesday is a
question about Tuesday, and every panel beneath the picture answers it for the whole period
instead: the pages, the sources, the places and the visits are the week's. The way to Tuesday was
the chooser, two dates typed into a dialog for a day the reader was already looking at.

The period is the whole screen's. 0028 put it in the address, so the two screens agree about it
and a link carries it; 0040 remembers it; 0041 put the population beside it. A day pressed on the
picture therefore has nowhere new to go and nothing new to be: it is a period, written the way
0028 already writes a chosen stretch, and everything that reads a period — the picker, the cards,
every list, the journeys screen, the bar's links — already knows what to do with one.

## Decision

**A press on a bucket of the picture narrows the period to the calendar day that bucket falls
in.** The day is the bucket's day where the website is, the same day the label names, written as
`2026-08-12..2026-08-12` — the two days a chosen stretch runs between, which happen to be one. The
whole screen re-reads for it, because the period is the whole screen's, and so does the journeys
screen when the reader crosses to it. It is written this way even when the day is today: "today"
names the day that moves, and a pressed day is that day and no other.

**A day, and not an hour.** A period is a run of whole calendar days. An hour has no spelling in
the address, no entry in the picker, no earlier hour for the cards to be measured against, and no
place in what the browser remembers; the engine cuts a window by the hour only at its running end.
Pressing an hour of a two-day period lands on the day the hour falls in, which its label already
carries. The single day is the floor: on a period that is already one, nothing narrower exists,
and the picture is not offered as something to press.

**The whole column is the target.** From the axis to the top of the plot, whatever the drawing —
columns, a line or an area — because what is being pressed is the day rather than the figure, and
a quiet day is as much a day to look at as a busy one. The point pressed is placed against the
grid and the nearest bucket answers; the axis labels and the margins belong to no day. The press
is read off the surface's own element rather than from the charting engine's element events,
which fire only on a drawn shape and never on the air above a short column. The wash over the
buckets still being judged does not stand in the way: a day still being judged is still a day.

**The table carries the same way in.** A canvas is not a control. The table the same figures are
published in gives each row's bucket as a button that lands on the same day by the same function,
named "Aug 12, look at this day", so a keyboard and a screen reader have the way in a pointer has.
On a phone, where a tap on the graph is that press, the table beneath is also the way to read one
bucket's figures without leaving the period. Where no day can be pressed, the rows read as they
always did.

**The reader is handed to the control that names the day.** After a press the picture is redrawn
for the day and the row of the table that was pressed has gone with the week, so the reading
position moves to the period control, which now reads "Your dates" with the day beneath it: it
says what changed, and it is the way to change it again. The dates under that control are read
out with it, so what a screen reader hears is the day. The page follows only where there was a
position to move — a row the reader had reached — and stays where it is after a press on the
picture, which nothing can hold, so a tap on a phone does not jump the page.

**The way back is the button the reader already reaches for.** A pressed day is written into the
history rather than over it, as 0028 requires of every change to the period, so Back returns the
week the day was pressed from. It is remembered as the last period chosen, as any chosen stretch
is under 0040: a day somebody pressed is a day they chose.

**The period before follows.** A single day is measured against the day before it, and the cards
say so in the words they already use for a chosen stretch of one day.

## Consequences

**A pressed day and a chosen day are the same period.** The address, the picker — "Your dates"
and the day beneath — the cards, every list, the link to the journeys screen and the browser's
memory treat them alike, because they are alike. Nothing that reads a period learns a new form.

**A week drills into a day drawn by the hour, and no further.** One day is cut by the hour, so
the day arrived at is twenty-four columns none of which can be pressed. The picture stops
offering a pointer, and the table stops offering buttons, at the floor.

**A screen reader may press the picture without being told it can.** The picture is announced
as an image with a sentence about what it shows, never as a control, and a keyboard cannot reach
it. Some screen readers nonetheless let the reader activate whatever is under their cursor, and
that press lands on the bucket at the middle of the plot. The table is the way in built for them;
the period control then announces the day that was pressed, and Back undoes it. Telling a real
press from a screen reader's would rest on which events a given reader chooses to send, which is
not a footing to build on.

**The picture's affordance is its cursor, and the cursor means one thing.** A canvas cannot
underline itself. A hand over the plot says a press does something; its absence on a single day
says it does not. The charting engine offers a hand of its own over every column, dot and slice,
whether or not pressing one does anything, and writes it onto the element it draws in on every
move of the mouse — so the surface sets the cursor on the canvas itself, where the engine's cannot
overrule it, and sets it to the plain arrow wherever no press means anything: on a single day, on
the ring, and on the live screen. The table's underlined buckets are the same affordance for
everybody else.

**Tests that make this true.** That the surface reports the bucket under a press and nothing
outside the plot is `chart.test.tsx` "tells its caller which category was pressed, by index",
"ignores a press outside the plot", "measures a press from its own corner rather than the page's"
and "shows a pointer only where a press means something", and that it listens to whoever is
interested now and to nobody once nobody is, "keeps the chart it has when the handler changes,
and presses the new one" and "stops listening once nobody is interested any more"; that a bucket
becomes its day where the website is, on both pictures and under a comparison, is
`traffic-chart.test.tsx` "hands the picture the day a bucket falls in, in the website's own zone",
"lands on this period's day rather than the earlier one's, with the period before drawn behind
it" and "takes the picture of who came into a day too"; that the table carries the same way in is
"offers the same day from the row of the table, for anybody without a pointer", "names the day an
hour falls in, on a period cut by the hour" and "offers a bucket still being judged the same way
in"; that a press past the last bucket names no day is "names no day for a press the surface
places past the last bucket"; that the whole screen re-reads, with an entry in the history, and
the reader is handed to the control that names the day, is `dashboard.test.tsx` "narrows the
whole screen to that day, and leaves the way back in the history" and "narrows the screen from
the picture itself, without moving the page", with `period-picker.test.tsx` "reads those days out
with the control"; that the day is the website's is `period.test.ts` "is
the next day east of the meridian once the evening there has begun". The floor is
`dashboard.test.tsx` "offers no day to press on a period that is already one" and
`traffic-chart.test.tsx` "offers no way into a day when none is offered, and the table reads as
it did".

**What stays as it was.** The population travels with the day, as 0041 made it travel with the
period. The journeys screen's own narrowings are untouched. The live screen draws a picture
nothing can be pressed on, since it is about now rather than about a period. No caption gains a
sentence: the picture is not explained, it is pressed.

# 0076 — Somebody chooses which earlier days a period is set beside

- **Status**: accepted
- **Date**: 2026-11-16
- **Applies to**: both editions, and the whole of it is in the free product. Dashboard only: the
  engine answers the same questions in the same shapes over other windows; no endpoint, column,
  migration or index is added, and nothing new is collected or kept. The catalogue gains
  `dashboard.compare` and three phrases under `dashboard.metrics.against`. Nothing in
  `integrations/`, the tracker or `ee/` changes, because nothing about what is captured changes.
- **Extends**: [0075](0075-a-run-of-days-is-set-beside-the-same-weekdays-before-it.md). Its stretch
  is still the one every period opens on wherever it is still kept, and a run of days may now be set
  beside two more. Its
  sixth point names the stretch through `measuredAgainst`; that is now `comparisonsFor` in
  `lib/analytics/period.ts`, which names every stretch a period is offered, and `previousSpan`,
  `previousWindow`, `comparedSpan` and `comparedWindow` take which stretch as an argument. Whether a
  last day pulled back to the end of a shorter month is counted whole (its fourth point) is decided
  by how the stretch was moved — by whole days or by calendar months — rather than by whether it is
  as long as the period, which a stretch moved by months can be on the last days of a month. Its test
  "sets a stretch of the day that is today beside the same days as Today" is now "…, set beside %s",
  run for every stretch, and "names any other run of days by how many days it covers and how many
  weeks earlier it is set" is now "names any other run of days the day before, or by how many days
  it covers and how many weeks earlier it is set".
- **Extends**: [0028](0028-what-a-link-carries.md). An address now carries which earlier days the
  overview's period is set beside, as `vs`. Links from one screen to another leave it behind.
- **Extends**: [0040](0040-what-a-screen-remembers.md). The browser remembers that choice as
  `dewiride.compared-with`, on the same terms as the period, the view, the comparison and the cut.

## Context

On 2026-11-16 the owner asked for "variety in comparison": today against the same day last week,
against the same day last month, against yesterday — "that's extra capability. But the normal
comparison should be working by days." 0075 made the normal comparison line up by the day of the
week. This record is the extra capability: a day is read against more than one other day, and only
the reader knows which they came for. Against the same day last week to see whether the week is
holding up, against yesterday to see what changed overnight, against the same day four weeks ago to
see where a month has taken it.

The owner settled four questions the same day. Today opens on the same day last week, and yesterday
is a choice (D2). "The same day last month" is the same weekday twenty-eight days back, and the
words say "4 weeks", never "last month" — on the twenty-ninth to the thirty-first, twenty-eight days
back is still the same month (D3). The same period last year is not built now: no hosted website has
a year of history before 19 August 2027, and a day's figures are kept for twelve months (0064), so it
needs each website's day-by-day figures kept for longer — its own decision, owed before August 2027
(D4). Dates somebody types in are not offered as a comparison (D5).

## Decision

1. **A run of days may be set beside three stretches, each only where it means something of its
   own.** `comparisonsFor` lists them in this order: the period's own (0075); the day before, where
   one day back clears the period, which only a single day does; and the same weekdays four weeks
   earlier, where twenty-eight days back clears it. A stretch is listed only where it lands on days
   no stretch listed before it lands on, so a run of twenty-two to twenty-eight days, whose own
   stretch is four weeks back already, is offered that once — and only where it begins on or after
   the first day still kept, so a day near the edge of the twelve months may be offered the day
   before and nothing else. This month and last month are offered their own stretch alone: a month
   is read as a month. This year and all time are offered nothing. On Monday 16 November in Kolkata:

   | Period       | Its own                 | The day before | Four weeks earlier  |
   | ------------ | ----------------------- | -------------- | ------------------- |
   | Today        | Mon 9 Nov               | Sun 15 Nov     | Mon 19 Oct          |
   | Yesterday    | Sun 8 Nov               | Sat 14 Nov     | Sun 18 Oct          |
   | Last 7 days  | Tue 3 – Mon 9 Nov       | —              | Tue 13 – Mon 19 Oct |
   | Last 30 days | Sun 13 Sep – Mon 12 Oct | —              | —                   |
   | Last 90 days | Wed 20 May – Mon 17 Aug | —              | —                   |
   | This month   | 1 – 16 Oct              | —              | —                   |
   | Last month   | 1 – 30 Sep              | —              | —                   |

2. **The choice is carried in the address and remembered by the browser.** `vs=day-before` and
   `vs=4-weeks-before`, written as a new entry in the history, as every choice about what is drawn
   is. An address saying nothing means the first stretch the period is offered — its own, wherever
   that is still kept — and the period's own is never written: choosing it leaves the address plain
   and forgets the choice. The browser remembers the last choice under `dewiride.compared-with`,
   and an address that says nothing is given it on arrival, over the entry the reader is already on
   (0040). A link's choice wins over the remembered one, and a word in the address that names no
   choice — `vs=year` — is read as silence. A period that does not offer the stretch chosen is set
   beside the first stretch it is offered, without the choice being forgotten (`comparisonOf` in
   `lib/analytics/compared-with.ts`): somebody who reads their days against yesterday and looks at
   a month for a moment has yesterday back on the next day they look at. The
   choice is the overview's alone. No other screen sets a period beside earlier days, so the links
   between screens leave it behind.

3. **The control sits beside the period.** At the end of the row of controls, each list as wide as
   its own longest entry, so the header grows no taller for it; the row keeps to the right-hand edge
   wherever it wraps, and on a phone each list takes the whole width. The screen's heading is
   aligned with the top of the row, since both lists carry their days on a second line. It is the
   browser's own list, as the period's is, named
   "Compared with", each entry named the way the cards name it — "vs yesterday" — with the days the
   stretch in force covers printed beneath it and read out with it. A period offered one stretch
   alone has nothing to choose, and the same two lines are written rather than offered, so the
   reader still sees which days the figures are measured against; a period offered none shows
   nothing. Reaching for the list — pressing it, or working it from the keyboard — asks for the
   figures of every stretch it offers, so whichever is chosen is waiting.

4. **Everything that reads against earlier days follows the choice.** The cards' figures and their
   words, the dashed line behind the picture, and the name of the control that draws it: "Compare
   with yesterday". `against=before` in an address now means "draw the stretch the cards are
   measured against", whichever that is. A run offered the day before or four weeks earlier is drawn
   by the hour or by the day, so the line is always the cards' own stretch then.

5. **The period never moves when the choice does.** Every stretch a period may be set beside is
   worked out from the moment the period was, so choosing another one three hours later asks for
   the earlier figures cut at the hour the period had reached when it was chosen, and asks nothing
   again about the period itself.

6. **A card keeps its number while the one it is measured against is on its way.** A card stands in
   until its own number is in and then shows it, holding the line saying how it moved in its place —
   the card's own sentence for a typical rise, laid out and hidden, with a bar pulsing over it —
   until the earlier figure is in too. It never shows a change worked out against days other than
   the ones its words name. Before this record a card waited for both figures; choosing other
   earlier days would then have blanked figures the reader was already looking at.

7. **The words.** Today: "the same day last week", "yesterday", "the same day 4 weeks ago". Any other
   single day: "the same day a week earlier", "the day before", "the same day 4 weeks earlier". The
   last 7 days: "the same days last week", "the same days 4 weeks earlier". A stretch of today's date
   that somebody chose is still a stretch they chose (0049) and reads "the day before" rather than
   "yesterday".

## What this deliberately does not do

- **No same period last year.** It needs day-by-day figures kept past twelve months — a change to
  0064's envelope, owed before August 2027 (D4).
- **No dates of the reader's own to compare with** (D5).
- **No choice for a month.** This month and last month are set beside the month before, date for
  date, and nothing else.
- **No choice on any other screen.** A post's page, Insights, the journeys screen and Live set no
  period beside earlier days.
- **No "the days immediately before".** A run set beside days that fall on other weekdays reports a
  website's week as rises and falls (0075), so it is not offered as a choice either.

## Consequences

**A day's figures can be read against yesterday again**, as they were before 0075, but only by
choice, and the words say so.

**The figures on the cards appear as soon as they are in**, on every period, rather than with the
figures they are measured against; the line beneath each waits at the height of the card's
sentence for a typical rise, so nothing below the cards moves when it arrives — save a line, where
the sentence that arrives is a digit or a word away from breaking its line and breaks it apart from
its stand-in.

**Reaching for the list asks the engine for up to two more totals** over windows of a day or a few
weeks, which it answers from the same reports as every other total.

**Tests that make this true.** The offer is `period.test.ts` "offers a single day the same day a
week before, the day before and the same day four weeks before", "offers the last 7 days the 7 before
them and the same days four weeks before", "offers the last 30 and 90 days, and either month, their
own stretch alone", "offers this year and all time nothing", "offers a run of %i days %j", "never
offers a stretch that lands on a day of the period, or two that land on the same days", "offers %s
only the stretches still kept: %j", "measures a period against none of the stretches it is not
offered" and "measures a period against the very days it lists for each stretch"; a pressed today is
set beside the same days as Today, whichever stretch, in "sets a stretch of the day that is today
beside the same days as Today, set beside %s". The windows are "cuts the day before at the hour today
has reached, and takes it whole beside a finished day" and "sets today beside the same hours of a day
before it the clocks changed on"; the dashed lines are "are the days of whichever stretch the cards
are measured against" and "never covers days this period also covers", which now runs every
stretch. The words are "names today, the last 7 days and the calendar months in words of their own",
"names any other run of days the day before, or by how many days it covers and how many weeks
earlier it is set", "names a stretch of the day that is today by its days rather than as Today" and
"names some period by every word it has, and by no other". Which stretch is in force is
`compared-with.test.ts`; how the choice is carried and remembered is `use-compared-with.test.tsx`;
the control is `comparison-picker.test.tsx`; the days beneath it are `written-days.test.ts`. On the
screen it is `dashboard.test.tsx` "what the period is compared with" — "offers today the same day
last week, yesterday and the same day 4 weeks ago, and opens on the first", "offers yesterday the
same day a week before it, the day before it, and the same day 4 weeks before it", "measures today
against yesterday once chosen, as a new entry in the history", "never moves the period, or the hour its
earlier days are cut at, when they are chosen later", "asks for the figures of every earlier stretch
offered once somebody reaches for the list", "asks ahead for the people’s figures where the screen is
kept to them", "keeps the figures on the cards while the ones they are
measured against are on their way", "writes the one earlier stretch the last 30 days are offered,
with its days, rather than offering it", "offers nothing to compare all time with", with "measures
this year against nothing, since last year's same days are never all kept", which now also finds no
control, "opens on the earlier days last chosen, written into the
address over the entry somebody is on", "lets a link name the earlier days over the ones
remembered", "sets the period beside its own stretch where the address names days that are no
choice", "leaves the address plain, and forgets the choice, when somebody goes back to the period's
own", "sets a choice aside on a period that does not offer it, and has it back on one that does" and
"draws the chosen earlier days behind the picture, and names the control by them" — with "shows each
number as soon as it is in, and holds the line saying how it moved until the one it is measured
against is in too", which replaces "draws each number only once the one it is measured against is in
too". That every phrase is written is `vocabulary.parity.test.ts` "says in words what a period is
measured against as %s".

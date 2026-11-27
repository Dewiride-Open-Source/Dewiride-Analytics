# 0078 — Who is here can be kept to one kind of visitor

- **Status**: accepted
- **Date**: 2026-11-25
- **Applies to**: both editions, and the whole of it is in the free product. Adds four counts to one
  of the four live statements and one record to its wire answer; adds no stored column, no migration
  and no index; collects nothing new and widens no retention envelope. Nothing in `integrations/`
  changes, because nothing about what is captured changes, and nothing in `ee/` changes, because the
  hosted site describes nothing on this screen.
- **Extends**: [0036](0036-what-is-happening-now-is-counted-not-judged.md), whose screen gains a way
  to keep its list to one kind of visitor, and
  [0039](0039-what-is-being-read-is-where-the-visitors-are.md), whose recognised groups become the
  first of the two ways.
- **Follows**: [0052](0052-a-row-says-on-its-first-line-how-somebody-arrived.md), which put on each
  row the kind of site that sent the visitor — the value every count here is taken from;
  [0038](0038-a-list-says-what-each-visit-was.md), whose wording for a kind of arrival standing on its
  own ("From a search engine") the chips use; and
  [0028](0028-what-a-link-carries.md) and [0040](0040-what-a-screen-remembers.md), under which what a
  reader chose is written into the address and a narrowing is never remembered.
- **Revised by**: [0082](0082-the-minutes-follow-how-the-visitors-arrived.md). Kept to the visitors one
  kind of site sent, the minute-by-minute drawing is kept to them too; kept to something recognised, or
  to everybody, it still counts everybody. The figures on the card and the pages being read are
  unchanged.

## Context

The live screen named what had been recognised among the visitors here — "A search engine's crawler 3",
"An automated browser 2" — and listed everybody, each row saying on its first line where the visitor
came from. On 2026-11-25 the owner of the hosted service asked for the same thing the overview says of
a period, said of the half hour: how many visitors a search engine sent, how many an AI assistant, and
a way to see only those visitors.

Every row already carried the kind of site that sent the visitor (0052), read from the same value the
verdict reads it from. What the screen lacked was a count over everybody and anything to press. A
reading carries at most the 100 most recently active visitors, so whether a narrowing could be done in
the browser depended on how often a half hour holds more. Measured read-only on the hosted install over
the thirty days to 2026-11-25, in half hours holding any page view:

| Website             | Half hours | Over 100 visitors | Over 50 | Most | 99th percentile |
| ------------------- | ---------- | ----------------- | ------- | ---- | --------------- |
| The busiest         | 1,428      | 2                 | 54      | 105  | 75              |
| The second busiest  | 500        | 4                 | 37      | 187  | 86              |
| Every other website | under 700  | 0                 | 0       | 19   | 9               |

## Decision

**Every chip in the summary can be pressed, one at a time, and pressing one keeps only the list to it.**
The summary holds two groups: "How they arrived", which is new, and "Recognised right now", which is
what 0039 named.
Pressing a chip presses it alone, pressing it again shows everybody, and the list of who is here shows
the visitors that chip describes — a row held open after its visitor has gone among them when it does.
The visitors and page views on the card, what is being read and the minute-by-minute drawing go on
counting everybody: they describe the half hour, and a figure that changed with a filter on another
card would be a second answer to the same question.

**How they arrived is counted over everybody.** Four counts — from a search engine, from an AI
assistant, from a social network, from a link on another site — are taken in the reading of who is
here, in the step that already counts everybody (`gathered`, beside `visitors_seen`), before the list
is cut to its hundred. Each is taken from `from_kind`, the very value a row's "From Google" is read
from, so a chip never counts somebody whose row says otherwise. Nobody is counted twice: a visitor has
one sender in the half hour — the earliest report in it that named one (0052).

**Somebody nothing named a sender for is under no chip.** On this screen that one fact reads two ways —
"Came straight here" where the half hour saw them arrive and "Arrived earlier" where it did not (0052)
— and a chip holding both would keep rows that contradict its own name. The four chips are therefore
not a breakdown of the headline, and are not drawn as one.

**What has been recognised is still counted from the rows a reading carries.** Naming needs the engine
run over each visitor, and the hundred is what bounds that on every beat (0039). Pressing one of those
chips keeps the list to the rows it was counted from.

**The list is narrowed in the browser.** The rows carried are the most recently active, so the ones a
chip keeps are the most recently active of that kind. Where nobody like that is here — an arrival chip
at nought, or a recognised chip at nought on a reading that carried everybody — the foot of the list
says "Nobody like that is here right now." Where a reading was cut short and none of the rows it
carried match, it says "None of the 100 most recently active." instead, and says it of a recognised
chip even at nought: that count covers only the rows carried, so whether anybody further back is that
kind cannot be known. Where the list shows some of them and not all, it says how many of the most
recently active it shows. Narrowing in the engine was considered and refused for what the table above
shows — two to four half hours a month on the busiest websites — at the price of a second question per
chip, a list that redraws on every press, a second rule for rows held open, and no way of applying it
to the recognised chips at all.

**A press is seen and heard where it lands.** The chips are on the card the screen leads with, which on
anything narrower than a wide screen stands three cards above the list, so a press there would change
the list out of sight and look as though it had done nothing. A list kept to something new is therefore
brought into sight wherever its heading is not already on screen clear of the bar across the top, and
the keyboard goes to that heading with it rather than being left on a chip scrolled away; where the list
is in sight, nothing moves. Either way, what the list now shows is said once to anybody listening, from
a status beside its heading: the foot's own sentence where it says one, "Showing 5 visitors." where it
shows every one of them, and "Showing everyone." when the list is widened again. It is said only when
the choice changes — never on a reading, so the half hour does not read itself aloud — and a screen
opening on what its address names neither moves nor speaks.

**The chips stay where they are.** Readings land every ten seconds, and a button that moves between a
reader aiming and pressing is pressed by mistake. "How they arrived" comes first, because its four
chips are drawn even at nought and never come and go: beneath the recognised row, which comes and goes
with the crawlers in the half hour, they would jump a row's height. Recognised chips follow the order
of the category vocabulary, and arrival chips the fixed order search engine, AI assistant, social
network, link, so neither row reorders itself by count. A recognised chip stays on offer at nought
while it is pressed or the keyboard is on it — a reading takes neither the control that shows everybody
again from under the hand that pressed it, nor a control from under the keyboard — and goes once it is
let go and the keyboard has moved on. The chips stay while the list is kept to something, even once
nobody is left in the half hour and only a row held open keeps the screen.

**The words are the ones a kind of arrival already has.** "From a search engine", "From an AI
assistant", "From a social network", "From a link on another site" — the wording 0038 settled for an
arrival standing on its own, so "a search engine" never reads as its crawler. The arrival chips carry
no colour mark: the colour the overview gives search engines is the tone a verdict of machinery is
drawn in, and beside "A search engine's crawler" it would draw a search engine's visitors in its
crawler's colour.

**The address says what the list is kept to.** `?sourceKind=` or `?category=`, spelled as the journeys
screen spells the same narrowings, written over the current entry rather than added to the history,
and never remembered (0040). An address naming a category this screen never names — a person, or one
that cannot be told — keeps nothing, and the screen opens on everybody.

## What it does not do

- It does not narrow the headline figures, the pages being read or the minute-by-minute drawing.
- It does not count "Came straight here" (above).
- It names no new AI assistant. Grok, DeepSeek, Le Chat, Meta AI and You.com send visitors counted as
  links from other sites until the source catalogue lists them, and moving an entry between kinds moves
  verdicts, so it ships with a ruleset (`TrafficSources`), not with a screen.
- It proves nothing about where somebody came from. A referrer is written by whoever asked for the
  page, so a crawler that forges Google's is counted under search engines exactly as its row says "From
  Google"; who it is, is said in the recognised group, not here.

## Consequences

- **Cost**: four window counts over the per-visitor rows the statement already holds, in the window
  step it already has. Still three statements per beat, and the statement's ten-second limit is
  unchanged.
- **A chip's count can be higher than the rows it keeps** whenever a reading is cut short; the foot
  says so whenever it is.
- **The journeys screen's chips change with these.** Both screens draw the shared chip, which now
  writes its count at the strength of its name — at the faded strength the journeys chips had, a
  14-pixel figure fell short of the contrast it needs (3.0:1 in the light theme) — and wears the glow
  every bordered control does.
- **Releasing**: the dashboard reads `arrivals` from every reading, so it ships with the engine that
  writes it. An engine sending it to an older dashboard is harmless. Going back removes the chips and
  changes nothing stored.

## Tests that make this true

- **The four counts**: `AnalyticsSqlCompilerTests.Live_Visitors` (approved
  `AnalyticsSqlCompilerTests.Live_Visitors.verified.txt`) and
  `A_Reading_Of_Who_Is_Here_Counts_Everybody_Before_It_Is_Cut_Short`, which holds the counts inside
  `gathered` — before `GROUP BY visitor_key` closes it, with the list cut from `gathered` and from
  nothing in between — and never spells `'direct'`. On real stores, `LiveTrafficTests`
  `How_Visitors_Arrived_Is_Counted_Over_Everybody_And_Not_Over_The_List` and
  `Somebody_Nothing_Named_A_Sender_For_Is_Counted_Under_No_Way_Of_Arriving`; on the wire,
  `LiveReadTests.A_Reading_Says_How_Many_Each_Kind_Of_Site_Sent`; through the reader,
  `LiveTrafficReaderTests.Carries_How_Everybody_Arrived_As_The_Store_Counted_It`. That the dashboard
  and the engine name the same four kinds: `vocabulary.parity.test.ts` "is accepted whole by the shapes
  the answers are checked against".
- **Which visitors a chip keeps, and what the foot says**: `live.test.ts` "which visitors a kept list
  keeps", "pressing a chip", "whether two choices keep the list to the same visitors", "the chips on
  offer" and "what the foot of the list says", every case; on the screen, `site-live.test.tsx` "keeps the
  list to the visitors a search engine sent once its chip is pressed", "keeps the list to one recognised
  kind of visitor once its chip is pressed", "keeps the list to one chip at a time", "admits a kept list
  is the most recently active when there are more of them", "says nobody like that is here when nobody
  arrived that way", "says when those visitors are further back than the list reaches", "hides a row
  held open while it is not one of those the list is kept to, and keeps it" and "keeps a row held open
  after its visitor left while it is one of those the list is kept to".
- **Counted over everybody, narrowing nothing else**: "counts how visitors arrived over the whole half
  hour rather than the rows listed", and "leaves the figures, the pages and the minutes counting
  everybody", which reads the minutes back from the drawing's own table.
- **Seen and heard where it lands**: "brings the list into sight once a chip is pressed far above it, and
  the keyboard with it", "leaves the page and the keyboard where they are when the list is already in
  sight", "moves nothing when the pressed chip is let go", "says what the list now shows once a chip is
  pressed, and nothing more on a reading", "says when nobody like that is here, and when everybody is
  shown again", "says nothing when it opens kept to what a link names", and "is the only part of the
  screen that announces itself on a reading".
- **The chips stay where they are**: "draws the ways of arriving above what has been recognised", "keeps
  a recognised kind pressed at nought once its last visitor has gone, and under the keyboard once it is
  let go", "keeps a recognised kind the keyboard is on once its last visitor has gone, until the keyboard
  moves on" and "keeps the chips while the list is kept to something, even once nobody is left in the
  half hour"; in `live.test.ts`, "offers all four ways of arriving, always in the same order, noughts
  and all" and "offers each recognised kind of visitor in the place the vocabulary gives it".
- **The address**: "opens kept to what a link names", "keeps the list to the kind of visitor where a
  link names a way of arriving as well", "writes the choice into the address over the entry somebody is
  on, and leaves it plain again", "reads %s as everybody" for five addresses naming nothing this screen
  keeps, and "forgets what the list was kept to once the screen is opened afresh".

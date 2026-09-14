# 0039 — What is being read is where the visitors are

- **Status**: accepted
- **Date**: 2026-09-14
- **Applies to**: both editions, and the whole of it is in the free product. Changes the shape of
  one of the four live statements and one wire record, adds one optional query value to one
  endpoint, and settles one ordering inside a shared fragment; adds no column, no migration and no
  index; collects nothing new and widens no retention envelope. Nothing in `integrations/` changes,
  because nothing about what is captured changes.
- **Follows**: [0038](0038-a-list-says-what-each-visit-was.md), which settled that one visit is
  described in one vocabulary wherever it appears — the list, the opened visit and the live row —
  and is the reason the live screen's panels are held to the same standard of agreement here.
- **Revises**: [0036](0036-what-is-happening-now-is-counted-not-judged.md), whose consequence that
  the pages being read and the half hour's total are "counted per minute and per page by rules that
  agree almost always and not quite always" is withdrawn here for the pages, and whose trail is now
  read under the reading its row came from rather than under a fresh present moment.

## Context

The live screen is one answer drawn four ways: a headline count of visitors, the half hour minute
by minute, the pages being read, and the list of who is here. 0036 built the four from three
statements over one window and one population of reconciled visitors, and said plainly that the
parts would not always agree with each other. Thirty days of real traffic showed the disagreement
was not occasional. It was the ordinary state of the screen.

**The pages panel counted deliveries; the headline counted visitors.** The pages statement summed
page views per page from every report in the window, keyless ones included, and kept only pages
where something had been delivered. The headline counted every visitor key with any report in the
window. So a visitor who arrived before the window opened and was still reading — sending nothing
but progress reports — was in the headline and in the list and on no page. A report that carried no
key was on a page and in no headline. And a visitor who touched three pages inside the window was
counted once by the headline and three times across the pages, so the pages summed to more than the
visitors, most of the time, by an amount that depended on how much people had been moving about.

**The panel's own figure said "visitors", and it was not the number of visitors.** Each row ended
in how often the page had been delivered, drawn as a bar against the busiest page's deliveries, with
a count of distinct visitors beside it as the detail. 0036 refused to end the rows in a share
because there was no honest whole to take one against — which was true, and was also the problem:
a panel on a screen whose one big figure is a count of visitors, ending in numbers that could not be
reconciled with that figure, is a panel a customer stops trusting.

**The headline's two sub-figures were counted from the list, and the list is cut short.** "N
visitors are still being watched" was the number of unnamed rows in a list capped at a hundred. On
a site being swept by three hundred addresses, the headline said three hundred, the badges said a
few dozen recognised, and the watching figure said eighty-odd — three numbers that could not be
added into each other.

**The trail was read over a fresh "now".** A row on the list was drawn from one stretch of
minutes; opening it read the visitor's trail over the stretch ending at the moment the trail was
asked for. A beat later, across a minute boundary, the trail's window begins a minute after the
row's did, and a page the visitor was on in that first minute is counted on the row and absent from
the trail. 0036's own test that the two count a page the same way held; they were counting the
same way over different minutes.

**One shared fragment chose a page's opening sighting by time, where the finished visit chooses it
by surface.** `LiveActivity.WithSources` marked as opening a page the earliest report about it, and
the browser's report can land before the server's. `VisitGrouping` marks the request path's
sighting first, because that is the sighting that knows what the site answered. Under 0037's rule
that a request for a page only an intruder asks for counts as probing only when the site refused
it, a site owner opening their own administration page on a site reported by both halves could be
named a scanner while they were on it — the live reading having taken the browser's sighting,
whose status is nothing — and excused once the visit finished. That is a withdrawal, and a live
name that can be withdrawn is the thing 0036 exists to prevent.

## Decision

**The pages being read are the visitors, grouped by the page each is on.** The statement gathers
the same population the reading of who is here gathers — every reconciled visitor with any report
in the stretch and a key to be counted under — places each on the page their most recent report
named, and counts visitors per page. The page a visitor is on is one expression,
`LiveActivity.CurrentPage`, and it is the expression that prints the page beside their row, so a
visitor is counted under the page they are printed beside and under no other. The visitors across
every page therefore add up to the visitors seen by construction; a list cut short at its cap of
twenty-five pages falls short of the headline by exactly the visitors on the pages it did not list,
which the screen says in one line rather than leaving as a discrepancy. Deliveries are no longer
counted per page. How much was read is the minute-by-minute drawing's question, and it goes on
answering it, keyless reports included, because a delivery nobody could be attributed to is still a
delivery.

**The panel's rows end in a share of everybody here.** Now that the pages have a whole and it is
the headline, each row is drawn against the busiest page and ends in its share of the visitors
seen. The rule 0036 gave for refusing a share — that there was no whole beside the parts — no longer
describes the panel, and the rule it gave for drawing one everywhere else on the dashboard does.

**The headline's sub-figures add up to it.** The named groups are counted from the list, which is
where the names are; the unnamed figure is the visitors seen less the named, never below nought, so
recognised plus watching is the headline however long the list was allowed to be. The screen's
empty state is keyed on the count of visitors seen rather than on the length of the list, for the
same reason, and a row somebody is holding open keeps the screen even when the count has fallen to
nought.

**A trail is read under the reading its row came from.** The endpoint takes that reading's instant
as an optional query value, `at`; the reader reaches back from it exactly as it did for the row,
through the one definition of the window; and the response echoes the instant it was read under, so
an answer says which reading it belongs to. The dashboard files
each trail under the reading it belongs to, so a new reading is a new question, the previous answer
stays on screen until the new one lands, and a row held after its visitor has gone keeps the
instant it was last seen in and is never asked about again. There is no clock of the trail's own:
the beat is the live reading's. Left out, the instant is the present moment, which is what a caller
with no row to sit the trail under means.

**The instant is held to being about now.** No further ahead of the engine's clock than one clock
may be of another — a minute, `LiveTrafficReader.Skew` — and no further back than a visit can reach,
`VisitorKeys.LongestVisit`. A screen somebody has held still may be an hour old and its rows must
still open; a day back is as far as any visit runs and is what the list of finished visits already
answers about. Outside that the request is refused where it arrives, before the site is resolved
and on the same terms as a key that could name nobody, and the refusal echoes nothing that was
written. The reader asks the same question again before it reads, against its own reading of the
clock, with a minute's grace behind the far end so that an instant admitted at the edge is never
refused a beat later inside. A value the framework itself cannot read as an instant is refused with the same status in
every environment: `RouteHandlerOptions.ThrowOnBadRequest` is switched off rather than left to
default to whether the product is being developed, because a refusal whose status depends on where
the product runs is not one a test can hold it to.

**The live reading opens a page with the sighting a finished visit opens it with.** The request
path's sighting sorts ahead of the browser's, then a page view ahead of anything else, then the
earliest — the order `VisitGrouping` uses — so a page both halves saw carries the status the site
answered with while the visitor is here exactly as it will once they have gone, and a rule that
reads that status reaches one conclusion in both places. The pages a visitor is on, and how many,
do not move: only which of two reports about the same page is taken as its opening.

**What stays as 0036 left it.** Nothing is judged, nothing is written, no name server is asked, no
address is selected, every statement gives up within the beat, nothing is read from outside the
window, and a visitor is named only where the naming cannot be withdrawn. The minute-by-minute
drawing is unchanged. The three readings on each beat and the fourth under an open row are the same
four statements.

## Consequences

**The four panels agree, and the agreement is arithmetic rather than hope.** Visitors on the pages
plus visitors on pages the list had no room for equals the headline; recognised plus watching
equals the headline; the pages under an open row are the pages its row counted. The first and the
third are tests against the real store rather than descriptions:
`LiveTrafficTests.Every_Visitor_Seen_Stands_On_Exactly_One_Page_Being_Read`,
`Somebody_Only_Reading_A_Page_Is_On_It_In_Both_Lists`, `A_Report_That_Named_Nobody_Puts_Nobody_On_A_Page`,
and `LiveReadTests.A_Member_Opens_A_Visitor_And_Sees_Where_They_Have_Been`, which now asks under the
reading's instant and asserts the trail echoes it. The second is arithmetic the dashboard does over
one reading, and is a pure-function test without a browser:
`live.test.ts` "counts everybody the list could not carry among those still being watched". The two
statements placing a visitor are held to one expression by
`AnalyticsSqlCompilerTests.The_Pages_Being_Read_Place_Each_Visitor_Where_The_Row_Listing_Them_Does`.

**The pages and the headline are two reads of a store that is still being written to.** The three
readings on a beat are taken one after another, a few milliseconds apart, and a report that becomes
visible between two of them is in one and not the other. The pages can therefore stand a visitor
out from the headline for one beat — one more on another page, or a share a point high — and the
next beat resolves it. The dashboard never draws a negative remainder, and the discrepancy is
bounded by what the store admitted in those milliseconds. Folding the pages into the visitors
statement would not remove it: the store gives no snapshot across two reads of the same table
inside one statement either, and a scalar carrying the pages beside every row would read the
window twice for the same answer.

**The pages panel answers a different question than it did, and the title is the same.** "What's
being read" is now where the visitors are — one visitor on one page — rather than how often pages
were delivered. A page one visitor kept reloading holds one visitor, and a page ten visitors are
reading outranks it, which is the order a reader watching their site wants. Somebody who wants
deliveries per page over a period has the busiest-pages list for it. The caption under the panel
says how the visitors are counted, once, in one sentence.

**The watching figure counts visitors the reading never examined, and says nothing about them.**
The named groups are counted from the hundred rows a reading carries, so on a site being swept by
three hundred addresses the headline says three hundred, the badges say a hundred recognised, and
the watching figure says two hundred — of whom the engine has looked at none. "Still being
watched" claims nothing about them, and "judged once their visit finishes" is true of them, so the
line is honest; it is also less than it could be. The engine could count the named groups over the
whole population rather than the carried rows, at the cost of running the engine over every
visitor on every beat, which the cap exists to bound. That is the lever if a customer asks, and it
is not this change.

**A trail under a stale reading is refused rather than answered about the wrong moment.** A screen
held still for more than a day and then opened onto a row is told to ask under a recent reading.
That is the honest answer — the visitor is long gone and the minutes are on the journeys screen.
The refusals this record adds are that one, an instant more than a minute ahead of the engine's
clock, and a value that is not an instant at all; each is a 400 that echoes nothing.

**A wire shape narrows and a query value is added, and both halves ship together.** The pages row
loses `pageViews`; the dashboard's schema drops it and an engine still sending it is harmless. The
trail gains `?at=`; an engine without it ignores the value and answers about now, and a dashboard
without it is answered about now, so neither side breaks the other during a deploy. The guarantee
that the trail and the row are one window holds only once both sides are the versions recorded
here, which the release shape already ensures.

**The status of a page both halves saw now comes from the server's sighting in the live reading.**
Where the two halves disagree about a page's opening instant, the row's request list carries the
server's instant rather than the earlier of the two; the pages themselves, the count of them, and
everything measured from the browser's reports are unchanged. `Live_Visitors` moved by one line and
was approved after reading.

**What this does not fix, and 0036 already named.** A visitor key rotates at midnight and across a
change of network, so a count of visitors is not a count of people, and this screen is where that
shows most. Nothing has been measured against a busy installation; the pages statement is one
further `GROUP BY` over a population the visitors statement already gathers, and what to watch is
still `read_rows` and `memory_usage` in the store's query log. Being told when something starts
sweeping remains commercial and remains unbuilt.

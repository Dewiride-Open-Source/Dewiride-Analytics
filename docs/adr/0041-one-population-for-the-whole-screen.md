# 0041 — One population for the whole screen

- **Status**: accepted
- **Date**: 2026-09-16
- **Applies to**: both editions, and the whole of it is in the free product. Gives twelve
  statements a second shape and the series answer one field; adds no column, no migration and no
  index; collects nothing new and widens no retention envelope. The metering statement and the
  fragment it shares with the headline are untouched, so nothing in `ee/` moves. Nothing in
  `integrations/` changes, because nothing about what is captured changes.
- **Revises**: [0040](0040-what-a-screen-remembers.md), whose decisions that "the cards above the
  chart are untouched" and that "the people-only figures … will not add up to the page views and
  visitors in the cards above" are given up: every figure on the overview, and the list of
  journeys, is asked of one population. Its decision that the filters on the journeys screen are
  not remembered stands — people-only is not a filter on that screen, it is who the screen is
  about.
- **Follows**: [0014](0014-where-traffic-comes-from.md) and
  [0018](0018-a-page-somebody-reported-reading-is-a-page-they-visited.md), whose per-visitor and
  per-delivery figures are asked here of a population and count it by exactly the arithmetic those
  records settled.
- **Followed by**: [0042](0042-a-day-in-the-picture-is-a-period.md), which makes the picture a
  way of choosing what the whole screen is about, as this record made the population one.

## Context

**The owner asked for the screen, not the chart.** 0040 let the picture be kept to people and left
the cards counting everybody, and said so in a caption. In use the honest split read as a
contradiction — a card saying four thousand above a picture saying nine hundred, on the same days
— and the question the owner brought ("how many people, where from, reading what") had no screen
that answered all of it at once. The product brief asks for exactly that: every major figure
recalculated for the people, and a filter chosen anywhere applied everywhere.

**The engine had no word for it.** "People" existed once, as a category the list of visits could
be narrowed to. Every question about activity — the totals, the series, the pages, the places, the
sources, the devices, the software, the presses, the readings, the rebuilt visits — was asked of
everything the site recorded, and the picture 0040 kept to people was drawn from the judged answer
instead: a second arithmetic, over finished visits rather than reports, which is why it could not
add up to the cards however carefully the caption explained the difference.

**What was needed already existed, in two halves.** A verdict is stored against a visit's identity
with the first and last instants of the activity it was reached from ([0035](0035-a-visit-is-judged-on-all-of-itself.md)),
and every question about activity settles who each report was about before it counts anything
([0018](0018-a-page-somebody-reported-reading-is-a-page-they-visited.md),
[0034](0034-a-visit-begins-where-somebody-arrived.md)). Joining the two is a narrowing of the
reports a question sees, not a new count.

## Decision

**A population narrows the reports a question counts, never the arithmetic.** `Population {
Everybody, People }` is a property of the twelve questions answered from activity, readings and
rebuilt visits, and of nothing else. Asked of people, each is the same statement over the reports
of fewer visits, so every figure asked of people is a share of the same figure asked of everybody
— and a screen kept to people adds up by construction rather than by caption. The questions
answered from verdicts carry no population, because a verdict already says what each visit was
and those answers carry every conclusion for the reader to read by; the account of one visit is
about that visit whoever made it; and what is happening now is counted rather than judged
([0036](0036-what-is-happening-now-is-counted-not-judged.md)).

**A person's report is a report the verdict on their visit covers.** Each visit is reduced to the
newest verdict about it, by the reduction the breakdown of who came uses, so the people counted
here are the people that panel counts. "People" is the one conclusion `LikelyHuman` and nothing
weaker or wider — never a floor on the evidence, because the band is what the reader is shown and
the count must agree with the panel that shows it. A report is kept if its instant falls between
the verdict's first and last instant; verdicts are read from a day before the window, which is as
long as a visit can be, so a visit that began the evening before keeps what it did inside the
window exactly as everybody's count keeps it. This is written once, in `JudgedPeople`, and every
statement asked of people is written through it.

**It is applied after identity is settled.** A verdict names a visit by the key the engine derived
once both halves of the measurement were folded together. A report the site's own server sent
arrives under a key of its own, and follows the person it was about only once reconciliation has
given it the browser's key — so the two expressions that keep the people are placed after the
settled identity in every statement, and the statement's first aggregation reads from them.

**Each statement has two shapes, chosen by what was asked.** On the terms [0029](0029-what-a-visit-was-is-rebuilt.md)
and [0038](0038-a-list-says-what-each-visit-was.md) chose the visit list's two shapes: the
everybody shape is the statement as it always was, reading no verdict and binding no extra value,
and the people shape is that statement with two expressions more and one more value bound — how
far back the verdicts are read. Within each shape the text does not vary. The approved statements
beside the compiler hold both shapes of every question.

**Presses are kept by the same verdict, still without reconciliation.** A press is reported by the
visitor's own browser under the browser's own key, which is the key the verdict names, so the
count of presses joins its raw selection to the people directly and folds nothing together — as
[0012](0012-operated-controls.md) settled.

**The rebuilt visits of people are the visits that were judged.** The activity is narrowed to the
people's reports before it is grouped into visits. A visit dropped only widens the silence either
side of the ones kept, so nothing merges, and the visit the grouping rebuilds is the one the
engine judged.

**The headline and the bill stay one fragment.** The fragment that counts pages delivered, and the
metering statement built from it, do not change ([0025](0025-one-number-for-the-screen-and-the-bill.md)).
A population changes which rows the fragment sees and never how it counts them.

**The wire says who, once, and never repeats it.** One word, `only=people`, on each of the twelve
endpoints. Anything else is refused before the site is resolved, as a grouping the vocabulary
lacks is, and the word the caller wrote is not echoed. Answers do not say who they are about; the
question did. The series answer says how far the judging has reached — `completeTo` — so a
picture of people can wash the buckets still being judged exactly as the picture of who came does:
the end of the window for everybody, because every report counts as it arrives, and an idle
timeout behind the present for people, because a visit is judged once it has been silent that
long.

**The screen asks every question of one population.** The control 0040 put on the chart moves
beside the period picker, on the overview and on the journeys screen alike, and both seed the
address from what the browser remembers under `dewiride.population`; the links between the two
screens carry the population with the period ([0028](0028-what-a-link-carries.md)), and the live
screen and the settings never acquire it. Every card and every list on the overview is asked of the
chosen population, and the chart's picture of how much is drawn from the same arithmetic as the
cards, washed from where the judging has reached. The panel of who came keeps its verdicts and,
kept to people, sets the people against everyone else on its ring, with the rows the people by how
sure. When the period holds nothing judged to be a person the whole screen says so in one place,
with the two facts kept apart — nothing judged yet, and nobody a person — and offers everyone back.
The journeys list is kept to the visits judged to be people, any conclusion the address named is
set aside while it is, and the screen's own narrowings by what a visit was still apply within the
people.

## Consequences

**What a screen kept to people shows adds up.** The table under the chart, the cards, the lists
and the journeys list are one arithmetic over one set of reports, and a share on any of them is a
share of the figure it sits under.

**The people's figures trail the present by the judging.** A visit not yet judged is not a person
yet, however like one it looks, so the newest stretch of a running period holds fewer people than
it eventually will. The series says where that begins, and the picture washes it; the caption
discipline [0036](0036-what-is-happening-now-is-counted-not-judged.md) and
[0039](0039-what-is-being-read-is-where-the-visitors-are.md) hold the live screen to is kept.

**Residuals, named.** A report about a visit that arrived after its verdict was reached — a page
left open and reported on the next morning — is counted for everybody and not for people; that is
the bound the opened visit already reads to ([0035](0035-a-visit-is-judged-on-all-of-itself.md)).
A report carrying no visitor key takes no part among people: it was never judged, because nobody
could be judged from it. A visit judged again and concluded not to be a person leaves the people's
figures on the next read, because verdicts are read where they are stored and never copied. The
visitor key changes at midnight UTC ([0034](0034-a-visit-begins-where-somebody-arrived.md)), so a
person's visit across it is two visits, each judged on its own, and the second half may be one the
engine could not tell — counted for everybody, not for people.

**What it costs.** One indexed read of verdicts per statement, a day wider than the window at its
start, and one hash join on the visitor key whose right side is the judged people of the window.
The activity is still read once. Should the day of reach show on a busy site, the store's query
log is the lever, and the reach is one bound value.

**Tests that make this true.** In the compiler's suite: twelve `*_Of_People` statements approved
beside the twelve they shadow; `A_Question_About_Everybody_Never_Reads_The_Verdicts`, which holds
the everybody shape to what it was; `Every_Question_Of_People_Narrows_After_Identity_Is_Settled`
and `The_Rebuilt_Visits_Of_People_Are_Grouped_From_Their_Reports_Alone`, which hold the two
expressions to their place; and the placeholder round trip over every people shape. Against a real
store, `PopulationTests`: a reader and a scraper counted twelve ways, then the visit nothing has
judged, the visit judged again under a newer and under an older ruleset, the report the site's own
server sent, the visit that began the evening before, and the scanner. At the endpoints,
`A_Member_Can_Ask_Any_Question_About_People_Alone`,
`A_Population_This_Product_Cannot_Separate_Is_Refused` and
`A_Series_Of_People_Says_How_Far_The_Judging_Has_Reached`. On the dashboard, `dashboard.test.tsx`
"asks every panel about the same people", "still checks everybody, quietly" and "replaces the
picture and the lists with one state when none of the people were counted"; `traffic-chart.test.tsx`
"names the measures for the people when the figures are theirs" and "washes the buckets the engine
has not finished judging"; `judged-traffic.test.tsx` "sets the people against everyone else on the
ring"; `journeys.test.tsx` "asks for the people alone while the screen is kept to them, whatever
the address says" and "forgets a conclusion the address named while the screen is kept to people";
`app-header.test.tsx` "hands the remembered population between the screens without writing it into
this one"; and `endpoints.test.ts` "asks about verdicts of everybody, always".

**What stays as it was.** The verdict questions, the opened visit, the live screen and the volume
statement carry no population. `VisitNarrowing` is untouched. The filters on the journeys screen
are still not remembered, for the reason 0040 gave.

# 0018 — A page somebody reported reading is a page they visited

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column, collects nothing new, and widens no envelope — it reads
  what was already stored.
- **Follows**: `0017-a-rented-address-is-a-lease-not-a-visitor.md`, which closed the other half of
  the same complaint.

## Context

A live installation reported **115 of its 206 visits as "too little to go on"** — 56% of them — and
every one of those visits had a page count of nought. The two facts were the same fact: the engine
refuses to judge a visit that asked for nothing, and nothing is what it was handed.

The visits were not empty. Read against the store, they held progress and departure reports naming
a page, carrying fifteen seconds of attention, a scroll to the foot of the article, and a pointer
having been used. One held twenty-nine such reports. What none of them held was a row of kind
`PageView`.

The panel underneath already knew. Opening one of those visits listed the page, because the journey
groups every report about an arrival into one row whether or not the arrival itself was reported —
so the product was simultaneously showing a reader the page a visit went to and telling them the
visit had gone to no pages. The journey was right and the count was wrong.

**Why the announcement is the report most often missing.** The tracker sends it first, on load,
before anything has been read. Reports travel by a transport that acknowledges nothing, and the
whole arrangement is deliberately built so that a lost report costs nothing. It is also the report
least likely to survive a visitor whose address changes between one request and the next: `0017`
reduced a rented network to itself, but a household on a carrier that rotates addresses must keep
its address as its identity, and there the announcement and the reading genuinely land under two
different visitors. Rebuilding the installation's activity with `0017` applied leaves **61 visits
still holding no announcement**, on some thirty different networks — VNPT, Sky, Telefónica Chile,
Emirates Internet, Kcell, Telmex, Telkom Indonesia — a visit or two each, fifteen to a hundred
seconds of reading apiece. Ordinary people on ordinary broadband, and no network rule can reach
them.

A second defect was found in the same reading. The tracker restates the time a page has held
somebody every time it reports, so each report contains the last one with more on the end; the
visit statement added the reports together. The installation's stored activity totalled **seventeen
hours of reading across fourteen hours of wall clock**, on a site whose busiest visit lasted twenty
minutes. Every excess minute pointed toward a person.

## Decision

**A page a visit went to is every report about one arrival at it, folded into one.** A report is
about a page whether or not it announces one: the tracker only reports how a page is being read
from the page itself, so a progress or departure report naming an address is evidence the address
was delivered. Written once, in `VisitGrouping`, as two expressions — `page_ordinal` says which
arrival a report belongs to, and `opens_page` marks the one report that stands for that arrival.

**It cannot count a page twice.** A path the visit announced arriving at counts once per
announcement whatever else was reported about it; a path it announced no arrival at counts once
altogether, however many reports named it. Where both a browser and the site's own server announced
the same arrival, the request path's report is still the one kept, because it carries the status
the site answered with — the marking of a second sighting is untouched.

**One definition, three readers.** The engine's evidence, the site-wide entry and exit pages, and
the journey a reader opens all take their pages from it. That is what stops the count and the list
disagreeing again, which is how this was noticed.

**The headline counts the same deliveries.** `DeliveredPageViews` credits the browser's half with a
delivery it plainly saw but never announced — one, not one per report. Leaving the headline out
would have fixed the visits and left the total they are a breakdown of describing a different site.

**Reading time is counted once per page rather than once per report.** The largest reading each
arrival attracted, added up across the visit's pages.

**The compiled ruleset moves to three.** The detectors are the same ones; what they are shown is
not, and from a customer's side that is the same thing — it is the same visit answered differently.
Verdicts are kept per ruleset, so the old answers stay on record and the installation's history is
re-judged rather than rewritten.

## Consequences

**Measured against the installation's own stored activity** rather than estimated, by rebuilding
its visits with the new statement:

| Over the same 213 visits                  |    Before |    After |
| ----------------------------------------- | --------: | -------: |
| Visits the engine refused to judge        |       117 |    **0** |
| Pages counted across those visits         |        98 |      216 |
| Reading time counted                      | 17 h 08 m | 1 h 38 m |
| Headline page views, against 202 visitors |       101 |      217 |

More people than pages was the shape of the original complaint. It is now the other way round,
which is the only way round it can honestly be.

**"Too little to go on" keeps its meaning.** It is still returned, and still returned as an answer
rather than hidden — for a visit that named no page at all, which is what the phrase was always
supposed to describe. What it no longer means is "we were told what this visitor read and did not
listen".

**Reading time falls by four fifths, and that is the point.** Every minute removed was a minute
counted more than once, and all of them pointed toward a person. The correction can only move
verdicts away from calling machinery human, which is the direction this product cannot afford to be
wrong in.

**A page can still be counted twice across two visitors.** Where an announcement and a reading land
under different identities, each fragment now counts its own page instead of one counting a page
and the other counting nothing. That is an identity failure being reported honestly rather than a
counting rule being wrong, and `0017` is what narrows it. It fails in the same safe direction: a
visit is credited with the page it can prove it read.

**It reads only what was already stored.** No column, no new report from the tracker, no byte spent
in a beacon with ninety-five to spare, and nothing collected that was not collected before.

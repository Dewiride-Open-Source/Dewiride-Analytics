# 0034 — A visit begins where somebody arrived

- **Status**: accepted
- **Date**: 2026-08-27
- **Applies to**: both editions. Changes the fragment every statement groups visits with, moves the
  ruleset to 7.0, and adds one migration that removes stored verdicts on activity that is no longer
  a visit. Collects nothing new, adds no column, and narrows nothing about what is retained. Nothing
  in `integrations/` changes: every surface there announces arrivals and nothing else, and the three
  report kinds this turns on are the browser tracker's alone.
- **Revises**: [0018](0018-a-page-somebody-reported-reading-is-a-page-they-visited.md), which named
  this failure in its own consequences and accepted it. It has now been measured.
- **Followed by**: [0035](0035-a-visit-is-judged-on-all-of-itself.md), which measured the consequence
  this one left open — a departure arriving after its visit was judged — and closes what re-judging can.

## Context

A tracker sends four kinds of report and only one of them announces an arrival. A page view says a
page was delivered; a progress report says how the reading is going; a departure report says the page
is being left; a press says a control was operated. The last three are all sent **from the page**,
name the page they were measured on, and announce no beginning of anything.

Visits were grouped by silence alone: a visitor's reports in order, and a new visit wherever the gap
exceeded the idle timeout. Under that rule, any report at all could begin a visit — so a departure
report arriving on its own became a visit of its own, carrying whatever reading the page had
accumulated.

That is not a rare shape. Measured over seven days of a live installation:

|                                           |                |
| ----------------------------------------- | -------------- |
| Visits reconstructed                      | 655            |
| …holding no arrival at all                | **186 (28 %)** |
| …of those, recorded as a person           | 101            |
| Departure reports belonging to no arrival | 237 of 1,183   |

Visit counts were inflated by 40 % and the count of real readers by 41 %. For a product whose whole
proposition is telling genuine engagement from machinery, that is the product failing at the thing it
sells — and failing generously, in the direction that flatters the customer.

**The cause is not a lost beacon.** It is the visitor key, and it is structural. A key is
`HMAC(the day's salt, site ‖ day ‖ connection ‖ user agent)` — the retention envelope in
[0005](0005-privacy-envelope.md), and deliberately not durable. So it changes when the network
address does, which a phone does between cells and a laptop does between networks, and it changes for
everybody at midnight UTC. The **tail** of a visit therefore routinely arrives under a key that never
announced anything, while the beginning of the same visit sits under the previous key and has already
been counted. Every one of those tails was a second reader.

The recovery this was built for is real but rare. In the same week, reports naming a page inside an
otherwise healthy visit that the visitor was never seen arriving at: **10 reports, 2 pages, 2
visits**. The mechanism fabricated 186 visits to recover 2 pages.

## Decision

**A visit begins where somebody arrived.** Only a page view opens one. A progress report, a departure
and a press are accounts of a page somebody was already on, and a report that announces no beginning
does not get to be the beginning of anything.

**The silence that ends a visit is still measured across every report, whatever its kind.** Any
report at all is somebody being there. A reader held by one article for an hour, whose page says so
four times and who then opens a second article, has not left and come back — measuring the gap
between arrivals instead would have split that reader in two, which is the same error in the other
direction.

**Every report belongs to the visit its own page was arrived at in.** That is what the report is an
account of, and it is knowable from the report itself: the visitor and the page are both on it. So a
tab dismissed the following morning belongs to the visit it came from rather than opening one, and a
reading is never handed to a visit that never went to the page it was measured on.

**A report naming a page its visitor was never seen arriving at is left out altogether.** The page
was delivered to somebody — that much the report proves — but nothing on it says which visit it
belonged to, and the nearest visit is a guess rather than an inference. Not knowing where something
belongs is an answer this product is already built to give.

**A delivery is counted from the reports that announce one, on the page list and in the headline
too.** 0018 credited the browser's half with a delivery it never announced but plainly saw, and
recorded the risk in its own consequences: "a page can still be counted twice across two visitors …
that is an identity failure being reported honestly". Measured over the same week, **181 of the 182
pages named only by a reading belong to a key that never announced anything at all** — a key that
came into existence part-way through somebody's visit. One is the genuinely lost announcement the
credit was built for. So the credit is withdrawn: a page named only by readings was announced under
another key and is already counted there, and adding it again reports one delivery as two. Inside a
visit, every report about a page is still folded into the arrival it is an account of, which is the
half of 0018 that was right and is untouched.

**The headline and the visits still describe the same site.** That was 0018's reason for changing
both together and it is this one's reason too: fixing the visits alone would have left 182 of the
week's 796 page views — very nearly a quarter — filed under visitors that no visit accounts for, so
the total and the breakdown of it would have described two different sites.

**One consequence is deliberate and worth naming: visits by one visitor may now overlap.** A
departure reported after that visitor has already started a new visit goes back to the older one.
Two arrivals are still separated by a silence; two visits are no longer separated in time.

**The ruleset moves to 7.0, and the verdicts that can never be superseded are removed.** A bump
re-judges every site from its beginning, but nothing will ever judge a visit that is not
reconstructed — and reads take the highest ruleset present per visit, so a verdict left behind is
shown for ever. `0008_visits_that_never_began` removes two shapes: a stored visit holding no arrival,
and one whose first arrival is later than the instant it is recorded as beginning, which is a visit
whose name has changed. It recognises both from the activity itself, is restricted to visits every
surface of which is the visitor's own browser — the only place a report can arrive without an
arrival, and the only keys the reconciliation leaves alone — and leaves alone any visit whose
activity has already passed out of retention. On the installation this was measured against it
removes 308 of 883 stored visits.

## Consequences

**Customers will see their visit numbers fall — by 28 % on the traffic measured here, 655 to 468.**
Nothing was lost; the previous number was counting one reader as two. There is no way to present this
as anything other than what it is, and the honest framing is the only one available: the smaller
number is the true one.

**The commercial edition meters what it bills on this number, and it falls with it.** The monthly
allowance is counted in pages delivered, from the same expression the dashboard's headline uses, so a
Cloud customer's usage drops by roughly what their headline drops by. They were being metered for a
delivery counted twice, and correcting a number a customer is charged against is not a decision to
make quietly: it is stated here, and the customer-facing note about it belongs with the release
rather than in this file.

**A departure reported after its own visit was already judged is left out rather than returned to
it.** The engine works forward and reads a window at a time, so the pass that would carry the reading
back has moved on. The reading is not lost from the site — the attention statistics group a reading
by visitor and page and never needed a visit to hang on — but the visit it belonged to keeps the
verdict it was given. Returning it as well is what revisiting a verdict on later evidence would add,
and that is separate work. What the departure can no longer do is become a second reader, and that is
the whole of what this changes.

**The measured cost of refusing to guess is two pages a week inside a visit, and one page a week on
the site total.** It is recorded here rather than left to be rediscovered, and it is the price of the
181 double counts beside it.

**The reading statistics deliberately keep counting a reading nothing announced, and are the one
place that still carries the double count.** A reading is one visitor on one page, and where a key
changed part-way through, the tail holds the only complete measurement of that page while the head
holds a partial one or none. Refusing the tail there would throw away the measurement the card exists
to report, so the count of readings on a site stays inflated by the same identity failure. It fails
in the modest direction — the card reports a larger denominator and therefore a lower share of pages
it could measure at all — and the number it is actually asked for, how long a page held somebody, is
unaffected.

**The proper fix for the underlying loss is not this.** It is for a report to carry the identity of
the page view it is an account of — a value minted per page load, dying with the page, and therefore
not a visitor identifier and no widening of [0005](0005-privacy-envelope.md). That would let a tail
arriving under a changed key rejoin its own visit instead of being discarded. It needs a column, a
tracker change and every capture surface in `integrations/`, so it is a separate piece of work and is
deliberately not smuggled in here.

**A migration that joins the two largest tables is a real cost on a large installation.** It runs
once, is bounded by the retention window rather than by how long the installation has been running,
and is written to leave alone anything it cannot decide.

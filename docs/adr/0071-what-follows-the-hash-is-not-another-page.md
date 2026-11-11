# 0071 — What follows the hash is not another page

- **Status**: accepted
- **Date**: 2026-11-11
- **Applies to**: both editions, and the whole of it is in the tracker, which is in the free product.
  Collects less than before — no report carries what follows a page's hash — adds no column and needs
  no migration. The engine is unchanged: it already knew a page by its path
  (`Domain/Telemetry/PagePath.cs`). No other way of capturing a visit changes: the no-script image and
  a reporter on a website's own server never receive what follows the hash, because a browser does not
  send it, and nothing is in `integrations/` yet.

## Context

The operator of the hosted installation reported on 2026-11-11 that a post's views were counted once,
twice, three, five or six times over, with no pattern, from the evening they shortened how often a
full-screen advertisement may show on their blog from once an hour to once a minute. While the
advertisement covers the page it puts a mark of its own after the address's hash, and when it is
closed the mark goes again.

The tracker started a new reading whenever the address changed without the page being fetched again,
and it compared the whole address, hash included. So the advertisement opening was a page view and its
closing was another, each of the same post, because the collector keeps a page's path and drops what
follows the hash. The same happened at every jump within a page — a table of contents, a footnote, a
link to the comments, a button whose link is `#` — and has since the tracker's first version.

Google describes these advertisements, and serves the code that runs them, as follows (code read on
2026-11-11, version m202611100101):

- A publisher sets how often one may show "from 1 minute to 1 hour", ten minutes unless changed
  ([AdSense help](https://support.google.com/adsense/answer/13956167)).
- One shows when a reader follows a link, and — from 9 March 2026, unless the publisher turns it
  off — while they stay on the page: after half a minute idle followed by a touch, on reaching the end
  of the article, on coming back to the tab
  ([AdSense help](https://support.google.com/adsense/answer/16531962),
  [announcement](https://support.google.com/adsense/answer/16853623)).
- Opening, it adds `#google_vignette` with `history.pushState`, or by setting `location.hash`.
  Closing, it takes the mark away with `history.replaceState` at once or a second later, goes back with
  `history.back()`, leaves the mark, or carries on to the link that was followed. It marks
  `#goog_rewarded` and `#goog_game_inter` the same way.

Measured read-only on the hosted installation on 2026-11-11. An extra page view can be recognised
after the fact: its referrer is its own page, and a report of that page being left arrived from the
same visitor stamped by the browser within 5 ms of it, because the tracker sent the two in one step.

| What                                                                       | Found                                                                                                                                         |
| -------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Page views on the blog whose referrer was the page itself, each hour       | 0–4 until 18:00 UTC on 2026-11-10, when the advertisement was shortened; 5–11 every hour after                                                |
| … of the 60 since, sent in one step with a report of leaving               | 59; the 60th was a page fetched afresh, and stands                                                                                            |
| Readings of one post by one visitor since 2026-11-10 carrying extra views  | 48: 29 with one extra, 15 with two, 1 with three, 3 with four                                                                                 |
| Extra views on every website of the installation since August              | 280 on four websites; on the blog, under 2% of each month's page views before the advertisement changed                                       |
| Verdicts of the 172 visits holding them                                    | none moved: no visit reached the number of pages that reads as a program working through a site                                               |
| Visits begun by an extra view                                              | 4: the extra view was the first thing each of them held                                                                                       |
| Time in front of somebody across the 40 readings affected since the change | 60.5 minutes by the browsers' own clocks; 63.1 as the engine read it, because a report of leaving can arrive after the page view sent with it |

Every figure counted from page views carried them: the views on the overview, a post's views, Insights,
All time, and the half hour's page views on the screen of who is here now. Each was a new page in its
visit too, so pages per visit rose, a visit that read one page stopped being one, and a visit's journey
repeated the post. Each reading was cut where the advertisement opened, which capped a page's typical
time at the gap between advertisements, and put a reader who had been on one post for over half an hour
back on the screen of who is here at every advertisement. The hosted service's monthly allowance counts
the same page views ([0025](0025-one-number-for-the-screen-and-the-bill.md)).

## Decision

**A page is its address up to the hash.** What follows the hash is never sent to the website's server,
so it never names another page: it marks a place on this one, or whatever a script on the page wanted
remembered. The tracker reads the address without it, and a change after the hash alone is not a move.
The reading goes on through the advertisement, and the time it covered the page is time on that page.
One comparison covers every way the advertisement changes the address, and every jump within a page.

**What follows the hash is never reported.** Every report carries the address up to the hash. Nothing
the engine counts ever read that part, and it can carry what nobody should be sent: some sign-in
services hand their token back to the page there.

**The browser's own announcement of a change after the hash is not listened for.** It announces nothing
that could be a move. Going back or forward to an address that differs before the hash is announced by
the event the tracker already listens for, in every browser it is built for.

**A touch is watched for through the whole life of the page.** Found while reading this: the tracker
stopped listening after the page's first press or key, though every reading starts out untouched, so
on a site that moves itself every reading after the first touch reported none.

The privacy-first analytics this product is measured against draw the same line: Plausible starts a
page view only when the path changes, with pages after the hash an opt-in
([Plausible](https://plausible.io/docs/script-extensions)), and Google Analytics drops the hash from
the page it records ([Google Analytics](https://developers.google.com/analytics/devguides/collection/ga4/views)).

## What this deliberately does not do

- **No setting for a website that moves between its pages after the hash.** A few do; a documentation
  site built with Docsify does unless told otherwise. Each is measured as the page it was loaded at,
  which is the one path the engine already kept all of its pages under. Measuring them page by page
  would need the engine to keep what follows the hash as part of the page, which it deliberately does
  not.
- **A change after the question mark is still a move.** That part is sent to the server and can name
  another page.
- **Nothing in the engine recognises an extra view sent by an older tracker.** Such a rule would be
  carried for good for a fault that has ended. A browser keeps its copy of the tracker for at most an
  hour; a page already open keeps the copy it loaded until it is closed.

## Consequences

- Every figure in the Context counts a page once per reading, whatever a script on the page does after
  the hash.
- The tracker grows by six bytes, to 1,942 compressed against its budget of 2,048.
- An installation's history from before it ran this tracker keeps the extra views. The hosted
  installation's was corrected by its operator, as below.

## Correcting the hosted installation's history

Carried out by hand after this release, not by the product: removing what an installation already
holds is an operation on that installation's data
([0060](0060-a-visit-from-an-ignored-website-is-never-kept.md)). For every extra view since August
carrying the signature above:

- **Inside a visit already judged.** The reports that followed it are first given the time the page had
  already been in front of somebody, which the report of leaving sent with each extra view carries, so
  the reading reads as one — what this tracker would have sent. Then the view is removed, and the
  visit's page count is lowered to match under every ruleset that judged it.
- **Beginning a visit that holds no other page.** The view is removed and the visit with it: with no
  page view under its key, what followed never was a visit, as `0008_visits_that_never_began.sql` has
  it.
- **Beginning a visit that holds other pages.** Kept, because removing it would rename the visit and
  leave its verdict naming nothing.

The months already kept that held one are then counted again from their reports
([0064](0064-a-month-outlives-the-reports-it-was-counted-from.md)); the month running now has not been
kept and needs nothing.

**Measured.** Carried out on 2026-11-11 at 04:14 UTC, fifteen minutes after the release reached the
server, after a backup taken by the release. 280 extra views were removed: 252 on the blog (8 in
August, 28 in September, 83 in October, 133 in November), 24 on a second website, and 2 each on a
third and a fourth. 870 reports were given back the time of the readings they continued, and the time
in front of somebody, read in each browser's own order, was the same to the hundredth of a minute
before and after on every website — 768.24 minutes across the blog's affected readings. Two views had
begun visits holding nothing else, and those visits went with them: six verdict rows under five
rulesets. 454 verdict rows across six rulesets had their page count lowered, none below one page. Eight
were kept: two that began a visit holding other pages, and six in visits not yet judged. Two later
runs, each after the owner had agreed to its exact number, removed six more as their visits were
judged: five of those six at 05:12 UTC, and at 05:20 one sent at 04:38 by a browser still holding the
old tracker. The history keeps the two that began a visit, and one in a visit still open at 05:20. The
six months already kept on three websites were counted again within seconds. The blog's August, September
and October now read 924, 2,427 and 4,697 page views (from 932, 2,455 and 4,779), and its people's
page views fell by 8, 28 and 77.

## Rolling back

An earlier release starts a new reading at every change after the hash again, and reports what follows
it. Nothing stored changes shape, so this tracker carries on from where it was when it returns.

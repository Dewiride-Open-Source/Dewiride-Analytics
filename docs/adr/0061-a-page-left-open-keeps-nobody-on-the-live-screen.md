# 0061 — A page left open keeps nobody on the live screen

- **Status**: accepted
- **Date**: 2026-10-30
- **Applies to**: both editions, and the whole of it is in the free product. Changes who two of the
  four readings about the present moment count, and one sentence of the screen they answer; collects
  nothing new, retains nothing new and needs no migration. Nothing in `integrations/` or in the
  tracker changes, because nothing about what is captured changes.
- **Revises**: [0036](0036-what-is-happening-now-is-counted-not-judged.md), whose unit was a visitor
  with any report inside the stretch.
- **Follows**: [0059](0059-a-page-left-open-is-not-a-page-read.md), which keeps the time a page was
  left open out of what a finished visit read, and
  [0052](0052-a-row-says-on-its-first-line-how-somebody-arrived.md), whose "Arrived earlier" is what
  such a row said on its first line.

## Context

On 2026-10-30 the hosted install's live screen showed one row at the top as active "Just now", every
minute, its page on screen for more than 800 minutes, still being watched and never judged. Measured
read-only the same morning:

| What             | Measured                                                                    |
| ---------------- | --------------------------------------------------------------------------- |
| The page         | one article on one website, opened at 16:07:50 UTC on 29 October            |
| The browser      | Firefox on Linux, over a business network in the United Kingdom             |
| Its reports      | one progress report a minute while the page was on screen, two departures   |
| That day's visit | judged at 00:29 on the 30th: a person, strong signs, under ruleset 11       |
| After midnight   | 382 progress reports by 06:22, under three keys and no page view under any  |
| Why three keys   | the day changed at midnight, then the network address at 00:47 and at 05:35 |

The tracker reports a page's progress once a minute for as long as it stays visible, so a page left
on a screen reports all evening, all night and on into the next day. Under 0036 every one of those
reports was somebody here, listed first because they reported last. Once the day or the network
changed, the reports arrived under a key with no page view behind it. `VisitGrouping` leaves a
report out of every visit unless its key was seen arriving at its page, so none of those will ever
be part of a visit — and the screen went on listing, beside "Judged once their visit finishes", a
visitor no verdict would ever reach.

It was not one tab. Across every website on the hosted install, per UTC day from 23 to 29 October:

| What, per day                                                                   | Measured                   |
| ------------------------------------------------------------------------------- | -------------------------- |
| Visitors                                                                        | 90 to 548                  |
| Keys that reported with no page view under them, so never part of a visit       | 11 to 25                   |
| Visitors still reporting more than half an hour after the last page they opened | 3 to 27                    |
| The longest of those stretches                                                  | 1,104 minutes, on the 29th |

Each of them sat on the list meanwhile as "Arrived earlier", rising to the top whenever its page
reported.

## Decision

**Somebody is here while a page they opened is inside the half hour.** `LiveActivity.OpenedAPage`
(`countIf(kind = 'PageView') > 0`) is the condition on a visitor, applied as `HAVING` to the
grouping by visitor in the reading of who is here and in the reading of the pages being read, so the
two hold the same people. The count of visitors is taken after it, so the headline, the list and the
pages still add up.

**What a listed visitor reported about a page opened earlier still counts for them.** Somebody who
opened a page inside the half hour while reading another is on whichever they reported from last,
and both pages are on their row and in their trail, as before.

**Nothing is read from outside the window.** Asking whether a visitor's key holds a page view
earlier in its day would keep somebody who came back to an old tab, and would cost a read of up to a
day of a website's reports on every beat of every watched screen. 0036's cheap reading of a few
minutes is kept.

**The count says what it counts.** Its note reads "Anyone who opened a page in the last half hour."

## What it does not do

- It changes nothing the tracker sends. A page left open still reports once a minute while it is
  visible, and 0059 already keeps that time out of what a finished visit read.
- It changes nothing about judging. A finished visit is judged on all of itself, and a report its
  key was never seen arriving at is left out exactly as before.
- It leaves the minute-by-minute drawing alone, which counts pages opened and never counted a page
  left open, and the trail, which is opened from a row that is listed.

## Consequences

- **A reader who stays on one page for longer than half an hour, opening no other, leaves the list**
  until they open one. Their visit is judged as it always would be once it is over. This is the
  price, paid by the 3 to 27 visitors a day measured above; all but two of them sent a departure in
  that stretch, from a tab put behind another or closed.
- **Somebody returning to a tab opened more than half an hour ago is not listed**, and nor is their
  leaving it again. A departure and a progress report are news of a page somebody already had.
- **Every visitor the list holds opened a page that is part of a visit**, so wherever judging is
  switched on, "Judged once their visit finishes" is true of each of them.
- **"Arrived earlier" now marks only a visitor who opened a page inside the half hour** after
  arriving before it: through a link on the site, or while another of its pages was open.
- **A press alone lists nobody.** Presses are recorded only where a website turns them on, and a
  press is made on a page that was opened, inside the half hour or before it.
- **Rolling back** to an earlier release lists such visitors again. Nothing stored changes either
  way.

# 0082 — The minutes follow how the visitors arrived

- **Status**: accepted
- **Date**: 2026-11-27
- **Applies to**: both editions, and the whole of it is in the free product. Adds four sums to the live
  statement that counts the minutes, and one record to each minute on the wire; adds no stored column,
  no migration and no index; collects nothing new and widens no retention envelope. Nothing in
  `integrations/` changes, because nothing about what is captured changes, and nothing in `ee/` changes,
  because the hosted site describes nothing on this screen.
- **Revises**: [0078](0078-who-is-here-can-be-kept-to-one-kind-of-visitor.md), whose "the
  minute-by-minute drawing go[es] on counting everybody" now holds only while the list is kept to
  everybody or to something recognised. Kept to the visitors one kind of site sent, the drawing is kept
  to them too. The visitors and page views on the card and what is being read still count everybody.

## Context

The owner asked on 2026-11-27 that pressing "From a search engine" or "From an AI assistant" on the live
screen narrow the minute-by-minute drawing as well as the list. Kept as 0078 left it, a press changed
the rows and left the columns beside them describing everybody, which reads as the press not having
worked.

## Decision

**Each minute says how many of its pages went to the visitors each kind of site sent.** The statement
that counts the minutes reads the window through the same reduction of referrers the reading of who is
here uses, puts each visitor under the kind the earliest report in the window naming a sender named —
the very `from_kind` the four counts of how visitors arrived are taken from — and sums each minute's
pages for each of the four kinds beside its total. Pages delivered to activity carrying no key, or to
somebody nothing named a sender for, are under none. The minute's own total is named `pages_read`
inside the statement, because the four sums read each visitor's `page_views` and a total of the same
name would be what that name meant there.

**The drawing is kept in the browser.** A reading already carries the four kinds for every minute, so
pressing a chip redraws from it at once, with no second question and no wait for the next beat. Kept to
a kind, the columns, the table beneath them and the description a screen reader hears are named for
those visitors — "Pages read by visitors from a search engine" — and a half hour holding none of their
pages says "Nothing has been read in the last half hour by visitors from a search engine."

**Recognised chips leave the drawing everybody's.** What has been recognised is named by running the
engine over the visitors a reading carries (0039), never over everybody the half hour holds, so no
minute can be kept to it. The columns keep everybody's name, "Pages read", which is what says so.

**The figures on the card stay everybody's**, as 0078 decided: they describe the half hour, and the
chips' own counts sit beside them.

## Consequences

- **Cost**: the minutes' statement reads the referrer reduction the visitors' statement already reads,
  groups the window by visitor once more and joins it back onto the minutes. Still three statements per
  beat, under the same ten-second limit.
- **The kinds are not a breakdown of a minute.** A minute's four sums need not add up to its total.
- **Releasing**: the dashboard reads `sentBy` from every minute, so it ships with the engine that writes
  it, as both images of one release do.

## Tests that make this true

- **The statement**: the approved `AnalyticsSqlCompilerTests.Live_Activity.verified.txt`.
- **Against the store**: `LiveTrafficTests`
  `Each_Minute_Says_How_Many_Of_Its_Pages_Went_To_The_Visitors_Each_Kind_Of_Site_Sent`, where a searcher's
  second page, reached from the site itself, stays under search, and a visitor who typed the address is
  under no kind; and `A_Site_Nobody_Is_On_Still_Draws_Its_Half_Hour`, now nought under every kind too.
- **The screen**: `live.test.ts` "the pages the drawing of the half hour shows"; `live-activity.test.tsx`
  "the half hour, kept to how visitors arrived"; `site-live.test.tsx` "keeps the minutes to the visitors
  the chip names, and the figures and pages to everybody".

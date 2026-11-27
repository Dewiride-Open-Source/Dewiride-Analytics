# 0081 — A visitor is somebody who opened a page

- **Status**: accepted
- **Date**: 2026-11-27
- **Applies to**: both editions, and the whole of it is in the free product. Changes who is counted as a
  visitor by the headline totals, the series, the list of pages, the lists of where visitors were, where
  they came from and what they used, and the months kept, and raises `CountingRules.Current` to 4. Adds no
  column, no migration and no index; collects nothing new and widens no retention envelope. Nothing in
  `integrations/` or in the tracker changes, because nothing about what is captured changes, and nothing
  in `ee/` moves: the metering statement counts pages, not visitors.
- **Follows**: [0034](0034-a-visit-begins-where-somebody-arrived.md), which decided that only a page view
  opens a visit and that a page is counted from the report announcing it. The visitor count is brought to
  the same terms.
- **Revises**: [0063](0063-the-picture-is-drawn-by-the-day-the-week-or-the-month.md), whose "a bucket
  counts each key once" becomes each key a page was delivered to in the bucket. Visitors stay daily:
  everything else that record says of them holds.
- **Revises**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose "a month's visitors
  are counted as the overview counts a period" still holds, by the overview's new arithmetic. Every month
  still held is counted again under version 4.

## Context

The owner asked on 2026-11-27 why, with the overview kept to people and the picture cut by the hour,
"Visitors judged to be people" stood above "Page views by people" in most hours of any day. Page views
are never fewer than the people who opened them, so a picture saying otherwise looks broken, and on a
product sold on trustworthy measurement it is.

The cause was the visitor count. `ReconciledEvents.Visitors` counted each key that sent any report in
the bucket. The tracker reports how a page is being read fifteen seconds after it opens and then at
widening intervals up to a minute while the page is in view, and reports the page being left; a press is
a report too. A reader who opens a page at 10:52 and is still on it at 11:06 therefore counted as a
visitor at ten and again at eleven, while their page view counted at ten alone.

Measured read-only on the hosted installation's busiest website over 20–26 November, by the hour in
Kolkata:

|                                                           | Hours with more visitors than page views |
| --------------------------------------------------------- | ---------------------------------------: |
| People, counted as before                                 |                               109 of 166 |
| Everybody, counted as before                              |                                28 of 168 |
| Either, counting only keys that opened a page in the hour |                                        0 |

On 26 November the 550 visits judged to be people came from 530 keys — twenty visits were a reader's
second that day — and opened 632 pages. The daily figure said 545: the 530, and 15 readers whose visit
began on the 25th and who, after midnight, only reported reading the page they already had open. Kept to
nobody, the same day counted 1,768 visitors, of whom 173 opened nothing that day: 17 had opened their
page the day before, and 156 never opened one under that key at all — the tails 0034 measured, which
arrive under a new key after the reader's network changes or the day's key does.

The product already answered this question this way in two places. The live screen counts "Anyone who
opened a page in the last half hour" (0061), and the months' busiest pages count a page's visitors as the
keys it was delivered to (`PostViewsStatement.ByPathWithVisitors`, 0080). The headline was the exception.

## Decision

**A visitor of an hour, a day, a period or a page is a key a page was delivered to in it.**
`ReconciledEvents.Visitors` is `uniqExactIf(visitor_key, visitor_key != '' AND delivered > 0)`, read
from the grouping of one row per key and page that every statement counting visitors already makes.
A report of how a page was read, of the page being left, or of a press is an account of a page its
visitor was already on, and the visitor is counted where that page was opened. It reads `delivered`
rather than `page_views` because the statements it is placed in name their own total `page_views`, and
inside them the name means the total; for every row it counts, the two are the same number.

**The lists divide the same visitors.** The lists of where visitors were, where they came from, which
device and which software they used keep, after grouping each visitor's activity, only the visitors a page
was delivered to (`ReconciledEvents.OpenedAPage`). A key that only reported reading is on no row, so the
rows still add up to the headline (0041). The list of pages counts a page's visitors by the same fragment
as the headline.

**Not changed.** Visits and page views are counted exactly as before. The presses list counts the keys
that pressed, which is what it is a list of. The live screen already counted this way, and so did the
months' busiest pages; a post's figures are views, and count no visitors.

**The months kept are counted again.** `CountingRules.Current` rises from 3 to 4 with a new fingerprint,
so every month whose reports are still held is counted again, at most four months per website per run,
the oldest first (0064). A month whose reports have gone keeps version 3's visitors and says which
arithmetic it was kept by, as 0064 decided.

## Consequences

- **No bucket, row or period holds more visitors than page views**, kept to people or not.
- **Visitors fall.** Kept to people the fall is small — on 26 November, 545 to 530. For everybody it was
  between 6 and 17 in a hundred over the measured week, 1,768 to 1,595 on the 26th. Pages per visitor
  rises with it. Nothing was lost: every key no longer counted opened nothing in the stretch it was
  counted in.
- **Among people, a day's visitors are the different keys of that day's visits**, except where a visit
  begun the day before opens another page after midnight. On 26 November that was nobody.
- **By the hour, visits and visitors can still differ by one** where a visit begins before the hour and
  opens another page after it: the visit is counted in the hour it began, the visitor in both hours they
  opened a page in.
- **The kept months change on the first runs after the release.** All Time and Insights move to the new
  figures as each website's months are counted again, a quarter of an hour for every four months held.

## Tests that make this true

- **The statements**: the approved `AnalyticsSqlCompilerTests` `Overview`, `Overview_Of_People`,
  `Visitors_By_Hour`, `Visitors_By_Day`, `Visitors_By_Week`, `Visitors_By_Month`, `Site_Pages`,
  `Site_Pages_Of_People`, `Site_Countries`, `Site_Countries_Of_People`, `Site_Towns`, `Site_Networks`,
  `Site_Sending_Sites`, `Site_Sending_Sites_Of_People`, `Site_Sending_Pages`, `Site_Source_Kinds`,
  `Site_Devices`, `Site_Devices_Of_People`, `Site_Browsers`, `Site_Browsers_Of_People` and
  `Site_Systems` files, and `ConsolidationSqlCompilerTests` `Month_Count` and `Month_Count_Of_People`; the
  version: `The_Version_Of_The_Arithmetic_Moves_With_The_Statements_That_Count`, with version 4's
  fingerprint.
- **Against the store**: `TelemetryQueryTests` `An_Hour_Counts_As_Visitors_Only_Those_Who_Opened_A_Page_In_It`
  and `A_Key_That_Only_Reported_Reading_Is_No_Visitor`; `PopulationTests`
  `A_Person_Still_Reading_When_The_Hour_Turns_Is_A_Visitor_Of_The_Hour_They_Opened_A_Page_In`;
  `SitePagesTests` `A_Key_That_Only_Reported_Reading_A_Page_Is_Not_Its_Visitor`; `SiteLocationsTests`
  `A_Key_That_Only_Reported_Reading_Places_Nobody`; `SiteSourcesTests`
  `A_Key_That_Only_Reported_Reading_Was_Sent_By_Nobody`; `SiteDevicesTests`
  `A_Key_That_Only_Reported_Reading_Counts_Towards_Nothing`; and `MonthConsolidationTests`
  `A_Kept_Month_Is_The_Overview_Of_That_Month`, which holds a kept month's visitors to the overview's, and
  `A_Month_Kept_Without_Its_Best_Hour_Is_Counted_Again_While_Its_Reports_Remain`, now counting under
  version 4.

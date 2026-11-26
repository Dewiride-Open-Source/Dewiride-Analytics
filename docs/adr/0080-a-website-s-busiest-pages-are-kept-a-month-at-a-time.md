# 0080 — A website's busiest pages are kept a month at a time

- **Status**: accepted
- **Date**: 2026-11-26
- **Applies to**: both editions, and the whole of it is in the free product. Adds the table
  `consolidated_page_months` (ClickHouse migration 0017) with no expiry, a pass over pages in the
  background service that keeps months, one endpoint, and a list of every page beside All posts;
  **widens the retention envelope**, which is stated in full below. Collects nothing new: every figure
  kept is worked out from reports the dashboard already reads, by the arithmetic of its list of pages.
  Nothing in `integrations/` or in the tracker changes, because nothing about what is captured changes.
  In `ee/` only the hosted service's privacy copy, the date its legal documents were last changed, and
  its release and correction runbooks change.
- **Extends**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose kept months each
  gain a row of their busiest pages, counted, counted again and removed on its terms; and
  [0069](0069-a-post-s-views-are-kept-a-month-at-a-time.md), whose way of keeping a post's views a month
  at a time every page is now kept by, within a bound.
- **Follows**: [0066](0066-insights-are-read-from-the-months-that-were-kept.md), whose way of reading a
  whole record — what was kept, then the reports — the list of every page is read by; and
  [0070](0070-a-post-is-read-on-a-page-of-its-own.md), whose rule that PostgreSQL compares a search of
  posts still holds for posts. A search of pages is compared by the telemetry store, as a bound value, and
  the reading of an address pasted whole (`PathIn`) moves to where both searches use it.

## Context

The owner asked on 2026-11-25 for the list of every post to offer every page of the website beside it,
with how often each was viewed, keeping the posts as they are. The overview's Pages card lists every page
of a period, but no further back than the twelve months a report is kept (0064), and nothing kept after
that names any page but a listed post (0069).

The owner chose on 2026-11-25 to count pages as posts are counted — each page's views kept month by month
until the website is removed — with two limits. Only each month's busiest few thousand addresses are
kept, because a page's address, unlike a post's, is whatever was asked for: anybody can make up addresses
without end, and a record that kept every one would grow as fast as somebody chose to make it. And a page
is kept for a month only where at least two different visitors opened it, because a page one visitor
alone opened — their search results, their order, their account — is the kind of address that can carry
what they typed or who they are.

Measured read-only on the hosted installation on 2026-11-25: the busiest month of the busiest website
delivered pages at 2,490 distinct paths, about 50 characters long on average and 208 at the longest.

## Decision

**A month's pages are one row, beside its totals and its posts.** `consolidated_page_months` holds one row
per website per finished month: the paths it kept, in ordinal order, each with the pages delivered there
to everybody and to the visits judged to be people; and three totals of what it left out — how many other
paths pages were delivered at, and the pages delivered at them to everybody and to people. The table
replaces on the instant the count was taken and every read takes the newest row of each month itself, as
0064's and 0069's do. It is a table of its own because it is counted on its own occasions, and because one
of its rows can be far larger than a month's totals.

**What a month keeps.** A path is kept for a month where:

- it is no longer than 2,048 characters;
- at least two different visitor keys were delivered a page there that month. A key is made anew each
  day (0062) and differs between devices and networks, so one person reading the page on two days, or on
  two devices, counts as two; and
- of the paths that meet both, it is among the 5,000 most viewed that month by everybody, or, read by at
  least one person, among the 5,000 most viewed by the visits judged to be people, with a tie settled by
  the path's byte order.

Everything else is added into the totals left out. The two rankings are kept together because automation
can flood the first with made-up addresses, while the second keeps the pages people read. A month
therefore keeps at most 10,000 paths of at most 2,048 characters each, and the limits (`KeptPages`) are
written into the statement that counts it, so the pin below covers them.

**One statement, the list of pages' arithmetic.** A month's pages are counted by one statement for both
populations, built from the pieces the count of posts is built from (`PostViewsStatement`): the month's
reports reconciled across the month, everybody's pages at each path from the visits identified and
people's from the visits attributed to people, as the dashboard's list of pages counts them. The two
rankings are each a selection of the most viewed with a limit, which the store sorts within that limit,
rather than a numbering of every path. It runs on one thread for at most ten minutes. A test holds
`CountingRules.Pages` to a hash of the statement, a version of its own, so a change to how pages are
counted counts again the months of pages still held, and nothing else.

**Counted while held, frozen after.** A finished month's pages are counted while its reports are held, and
counted again for the reasons a month's totals are (0064): a newer arithmetic, a change of zone, a count
that ended before the month did, judging having moved on through it. Once its reports may have begun to
expire the month stays as it was kept. The background service now runs three passes over each website:
the months, their posts, their pages. The months' totals are kept first, and a failure keeping them ends
that website's run before either other pass, as it did before. A pass over posts or over pages that fails
as a whole is logged and stops neither of the others, and a failed count of one month's pages is logged as
5209, naming the website and the month and never a path. Pages are kept one month to a write.

**The list reads what was kept, then the reports.** Every page's views are the views kept for it in each
month before the first month answered from its reports, and the list of pages' arithmetic over the
reports since, added in one statement and grouped by path, the most viewed first and a tie by byte
order. Every answer carries the most viewed page that matches and how many match, so a page of the list
asked for past its end still says how long the list is, and the most viewed sets the scale of every bar.
The statement does not number every path to find a page: it counts the paths that match and keeps, as it
reads them, only the most viewed as far down as the page asked for, and never further than the 10,000
most viewed (`AllPagesQuery.MostListed`), so what it holds is bounded by the page asked for rather than by
how many paths there are. A page below the 10,000 most viewed is found by searching for it. In the month
running now a page has exactly the views the overview's Pages card gives it.

**A search of pages is a bound value, and the store compares it.** A post is found among the posts a
website publishes, which PostgreSQL holds (0070); a page exists only in the telemetry store, so a search
of pages reaches it, bound to the statement and never written into it. The text is read as words, at most
fifty, an address pasted whole reduced to its path word by word (`PostSearch.PathIn`, which both searches
now share). A page matches where every word is found, without regard to case, in its path as stored or in
that path decoded, so a word typed as the screen shows it — "café", or letters of another script — finds
the page whose path holds it escaped.

**The screen.** All posts gains "Posts" and "Pages" in its heading, written into the address as
`?of=pages`. Pages lists every page, the most viewed first: the address as text read left to right, never
a link, because an address anybody can make up must never become one on the dashboard; a bar against the
most viewed; and its views, in pages of 25, 50 or 100. It has no choice of order. The box to search in is
the one Posts has, so what was typed survives the switch. The list says since when views are counted, and
where a kept month left pages out for the population on screen, "A page few visitors opened in an earlier
month may show fewer views than it had." Where more than 10,000 pages match, it is paged through as far
as the 10,000 most viewed and says that a search finds any other. Nothing viewed yet offers the tracking
code; no people yet offers everybody back; and where earlier months were viewed but kept no page and
nothing has been viewed since, it says there is no page to list yet, rather than that nothing was ever
viewed. Where Insights cannot show posts, its card leads to every page, so a website with no sitemap
reaches the list.

## The retention envelope, changed

0064's envelope stands, and gains one line:

- Each website's busiest pages, month by month — for each finished month, the path of each page that at
  least two visitor keys were delivered that month, none longer than 2,048 characters, that was among the
  5,000 such paths most viewed by everybody or the 5,000 most viewed by the visits judged to be people,
  with how many pages were delivered there to everybody and to people; and how many other paths pages
  were delivered at, and how many pages: until the website is removed, even after it stops being
  measured, or until its account's records are deleted thirty days after the account is closed.

A month of pages holds no query, no network address, no visitor key, no user agent and nothing about any
one visit. A path, though, is whatever was asked for. A website that builds its addresses from what its
visitors type, or from their names, keeps those paths beyond twelve months wherever two visitor keys
reached the same one and it ranks — one person reaching it on two days is enough — and on a small website
everything two keys reached ranks. That is said here rather than discovered.

## Measured

On a copy of the pinned store with its shipped limits — 1 GiB a statement, grouping and sorting moved to
disk past 512 MiB — for one website and one month, the list asked for its first page of 25 and for the
page that ends at its 10,000th row:

| Load in one month                                                                      | Counting its pages | Listing every page   |
| -------------------------------------------------------------------------------------- | ------------------ | -------------------- |
| 2.25 million reports at 3,000 paths, 150,000 keys, a third of them people              | 9–11 s, 515 MiB    | 2.0–2.6 s, 522 MiB   |
| 0073's heaviest: 2.25 million reports, every one named, its halves under two keys      | 24–49 s, 914 MiB   | 10.8 s, 819 MiB      |
| A flood of a million made-up paths of 2,000 random characters, almost all one key each | 144 s, 638 MiB     | 80–82 s, 696–935 MiB |
| 30,000 of the widest path the collector stores, about 18,000 characters escaped        | 7 s, 633 MiB       | 3.6 s, 643 MiB       |

The listing at 0073's load was measured before the bound below, and the bound only lowers what it holds;
at the first load the bounded and the unbounded listing took the same time and held the same memory, and
gave the same first page. Asked about people, every listing held less. In the flood a month kept its 499
real paths, and the million made-up ones only in its totals left out, each having had one key. While the
statement still handed back every path down to the end of the page asked for, the page ending at the
10,000th row of the widest paths sent 171 MB and took 8.8 s; it now hands back only the page.

Before the bound, the list numbered every path that matched in order to find a page, and a numbering holds
every row it numbers. It ran out of memory once the month running now held about 200,000 made-up paths
of 2,000 characters, or 15,000 to 30,000 of the widest — one made-up request every ninety seconds for a
month. The list now holds only as many paths as the page asked for reaches, and never more than 10,000.

The count's memory stays level under a flood, because every grouping moves to disk, but its time grows
faster than the flood: 43 s at half a million random paths of 2,000 characters, 144 s at a million. At
that rate it would use up its ten minutes somewhere between 2.5 and 4 million such paths in one month,
sixty to ninety made-up requests a minute for the whole of it. That month's pages would then not be kept,
and would be tried again, for ten minutes and some 20 GiB of temporary files, on every run while its
reports are held. Counting a hash of each path, and the text only of the paths kept, is the change to make
before a website is flooded at that rate. The list under the measured flood answers in about eighty
seconds, inside the hundred the engine allows a statement and the two minutes the dashboard waits, and its
memory, which comes from reading back the grouping moved to disk, peaked at 696 MiB on one run and 935 MiB
on another. A flood not much larger in the month running now would take it past one limit or the other,
and the list would say it could not be read until that month is kept.

## What it does not do

- **Bring back a page a frozen month left out.** Its reports are gone; the month keeps only its totals.
- **Name the pages a month left out.** It says how many there were and how often they were viewed.
- **Credit a post with the page it was redirected to.** Pages lists each path as it was asked for; All
  posts adds a post's own path and the path it landed on (0069).
- **Settle people in a month that froze unsettled**, or **move a frozen month to a new zone**, as 0064 and
  0069 say of theirs.
- **Keep a query.** A path is kept as the collector stores it, without anything after it.

## Consequences

- At its largest, a month of pages is 10,000 paths of 2,048 characters, about 20 MB, written one month to
  a write inside the guard that holds a website's removal back while it lasts. Until the store merges a
  month counted several times, a read takes every row of it before keeping the newest.
- Every month still held is counted after the release, four a website a run, every fifteen minutes: the
  hosted service's three finished months in its first run, a year of months in three runs, about half an
  hour after start. Until a month is kept the list reads it from its reports, so on a long record the
  first list asked for reconciles more than the running month.
- A month almost always leaves out some page only one visitor opened, so once a month has been kept the
  list nearly always says that a page few visitors opened may show fewer views than it had.
- A failed count of a month's pages is logged as 5209, a pass over posts that fails as a whole as 5210, and
  a pass over pages as 5211. A failure keeping the months' totals is logged as before (5204) and ends that
  website's run.
- The dashboard reads the list from an endpoint the engine adds, so it goes out with the engine that
  answers it. An engine that predates this neither writes nor reads the table, and does not delete a
  removed website's months of pages, which never expire: the release runbook says what to delete by hand
  after going back, as for 0064's months and 0069's months of posts, and the correction runbook deletes
  them with the others so they are counted again. The release carries a migration, so it takes a backup
  first.
- The README names the list of every page and what is kept of it; the hosted service's privacy copy names
  the busiest pages among what each month keeps, and the date its legal documents were last changed moves
  with it.

## Tests that make this true

- **What a month keeps**, against the store: `PageMonthConsolidationTests`
  `A_Pages_Month_Is_The_Pages_List_Of_That_Month`, for both populations;
  `The_Busiest_Pages_For_Everybody_And_For_People_Are_Kept_And_The_Rest_Counted_As_Left_Out`, with 5,002
  paths read by automation; `A_Path_Longer_Than_Any_Kept_Is_Counted_As_Left_Out`, built of escaped letters
  past 2,048 characters beside one of exactly 2,048 that is kept;
  `A_Path_One_Visitor_Opened_Is_Left_Out_And_One_Two_Visitors_Opened_Is_Kept`, with activity under no key;
  and `Every_Page_Is_Counted_However_Awkward_Its_Path`. In the statement:
  `ConsolidationSqlCompilerTests` `A_Months_Pages_Are_Counted_By_The_Arithmetic_Of_The_List_Of_Pages`,
  `A_Months_Pages_Keep_The_Busiest_For_Everybody_And_For_People_And_No_Path_Longer_Than_The_Bound` and the
  approved `ConsolidationSqlCompilerTests.Page_Count.verified.txt`; on one thread, for at most ten minutes
  and with nothing but bound values, by the suites over `EveryCountingStatement`.
- **The pin**: `The_Version_Of_The_Pages_Arithmetic_Moves_With_The_Statements_That_Count_Them`, beside the
  unchanged `The_Version_Of_The_Arithmetic_Moves_With_The_Statements_That_Count` and
  `The_Version_Of_The_Posts_Arithmetic_Moves_With_The_Statements_That_Count_Them`, and the posts' approved
  statements as they were.
- **Which months are counted**: `PageMonthsToCountTests`, every case.
- **A failed pass over posts or pages stops neither the other nor the months**: `MonthlyConsolidationTests`
  `A_Months_Pages_Are_Kept_With_What_The_Bound_Left_Out`, a month to a write;
  `A_Months_Pages_That_Fail_To_Count_Stop_Neither_The_Other_Months_Pages_Nor_Their_Totals_Or_Posts`;
  `A_Pass_Over_Pages_That_Fails_Leaves_The_Months_Totals_And_Posts_Kept`;
  `A_Pass_Over_Posts_That_Fails_Leaves_The_Months_Totals_And_Pages_Kept`;
  `A_Website_That_Lists_No_Posts_Has_Its_Pages_Counted`; and `Nothing_Is_Ever_Deleted_By_Counting`.
- **The table**: `TelemetrySchemaTests` `Kept_Months_Are_One_Row_Per_Month_And_Never_Expire` and
  `A_Month_Of_Pages_Is_Its_Paths_And_How_Often_Each_Was_Viewed_And_Nothing_Else`; removed with its website,
  `TelemetryPurgeTests` `The_Page_Months_Kept_For_A_Site_Go_With_It_And_Nobody_Elses_Do`.
- **The list**, against the stores: `AllPagesReadTests`
  `Every_Page_Is_Listed_With_Its_Views_Across_The_Whole_Record`,
  `In_The_Running_Month_A_Page_Has_The_Views_The_Pages_Card_Gives_It`,
  `Pages_Of_People_Count_The_Reader_Alone`,
  `Pages_Are_Read_A_Page_At_A_Time_And_A_Page_Past_The_End_Still_Says_How_Many_There_Are`,
  `It_Says_So_Where_A_Month_Kept_Only_Its_Busiest_Pages`,
  `A_Website_Whose_Only_Kept_Month_Kept_No_Page_Lists_None_And_Says_Pages_Were_Left_Out`,
  `A_Website_Nothing_Is_Held_Of_Is_Answered_With_No_Pages`,
  `A_List_That_Cannot_Be_Answered_As_Asked_Is_Refused_Whether_Or_Not_The_Website_Exists`,
  `Pages_Of_A_Website_Somebody_Cannot_See_Are_Not_Found`, `The_List_Is_Not_Read_By_Anybody_Signed_Out` and
  `The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads`. In the statements:
  `Every_Page_Is_Listed_By_The_Arithmetic_Of_The_List_Of_Pages`,
  `The_Pages_List_Always_Carries_Its_Busiest_Page_And_How_Many_Match`, the approved `All_Pages`,
  `All_Pages_Of_People`, `All_Pages_Searched` and `Kept_Page_Months` files,
  `A_Kept_Month_Is_Read_As_Its_Newest_Count`, `Asking_For_An_Impossible_Number_Of_Pages_Is_Refused`,
  `Starting_The_Page_List_Before_Its_Beginning_Is_Refused` and
  `Asking_For_Every_Page_By_Words_No_Search_Holds_Or_From_A_Month_Not_Begun_Is_Refused`.
- **The search**: `A_Search_Finds_Pages_Holding_Every_Word_Whatever_The_Case`,
  `A_Search_Finds_A_Page_By_The_Letters_Its_Address_Escapes`, with `%FF` and `%C3(` among them,
  `An_Address_Pasted_Whole_Finds_Its_Page` and `A_Search_That_Reads_As_Sql_Finds_Nothing_And_Changes_Nothing`
  against the stores; `A_Pages_Search_Is_Bound_And_Never_Written` and
  `Every_Word_Searched_For_Is_Required_Of_An_Address_After_Grouping` in the statement; `PostSearchTests`
  `A_Search_Is_Read_As_Every_Word_It_Holds`, `A_Pasted_Address_Is_Searched_For_By_Its_Path`,
  `A_Whole_Web_Address_Is_Compared_By_The_Path_It_Names` and
  `Text_That_Is_No_Whole_Web_Address_Is_Compared_As_Written`.
- **The screen**: `all-pages.test.tsx`, every case, among them "switches to every page from the heading and
  back, writing the choice into the address", "keeps the search box, and what was typed, across the
  switch", "lists every page the most viewed first, as text read left to right, never a link", "says since
  when views are counted, and that a page few visitors opened may show fewer views only where a month left
  pages out", "says a page past the end has nothing on it, keeping the list's heading", "never says the
  list is empty while the next answer is on its way", "says there are no pages to list yet where earlier
  months kept none, offering everyone back to people", "pages through the 10,000 most viewed and no
  further, and says how to find the rest" and "says to narrow the search where a search itself matches
  more than the list reaches"; `pages-listing.test.ts`, every case;
  `all-posts.test.tsx` "opens on the list its address names"; `posts.test.tsx` "leads to every page
  wherever the posts cannot be shown, counting the same people"; `endpoints.test.ts`, "asking the engine for
  a page of every page of a website"; and `vocabulary.parity.test.ts` "offers no page of every page longer
  than the engine lists at once" and "lists no further than the engine reads".

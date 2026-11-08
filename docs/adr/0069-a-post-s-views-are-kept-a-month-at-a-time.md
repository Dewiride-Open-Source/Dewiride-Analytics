# 0069 — A post's views are kept a month at a time

- **Status**: accepted
- **Date**: 2026-11-07
- **Applies to**: both editions, and the whole of it is in the free product. Adds the table
  `consolidated_post_months` (ClickHouse migration 0015) with no expiry, a pass over posts in the
  background service that keeps months, one endpoint, and the posts sections of Insights; **widens the
  retention envelope**, which is stated in full below. Collects nothing new: every figure kept is worked
  out from reports the dashboard already reads, by the arithmetic of its list of pages, for paths the
  website's own sitemap lists. Nothing in `integrations/` or in the tracker changes, because nothing
  about what is captured changes. In `ee/` only the hosted service's privacy copy changes, to name what
  a month now keeps, and its release runbook, to say what to delete by hand after going back.
- **Extends**: [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md), whose kept months each
  gain a row of posts, counted, counted again and removed on its terms; and
  [0068](0068-a-website-s-posts-are-read-from-the-sitemap-it-names.md), whose posts are read on Insights.
- **Follows**: [0066](0066-insights-are-read-from-the-months-that-were-kept.md), whose screen the posts
  join and whose way of reading a whole record — what was kept, then the reports — a post's views are
  read by.
- **Revised by**: [0070](0070-a-post-is-read-on-a-page-of-its-own.md). The two questions about one post
  bind its paths — its own, and where it landed — as values, compared once the reports are reconciled;
  every statement that counts or reads every post still names none. Its list of every post gives each
  the views this decision gives it on Insights.

## Context

0068 left the engine knowing which pages of a website are its posts, what each is called and when it was
published, and Settings saying how many there are. The owner asked on 2026-11-07 for the posts on
Insights: how many, the ten latest with their views since the website was first measured, the most read
ever, and a calendar of the last twelve months of posting where a day says how many posts went out on
it.

The views are the hard part. A report is deleted twelve months after it arrived (0064), so "views
since the website was first measured" cannot be answered from reports, and 0064 keeps nothing about
any page. The owner's direction, as for 0064, was a month at a time: nothing day by day outlives the
twelve months a report is kept; how often each post was read is kept month by month, counted while the
month's reports are held and left as it was once they go.

## Decision

**A month's posts are one row, beside its totals.** `consolidated_post_months` holds one row per website
per finished month: the paths of the website's posts a page was delivered at that month, in ordinal
order, each with the pages delivered there to everybody and to the visits judged to be people, which is
nought where none went to a person. A path nobody read that month is not kept. The table replaces on
the instant the count was taken and every read takes the newest row of each month itself, as 0064's
does. It is a table of its own rather than more columns on 0064's, because the two are counted on
different occasions — a month's totals never change because a post was found, and a month's posts do.

**A post's views are its row on the list of pages.** The statement that counts them
(`PostViewsStatement`) is the arithmetic of the dashboard's list of pages for every path a page was
delivered at, with the two halves of the measurement reconciled across the whole window, so a server's
report of a post is still told whose it was by a browser that echoed another page. It is written once,
for the count kept for a month (`ConsolidationSqlCompiler.CompilePostCount`) and the count a reader is
given (`PostViewsQuery`). A test holds `CountingRules.Posts` to a hash of the counting statements, a
version of its own, so a change to how posts are counted counts the months of posts still held again
and none of the months' totals, and the other way round.

**A post is read at its own path, and where it landed.** A post the website redirected to another of
its own paths (`landed_path`, 0068) is read there too, because that is where a browser following its
link ends and what a visit is recorded under — except where that path is another listed post's own,
which keeps its views to itself. The paths counted for a website are the distinct union of the two
(`PostsInsights.CountedPaths`). The background work reads them through `IPostCatalogue`, which, like
`IMonthConsolidation`, answers for the system and takes no scope; an architecture test keeps it, with
them, away from everything that answers a request.

**Counted while held, and again when out of date.** A month's posts are counted once the month has
finished and while every report of it is held, oldest first, a run's allowance at a time, and again
for every reason a month's totals are (`PostMonthsToCount` shares `MonthsToCount`'s rule, held to
`CountingRules.Posts`) — and for one more: the website now lists posts other than the ones the month
was counted for. A row keeps which list it was counted for as a fingerprint, the first sixteen bytes of
the SHA-256 of the sorted paths (`PostFingerprint`), and a month whose list differs is counted again
once its row is an hour old (`PostMonthsToCount.RecountAfter`). A sitemap is read a page a second, so
while a large one is being read the list moves on every run; the hour is what keeps that from counting
the same months again every quarter of an hour. A website that lists no posts has nothing counted and
nothing about its posts read.

The posts are a pass of their own in `MonthlyConsolidation`, after the months' totals have been kept,
over the same calendar and the same reading of how far judging had reached. Each month is counted on
its own, a failure is logged (event 5208, naming the website and the month and never a path) and tried
again on the next run, and a pass that fails as a whole leaves the totals kept. Nothing in the pass
deletes anything, and a month of posts is written only while the website exists, under the lock 0064's
months are written under.

**One statement to a question, whatever a website lists.** The statement names no path. The store
answers with every path a page was delivered at in the window, and the engine keeps the posts' rows as
they arrive — each looked up among the posts' paths exactly as the collector stored them, the rows of
every other page dropped as they are read — and puts what it kept in ordinal order itself
(`PostViewsStatement.ReadAsync`). The paths come from a sitemap, which whoever runs the website writes,
and none of them is ever part of a statement or a value bound to one, so how many posts a website lists
and how long or strangely their paths are spelled change nothing the store is sent. What a question
costs instead is the list of pages: one statement each time Insights reads a website's posts, and one
for each population for each month counted, each returning as many rows as its window has paths a page
was delivered at, as the list of pages reads them.

**A post's views are what was kept and the reports since.** `GET /api/sites/{id}/insights/posts[?only=people]`
reads the record's months as the other readings of it do, so every month instant is the store's. The
reports take over from the first month of the record that is the one running now, or a finished month
nothing was kept of the posts of while its reports are all held; every month from there on is counted
from its reports, and the rows kept for any of them are left unread rather than counted twice. Before
it, every month whose posts were kept is read as kept. The answer says the month the views are counted
from (`viewsFrom`): the earlier of the first month whose posts were kept and the first answered from
its reports.

**What the answer holds.** How many posts the sitemap lists and how many of them say when they were
published, which are the numbers Settings gives; the ten newest with a date, newest first and the first
by path of two published at once; the most read, of two read as much the one published first and any
with a date before any without; and the calendar, from the first of the month eleven months before this
one to today on the website's calendar, each day a post was published on with how many. A post's day is
the day it was published where the website is, and a post dated after today is not on the calendar.
Only the posts shown, eleven at most, are named — address and title — from the control plane, and
neither is ever logged. A website that lists no posts is answered from the control plane alone. The
population is checked before the website is resolved, and a website that does not exist and one the
caller has no role on are answered identically.

**The screen.** Insights gains a third highlight, the number of posts — the number Settings gives,
whoever the screen is kept to — and, between the busiest times and the months and years, the posts: the
ten newest with their views, the most read, and a calendar of the last twelve months of posting. Views
are kept to people where the screen is; the count and the calendar are not, because a post is not a
visit. A link is drawn only to an address that is a web address, and opens in a new tab without saying
where it came from; a title is shown in its own direction and only as text. The calendar is one stop
for the keyboard: the arrow keys move a day and a week, the keys that page move a month, Home and End go
to either end, and a day says how many posts it saw while it is reached, pointed at or touched; the days
that saw a post are also a list. Where a website has no sitemap, has one still being read, could not be
read, or lists no posts with a date, the screen says which, with a way to the sitemap's setting for
whoever may change it; where views are counted from a month later than the record's first, it says
since when. The lists are asked for again when a reading of the sitemap finishes and once more when its
pages have all been read, not on every look at how reading is going.

## The retention envelope, changed

0064's envelope stands, and gains one line:

- Each website's posts, month by month — for each finished month, the path of each post its sitemap
  listed that a page was delivered at, with how many pages were delivered there to everybody and to the
  visits judged to be people: until the website is removed, even after it stops being measured or its
  sitemap is removed, or until its account's records are deleted thirty days after the account is
  closed.

A month of posts holds no address, no visitor key, no user agent and nothing about any one visit, and no
path but the website's own posts, which it publishes. For a quiet month it can say that one post was
read once.

## What it does not do

- **Count a post found late into a month that has frozen.** A month's posts are counted again for a post
  found later only while its reports are held; a month that froze before the post was found never
  gains its views. While the month is held, a post found after it was counted is missing from it until
  it is counted again, an hour after it was last counted at the soonest and on the run after that at
  the latest.
- **Say which months a post's views leave out.** A month whose posts were never counted while it could
  be — counting switched off for most of a year — adds nothing, and `viewsFrom` names the first month
  views are counted from, not the gaps after it.
- **Settle people in a month that froze unsettled.** A month of posts kept before judging had finished
  with it keeps the people it was counted with, where 0064 names such a month's totals as not fully
  judged.
- **Keep one view to one post where two landed in one place.** Two posts redirected to the same path,
  itself no listed post's, are each credited with its views.
- **Forget a website's posts when its sitemap is removed.** Removing the sitemap deletes what was read
  from it (0068) and leaves the months of posts kept; removing the website deletes those.
- **Move a frozen month to a new zone.** As 0064 says of a month's totals.

## Consequences

- Counting a month's posts reads the month's reports once for each population, on one thread and for at
  most ten minutes a statement, however many posts the website lists; each count returns a row for every
  path a page was delivered at that month, as the list of pages reads them, and the engine keeps the
  posts'. A website listing no posts costs one read of the control plane per run.
- Reading the posts answer costs two small reads of the kept months and of the months of posts, a read
  of the kept views where any are older than the reports, and one count of the reports since then,
  returning a row for every path a page was delivered at, as the list of pages reads them; and two reads
  of the control plane.
- Reading the posts answer reads the path of every post the website lists from the control plane and
  holds them while it counts, as many as 0068's limits on what a website may hold allow: fifty thousand
  unless an installation raises it, and a million at the most.
- An engine that predates this record neither writes nor reads the table, and does not delete a removed
  website's months of posts, which never expire: the release runbook says what to delete by hand after
  going back, as for 0064's months.
- The README says each post's views are kept month by month with the months' totals, and the hosted
  service's privacy copy names them.

## Tests that make this true

The engine:

- `ConsolidationSqlCompilerTests`: the approved `Post_Count`, `Post_Count_Of_People` and
  `Kept_Post_Month_Descriptions`; `A_Months_Posts_Are_Counted_By_The_Arithmetic_Of_The_List_Of_Pages`;
  `The_Version_Of_The_Posts_Arithmetic_Moves_With_The_Statements_That_Count_Them`; and the counting
  statements held to one thread, bound values and no value in their text.
- `AnalyticsSqlCompilerTests`: the approved `Post_Views`, `Post_Views_Of_People`, `Kept_Post_Months`,
  `Kept_Post_Views` and `Kept_Post_Views_Of_People`; `No_Posts_Path_Enters_A_Statement_Or_Its_Bound_Values`;
  `A_Kept_Month_Is_Read_As_Its_Newest_Count`; `Asking_About_No_Post_Or_More_Than_A_Question_May_Name_Is_Refused`;
  and the post views among every question asked of people.
- `PostMonthsToCountTests`: `A_Month_Whose_Posts_Were_Never_Kept_Is_Counted`,
  `A_Month_Kept_For_Another_List_Is_Counted_Again_Once_What_Was_Kept_Is_An_Hour_Old`,
  `A_Months_Posts_Are_Counted_Again_In_A_New_Time_Zone`, `A_Months_Posts_Are_Counted_Again_Once_Judging_Has_Moved_On`,
  `A_Months_Posts_Are_Counted_Again_Once_Newer_Rules_Have_Judged_All_Of_It`,
  `The_Posts_Of_The_Month_Running_Now_Are_Never_Counted`,
  `The_Posts_Of_A_Month_Whose_Reports_May_Have_Begun_To_Expire_Are_Never_Counted`,
  `A_Website_That_Lists_No_Posts_Has_No_Month_Of_Posts_Counted` and
  `A_Run_Counts_The_Posts_Of_No_More_Months_Than_Its_Allowance_The_Oldest_First`.
- `PostFingerprintTests` and `PostsInsightsTests`, among them
  `The_Ten_Newest_With_A_Date_Are_The_Latest_And_A_Tie_Goes_By_Path`,
  `Of_Two_Read_As_Much_The_One_Published_First_Is_The_Most_Read_And_An_Undated_One_Comes_Last`,
  `A_Post_Is_Read_At_Its_Own_Path_And_Where_It_Landed`, `A_Path_Another_Post_Is_Read_At_Belongs_To_That_Post_Alone`,
  `The_Calendar_Runs_From_The_First_Of_The_Month_Eleven_Months_Before_To_Today_Where_The_Website_Is`,
  `Posts_Published_After_Today_Or_Before_The_Calendar_Are_Left_Off_It`,
  `A_Finished_Month_Whose_Posts_Were_Never_Kept_Is_Counted_From_Its_Reports` and
  `A_Month_Whose_Posts_Were_Never_Kept_And_Can_No_Longer_Be_Counted_Is_Passed_Over`.
- `MonthlyConsolidationTests`: `A_Months_Posts_Are_Kept_With_The_Peoples_Pages_Beside_Everybodys`,
  `A_Months_Posts_That_Fail_To_Count_Stop_Neither_The_Other_Months_Posts_Nor_Their_Totals`,
  `A_Pass_Over_Posts_That_Fails_Leaves_The_Months_Totals_Kept`, `A_Website_That_Lists_No_Posts_Has_None_Counted`
  and `Nothing_Is_Ever_Deleted_By_Counting`.
- `AuthorisationBoundaryTests` `Nothing_Answering_A_Request_Counts_For_The_System`.
- Against real stores, `PostMonthConsolidationTests`: `A_Posts_Month_Is_The_Pages_List_Of_That_Month_For_Its_Paths`
  (both populations), `Every_Post_Is_Counted_However_Many_There_Are_And_However_Long_Or_Awkward_Their_Paths`
  (three thousand paths, fifty of them over two thousand characters, others with a tilde, quotes or
  escapes in them), `A_Page_That_Is_No_Listed_Post_Is_Neither_Kept_Nor_Credited_To_A_Post` (a post's path
  escaped another way, in other letters, without its last slash or beneath it) and
  `An_Older_Post_Found_Later_Is_Counted_For_The_Months_Still_Held`; `InsightsPostsReadTests`:
  `Posts_Are_Listed_Newest_First_With_What_Each_Was_Read_And_When_It_Was_Published`,
  `Posts_Of_People_Count_The_Reader_Alone`, `A_Posts_Views_Are_The_Months_Kept_And_The_Reports_Since_Added_Up`,
  `Posts_Refuse_Anybody_But_Everybody_Or_People`, `Posts_Of_A_Website_Somebody_Cannot_See_Are_Not_Found`,
  `A_Website_That_Lists_No_Posts_Is_Answered_Without_Asking_The_Telemetry_Store` and
  `The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads`; `TelemetryPurgeTests`
  `The_Post_Months_Kept_For_A_Site_Go_With_It_And_Nobody_Elses_Do`; and `TelemetrySchemaTests`
  `Kept_Months_Are_One_Row_Per_Month_And_Never_Expire`, for both tables.

The dashboard:

- `components/insights/posts.test.tsx`: "counts the posts the sitemap lists among the highlights",
  "counts the same posts on a screen kept to people", "lists the ten newest posts, each with how much it
  was read", "opens a post in a tab of its own, passing nothing on about where it was opened from",
  "writes a title in its own direction, and the address it is read at left to right", "names a post by
  its address where its page gave no title", "names the post read most of all, with how much it was
  read", "asks for the views of people on a screen kept to people", "says since when views are counted,
  where that is later than the record begins", "puts one card in place of the newest and the calendar
  where no post is dated", "says they are still being read, rather than that none says when it was
  published", "asks for the posts again once every page has been read", "tells somebody who may only
  read the numbers who can add one, and offers nothing to press" and "says part of the sitemap could
  not be read, above the posts it did find".
- `components/insights/posting-calendar.test.tsx`: "lays the last twelve months out a block to a month,
  newest last", "shades each day by how many posts it saw, and gives five or more the darkest", "stops
  at today", "is one stop on the way through the screen, on today", "moves a day with up and down, and a
  week with left and right", "moves a month with the keys that page", "goes to either end with Home and
  End, and never past them", "takes the tip away with Escape and leaves the keyboard where it was",
  "never moves the keyboard to a day pressed with a pointer", "shows the tip on a touch, and takes it
  away on a second touch or one elsewhere" and "lists the days that saw a post, newest first, for
  anybody who reads rather than looks".
- `lib/analytics/posting-calendar.test.ts` and `lib/analytics/posts.test.ts`: the calendar's months and
  moves, where a post can be opened from (only an address on the web), and what stands where the posts
  are shown in each state of the sitemap.

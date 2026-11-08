# 0070 — A post is read on a page of its own

- **Status**: accepted
- **Date**: 2026-11-07
- **Applies to**: both editions, and the whole of it is in the free product. Adds three endpoints, two
  questions, two statements, two lookups in the control plane and two screens. No migration, and nothing
  collected or retained that was not already: every figure is worked out from reports the dashboard
  already reads and from the months [0069](0069-a-post-s-views-are-kept-a-month-at-a-time.md) already
  keeps. Nothing in `integrations/` or in the tracker changes, because nothing about what is captured
  changes, and nothing in `ee/` moves.
- **Revises**: [0069](0069-a-post-s-views-are-kept-a-month-at-a-time.md), under which no post's path
  was ever part of a statement or a value bound to one. The two questions about one post bind its
  paths — its own, and where it landed — as values, compared once the two halves of the measurement
  are reconciled. Every statement that counts or reads every post still names none.
- **Follows**: [0066](0066-insights-are-read-from-the-months-that-were-kept.md), whose table of months
  and years the page lays one post out in, and whose way of reading a whole record — what was kept,
  then the reports — the page reads one post by; and
  [0068](0068-a-website-s-posts-are-read-from-the-sitemap-it-names.md), whose sitemap decides what is a
  post.
- **Follows**: [0063](0063-the-picture-is-drawn-by-the-day-the-week-or-the-month.md), as
  [0064](0064-a-month-outlives-the-reports-it-was-counted-from.md) revised it, whose picture of a
  website — cut by the hour, the day, the week, the month or the year, drawn, pressed into and
  remembered — the post's picture is.

## Context

0069 put a website's posts on Insights — how many, the newest ten and the most read, each with how
often it was read across the whole record — but a post could not be looked at by itself: its title
opened the post on the website. The owner asked on 2026-11-07 for what WordPress gives a post: a page
of its own with how often it has been read in all, every month and year it was read by total views or
by average per day, and its recent weeks day by day — and for every post in one list, searchable, each
opening its page.

The months are kept (0069); the days are not, beyond the twelve months a report is held (0064). So a
post's page can say how it was read in every month since its views were first counted, and how it was
read day by day only for as long as reports are held. It cannot name a best day ever, and does not try.

## Decision

**A post is found by its path, among the posts its sitemap lists now.**
`GET /api/sites/{id}/insights/post?path=…[&only=people]` (`InsightsPostEndpoints`) checks what it was
given — a path, and everybody or people — before the website is resolved, then looks the path up among
the website's listed posts, exactly as written, by the control plane's own key (`ISitePosts.FindAsync`).
A path that is no listed post — unlisted, a page that is not a post, another website's, spelled in
other letters or without its last slash, longer than any address a sitemap may name, or holding a NUL —
is answered as a website that does not exist and one the caller has no role on are: the same 404,
before the telemetry store is asked anything. The engine never logs the path — the host's own lines
about each request, which carry its query, are written only at the development log level — and never
echoes it in an answer, and the value sent on to the store is the one the control plane holds, never the
one in the request.

**It is read where Insights reads it.** At its own path, and where the website redirected it unless
that path is another listed post's own (`PostsInsights.PathsOf`, the rule `CountedPaths` applies to
every post), so a post's page and its row on Insights credit it with the same pages.

**Its figure is its row on the list of pages, kept to its paths after reconciliation.** The two new
statements bind the post's paths as one or two values (`{post_paths:Array(String)}`), as the visit
list binds the entry pages somebody narrows it to, and compare them only after the two halves of the
measurement are reconciled across the whole window. So a server's report of the post is still told whose
it was by a browser that echoed another page; comparing them earlier would let a post count twice where
the list of pages counts it once. The post's series (`CompilePostSeries`) is the website's series of
page views with that one comparison added. The post's kept months (`CompileKeptMonthsOfPost`) pick its
paths out of each month's newest count after that count is chosen, so an older count is never read, and
a month counted with nobody reading any post stays a month — one the post was not read in.

**The figure somebody pressed is the figure they land on.** The page's headline is worked out as Insights
works out the post's views: the months kept before the reports take over, and the list of pages since,
kept to the post's paths in the engine (`PostViewsQuery`). It is not the sum of the months.

**Its months are what was kept, then the reports.** A month before the reports take over is its newest
kept count. A month counted without the post in it is a month the post was not read in — nought — unless
it was counted before the post was first listed, which no count could have known of: such a month has no
figure, as a month never counted has none, and the page says it was not counted rather than nought.
From the month the reports take over, each month is counted from its reports; where that is the month
running now alone, the one count serves both it and the headline. The months run from the earlier of
the month the post was published and the first month it was read, never earlier than the month its views
are counted from (`viewsFrom`). An undated post's months begin with the first that may hold its views.

**An average per day divides by the days the post could have been read.** A month's days are the days
the website was measured in it, as on Insights, counted in the month the post was published from the day
it was published; an undated post is given the website's days, and the month running now counts today.
The answer carries the first day its first month counts (`from`) beside the website's first day.

**Its recent weeks are thirteen, a day at a time.** Monday to Sunday on the website's calendar, from its
reports (`PostRecord.RecentWeeks`): nought on a day nothing was read, and no figure on a day before the
website was first measured. The page sets each week's average per day against the week before's, and
averages the week running now over the days of it that have finished, so the morning of a new week never
reads as a fall. A day of the week the post was published in, before the day it was, is said to come
before it was published rather than to be a day nobody read it, wherever the picture below would open
on that day.

**Kept to people where the screen is.** As Insights. The answer's `completeTo` says how far the people are
settled: the judging of the reports it read, and of every kept month counted before judging had finished
with it while that month can still be counted again. The dashboard asks for everybody's answer beside
people's only where people's has nothing to show.

**Its picture is the website's picture of page views, kept to the post.**
`GET /api/sites/{id}/insights/post/series?path=…&granularity=…&from=…&to=…[&only=people]`
(`SiteInsightsPostSeries`) refuses a missing path first, then checks the way it is cut, the window and
whom it counts as the website's series does, refusing each in that series' own words; then it finds the
post as the page does, with the same 404 for a path that is no listed post, and answers the website
series' own contract: page views bucket by bucket from the post's series statement, complete for people
as far as judging has reached over the window. There is no measure to choose, because a kept month holds
a post's page views and no more. All time is never asked for: the page already holds every month of the
post, and draws all time by the month — or by the year, where the post's record runs across a first of
January — from those months, so the picture, the table and the three figures read one answer. All time
opens on the post's first day (`from`), so its first month is drawn as the days of it the post was
counted on. A shorter period that began before the post was published opens on the day it was, and
says so, wherever the post's record begins that day and none of its recent days before it holds a view;
one that ended before that day is said rather than drawn. A day before a post was published is not a day
nobody read it, and drawn as nought it would say so. An undated post, and one whose views were counted
only from later or were read before the day it states, is drawn over the whole period, every day as it
was counted. The period, the way it is cut and drawn, and the days a pressed bucket narrows it to are the
Overview's own, carried in the address and remembered as they are there; the period is chosen in the
picture's card, because every other figure on the page covers the post's whole life. Only a month whose
days are still held can be pressed. Nothing is drawn against an earlier period.

**The screen.** `/app/insights/post?path=…`, under Insights. A post's title on Insights opens it, and a
small link beside the title opens the post on the website in a new tab without saying where it came
from. The page names the post — its title in its own direction and only as text, or its address where
its page gave none — when it was published, and where it is read. It shows three figures for its whole
record — its views, the figure Insights shows; its average per day across the months that have a figure;
and its best month — its picture over the period chosen in the picture's card, the months and years of
0066 for the one post, by total views or by average per day, and its recent weeks, a table on a wide
screen and a card to a week on a phone. Where its views are counted from a month later than it was
published, it says since when. Where the address names no post the website lists, the screen says it
cannot find the post and leads back to Insights, or to the page of the list of every post it was opened
from.

**Every post, in one list.** `/app/insights/posts`, called All posts, is a screen of its own under
Insights: it lights the Insights tab, and the posts on Insights lead to it ("See all 1,234 posts"),
carrying whether the screen is kept to people. It is not a card on Insights. A list somebody works
through is not a record somebody looks back over; a card would add the list's reading to every visit, for
the many who never open it; and somebody coming back from a post would land at the top of a long screen.
Everything that makes the list what it is lives in its address — what was searched for, the order, the
page, how many to a page and the people — so the way back from a post is the same page of the same list,
a reload keeps it, and it can be sent to somebody. Each of those but the search is a new entry in the
browser's history; the search rewrites the entry it is typed on. The pages are numbered, as the User
journey list's are, one question and one address each: posts added on request would need the address to
say how many had been added for the way back to restore them, and would ask for every batch again once
the answers had expired.

**One question, on Insights' arithmetic.** `GET /api/sites/{id}/posts` takes `search`, `order`
(`most-read`, the default, or `newest`), `limit` (1 to 100, 25 unless said), `offset` and `only=people`.
It answers with how many posts the sitemap lists — the figure Settings gives — how many hold what was
searched for, the month views are counted from, the most any of those was read, so that a bar means the
same on every page, and the page asked for, each post as Insights carries one. A post's views are the
figure Insights shows for it: the same reading, credited by the same rule — a path another listed post
owns stays that post's — applied across every post the website lists before a search narrows them, so a
post a search finds is never credited with a path owned by a post it did not find. The most read come
first; of two read as much, the one published first, any with a date before any without, then by path.
That is the order Insights picks its most read post by, written once for both, so the first post listed
is that one. The newest come first, two published at once by path, and the posts that do not say when
they were published last, by path. A page holds at most a hundred, the most one question may name, and
only the posts on it are named. Everything asked is checked before the website is resolved, and a website
that does not exist and one the caller has no role on are answered identically. Where the sitemap lists
nothing, or nothing holds what was searched for, the answer is given without asking the telemetry store.

**A search is a value, and PostgreSQL compares it.** A post matches where its title or its path holds the
text — the path rather than the whole address, because every address a website lists begins with the
website's name, and a search for that name, or for the brand it is named after, would otherwise find every
post. An address pasted whole is compared by the path it names, spelled as a post's path is, so it still
finds its post (`PostMatch.PathIn`). Letters are compared without regard to case, with `%`, `_` and `\`
escaped so that each stands only for itself (`ILIKE … ESCAPE '\'`). The text is read as words — each run
of space or control characters one space, the NUL that PostgreSQL text cannot hold among them — and
refused over a hundred characters. It is bound to the statement, never part of it, and logged no more
than the path of a post's page is. Searching in the engine would read the title and the path of every
listed post — up to 50,000 of each, a path up to 2,048 characters — for every question, where PostgreSQL
walks one website's rows by its primary key and returns only the paths that match. There is no index of
trigrams: the comparison never leaves one website's rows, and an index would be an extension and a
migration to save part of that walk.

**The list on the screen.** All posts lays out Insights' rows — the title opening the post's page and the
small link beside it opening the post on the website, when it was published, and its views with a bar
against the most any matching post was read — under a box to search them in and a choice of order, with
the pages and how many to a page beneath. What is typed is asked about once typing has stopped for 300
milliseconds; Enter and clearing the box ask at once. Moving to another page takes the reader to the count
above the rows, which is also said to anybody listening. Where nothing holds what was searched for, the
screen says so and offers to clear the search; a page past the end offers the first; and each state of
the sitemap Insights explains is explained here the same way, with the same way to the sitemap's setting.
A post opened from the list leads back to the same page of it: the list's place travels in the post's
address and is read back through the list's own keys into an address on the list, so an address somebody
sends can lead nowhere else.

## What it does not do

- **Count a post's visitors, or name its best day ever.** A kept month holds a post's page views and no
  more, and days are held only as long as their reports.
- **Read a page that is not a listed post, or a post no longer listed.** A post taken off the sitemap
  keeps its months, which go unread.
- **Give days or weeks older than the reports.**
- **Set a post's picture against the period before it.** One measure is drawn, with no earlier period
  beside it.
- **Make the months add up to the views where more than one month is counted from reports.** Each
  month takes the larger of the two halves' counts for a visitor, so the months can come to a little
  more than the headline.
- **Know when a post really appeared.** A post re-dated later than it was first read has its new month
  of publication averaged over the days since the new date though its views include earlier ones, so
  that month's average reads high. Where those earlier views fall in the same month as the new date and
  before the recent weeks, its picture of a period opens on the new date, so they are in that month's
  figure and not on the picture.
- **Count a page that became a post only once its page was read into a month frozen before then.** That
  month reads nought for it.
- **Settle people in a month of posts that froze unsettled**, as 0069 says.
- **Search what a post says, or its path as it reads once unescaped.** Only the title and the path as an
  address spells it are compared, so a word in letters an address percent-encodes — `café` in
  `/caf%C3%A9/` — finds the post by its title, or by its whole address pasted in, and not typed alone.
- **Fold the case of every letter.** Case is folded as the database's character type folds it. On the
  image the stack ships — PostgreSQL 18 on Alpine, created `en_US.utf8` — accented, Greek, Cyrillic and
  full-width letters fold, but not a letter whose other case is spelled with more letters, so `straße`
  does not find `STRASSE`; a database a self-hoster created in the C locale folds ASCII letters alone.
- **Remember the order, the page or how many to a page between visits.** A visit from Insights opens on
  the most read, 25 to a page; the address holds the rest for as long as it is used.

## Consequences

- A page costs a read of the control plane — the website, then one or two lookups by its key — and, in
  the usual case where only the month running now is answered from reports, four statements: two small
  reads of what was kept, and two that read the website's reports, the list of pages since the month the
  reports take over and thirteen weeks of the post's days. A third, the post's months, is read where more
  than one month is answered from reports. With no index on a path, each reads the website's reports over
  its window, as the website's own series does.
- The picture of a period costs one more of those statements, over the period's window, each time the
  period or the way it is cut changes; all time's picture costs nothing the page has not already read.
- Two things change on every picture and every screen, the Overview's included. A figure drawn as a line
  or an area with no figure on either side of it — a month between months never counted — is drawn as a
  dot of its own wherever the picture is too long to carry a dot on every bucket, so a month the table
  gives a figure is never empty on the picture. And whatever the page is scrolled to — the period a
  pressed bucket hands the reader to among them — stops below the bar across the top rather than under
  it, the bar telling the page how tall it is.
- A page of the list costs a read of the control plane — the posts the website lists, those a search
  matches where there is one, and the names of the posts on the page — and, where anything is listed and
  matched, Insights' reading of its posts: two small reads of what was kept, a read of the views kept
  where any month of them is older than the reports, and one count of the reports since. Ranking a page
  needs every post's views, so each page, order and search costs that reading; the dashboard asks once
  typing has stopped and holds each answer for a minute.
- The list changes four things it shares with other screens. A post's row on Insights opens its page
  from anywhere on the row; every list's paging line sets thousands apart; on a phone, the paging of a
  list too long to number whole offers the page somebody is on and both ends, so it never runs off the
  screen; and on a phone the controls the Overview and the User journey list share with it — a choice
  between ways of reading, People only, the paging — are at least 40 pixels tall.
- The values bound are at most two paths of at most 2,048 characters, a few kilobytes once
  percent-encoded.
- No migration, and nothing new is collected or retained: the hosted service's privacy copy, which
  already names each post's monthly views, stands.

## Tests that make this true

The engine:

- `AnalyticsSqlCompilerTests`: the approved `Post_Page_Views_By_Day`, `Post_Page_Views_By_Month`,
  `Kept_Months_Of_Post` and each of them of people; `One_Posts_Series_Is_The_Websites_Series_Kept_To_Its_Paths`,
  `One_Posts_Paths_Are_Kept_To_Only_Once_Both_Halves_Are_Reconciled`,
  `A_Posts_Paths_Are_Bound_Rather_Than_Written_Into_The_Statement`,
  `Only_The_Questions_About_One_Post_Bind_A_Posts_Paths`,
  `A_Posts_Kept_Paths_Are_Picked_Out_Of_The_Newest_Count_Of_Each_Month` and
  `Asking_About_No_Path_Or_More_Paths_Than_One_Post_Is_Read_At_Is_Refused`; and
  `No_Posts_Path_Enters_A_Statement_Or_Its_Bound_Values`, which still holds every statement that counts or
  reads every post to naming none. The website's own series are approved unchanged, and the versions of
  both counting arithmetics stand.
- `PostRecordTests`, among them `A_Posts_Views_Are_Its_Months_Kept_Before_The_Reports_Take_Over_And_The_Reports_Since`,
  `A_Month_Counted_After_The_Post_Was_Listed_Without_It_Is_Nought`,
  `A_Month_Counted_Before_The_Post_Was_Listed_Has_No_Figure`,
  `An_Undated_Posts_Months_Begin_With_The_First_Month_That_May_Hold_Its_Views`,
  `The_Month_A_Post_Was_Published_In_Counts_Its_Days_From_The_Day_It_Was_Published`,
  `A_Post_Dated_Later_This_Month_Has_A_Record_That_Begins_Today`,
  `The_Recent_Weeks_Begin_On_A_Monday_And_Hold_This_Week_Among_Them`,
  `The_Recent_Weeks_Lie_Inside_The_Reports_Still_Held`,
  `A_Kept_Month_Counted_Before_Judging_Had_Finished_Holds_The_People_Back_While_It_Can_Be_Counted_Again` and
  `A_Single_Month_Answered_From_The_Reports_Is_Its_Views_Without_Being_Counted_Twice`; and the one-post
  paths in `PostsInsightsTests`.
- Against real stores, `PostReadingTests`: `One_Posts_Views_Fold_A_Servers_Report_Into_The_Visitor_A_Browser_Named_On_Another_Page`,
  `One_Posts_Series_Over_One_Bucket_Is_Its_Rows_On_The_List_Of_Pages` (both populations),
  `One_Posts_Series_Counts_Its_Own_Path_And_Where_It_Landed_And_No_Path_Like_Them`,
  `A_Long_Awkward_Or_Hostile_Path_Is_Bound_And_Matched_Exactly_As_Written` and
  `A_Month_Kept_With_No_Post_Read_In_It_Is_Nought_Rather_Than_Missing`; and `InsightsPostReadTests`:
  `A_Posts_Views_Are_The_Figure_Insights_Gives_It` (both populations),
  `A_Post_Is_Read_Where_It_Landed_As_Well_Unless_That_Is_Another_Listed_Posts_Own_Path`,
  `A_Month_Whose_Posts_Were_Never_Kept_Is_A_Gap_Rather_Than_Nought`,
  `A_Path_That_Is_No_Listed_Post_Is_Not_Found_As_A_Website_That_Does_Not_Exist_Is_Not` (an unlisted path,
  a page that is not a post, another website's post, other letters, no last slash, one character too
  long and a NUL, each answered with the whole problem a website that does not exist gets),
  `A_Path_That_Is_No_Listed_Post_Is_Answered_Without_Asking_The_Telemetry_Store`,
  `A_Long_Awkward_Path_Finds_Its_Post_Through_The_Address`,
  `The_Path_Asked_About_Travels_In_No_Trace_Of_The_Request` (the page and the picture both) and
  `The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads`.
- The picture, against real stores, `InsightsPostSeriesReadTests`:
  `A_Posts_Series_Counts_Its_Views_Bucket_By_Bucket` (by the hour, the day, the week and the month, its
  own path and where it landed, and no path like them),
  `A_Posts_Series_Of_People_Counts_The_Reader_Alone_And_Says_How_Far_Judging_Has_Reached`,
  `A_Posts_Series_Refuses_What_The_Websites_Series_Refuses_Before_The_Website_Is_Looked_For`,
  `A_Posts_Series_Refuses_A_Missing_Path_Before_The_Website_Is_Looked_For`,
  `A_Posts_Series_Of_A_Path_That_Is_No_Listed_Post_Or_Of_A_Website_Somebody_Cannot_See_Is_Not_Found`
  (without asking the telemetry store), `A_Posts_Series_Of_This_Month_Is_The_Month_Its_Page_Shows` and
  `A_Posts_Series_Is_Written_Under_The_Names_The_Websites_Series_Is`.
- The list, in `PostsInsightsTests`: `Every_Post_Is_Listed_With_The_Views_Insights_Credits_It`,
  `The_Most_Read_Come_First_Then_The_One_Published_First_Then_The_Undated_Then_By_Path`,
  `The_First_Post_Listed_By_The_Most_Read_Is_The_Most_Read_Post`,
  `The_Newest_Come_First_A_Tie_By_Path_And_The_Undated_Last_By_Path`,
  `Only_The_Posts_Searched_For_Are_Listed_And_Counted`,
  `A_Post_Searched_For_Is_Never_Credited_With_A_Path_Another_Listed_Post_Owns`,
  `A_Page_Is_The_Slice_Asked_For_In_Order`, `A_Page_Past_The_End_Is_Empty_And_Still_Says_How_Many_Match`,
  `Every_Bar_Is_Drawn_Against_The_Most_Any_Matching_Post_Was_Read_Whatever_The_Page` and
  `A_Page_That_Starts_Before_The_First_Post_Holds_None_Or_Is_In_No_Order_Is_Refused`, with Insights' own
  facts unchanged; `PostSearchTests`: `Nothing_Asked_Is_No_Search`,
  `A_Search_Is_Read_As_Words_With_Every_Run_Of_Space_Or_Control_Characters_As_One_Space`,
  `A_Search_As_Long_As_Allowed_Is_Read_And_One_Character_Longer_Is_Refused`,
  `A_Search_Is_Measured_Once_Its_Space_Is_Folded` and
  `Text_That_Is_Not_Well_Formed_Is_Read_With_The_Replacement_Character`; and `PostMatchTests`:
  `Wildcards_And_The_Escape_Character_Stand_For_Themselves`, `Every_Other_Character_Is_Left_As_Written`,
  `A_Whole_Web_Address_Is_Compared_By_The_Path_It_Names` and
  `Text_That_Is_No_Whole_Web_Address_Is_Compared_As_Written`.
- The list against real stores, `SitePostsReadTests`: `Every_Post_Is_Listed_With_The_Views_Insights_Gives_It`
  (both populations, its first post Insights' most read),
  `Posts_Are_Listed_Most_Read_First_Unless_The_Newest_Are_Asked_For`, `Posts_Of_People_Count_The_Reader_Alone`,
  `A_Search_Finds_Posts_By_Their_Title_Or_Their_Path_Whatever_The_Case`,
  `A_Search_For_What_Every_Address_Holds_Finds_Only_The_Posts_That_Say_It`,
  `An_Address_Pasted_Whole_Finds_Its_Post`,
  `A_Search_Holding_Wildcards_A_Backslash_Or_Quotes_Matches_Them_And_Nothing_Else`,
  `A_Search_That_Reads_As_Sql_Finds_Nothing_And_Changes_Nothing` (invalid percent-encoding and a NUL among
  them), `Posts_Are_Read_A_Page_At_A_Time_And_A_Page_Past_The_End_Is_Empty`,
  `However_Many_Posts_Are_Listed_A_Page_Holds_Twenty_Five_Unless_More_Are_Asked_For`,
  `A_Search_That_Matches_Nothing_Is_Answered_Without_Asking_The_Telemetry_Store`,
  `A_Website_That_Lists_No_Posts_Is_Answered_Without_Asking_The_Telemetry_Store`,
  `Posts_Of_A_Website_Somebody_Cannot_See_Are_Not_Found`,
  `A_List_That_Cannot_Be_Answered_As_Asked_Is_Refused_Whether_Or_Not_The_Website_Exists` (each refusal,
  and the order they are checked in, the same for a website that does not exist),
  `The_List_Is_Not_Read_By_Anybody_Signed_Out` and `The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads`.
  The website-list paging refusals, now read by one `QueryVocabulary.TryReadPaging`, pass unchanged.

The dashboard:

- `components/insights/post-insights.test.tsx`: "asks about the post the address names, exactly as it is
  written, on the website chosen", "shows the views Insights showed for the post", "averages its views
  over the days measured since it was published", "calls the months before an undated post's views were
  counted not measured, and those since months of no views", "says in words that a month never counted
  for the post has no figure", "averages the week the post was published in over the days since it was
  published", "says how each week's average moved against the week before, in words as well as colour",
  "says the post cannot be found once another website is chosen", "says nobody has read the post since
  its views were counted, where it was published before then" and "asks nothing about everybody while the
  people's record has views to show".
- `lib/analytics/recent-weeks.test.ts`, among them "averages the week running now over the days that
  have finished", "sets nothing for the week a post was published in until one of its days since has
  finished", "says the days of the week a post was published in before it was are before it, with no
  figure" and "comes to the same weeks whether or not it is told the day the post was published", with
  "says a day of this week before the post was published is that, in the table and on a phone" in
  `components/insights/post-insights.test.tsx`; `lib/analytics/post.test.ts`; and `components/insights/posts.test.tsx`, where a post's
  title opens its own page and the post itself opens in a tab of its own.
- `components/insights/post-chart.test.tsx`, the picture: "opens on the last 7 days by day where
  nothing is remembered", "opens on the period and the cut remembered last, and writes them into its
  address", "asks for the post's views over the window of the period on screen, of the people where the
  screen is kept to them", "draws all time from the post's months without asking the engine anything",
  "cuts all time's first month at the post's first day", "adds all time up into years, naming the months
  a year leaves out", "offers years only where the post's record runs across a new year", "narrows the
  period to the days of a pressed bucket and hands the reader to the period", "offers only the months
  still held day by day to be pressed under all time", "washes the buckets still being judged where the
  screen is kept to people", "puts a sentence in place of a period nobody read the post in" and "says
  what went wrong inside the card and keeps its controls"; and, for a period that begins before the post
  was published, "opens on the day the post was published, and says so beneath the picture", "says a
  period that ends before the post was published, and asks nothing about it", "draws every day of the
  period for a post read before the day it gives" and "draws every day of the period for a post that
  says no date" or "was published before its views were counted". With `lib/analytics/post.test.ts`
  ("lays a post's months out as the buckets of all time", "adds a year's months up and leaves a year with
  no figure nothing", and where a picture of a post over a period opens), `lib/api/endpoints.test.ts`
  ("asks for the post's views in buckets over a period, naming the post first"),
  `components/insights/post-insights.test.tsx` ("sets the picture between the highlights and the months,
  with the period in its own card", "asks only for the picture again when another period is chosen, and
  leaves every other card as it was"), `components/dashboard/activity-chart.test.ts` ("draws a month with
  no figure on either side as a dot", "keeps the dots out of the card a hover opens and out of reach of
  the pointer") and `components/chrome/app-header.test.tsx` ("tells the page how tall it is, again
  whenever that changes, and nothing once it has gone", so a control the reader is handed to stops below
  the bar). The Overview's own suites pass with the picture's parts shared, nothing in them changed but
  where one type is imported from.
- `test/vocabulary.parity.test.ts`: the dashboard lays out as many recent weeks as the engine answers,
  and says where a post's picture is cut for every way it can be cut and no other.
- The list, `components/insights/all-posts.test.tsx`: among them "lists the posts the most read first,
  each with how much it was read", "draws every bar against the most read of all the posts that match,
  not the first on the page", "opens each post's own page from anywhere on its row, and the post itself
  in a tab of its own", "names the page of the list each post is opened from, so the post's page leads
  back to it", "asks for what was typed once typing stops, rather than at every letter", "writes what was
  searched for into the address over the entry it was typed on, from the first page", "says no post
  matches, and offers to clear the search", "moves a page at a time, writes the page into the address
  and brings the reader to the top of it", "keeps the page it shows while the next one is read, and says
  it is busy", "tells anybody listening about each answer once, with which of the posts the page shows",
  "opens on the search, the order, the page and the size its address names", "starts again from the
  first page once another website is chosen, keeping what was searched for", "says a page past the end
  has nothing on it, and offers the first page", "never shows everybody's views while the people's are on
  their way", "makes every control large enough to press on a phone", and the sitemap's states, the way
  it stands in and what it says where it is refused. With `components/insights/posts.test.tsx` ("offers
  every post on a screen of its own, carrying who the screen is kept to", "offers the one post a sitemap
  lists on the screen of every post, in words that fit one"), `components/insights/post-insights.test.tsx`
  ("leads back to the list it was opened from where the post can no longer be found"),
  `lib/analytics/post.test.ts` ("names the page of the list of every post it is opened from, as the list
  writes it"), `lib/analytics/posts.test.ts` ("reads a search as words, as the engine does, no longer than
  it reads one", "reads a page only as a whole number from one up"), `lib/paging.test.ts` ("offers a phone
  the page somebody is on and both ends, and the pages beside it only where there is room"),
  `lib/api/endpoints.test.ts` ("asks for a page of the most read, saying nothing of an empty search or of
  everybody", "refuses an answer that does not say how many match") and `test/vocabulary.parity.test.ts`
  ("stops a search in the box exactly where the engine stops reading one", "offers no page longer than
  the engine will name at once", "orders the posts only in the ways the engine lists them, and names
  each").

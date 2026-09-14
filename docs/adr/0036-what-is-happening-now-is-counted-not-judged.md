# 0036 — What is happening now is counted, not judged

- **Status**: accepted
- **Date**: 2026-08-28
- **Applies to**: both editions, and the whole of it is in the free product. Adds four statements
  and two endpoints, collects nothing new, retains nothing new, and needs no migration. Nothing in
  `integrations/` changes, because nothing about what is captured changes.
- **Follows**: [0035](0035-a-visit-is-judged-on-all-of-itself.md), which settled that a visit is read
  once it is over and removed the column that would have expressed a verdict reached before then.
- **Follows**: [0034](0034-a-visit-begins-where-somebody-arrived.md), whose consequence — that visits
  by one visitor may overlap — is the reason nothing here is counted per visit.
- **Revised by**: [0039](0039-what-is-being-read-is-where-the-visitors-are.md), which counts the
  pages being read as visitors by the page each is on, so they add up to the headline, and reads a
  trail under the reading its row came from rather than under a fresh present moment.

## Context

The product can say a great deal about a visit that has finished and nothing whatever about the site
as it is being read. A reader who wants to know whether something is sweeping their site right now
has no screen to look at, and the first thing they would ask about a crawler is when.

The obvious way to build one is to run the engine over each visit still in progress and show the
answer. That is the thing this decision refuses, and the reason is not caution about the interface.

**A conclusion drawn part-way through is frequently the opposite one, in the colour reserved for
traffic nobody asked for.** `RenderingDetector` reports `automation.no_script_execution` at weight 50
whenever something in the request path saw a visit and no script has run. On the arrangement this
product recommends — a reporter on the site's own server alongside the tracker in the browser — the
server's report reaches the collector first, and for that interval a person quietly opening an
article has executed nothing. `EvidenceScorecard.Weighed` has no other reading available: nothing
points toward a person yet, one thing points away, and the answer is `SuspiciousAutomation` on weak
evidence. Worse than a label that corrects itself, the two halves are two visitor keys until the
browser echoes its correlation identifier, so the screen would show a _second_, red row that later
disappears. The tracker sends nothing behavioural for its first fifteen seconds, so the opening of
every visit is evidence-free by construction.

Nor is it only an arrival. `EvidenceScorecard.Decide` falls through to a weighing for everything not
caught by an ordered rule, and two of its ordered rules can also be withdrawn: `probing.missing_paths`
is weighed as the share of a visit spent asking for pages that are not there, and a share falls as a
visit goes on; the systematic-retrieval rule requires that nobody has been observed reading, so a
content scraper stops being one the moment somebody reads for two seconds.

0035 named the shape of the harm in its own words: two answers about one visit "answer different
questions and are printed six lines apart", and called that the defect it existed to remove. A live
screen and the visit list would print them half an hour apart, under the same words, in the same
pills.

**Separately, "a visit that is not over" is not "somebody who is here".** Closure is `max(server_ts)`
against an idle timeout, and under 0034 a report about a page belongs to the visit that page was
arrived at in however long the silence before it. So a tab dismissed the next morning reopens a visit
whose reader left hours ago, and a reader with an old tab open has two visits running at once. A
count built on it is wrong in both directions.

## Decision

**The live screen counts visitors over a trailing stretch of minutes, and judges nobody.** The unit is
a visitor with any report inside the stretch, not a visit — one row per reconciled visitor key, no
visit ordinals, no overlap, nothing reopened by a report that arrived late about something that
happened this morning. Both counterexamples above simply do not arise, because there are no visit
boundaries in the answer for them to fall across.

**The stretch is one idle timeout, taken back to a whole minute.** Being here and not yet having
finished are one idea, so they are one number: `ClassificationOptions.IdleTimeout` and not a second
constant that would drift from it. The near end is aligned to a minute so the drawing of it has whole
minutes to draw; the window is therefore a little wider than the silence and never narrower, which is
the safe direction.

**Nothing is read from outside the stretch.** Every other reconstruction in this product reaches a
full day either side, because a visit has to be read from where it began. These do not: they describe
a few minutes, and a report outside them is not part of that description. That is what makes the
reading a prefix of the primary key with no window functions over visits behind it, and so cheap
enough to ask every few seconds — and it is asserted rather than described, in
`AnalyticsSqlCompilerTests.No_Reading_About_Now_Reaches_Outside_Its_Own_Minutes`.

**A visitor is named only where the naming cannot be withdrawn.** `SettledIdentity` keeps a verdict
only when it rests on an observation that accumulates: an operator vouching for the address, a
crawler naming itself, a browser declaring it is being driven, a request for a path only an intruder
asks for, or a name worn while arriving from another company's crawler addresses. Everything reached
by weighing is withheld, and so are the two ordered rules that can be withdrawn. The rule is written
as a set of observations rather than a set of categories, because what a category means is decided by
the order of the scorecard's rules and a second copy of that order would be kept in step by nobody.

**Everybody else is counted, listed, and called nothing at all.** There is no category meaning "we
have not looked long enough", and borrowing one that means something else would be this product
saying a thing it does not know in order to fill a column. A visitor with no conclusion carries no
category, no band, no ruleset and no evidence, and their settled verdict appears on the screen that
reports finished visits.

**No name server is asked about anybody in a live reading.** `SessionClassifier` will ask about an
address behind a visit that came out looking like machinery and refuses to ask about one that came
out looking like a reader, because a reader's address is not ours to ask anybody about. Half-way
through a visit that distinction does not exist — a person who has not scrolled reads as machinery —
so asking here would send readers' addresses to third parties on the strength of their not having
scrolled yet. What a company publishes about its own machines was settled by the collector and
travels as an answer, which is enough and is the only identity these readings use.

**One visitor's trail is a fourth statement over the same minutes, not the visit reconstruction
narrowed.** The trail is opened from a row, so the pages it shows must be the pages that row printed,
and that holds only while both are one visitor's activity over one window gathered by the page —
which `AnalyticsSqlCompilerTests.A_Trail_Counts_A_Page_The_Way_The_Row_It_Was_Opened_From_Counted_One`
holds them to. Reusing `SiteVisitJourneyQuery` would have broken it three ways over. That statement is
read from where a visit began to as long as one may last and clamped to the instant a verdict was
written — which for a visit still under way is no upper bound at all, until a verdict appears and
snaps it back. It reads forward with no reach-back, so a departure arriving from a stale tab is folded
into whichever visit last arrived at that page and can print an hour of reading against a page opened
three minutes ago. And its page count is taken over a different window again, so a panel built on it
would have said "three of four pages" for a reason that is nothing but a difference in timing.

**The trail establishes nothing about the visitor, and asks no catalogue.** Where they are, what they
are reading on and who sent them travel on the row it was opened from, settled over these same
minutes. Answering the question a second time moments later would be a second answer free to disagree
with the first, and would be the thing this decision exists to prevent, arriving through the back door.

**Nothing is written.** 0009's removal of the provisional flag stands: no verdict is stored before a
visit is over, nothing here supersedes anything, and nothing here is counted into any total the rest
of the product reports. What a reader sees on this screen is derived at the moment they ask and kept
nowhere.

**A reading gives up rather than running on.** Every one of these statements carries
`max_execution_time`, because the store's default is no limit and a screen that renews itself asks
again whether or not the last answer has arrived. A limit on time rather than on the caller going
away, because the store's setting for the latter does not work: a query whose client has disconnected
runs to the end, and the issue asking for that to change is closed as not planned. The answer is also
`no-store` and varies on the cookie, since nothing in the address distinguishes two callers and some
of these answers carry a renewed sign-in with them.

**The address a visitor arrived from is not selected.** The visit reconstruction carries it, for one
purpose that does not exist here, and a statement written as a near-copy of that one would be a line
away from carrying raw addresses into something screen-facing. It is a test rather than a convention:
`AnalyticsSqlCompilerTests.No_Reading_About_Now_Asks_For_An_Address`.

## Consequences

**A customer can be told the thing this product exists to tell them, at the moment it matters.**
"GPTBot is on your site now, verified from OpenAI's own published addresses" is a claim this product
can make about a visitor who has not finished, and stand behind entirely. No competitor's live screen
makes it, and it is the only live claim here that rests on something the visitor did not author.

**A busy site's live screen will often name nobody, and that is the design working.** On a site whose
traffic is people, the screen is a count, a shape of the last half hour, and a list of pages — and
every row says nothing about who. A reader who wants to know what their traffic was made of is
reading about visits that have finished, where the answer is settled.

**The honesty is a build gate, not an intention.** `SettledIdentityTests` replays every kind of
visitor these tests know about request by request and asserts that once something has been named it
is still named as the visit goes on, and is never afterwards called a person. Adding an observation
to the settling set that can be withdrawn fails two tests, which was checked by adding one.

**A live name may sharpen, and only in that direction.** Something asking for an administration panel
is a scanner until the address it arrived from turns out to belong to a company that publishes it, at
which point it is that company. The ordered rules allow no other movement, and machinery never
becomes a person.

**Three questions on every beat, and a fourth only where a row is open.** Who is here, how much
was read minute by minute, and which pages — taken one after another over the same minutes of one
site, which the store serves from the same marks. Asking for three at a time would treble what one
watched screen costs the store on every beat. A trail is a fourth, and it is asked only while
somebody has a visitor open, which is why it is not on that beat by default.

**A count of visitors is not a count of people, and this screen is where that shows most.** A visitor
key mixes in the day the report was observed and the network it arrived over, so it changes at
midnight and again when a reader moves between networks. On a thirty-day total the resulting split is
a rounding error already discounted; on a half-hour view it is the whole view for the half hour after
midnight. The screen therefore says visitors and never people, and reports no duration of presence —
only when somebody was last seen. The fix is the one 0034 already names, a report carrying the
identity of the page view it is an account of, and it is not this change.

**A trail and the same visit once it has finished disagree in exactly one place, and it is visible.**
Somebody who leaves a page and comes back to it inside the window is one page on the trail and two
steps on the finished visit. Both are honest answers to the questions they were asked: a finished
visit knows where each arrival was, and a trail has no visit boundaries in it at all — which is what
makes it answerable about a moment — so it has nothing for "again" to mean against. The trail agrees
with the count printed beside it, which is what a reader compares it to, and the finished visit agrees
with the arrivals it was rebuilt from. It is a test rather than a note:
`LiveTrafficTests.A_Trail_Shows_A_Page_Somebody_Returned_To_Once_Where_A_Finished_Visit_Shows_It_Twice`
fails if the difference ever becomes a second one.

**A key that could name a visitor is one thing, stated once.** The identity of a finished visit and
the visitor a trail is opened for are two addresses somebody can type, and both are refused at the
edge for being the wrong shape. `VisitorKeys.IsWellFormed` is that shape; a second copy of it would
eventually accept on one screen what it turned away on the other. Neither refusal is what stands
between a hostile value and a statement — a key always reaches the store as a bound value.

**Nothing has been measured against a busy installation.** The shape was chosen to avoid the
expensive path rather than because the expensive path was measured and found wanting: these readings
are a range scan over one site's last half hour with no reconstruction of visits behind them, where
the alternative would have run the eight-expression visit grouping over a day of activity every few
seconds per watching screen. That argument is sound and it is not a measurement. What to watch is
`read_rows` and `memory_usage` in the store's own query log on an installation with real traffic, and
the first lever if it is wanted is a short memory of one reading per site per beat, so that ten people
watching one site cost what one does.

**A screen that asks for ever also signs somebody in for ever, so it does not ask for ever.** The
sign-in cookie slides: every answer that carries a renewed one puts the fortnight back to its full
length, and a screen left open on a monitor would hold an account signed in indefinitely without
anybody touching it. Two rules bound that, and both were wanted for their own sake anyway. A tab
nobody is looking at asks nothing — the beat still falls and the question is skipped — so a browser
left open overnight costs a browser left open. And the screen carries a way to hold it still, which
exists because reading a list that rearranges itself under you is the one thing a live screen makes
harder than a static one, and which stops the asking as a consequence.

**A row somebody has open outlives the visitor behind it.** Everything else follows the reading
exactly, in the order it arrives, and is never sorted again in the browser. But a panel disappearing
from under a reader part-way through it is the one failure a screen like this can actually cause, so
an opened row is kept after its visitor has left the window, marked as gone, with its trail frozen
where it stood; closing it lets it go on the next beat. That rule is a pure function tested without a
browser, because it is the piece of this screen that is easiest to get subtly wrong and hardest to
notice.

**The minute the reading was taken inside is short, and is said to be short three times.** Thirty
one-minute columns are the shape of the half hour, and the last of them is a few seconds old rather
than sixty. Drawn plainly beside twenty-nine finished minutes it is a cliff, and a screen that
redrew that cliff every few seconds would be reporting a collapse that never happened. So the
running minute is faded at the same strength a bucket still being judged is faded on the overview,
and marked in the table the drawing publishes — twice, because a fade is nothing at all to somebody
reading rather than looking, and this is the one figure on the screen that is genuinely incomplete
rather than merely unjudged. A clause under the drawing explains the fade, and appears only where
there is a faded column to explain: a half hour that has gone quiet ends on a minute that is both
still running and empty, and there a sentence about the last column still filling would read as an
excuse for the silence beside it rather than as a note about the newest figure. Which minute is
running comes from a pure function over the reading's own instant, not the browser's clock, so an
answer left on screen after the engine stopped replying goes on describing the moment it was taken.

**The pages being read end in a count, not in a share.** Only the leading handful come back from a
reading, so a share would be taken against the part of the half hour that fitted on the card while
reading as a share of all of it. The half hour's true total is not put beside them either: it is
counted per minute and per page by rules that agree almost always and not quite always, and a whole
that can disagree with its parts is worse than no whole at all.

**Two things on this screen are commercial and neither is built.** Being told when something starts
sweeping, rather than having to watch; and what one operator seeing many customers' traffic can say
about a network that no self-hosted installation could work out. Both are named here so the boundary
is visible, and both are absent from this change rather than present as empty slots — a seam with
nothing behind it is scaffolding, and the place to cut one is the change that gives it something to
carry.

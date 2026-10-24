# 0052 — A row says on its first line how somebody arrived

- **Status**: accepted
- **Date**: 2026-10-23
- **Applies to**: both editions, and the whole of it is in the free product. Changes the shape of
  three statements — both forms of the visit list and the reading of who is here — and two wire
  records; adds no stored column, no migration and no index; collects nothing new and widens no
  retention envelope. Nothing in `integrations/` changes, because nothing about what is captured
  changes.
- **Follows**: [0036](0036-what-is-happening-now-is-counted-not-judged.md), whose reading of who is
  here describes a stretch of minutes and nothing outside it — which is why where a visitor came
  from cannot be read off that stretch without also saying whether the stretch saw them arrive.
- **Revises**: [0038](0038-a-list-says-what-each-visit-was.md), which put where a visit came from on
  a row's second line beside where it was and what it was read on, left the page it began on to the
  panel opened from the row, and had a visit whose activity has aged out read "Came straight here".

## Context

Each of the two lists is scanned for one fact before anything else, and neither row said it where
a reader looks first.

**On the live screen it is where somebody came from** — a search, an AI assistant, another site,
or straight in. The row said what the visitor was being called, which page they were on, and
roughly where they were and on what. Where they came from was in the panel, a press away, on every
row. The reading already carried it: the live row has carried the opened visit's eight-field
account since the live screen was built (0036), 0038 gave the journeys list the same account, and
the panel shows its "Came from".

**On the User journey list it is which page a visit landed on.** 0038 put where the visit came
from, roughly where, whose network and what it was read on under the verdict; the page it began on
was the first step of the trail inside the panel. The rebuild behind the list already settled it,
because the "Landed on" narrowing reads it (0029). It never reached the row.

**Putting the live source on the row exposed what the panel had been hiding.** The live reading
names as a visitor's sender the earliest report inside the half hour that named another site. A
visitor who arrived from a search forty minutes ago and is still reading has no such report inside
the half hour, and the panel said "Came straight here" about them. That was false, and tolerable
only while it sat a press away. A row's first line is read as a headline and cannot say it.

## Decision

**The live row's first line ends with how the visitor arrived.** After the naming, the strength, a
confirmed name and "Gone" where each applies; introduced by a middot so that a band and an arrival
do not run together into one phrase ("strong signs From Google"), and last so that an arrival and a
departure do not either ("From Google Gone"). It stays while the row is open, because it is the
row's heading. The line beneath it that says roughly where and on what still hides while the row is
open, because the panel lays those facts out in full (0038).

**That nobody sent a visitor is said only where the reading saw them arrive.** The reading of who
is here carries `seen_arriving`, worked out per visitor by `LiveActivity.SeenArriving`. A visitor
is seen arriving unless the window holds none of their page views, holds something of theirs from
before the earliest of them — whichever surface reported it — or the page view that decides
positively names the site itself as where it was reached from: a referrer with a host that the
reduction in `SendingSites` turns into no sender, the site or one of its subdomains. The page view
that decides is the tracker's own first, because the tracker reports what the browser says sent
each page; otherwise the earliest from a surface able to say; and the no-script image's only where
nothing else reported a page, because it never says. A progress report, a departure or a press
ahead of any page view is somebody already reading a page, and a page reached from another page of
the same site is somebody who was already here. Three phrases follow, written by one function,
`fromWords`:

| The reading holds                                        | The row says         |
| -------------------------------------------------------- | -------------------- |
| a page view naming another site, anywhere in the window  | "From {site}"        |
| no such page view, and the visitor was seen arriving     | "Came straight here" |
| no such page view, and the visitor was not seen arriving | "Arrived earlier"    |

Where the visitor arrived earlier, the panel opened from the row leaves its "Came from" fact out
rather than guess. `seen_arriving` belongs to the account a reader is shown and never to the
evidence: it is not in `SessionEvidence`, no detector reads it, and nothing the engine concludes
rests on whether a stretch of minutes happened to hold an arrival. The statement reads the
referrer's host to settle both answers and adds only a one or a nought to what it hands back: the
referring address never leaves it, and the host leaves it only as the sender it names.

**A finished visit is seen arriving wherever its first page is on record.** A visit whose activity
has aged out of the store lists with an account that establishes nothing (0038), and so names no
sender — not because its arrival named none, but because nothing of its arrival is left. 0038 had
it read "Came straight here", noting that the store held no difference between the two. The page a
visit landed on is that difference: a visit with no page on record and no sender says nothing about
where it came from, on its row or in the panel opened from it, rather than "Came straight here". Its
row then says nothing beyond what it was judged to be — the honest account of a visit nothing is
left of. Only that half of the live rule is applied to a finished visit; what its first page was
reached from is not asked, and the consequence is recorded below.

**The User journey row's first line ends with the page the visit landed on.** "Landed on" and the
address, written the way every address on the dashboard is written (`readablePath`), from the
column the "Landed on" narrowing reads — `JudgedVisitDetails.EntryPage` — so the row and the
narrowing that found it name one page one way. It is read back as a ninth column after the eight
the visit's account is read from, never as one of them: the account is what the opened visit gives
of itself, read from both rows by one helper, and has to stay the eight (0038). A visit whose
activity has aged out, or that opened no page, says nothing there rather than "/", which would name
the front page.

**One component draws the fact, and one function writes an arrival.** `RowFact` places the middot
with its words, hidden from anybody listening, and isolates the words so a name written right to
left does not rearrange the line. The words may take two lines and break anywhere: at phone width a
badge leaves about twenty characters, one line of "Landed on" cut short says little more than that,
and a title says the rest to a pointer and nothing at all under a finger. How a visitor arrived is
worked out once, by `cameFrom`, from what was established and whether the arrival was seen — the
row knows which, and hands the answer to the panel it opens rather than letting the panel work it
out a second time — and written once, by `fromWords`, which the live row, the line under a
journeys row and the opened panel all use. An arrival nobody saw is written only on the live row,
as "Arrived earlier"; everywhere else it is a fact nothing answered and is left out. The panel
keeps naming the site alone under its "Came from" label, as 0038 settled, because "From Google"
there says "from" twice.

**A direction control cannot rearrange an address, and an address runs left to right.**
`readablePath` decodes an address for reading and then writes back as codes the characters whose
only effect is to change the direction text runs in — Unicode's `Bidi_Control` set, twelve marks,
embeddings, overrides and isolates — whether they arrived encoded or not. One placed part-way
through a page's name turns the rest of the name around, so "/invoice", the control and "fdp.exe"
is drawn as "/invoiceexe.pdf". The "Landed on" choices in the filter, and the pills they leave
behind, go through the same function, while the value the engine is asked for stays exactly as
recorded; a pill keeps the whole of its value reachable as a title on the value itself. Wherever an
address is drawn on its own — a step of a trail, the lists of pages on the overview and on the live
screen, a "Landed on" choice and its pill — it is set to run left to right, as RFC 3987 §4.1 asks
of an address holding right-to-left letters; left to find its own direction, one whose first part
is written in Arabic would be drawn back to front. Any value written right to left begins at the
start of its line in a list of choices, like every other, rather than at the far end of a box the
width of the row.

## Consequences

**A live row can change its first line while it is being read.** As the half hour slides past
somebody's arrival, "From Google" becomes "Arrived earlier". That is the reading knowing less, not
the visitor changing, and it is the price of 0036's rule that nothing is read from outside the
window. A visitor who was already here and then came back through a search reads "From Google" and
was not seen arriving; both answers travel, and the row leads with the sender.

**A direct arrival on a page the tracker does not run on, followed by one it does, reads "Arrived
earlier".** The tracker's first report names the site itself, and it is taken ahead of the server's
report of the earlier page. The finished visit says "Came straight here". Preferring the tracker is
still right: a reporter on the site's own server need not pass on where a page was reached from,
and a server report naming nothing would otherwise make somebody already clicking through the site
read as having just arrived.

**A visitor only a silent surface saw reads as seen arriving.** The image a page with no script
running asks for reports no referrer, so it decides only where nothing else reported a page, and a
referrer naming no host at all, such as `about:blank`, reads as nobody having named a sender. Each
then reads "Came straight here", which is what the finished visit says about the same reports. A
no-script direct arrival on a page the server's reporter missed, followed by one it reported,
reads "Arrived earlier" — an understatement, the same trade as preferring the tracker.

**A site whose pages send no referrer to one another can still read somebody already here as
seen arriving.** Under `Referrer-Policy: no-referrer`, as a header or a meta tag, or in a browser
set to send none, a page reached from another page of the site names nothing. Usually the
visitor's own reading of the page before — its progress reports, its departure — is in the window
ahead of it and says they were already here. Where none of it is, they read "Came straight here"
on the live row, while their finished visit names whatever sent them.

**A finished visit that opens on a page reached from the site itself reads "Came straight
here".** A visit begins at the first page view after the idle timeout, and at the first under a
visitor key that changed at midnight UTC or with the network (0034), so somebody still clicking
through the site across either boundary begins a visit whose first page names the site as where it
was reached from. The list and the panel opened from it say "Came straight here" where the live
row would say "Arrived earlier". Bringing the two into line means asking the finished visit's
first page view the live rule's question, which moves `Judged_Sessions`,
`Judged_Sessions_By_Detail` and the listed visit's wire record again; it is left as a decision of
its own.

**Right-to-left letters still read the way their script does.** Running an address left to right
keeps its parts in order where right-to-left letters stand alone between them, but two
neighbouring parts written right to left, or one followed by a number, are drawn in that script's
order: "/docs/سلام/2024/report" is drawn "/docs/2024/سلام/report". RFC 3987 §4.4 shows the same
of every address displayed that way, in its examples 2 to 8. Drawing each part in an isolate of
its own would hold the recorded order; it is not done here. A row that says an address inside a
sentence — "Landed on …", "Reading …" — takes the sentence's direction.

**A sender this product has no name for is on the first line verbatim** — as text and never as a
link, as it already was on the journeys row and in the panel. Referrer spam that names a host
therefore reaches a headline. The answer to that is recognising spam, not hiding the host.

**"Came straight here" is still an attribution category, not evidence that somebody typed the
address.** The master plan's 8.3 still owes copy that says so.

**Two wire shapes change together.** A listed visit gains `entryPath` and a live visitor gains
`seenArriving`; the dashboard requires both, so the engine and the dashboard land in one release,
as they always do. There is no shim.

**The live reading reads nothing more from the store.** The referrer's host was already read to
work out the sender; it is now carried one step further and folded into one more aggregate per
visitor. `Live_Visitors`, `Judged_Sessions` and `Judged_Sessions_By_Detail` moved, and were approved
after reading; no other approved statement changed.

**What holds it.** `AnalyticsSqlCompilerTests` pins the rule's text, that the tracker is read first
and the image last, that the reading hands back no referring address, and that a listed visit
names its page from the column the narrowing reads. Against the real store, `LiveTrafficTests`
covers a search inside the window, a first page naming no sender, a first page reached from the
site, somebody only reading, a return through a search, a site reported only by its server, a
referrer naming no host, the tracker taken at its word ahead of a server report that named nothing,
the image's silence read after a server report that named the site, a visitor only the image saw,
and somebody already reading whose next page named nothing;
`VisitDetailsTests.Narrowing_To_The_Page_A_Visit_Landed_On_Finds_The_Visit_Naming_It` and
`TrafficReadTests.A_Listed_Visit_Names_The_Page_Its_Steps_Begin_On_And_The_Filter_Finds_It_By` hold
the row, the trail and the narrowing to one page; and
`LiveReadTests.A_Row_Says_Whether_The_Reading_Saw_The_Visitor_Arrive` holds the wire. The rule for
an arrival is held by `visit-context.test.ts`, the screens by `site-live.test.tsx`,
`journeys.test.tsx` and `visit-journey.test.tsx` — including a visit nothing of whose arrival is on
record — the direction controls by `pages.test.ts`, and an address running left to right by the
trail's, the page lists' and the "Landed on" filter's own tests.

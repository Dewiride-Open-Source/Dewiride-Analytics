# 0073 — A page's name joins two keys only when it can mean one person

- **Status**: accepted
- **Date**: 2026-11-15
- **Applies to**: both editions, and the whole of it is in the free product: the statements that
  read raw activity (`Infrastructure.ClickHouse/Analytics/ReconciledEvents.cs`), the server
  collector (`Api/Endpoints/ServerCollectEndpoint.cs`) and its limits. No column, no migration. The
  arithmetic a kept month is counted with moves to version 2, for its totals and its posts; the
  ruleset does not move. The shortest idle timeout an installation accepts rises to half an hour.

## Context

A reporter on a website's own server and the tracker in the browser both see every page a person
reads, and each derives its own key for the visitor from the address the request arrived from. The
two addresses need not agree — the page and the collector are different hosts, and a visitor whose
network offers both kinds of address reaches one over each — so the reporter names the page it
delivered, `Server-Timing: dw;desc="…"`, the tracker sends the name back, and the two keys are
recognised as one person (`docs/server-reporting.md`, "Naming the delivery").

Until now a name joined the two keys on the strength of any browser that sent it back. Each name
found the smallest browser key among those that echoed it, and the reporter's key was then re-keyed
to that browser for everything it did in the window being read. Three things were wrong with that,
and all three become real the day a reporter stamps names, which the WordPress plugin's server half
will be the first to do:

- **A page cache hands the name to whoever asks.** Batcache, which WordPress.com and Pressable run,
  keeps a page once it has been asked for twice in two minutes and serves it for five, replaying
  every header; a host's Varnish or nginx cache and Cloudflare's HTML caching do the same. One
  delivery's name therefore comes back from every reader the copy reaches, and a crawler's request
  can come back from a stranger's browser. The smallest key among them was an arbitrary reader, who
  was handed the reporter's whole day — and could then be judged on a crawl it never made.
- **Nothing was local.** An answer in the morning re-keyed what the reporter's key did in the
  evening.
- **Nothing was settled.** A later answer from another browser changed which key was smallest and
  re-keyed reports in visits already judged, which leaves a verdict about a visit that no longer
  exists under its name.

No stored report carries a name. Read on the hosted installation on 2026-11-15: 113,445 reports
since 2026-08-19, none with a correlation identifier, from a browser or from a server.

## Decision

**An answer is one browser, in time.** A name is _answered_ by a browser when that browser's key is
the only one to have sent it back within two minutes either side of the reporter's report
(`EchoedDeliveries.AnswerWindow`), and the reporter's key the only one to have given it in that
time. Either report can reach the collector first. A name two or more browsers sent back within that
time was handed out by a cache, and a name a second reporter key also gave cannot be one delivery to
one visitor: either is a name the reporter gave that nobody in particular answered, and counts as
one. A browser sending a name back later than the window is reading a copy, and is not counted at
all — which is what keeps the reader who first asked for a page its own answer however many readers
the cache serves afterwards. Each report is read on its own, against what carried its name within
the answer window of it and nothing further — so the same name given again later, a cached response
reported a second time or a batch sent twice after the day's keys were renewed, is read as a report
of its own, then and there, and changes nothing about the first. Read against the first report
instead, it would decide differently in a reading that reached back to the first and in one that did
not, and could take back hours afterwards a decision a verdict already rests on. A name nobody sent
back is a page whose tracker never ran.

**A report is given to a browser only when that browser is the only one, and answered at least
half.** Each report a reporter's key made at instant _t_ is given to browser _B_ when, among the
reporter's names delivered within 28 minutes either side of _t_ (`EchoedDeliveries.Reach`), _B_ is
the only browser that answered any, and _B_'s answers are at least half of all of them, answered or
not. That keeps a redirect, a page that was not there, a feed, or a page whose tracker never ran
inside the reader's visit. The half is what stops a key that named hundreds of pages and was
answered once — a crawler whose copy a cache passed to one reader — being folded into that reader.
Being the only browser is what stops one address shared by several people — a household, a relay
that shares its addresses among many — being folded into any of them.

**Whose a report was is settled before its visit is judged.** The decision reads nothing further
from a report than the reach and the answer window together: thirty minutes
(`EchoedDeliveries.Settled`). A visit is judged once it has been quiet for the idle timeout, so the
idle timeout may not be shorter than that: `ClassificationOptions.IdleTimeout` accepts half an hour
to twelve hours, checked at start-up, and a test holds the bound's literal to the figure. Every
report in a judged visit therefore had everything that decides it before the verdict was reached,
and nothing arriving later can give it to somebody else.

**No answer is waited for.** A name not sent back yet counts as unanswered at once. Waiting would
need a moment to wait from, and none of the statements is given one: taken from the newest report in
what is read, a name would stay pending through a quiet night and settle when the morning's first
report arrived — after the visit holding it had been judged. The half makes this cost little. A
reader whose newest page has not been answered yet still has half of two answered, so the live view
shows one person; only a reader with two pages in flight at once shows as two, for the second or two
before the answers arrive.

**Written so the store adds each row once and holds little.** The reports of each name are gathered
by grouping, which the store can spill to disk once a statement reaches its memory limit, and not by
joining a name's reports to one another, whose table has to fit. The rule over them is three counts
and two ends: how many of the reporter's names lie within the reach, how many were answered, and the
first and last answer among them. The same browser at both ends, with as many of its own answers
between them as there are answers in the reach, is the only browser that answered. Every frame opens
where the reporter's activity does — a count up to the reach's far edge less a count before its near
one, and the first answer read in reverse order — because ClickHouse starts a frame's aggregate
again whenever the frame's start moves, and a frame that slid would cost, for a crawler answered
once, the square of what it fetched. A report that carries a name is decided where its name stands,
so it is laid out once. What is kept is only the instants at which the answer changes for a
reporter, usually one or two per key, and each report takes the answer of the latest change at or
before it — a nearest-earlier lookup, so the table every report is looked up in is that short list
rather than an entry per report. Two keys are told apart by the least and greatest differing, which
is all that has to be known.

**The arithmetic of a kept month moves to version 2, for its totals and for its posts.** Both
fingerprints include the reconciliation, so `CountingRules.Current` and `CountingRules.Posts` go
from 1 to 2 and every finished month still held is counted again. **The ruleset does not move.** No
verdict rests on a name, because none was ever stored; raising it would judge every visit since
August again to reach the same answers.

**The collector says what a key is for.** Its answer gains `site` (the website the key belongs to),
`hosts` (the hostnames the website covers, each standing for itself and every hostname below it: the
allowed origins where set, otherwise its address; empty while its account is closed), `measuring`
(whether anything sent is kept: false while the account is closed, and on the hosted service while
the plan has stopped admitting reports) and `from` (the address the batch arrived from, which is
where the reporter's own server's requests to its own website arrive from too). A batch with no
observations stores nothing and is answered in full, so it is how a reporter checks its connection.
A reporter treats every field past the two counts as optional, because a newer reporter meets older
collectors.

**Batches are allowed per live key.** One host commonly serves many websites, each reporting with
its own key, and counted per address a busy neighbour spent everyone's allowance. A key costs
nothing to invent, though, and counted per key on its shape an invented one would arrive with an
allowance of its own, a limiter of its own and a lookup in the database each time. So a batch is
counted against its key (`ServerBatchesPerMinutePerKey`, 600) when the key is live — issued and not
withdrawn, as the engine read the keys within the last minute — and against the address it came from
(`ServerBatchesPerMinutePerAddress`, 600) otherwise: no key, a value not shaped like one, a key that
matches nothing, a withdrawn one. Both are decided from the key's hash and memory alone, before
anything is looked up. The list is read before the collector takes its first batch and every minute
after, so a website quiet for a day, or reporting just after a restart, is counted under its own key
from its first batch; invented and withdrawn keys share their address's count, and an address past
it is turned away without a lookup while the live keys reporting from it carry on. Every refusal for
too many requests, here and on every other allowance, says in `Retry-After` how long to wait: on
this release of .NET the whole length of the window, which is enough, and can be up to a window more
than was needed — a minute on the collectors and the live view, five on signing in.

## What this deliberately does not do

- **It does not say which of several people a shared key was.** Where two browsers answered one
  reporter key within the reach, none of its reports is given to either. Those pages are counted
  under the reporter's key and under each browser's — twice in the totals — and the reporter's key
  is judged as a visitor of its own on what the server saw. Folding the reports into one of the
  people would credit one person with the others' pages; there is nothing in the reports to tell
  them apart.
- **It cannot tell a crawler's copy handed to one reader from that reader's own page.** A crawler
  that sends a browser's headers and never runs the tracker fills a cache, and exactly one reader is
  handed its copy within the answer window: that is the only answer to a key that named nothing
  else, which is exactly what a reader's own page looks like. The crawler's request is read as the
  reader's, and the reader's visit holds it. `JudgingTests` pins the case.
- **It does not read past the window a question is asked about.** A statement reconciles what it
  read. A report whose deciding answer lies just outside a dashboard's period is settled differently
  there than in the engine's own reading, which takes a day either side, exactly as a name echoed
  outside the window was before. Only reports within half an hour of a period's edges can differ.
- **It does not tell a batch replayed long after it was sent from pages delivered when it arrives.**
  The collector times every report by its arrival, so to it a batch replayed hours later is pages
  delivered hours later, and their names, which nobody sent back then, count against the reporter's
  key as pages whose tracker never ran.
- **It does not limit a key across installations, or a website across its keys.** The allowance is
  per key on each installation; a website that is given two keys has two.
- **It does not bound how many keys there are.** Every live key is held in memory by hash, about two
  hundred bytes each, and nothing limits how many keys a website may be given. That is a gap in
  issuing keys rather than in counting them, and is where a bound belongs.

## Consequences

- Every statement that reads raw activity carries the new fragment: 57 approved statements changed
  in one way, read line by line, and the installation's accounting reads the instant each report
  arrived. What that costs depends on how many reports carry a name and how many readers' halves are
  under different keys. Measured on a copy of the pinned store with its shipped limit of 1 GiB a
  statement:
  - With no name stored, which is the hosted installation today, the overview over 450,000 reports
    takes 0.2 s and 76 MiB, as before.
  - With every report named and every reader's halves under different keys — the most a website
    could send — the overview over 450,000 reports takes 1.0 s and 145 MiB where it took 0.3 s, and
    a month's count 4.6 s and 316 MiB where it took 1.3 s and 199 MiB.
  - At five times that, 2.25 million such reports in one month, the overview takes 4.6 s and 584 MiB
    where it took 1.1 s and 399 MiB, and a month's count 25 s and 1,015 MiB where it took 6.1 s and
    594 MiB. A month's count reads the reconciliation three times, for the month, its days and its
    hours, and holds it three times over; past about that volume it would stop at the memory limit
    and the month would not be kept. Counting the three cuts from one reading lifts that ceiling,
    and is the change to make before a website reports at that rate.
- A website receiving more than 600 requests a minute, reporting each as it is answered, loses what
  is reported past that for the rest of the minute, and is told when to try again. The figure is a
  setting.
- An installation configured with an idle timeout shorter than half an hour refuses to start until
  it is raised.
- Once released, each finished month is counted again. With no stored name, every recount must equal
  the month it replaces, in `consolidated_months` and `consolidated_post_months`; one that does not
  is a fault in this change, not a correction.

## Rolling back

An earlier release reads the old rule again. Months counted under version 2 are kept as they were
counted, because a release counts again only months older than its own version — and with no stored
name they are the same figures. The collector's answer loses its new fields, which a reporter does
without; its allowance goes back to being counted per address; the idle timeout's bound goes back to
a minute.

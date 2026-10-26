# 0058 — A browser is told whether its website records presses

- **Status**: accepted
- **Date**: 2026-10-25
- **Applies to**: both editions, and the whole of it is in the free product. Changes the tracker and
  what the collector answers; adds no column and changes no stored data.
- **Revises**: [0012](0012-operated-controls.md), under which the tracker listened for every press on
  every page it ran on.
- **Revises**: [0057](0057-a-text-box-is-never-read-and-clicks-start-switched-off.md), which left a
  press travelling from a website that records none, and named this as the next change.

## Context

0057 switched recording what people press off for every website, and left one thing stated rather
than fixed: the tracker could not know whether its website recorded presses, so it sent every press
— the words on the control and where it pointed — and the collector dropped it where it arrived.
A switch that decides what is kept but not what a visitor's browser sends is weaker than it reads.
The owner chose to have the collector tell the browser.

Two things stood in the way. The tracker sent its reports as plain text through the browser's
beacon, which posts without asking permission and cannot read a reply. And the collector answered
every report it could read with the same empty reply, so that nothing in it said why a report was
refused.

A third turned up on the way. A beacon is sent in the mode whose reply nobody reads, and in that
mode the Fetch standard gives a `POST` from a page whose referrer policy is `no-referrer` — or
`same-origin`, to another origin — the origin `null`. Firefox and Safari follow it; Chromium sends
the page's own. The collector refuses an origin it cannot place, so every report from such a page in
those browsers was already turned away, and a tracker that asked with one kind of request and
reported with the other would have had the page's first view stored and nothing after it. Measured
on 2026-10-25 with Playwright 1.64.1 driving Chromium 156, Firefox 155 and WebKit 27.2 (builds
chromium-1248, firefox-1543 and webkit-2359) under both policies: that tracker had Firefox and
WebKit store a page's first view and refuse every report after it — its presses, its departure, the
next page it moved itself to; Chromium stored everything.

## Decision

**Every report goes the one way, from a browser that can keep a request alive.** Where
`'keepalive' in Request.prototype` (MDN browser-compat-data, `Request.keepalive`: Chrome 66, Edge 15,
Firefox 133, Safari 13) and the browser has `BigInt`, every report is sent with `fetch` in CORS mode,
without credentials, kept alive, and as plain text, so no preflight is needed and each one carries
the page's own origin — in practice from Chrome 67, Edge 79, Firefox 133 and Safari 14. `BigInt`
stands in for what no property shows: Safari 13 has `keepalive` but will not start a kept-alive
request while its page is being left for another, and fires no `visibilitychange` on such a
departure, so a reading's last report goes from `pagehide` and is never sent. WebKit let such a
request start from Safari 14 (bug 214630), the first Safari with `BigInt`. Any other browser hands
every report to its beacon, as before, sends what that refuses with `fetch`, and is never asked
anything: asked, it would have to send its first page view apart from the rest, where it could be
lost with the page or be the only report of it the collector could place.

**The first page view asks.** Its reply is the only one the tracker reads.

**The collector answers one thing.** A page view that was stored — the website exists, is still
measured, and the request's origin is one it accepts — and whose website records presses is answered
`200 {"presses":true}` (`CollectAnswer`, from `EventIngestor.IngestWithReceiptAsync`), unless the
request's `Sec-Fetch-Mode` header says `no-cors`: nothing could read that body, and Firefox's
opaque-response blocking reports one sent back to a beacon as a failed request on the customer's
page. A beacon from a browser too old to send fetch metadata is answered as any other request is,
and nothing reads that body either. Every other report the collector reads, and a page view for a
website that records none, that does not exist or is no longer measured, or from an origin it does
not accept, gets the same empty `204` as before; a malformed or oversized body, and an address over
its allowance, are refused plainly, as before. Why a report it could read was not stored is never
in the answer. A report for a website that does not exist — a closed account's websites among them,
which are no longer found — or from an origin it does not accept is written to the operator's log
at debug level; one from an account whose trial or subscription has lapsed, or that is over its
limit past the grace period, and a press for a website that records none, are dropped without a
word.

**The tracker listens only on a yes.** The press listener is attached when that reply says presses
are recorded, and never otherwise: an empty reply, a refusal, an unreadable body and no reply at all
all mean no. A page the site moves itself to does not ask again, and a page the browser restores from
its own cache keeps what was decided.

**The cross-origin policy is unchanged** — any origin, `POST`, no credentials — and its recorded
justification now names the one fact a cross-origin reader can obtain, which only a page on a host
the website accepts is ever told.

**The beacon got smaller.** The oldest browsers it supports compile object spread into two helper
routines, and the tracker used spread in only five places. Those build their reports with
`Object.assign` instead, and the beacon is 1,936 bytes compressed against its 2,048-byte budget —
less than the 2,034 it was before it learned to ask.

## What a yes shows

A yes shows that a page view was stored. For a website that records presses, that is that its
account is still measured — not closed, its trial or subscription not lapsed, not over its limit past
the grace period — and that the host the request was judged by is one the website accepts: its
`Origin`, failing that its `Referer`, failing both the host of the page it reported.

None of it was hidden before. The collector writes a report with ClickHouse's asynchronous insert and
waits for the flush, so a stored report is answered only once the store has taken it and a refused
one at once: on a local stack on 2026-10-25, eight stored page views were answered in 72–306 ms and
sixteen refused ones in 4–17 ms. How long an answer takes has always said, of every website, whether
a report was stored. Telling a website no longer measured that it records presses would hide nothing,
and would have its visitors' browsers send presses only for them to be dropped.

A host can be confirmed one guess at a time and never listed, and only by a caller that is not a
browser — one that writes any `Origin` header it likes, or none and names the host in its `Referer`
or in the page it reports. A browser sends its page's own origin, or `null`, with every
cross-origin request whose answer it lets the page read, so a page on a host the website does not
accept only ever reads the empty answer. Site identifiers are version-7 GUIDs carrying 74 random
bits, so none is found by guessing.

## Consequences

- A page opened on a website that records no presses sends none. Presses reach the collector from
  such a website only from a page told yes before the switch reached the engine that answered it —
  where more than one engine runs, up to a minute after it was switched off (0045) — and from pages
  running the tracker that came before this release: a browser may load that from its cache for up
  to an hour after the release, and a page running it, or an older copy served from elsewhere, sends
  every press until it is closed. The collector drops them all, as before. A page keeps what it was
  told until it is closed or reloaded: a page the site moves itself to, or one restored from the
  browser's cache, does not ask again.
- A change to the switch reaches the collector at the next report on the instance it was saved
  through, which evicts the website from its cache, and within a minute on any other (0045). A
  browser learns of it at the next page it loads afresh.
- The dashboard's copy of the tracker is reused for new pages for up to an hour
  (`Cache-Control: public, max-age=3600, must-revalidate`), and a page keeps the copy it loaded until
  it is closed; a copy somebody serves from elsewhere changes when they change it.
- On a website that records presses, a press made before the reply arrives — ordinarily the first
  fraction of a second a page is open — is not reported.
- Readers in Firefox from 133 and Safari from 14 on a page whose referrer policy is `no-referrer` or
  `same-origin` are now measured, where every report from them was refused before; such a website's
  numbers rise by readers who were there all along. An older browser on such a page hands its
  reports to the beacon and fares as it did before.
- A browser that hands its reports to the beacon — Chrome before 67, Edge before 79, Firefox before
  133 and Safari before 14, among those supported — reports no presses on any website, and
  everything else as before.
- Going back to an earlier release puts the tracker back to sending every report by beacon and every
  press with it, dropped at the collector for a website that records none, and has Firefox and Safari
  readers on a page whose referrer policy is `no-referrer` or `same-origin` refused again. Until the
  copies of this release's tracker that browsers hold run out — up to an hour, and for as long as a
  page loaded with one stays open — those pages ask a collector that never says yes, and report no
  presses even for a website that records them.

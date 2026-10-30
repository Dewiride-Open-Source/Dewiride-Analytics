# 0060 — A visit from an ignored website is never kept

- **Status**: accepted
- **Date**: 2026-10-30
- **Applies to**: both editions. The mechanism is in the free product, which lists nothing; the
  hosted service lists its own websites in the commercial edition's deployment. Adds one setting;
  adds no column, changes no stored data and needs no migration. Every collection point reaches the
  one ingest path, so nothing in `integrations/` or in the tracker changes.

## Context

From 2026-10-28 one fleet arrived on the hosted install naming `www.aocr.org` as the website it came
from. Measured read-only on 2026-10-30, over the fourteen days before:

| What                 | Measured                                                                                                  |
| -------------------- | --------------------------------------------------------------------------------------------------------- |
| Page views naming it | 320 — 1, 2, 93 and 196 on 20, 27, 28 and 29 October, 28 by 04:00 on the 30th                              |
| Websites             | 2 — 317 on one, 4 on the other                                                                            |
| Collection point     | the browser tracker, every one                                                                            |
| Browser              | Chrome 146 on Windows on a desktop, every one                                                             |
| Networks             | 40, in 18 countries — mostly data-centre networks                                                         |
| What each sent       | one page view, then a report or two of how the page was going (440 in all)                                |
| Departures, presses  | none, except from one visitor (below)                                                                     |
| Share of page views  | about three in ten on the two websites in the 36 hours to 04:00 on the 30th, more than half in some hours |

Ruleset 11.0 ([0059](0059-a-page-left-open-is-not-a-page-read.md)) already reads such a visit as a
page left open: 304 of the 305 visits it judged were something the product could not tell, and the
one person was a visitor who arrived from the same address once and viewed about sixty pages that
day. But a visit is judged only once it has been quiet for half an hour, and until then each one
was on the screen of who is here now, whose first line
([0052](0052-a-row-says-on-its-first-line-how-somebody-arrived.md)) says where a visitor came from:
"From www.aocr.org", every few minutes, on a website that had a few dozen page views an hour.

The owner asked for this traffic not to be processed at all, and chose one list for the whole
service, changed only by a release, ahead of a list in each website's settings.

## Decision

**A configured list of websites.** `Dewiride:Collector:IgnoredSenders` holds host names, each
standing for itself and every address below it, so `aocr.org` covers `www.aocr.org`. Names are
compared in the ASCII form a browser sends and without regard to case. An installation does not
start with an entry nothing could match: each must be a host name of two labels or more, with no
scheme, port, path or wildcard. Empty unless configured.

**The whole visit is turned away at the door.** A report naming a listed website as where its page
was reached from is not stored, and its visitor is remembered. Every later report from that visitor
to that website is turned away too, and counts as hearing from them again; once they have been quiet
for as long as a visit lasts (`Dewiride:Classification:IdleTimeout`, half an hour unless configured)
they are forgotten, and what they send next is kept. Only a page view names a sender: the tracker
reports how a page is going, that it was left and what was pressed on it without one. Turning away
the page view alone would have left those stored — and the screen of who is here now lists anybody
who sent anything inside its window, so every visit turned away would still have shown there, as a
visitor with no page.

**Never the website itself.** A page reached from one of the addresses the website is published at
names no sender, whatever the list says. A website listing its own address would otherwise turn away
every visitor who went on to a second page.

**Answered as every report not kept is answered.** A browser is answered with nothing, and never
with the answer that tells the tracker to listen for presses
([0058](0058-a-browser-is-told-whether-its-website-records-presses.md)); a website's own server
finds the observation counted in `rejected`, as its batch answer already says what became of each
one. A debug-level line records that a report was turned away and for which website, without the
name the visit gave: whoever sent the visit wrote it, and the operator who listed it already knows
it.

**The hosted service lists `aocr.org`,** in the commercial edition's deployment, and changes the
list with a release.

## What this deliberately does not do

- **It is not a verdict.** Nothing about the visit is judged. The list is an operator's decision
  about what is not worth keeping, and a person who follows a real link from a listed website is
  turned away with everything else — the one person among the 305 above would have been.
- **No list in a website's settings yet.** One would add to this list rather than replace it.
- **Nothing already stored is removed.** Taking out what an installation already holds is an
  operation on that installation's data, carried out by its operator, rather than something the
  product does when a website is listed.
- **No list ships with the free product.** A self-hosted installation turns away what its operator
  lists.

## Consequences

- A program leaves the list by naming another website, or none, and is then judged as everything
  else is.
- A visitor is remembered by the key the collector gives them, which on a rented network stands for
  everything describing itself identically on that network that day
  ([0017](0017-a-rented-address-is-a-lease-not-a-visitor.md)): while such a visitor is remembered,
  anybody else under the same key is turned away with them.
- The memory is held by the engine itself. It is lost when the engine restarts, an installation
  running more than one engine remembers a visitor only on the engine their arrival reached, and a
  visitor's key changes at midnight UTC. In each case a report from a visitor no longer remembered
  is stored — and with no arrival before it, it never becomes a visit, though it can appear for half
  an hour on the screen of who is here now.
- At most 50,000 visitors are remembered at once. Past that a new arrival is still turned away, and
  what it sends next is stored and never becomes a visit, so a flood costs storage rather than the
  engine's memory.
- The no-script image reports no referrer, so it never names a sender; a visitor already remembered
  is turned away from it as from every other collection point.

## Rolling back

An earlier release does not read the setting. Visits from a listed website are stored and judged as
they were before, and the list stays in the configuration doing nothing until the release that reads
it returns.

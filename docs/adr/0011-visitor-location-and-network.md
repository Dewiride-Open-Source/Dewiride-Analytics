# 0011 — Visitor location, network operator and device

- **Status**: accepted
- **Date**: 2026-08-19
- **Supersedes**: nothing. Widens the envelope recorded in ADR 0005.

## Context

Every event already carries the visitor's address for 72 hours and then loses it. Nothing has ever
been derived from it, so the product cannot answer where a site's readers are, whose network they
came in on, or what they were reading on — three of the most ordinary questions an analytics
product is asked, and the third of which is load-bearing for this one: a request from a hosting
network is not somebody reading at home, whatever its user agent claims.

Deriving these is not an addition that can be made later against stored data. The address is
erased by a column policy 72 hours after the event, so anything not resolved at ingest is lost for
good.

## Decision

**Resolve at ingest, store the derived attributes, keep the address's retention unchanged.** Nine
columns are added to `events` (migration `0004_visitor_context.sql`): country, subdivision, town,
autonomous system, network operator, device class, browser family, operating system, and whether
the client declared itself handheld. Each is empty or `Unknown` where nothing could be
established, which is a state the interface reports rather than hides.

**Precision is country and town, and no coordinates.** The owner chose town-level. A country list
and a town list need no latitude or longitude, and a pair of coordinates placing a reader within a
few streets is a different kind of record from the name of their nearest town — so the field is
not read from the database and not stored. What _is_ stored is an estimate and the interface says
so on the card: address ranges are allocated to networks rather than to streets, and the answer is
frequently the nearest sizeable town.

**Two published datasets, fetched rather than vendored.**

| Data                           | Source                                      | Licence   | Cadence |
| ------------------------------ | ------------------------------------------- | --------- | ------- |
| Country, subdivision, town     | DB-IP Lite, `dbip-city-lite-{yyyy-MM}.mmdb` | CC BY 4.0 | monthly |
| Autonomous system and operator | iptoasn.com, `ip2asn-combined.tsv`          | PDDL-1.0  | hourly  |

Both are free to redistribute and neither needs an account or a key — a lookup that stops working
when somebody's free tier lapses is not something to build a measurement on. MaxMind's GeoLite2
stays excluded for the reason recorded in ADR 0003: its terms require destroying superseded copies
within thirty days and forbid third-party disclosure, neither of which a self-hostable artifact can
honour. Only MaxMind's _reader_ is used, which is Apache-2.0 and ships no data.

The place database is about 120 MB and is fetched in the background after the host is up, into a
volume rather than the image. A first run therefore measures traffic immediately and reports every
country as not known until the download lands. `Dewiride:ReferenceData:AutoDownload=false` turns
fetching off for an install with no route to the internet; files placed in the directory by hand
work identically.

**Device is read from what the browser volunteers, then from its user agent.** The three
low-entropy client hints — `Sec-CH-UA-Mobile`, `Sec-CH-UA-Platform`, `Sec-CH-UA` — are sent
unasked, cross-origin, by Chromium browsers on a secure connection. None is requested: asking
means returning a header inviting the browser to describe itself more precisely, and the extra
precision is exactly the part that would help identify a person. High-entropy hints are never
touched, which keeps rule 13's ban on high-entropy client hints intact.

Where the hints are absent — every non-Chromium browser, and any install being tried over plain
HTTP — the user agent is read instead, by an ordered set of plain substring tests over a curated
catalogue. **No regular expressions**: the user agent is attacker-supplied and arbitrarily long,
and a pattern set rich enough to name every browser is a pattern set rich enough to be made to run
for a very long time on one line of text. Nothing the client wrote is stored: a match returns the
catalogue's own word, so the columns hold a closed set however many visitors invent a browser.

**Classification is untouched by this change.** The disagreement between a user agent and the
hints beside it is a strong headless-browser tell and is deliberately not wired in yet: adding a
signal is a ruleset version change, and this change must not be able to move a verdict.

## Consequences

- **The attribution reaches the interface, not just a file.** CC BY 4.0 requires a link back to
  db-ip.com from any page displaying results from the data. It is a message-catalogue string on the
  locations card and travels with any future screen showing country, town or network-owner data.
- **Rows written before this migration are permanently unresolved.** Correct — nothing was known
  about them — and there is no backfill that could change it.
- **An installation behind a proxy that does not forward the visitor's address resolves nothing
  at all.** The unresolved group is a row on the list rather than an omission from it, so that
  install can see the problem instead of concluding it has few readers.
- **A private, loopback or link-local address resolves to nothing on purpose.** It is what arrives
  when the product is run locally, and answering with a country would be inventing a fact.
- **A monthly release means a monthly download.** Superseded releases are deleted as the new one
  is loaded, so the volume holds one file rather than one per month.
- **The place database is memory-mapped rather than read into memory**, which is what keeps 120 MB
  off the heap and inside a laptop's budget. A replaced reader is therefore closed one refresh
  interval after it leaves service, never at the moment it is replaced.

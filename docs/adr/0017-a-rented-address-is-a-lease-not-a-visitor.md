# 0017 — A rented address is a lease, not a visitor

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column, collects nothing new, and narrows the envelope rather
  than widening it.
- **Follows**: `0016-where-a-visit-came-from-counts.md`, which brought the catalogue this reads.

## Context

`0016` established that a visit arriving over a network that rents servers is weighed as machinery.
It left the count alone, and the count was the louder half of the problem.

A live installation reported **169 visitors and 82 page views** over two days — more people than
pages, which cannot happen. Read against the store, the whole of the gap was one program:

| Network                              | Addresses | Visitors | Agents | Page views | Rows |
| ------------------------------------ | --------: | -------: | -----: | ---------: | ---: |
| AS45102 — Alibaba's international    |       103 |      103 |      1 |         65 |  112 |
| AS8075 — Microsoft                   |        13 |       16 |      3 |          5 |   16 |
| AS45899 — VNPT, a Vietnamese carrier |         5 |        5 |      1 |          0 |    5 |

One browser string, one hundred and three addresses, one hundred and three visitors. The visitor
key is `HMAC(day salt, site ‖ day ‖ address ‖ user agent)`, so a pool of rented addresses produces a
new person on every request by construction.

**The count was the visible half. The invisible half was worse.** Those 112 rows are 65 page views
and 47 reports of how a page was read, and every single one arrived from a different address — so a
page landed under one visitor and the account of how it was read under another. That is where the
installation's _"99 visits, 0 pages, too little to go on"_ came from, sitting above a journey panel
that listed the page those visits had supposedly not seen. The engine was not confused; it was
told, truthfully, that it held forty-seven visits in which nobody had asked for anything.

Three repairs were considered and two rejected.

**An identifier the browser stores and carries** — `sessionStorage`, cleared when the tab closes —
is what most products reach for. Rejected twice over. It is storage on the reader's own device,
which is what a consent banner exists to ask about, and this product's README promises there is
nothing to click through; and it is written by the visitor, so the one actor it most needs to count
correctly is the one that would supply a fresh value per request.

**Grouping by address block** — a `/24` — was measured rather than guessed. It collapses Alibaba's
103 addresses to 4, Microsoft's 13 to 11, and VNPT's 5 to 5. It is arbitrary arithmetic that
happens to help in one case, and it says nothing true.

## Decision

**On a network that rents servers, a visitor is recognised by the network rather than by the
address.** `VisitorConnection.Identifying` reduces a connection to what is worth recognising it by,
and the ingestor hands that to the key factory instead of the address. Everything else about the
derivation is untouched: the same salt, rotating daily, the same user agent beside it, the same
refusal to derive anything at all when neither is present.

**The reason is that the two networks answer different questions.** A household, an office or a
phone holds one address at a time, so activity arriving over it is one visitor's. A rented address
is a lease held for as long as somebody is paying for it, and pools of them are sold for the
express purpose of not being recognised — so there the address answers _which lease was this
request billed to_, and never _who asked for this page_.

**It reads the same catalogue the engine weighs**, `HostingNetworks`, so identity and verdict can
never disagree about what a rented network is. The three kinds of network `0016` excluded on
purpose — delivery networks, corporate proxies, and networks carrying consumer privacy services —
are excluded here for free and for the same reason.

**Matched on the routing number, never the name**, as `0016` established.

## Consequences

**Measured against the installation's own stored activity** rather than estimated — seven days of
it, reduced the way this decision reduces it: **195 visitors become 78, against 93 page views.**
Alibaba's 103 collapse to one, whose 65 page views and 47 readings are one visit rather than 112
fragments; Microsoft's 16 become 3, one for each user agent. More people than pages was the
symptom, and 78 against 93 is a pair of numbers that can both be true at once.

**The stated cost.** Where several unrelated programs run on one rented network and describe
themselves identically, they are counted as one. That understates how many machines were reading.
It is much the smaller of the two errors and it fails in the safe direction: it can never turn
machinery into people, which is the only mistake this product cannot afford.

**It is narrower than it looks, and privacy improves.** On every other network nothing changes at
all. On a rented one the key is derived from strictly less about the visitor than before, so no
identifier here distinguishes anybody it did not already distinguish.

**Forward-only.** The key is derived when a row is written and rows are never rewritten, so
activity already stored keeps the identity it was given. That is the same rule every migration in
this product follows, and there is nothing to recover it from in any case: the address a stored key
was derived from is erased 72 hours after the row is written.

**What this does not fix.** VNPT's five rows are a pool too — rented residential addresses, sold by
the same people and for the same purpose, on a network that genuinely carries households and must
never be treated as a datacentre. Nothing about the network can separate those five from the
carrier's real customers. Joining them needs an account of the page itself rather than of the
connection it arrived over, which is the next decision and a different one.

# 0015 — One visit in one place

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column, collects nothing new, and widens no envelope.

## Context

Opening a visit showed what it did — every page in order, with attention, depth, the status the
site answered with, and every control operated — and what the engine concluded from it, including
the evidence pointing the other way. It said nothing at all about _who_ it was.

Every fact needed to say so was already stored on each of that visit's rows and was already being
read back: the referring address since `0001_events.sql`, country, town, network owner, device
class, browser family and operating system since `0004_visitor_context.sql`. All of it was reported
only as whole-window totals — a ranked list of countries, a split between phones and computers, a
list of sending sites — and never gathered back onto the one visitor somebody was looking at. A
reader could see that a third of the window's traffic came from search and that one visit read six
pages for four minutes, and could not find out whether _that_ visit came from search.

## Decision

**The account of a visitor is answered with the journey, in one statement.** The journey is asked
for only when somebody opens a visit, so a second question would double the cost of one press for
an answer nobody can act on until both halves arrive. The account is settled over the visit and
cross-joined onto its steps, repeating on every row — the same trade the source and page lists
already make with their whole-window totals.

**Each fact is the earliest report of the visit that carried one**, taken with `argMinIf` over
`(server_ts, event_id)` rather than `anyIf`. Geography and software are resolved per report, and a
visit watched by both a tracker in the browser and a reporter on the site's own server holds
reports that resolved them and reports that did not. `anyIf` would answer from an arbitrary row: a
panel that named a different browser on each reading is a defect, and the ordering pair makes the
answer exact even where two reports share an instant.

**The sending site is named by the same catalogue the source lists use**, and the reduction is now
one fragment (`SendingSites`) rather than two copies. The reason is correctness before tidiness: a
visit and the list it appears on must describe the same arrival with the same word, and a
catalogue applied in two places eventually diverges in one of them. The measured site's own
address is bound as a parameter and read from the site catalogue by the endpoint, never from the
request — the rule ADR 0014 set, applied to a second statement.

**Only what was established is shown.** A panel of four facts reading "not known" describes its own
gaps rather than the visit. A fact nothing answered is left out, and the section says once, in a
sentence, that the rest went unobserved. Where a visit came from is the exception and is always
shown, because "nothing named a sender" is an answer rather than an absence — the same row the
source lists carry.

**The place credit follows the place.** DB-IP's licence asks for a link back wherever its results
appear, and one visit showing one town is exactly that. The link was a private function inside the
locations card; it is now a component rendered beside the data in both places, because a condition
satisfied by copying a link into each new screen is one that will eventually be missed on a screen.

## Consequences

Nothing to migrate, nothing to backfill, and nothing new collected: every visit already recorded
answers this the moment the code ships.

**A single visit is more identifying than a total, and that is the point of it.** A row now reads
"from Google · Pune, India · Chrome on Android" beside the pages that visit read. Each of those was
already retained under ADR 0005 and already shown in aggregate; what changes is that they appear
together against one visitor. The envelope is unchanged and deliberately so — the town remains an
estimate from the network rather than a position from a device, no coordinates are stored or shown,
the visitor key still rotates daily, and the raw address is still dropped after 72 hours. The screen
says the town is an estimate rather than letting the pairing imply more precision than the data
carries.

**The catalogue's imprecision arrives here too**, and on the same terms: a site absent from it keeps
its own address, which is honest, and a site present in it is named. An address engineered to look
like a catalogued one is reduced to the label in front of its public suffix before any lookup, so it
cannot borrow a name it has no claim to — proved against `google.attacker.test`, which is shown as
itself.

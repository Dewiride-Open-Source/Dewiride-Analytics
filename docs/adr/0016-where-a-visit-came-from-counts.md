# 0016 — Where a visit came from counts

- **Status**: accepted
- **Date**: 2026-08-20
- **Ruleset**: 1.0 → **2.0**. Moves sessions between categories, so the major component moves.
- **Supersedes**: nothing. Adds no column and collects nothing new.

## Context

A live installation reported 175 visitors over two days. They arrived from 176 distinct addresses
and ran **16 distinct browsers**. One browser string accounted for 158 of those addresses, walking
63 pages, every request from a different address in `AS45102` — Alibaba's international network,
racked in Singapore.

The engine called 68 of those visits `LikelyHuman`. It was not wrong by its own rules: the traffic
ran a real browser, read for tens of seconds and scrolled to the bottom of the page, which is
everything `EngagementDetector` looks for. A scraper driving a headless browser produces every one
of those readings, and increasingly does — the behavioural signals this product was built on are
now cheap to manufacture.

The network the request arrived over was **already resolved at ingest, already stored on every row
since `0004_visitor_context.sql`, and already displayed** on the places card as a country. It was
never handed to the classifier: `SessionEvidence` carried the user agent, the language, the
viewport, the reading, the scrolling, the pointer and the automation declaration, and nothing about
where the request came from. The one observation the visitor cannot author was collected, stored,
shown, and then ignored at the moment it decided whether they were a person.

## Decision

**Where a visit came from is weighed, as one observation among the others.** `SessionEvidence` gains
`AutonomousSystem` and `NetworkOwner`; the session statement selects them; `NetworkDetector` reports
`network.hosting` when the routing number is one this build recognises.

**It is matched on the routing number, never on the name.** Numbers are assigned by the regional
registries and outlive the names their holders trade under. The name is carried for the reader and
takes no part in the decision, and the name shown is the catalogue's own spelling rather than the
registry's, so a screen says "Alibaba Cloud" however the registry words it this month.

**Every number was verified against the routing data this product already downloads**
(`ip2asn-combined`, PDDL-1.0), looked up by number with its holder confirmed. Not from a third-party
reputation list — those carry licences and opinions — and not from memory.

**It weighs 65: above the heaviest sign of a person, and no higher.** It has to outweigh reading
time, because a session that reads for a minute from a rented server is a scraper running a browser
and calling it a reader is the one mistake this product cannot afford. It goes no higher because it
is a single observation, and a single observation does not reach this engine's firmest band alone.
In practice a datacentre visit that behaves like a reader now lands as automation at **Moderate**,
with the reading listed as evidence pointing the other way — which is the honest description of it.

**The reading is kept, not discarded.** It happened. It appears under "pointing the other way" and
holds the strength back. A verdict shown without the case against it is an assertion.

**Absence is not a claim.** There is no complete list of every network that rents servers. A network
missing from the catalogue produces no signal at all — never a signal that the visit was therefore
human. The catalogue can say where a visit came from and can never say where it did not.

**Three kinds of network are excluded on purpose, for one reason: real people browse from them.**

- **Delivery networks** — Cloudflare, Akamai, Fastly. A site behind one sees _its own readers_
  arrive from it. Including them would brand an entire customer's audience as automation on the day
  they turned a CDN on. This is the highest-cost mistake available here and it is designed out.
- **Corporate security proxies** — Zscaler and its kind. Everyone behind one is an employee at a
  desk.
- **Networks that mostly carry consumer privacy services.** A reader using a subscription VPN wanted
  privacy; this product should be the last to punish them for it. Where a network is genuinely both,
  it is left out — missing some automation costs far less than calling a private reader a robot.

A test names those exclusions explicitly, so their absence is a decision on the record rather than
an oversight nobody notices.

**The network is also shown.** `LocationGrouping` gains `Network`, giving the places card a third
view beside Countries and Towns. This is not decoration: countries are what hid the problem. A
rented server in Singapore reports Singapore, truthfully, and ninety-nine of them read as a
Singaporean readership. A network row reads "Alibaba Cloud — 99 visitors" and needs no
interpretation. A network carries no country, because splitting one company's datacentres by
country would recreate exactly the fragmentation this view exists to undo.

## Consequences

**Verdicts change, and both answers stay on record.** Verdicts are filed per ruleset, so the 1.0
answers remain and 2.0 answers are added beside them. A customer who noticed the change can be shown
what produced it. Nothing is rewritten and nothing is lost.

**The bar for "a person" is now higher than behaviour alone.** That is the point. A site whose real
readers genuinely browse from a cloud desktop will see them reported as automation, with their
reading shown as evidence against it. That is a real cost, it is visible on the screen rather than
hidden, and it is the correct side to err on for a product whose proposition is that a number can
be trusted.

**A catalogue to keep current**, on the same accepted terms as the crawler and traffic-source
catalogues. It is consulted when a visit is judged, so adding an entry is a code change and a
re-judging — no migration, nothing to backfill.

**Two things this does not do, deliberately.** It does not join one visitor's reports back together
when their address changes mid-visit — the same rotating pool that made this scraper 158 people also
splits genuine mobile visitors, and repairing that needs an identifier the tracker carries, which is
its own decision. And it does not change what happens to a visit whose page-view report never
arrived: those still read as "too little to go on", and still contradict the journey shown beneath
them. Both are known, both are next.

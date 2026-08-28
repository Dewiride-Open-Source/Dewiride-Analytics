# 0033 — An identity from the name an address answers to

- **Status**: accepted
- **Date**: 2026-08-27
- **Applies to**: both editions. Adds one port, one background lookup, and one column to the visit
  reconstruction. Collects nothing new about a visitor, adds nothing to what is stored, and is the
  first thing in this product that sends a visitor's address anywhere.
- **Completes**: [0032](0032-a-claim-becomes-an-identity-at-the-address.md), which built the half of
  this that works from published lists and recorded this half as unbuilt.

## Context

0032 made an identity possible by checking the address a visit arrived from against a file the
operator publishes. It left a hole it named: **a company that publishes no such file cannot be
settled at all**, however plainly its crawler identifies itself.

Three companies in the catalogue are in exactly that position, and all three were re-read from their
own documentation before this was written:

| Company | Publishes a list | Documents a domain                      |
| ------- | ---------------- | --------------------------------------- |
| Yandex  | no               | `yandex.ru`, `yandex.net`, `yandex.com` |
| Baidu   | no               | `baidu.com`, `baidu.jp`                 |
| Ahrefs  | no               | `ahrefs.com`, `ahrefs.net`              |

Four more — Google, Microsoft, Apple and Common Crawl — publish both, and documenting a domain is not
a lesser statement than publishing a list. Google's own verification page describes the domain check
first and the file second.

The check they all document is the same one: take the address, ask what name it answers to, then ask
what addresses that name points at, and require the two to agree. Neither question is answerable by
anybody but the company. Only the holder of a block of addresses can decide what they answer to, and
only the holder of a domain can decide which addresses it points at — so an answer that survives both
came from somebody who controls both, which is the company or nobody. **Either question alone proves
nothing**: anybody may make their own machine answer to `crawl-1-2-3-4.googlebot.com`, and anybody may
point a name they hold at an address they do not.

The obstacle is where the check can run. 0032 resolves identity on the ingest path, because
`events.ip_address` is erased after 72 hours and an answer missed on the way in cannot be recovered.
But `ICrawlerAddressDirectory` is contractually memory-only, and rightly: a lookup that waited on
somebody else's name server would put their availability between a customer's visitors and their own
measurements, and the addresses are attacker-chosen, so a remote call per address is a way of asking
to be flooded.

## Decision

**The question is asked when a visit is judged, not when it was collected.** A visit is judged once
it has been silent for an idle timeout, which is half an hour by default, on a background worker
where a slow answer costs nothing anybody can see. That is comfortably inside the 72 hours the
address is kept, so in ordinary running every finished visit can still be asked about. The cost is
stated rather than hidden: **a visit judged more than three days after it happened cannot be asked
about, because there is no address left**. That is the same limit 0032 records, arrived at from the
other side.

**A visit that looks like somebody reading is never asked about.** The pass judges everything once,
and only visits that did not come out as `LikelyHuman` have their addresses sent anywhere. This is
the first time this product sends a visitor's address outside the process, and it will not do it out
of curiosity about a person. The price is real and is accepted: a crawler that produced enough
engagement to be taken for a reader stays unrecognised, and the answer to that is the published-list
route rather than a wider net.

**One signal code covers both proofs.** A visit settled by a name carries the same
`identity.confirmed_crawler` at the same weight as one settled by a file, because it is the same fact
established a second way. Which route settled a particular visit is a detail of this product, and
rule 12 is about not overstating certainty, not about narrating mechanism to a customer. Three
sentences in the message catalogue that named only the published-list route were reworded to cover
both.

**A name matches only on a label boundary.** It is the domain itself, or something below it — never
merely a string ending in it. `not-googlebot.com`, `evil-googlebot.com` and
`googlebot.com.example.net` are all names anybody may register and point at their own machine, and a
match written as "ends with" would hand them Google's name. Comparison is case-insensitive and a
trailing root dot is ignored, because both spellings are the same name.

**A domain anybody can answer from is nobody's.** Google documents `googleusercontent.com` alongside
its own two, and that is where any App Engine application answers from. It is excluded for exactly
the reason 0032 excludes the matching file: those machines belong to everybody who runs an app there,
so arriving from one proves nothing about who is calling.

**A settled address is handed back to the collector.** Once a name has established whose machine an
address is, later reports from it are stamped as they arrive, exactly as a published address is. That
is what makes an identity found this way durable: the answer ends up on stored activity, where it
outlives the address it was worked out from and is still there when the visit is judged again under a
later ruleset. Those addresses expire when the published files are next reloaded, twelve hours at
most, because they were never published as a set and so cannot be compared against a newer one —
letting them expire is what stops an address a company has stopped using from carrying its name for
as long as the process runs.

**Answers are remembered for a day, and the ones worth remembering most are the negatives.** A
determined visitor from one address would otherwise be a question about that address for every visit
it ever made. A day is short enough that a machine a company has just commissioned is recognised the
following day at the latest. A question that went unanswered is deliberately **not** remembered:
nothing was established either way, and recording silence as an answer would let one slow moment
settle an address for a day.

**Which domains stand for which company is catalogue, not configuration.** They sit in
`CrawlerCatalogue.Proofs` beside the published files, because a domain listed there decides whose
name goes beside a customer's traffic and being able to add one in an environment file would make it
possible to have this product vouch for anybody. What is configurable is whether this installation
asks at all, how long it waits, how many questions it asks at once, and how much it remembers.

**Two companies were considered and refused.** Sogou and Yisou are named in third-party directories
as verifiable by name, and neither publishes anything of its own that says so — Sogou's webmaster
page documents `robots.txt` and nothing else. The catalogue's standing rule is that every entry comes
from the operator's own documentation, so both stay unrecognised by address. Amazon publishes its
crawler addresses as three web pages rather than as a file anything can read and documents no domain;
Meta and Slack publish neither. All four are recognised by name and reported as claims, which is the
whole truth about them.

## Consequences

**The ruleset moves to 6.0, so history is re-judged and every earlier answer stays on record.**
Visits collected more than three days before this shipped keep the verdict 5.0 reached, which remains
the honest one for evidence that never included the check.

**Nothing about the categories or the bands changed.** No new signal code, no new category, no new
column in either store. A visit settled this way lands in the same "known" categories at the same
`Verified` band as one settled against a file. `LikelyAnalyticsSpam` remains unreachable, as 0032
left it.

**A visitor wearing the wrong company's name is now caught by this route too.** Naming one company
while arriving from an address another company vouches for is `identity.false_claim` and
`SuspiciousAutomation`, whichever of the two proofs established the contradiction.

**The visit reconstruction now carries the address, and it goes no further than the pass that reads
it.** It rides on `ObservedSession` beside `SessionEvidence` rather than inside it, so the engine
still cannot reason about where somebody lives; it is never stored on a verdict, and never logged.

**An installation that cannot reach a name server is quieter, not degraded.** Every visit is still
measured, every published list still settles what it settles, and crawlers that publish only a domain
are reported as having said what they said. A pass in which every single question went unanswered
logs one warning, because that is an install with no way to ask rather than a run of slow servers.

**The integration suite is pinned off the network.** The addresses those tests write are
documentation ranges nobody answers for, so every question would be a real query leaving the machine
and timing out. Both the name check and the published-list downloads are switched off there, and the
rule the answers are put through is proved separately against a name server that says exactly what
the test tells it to.

**The residual risk is a name server that lies, and it is bounded by arithmetic rather than by
trust.** Somebody who controls a company's authoritative DNS could make this product vouch for them —
but that person can already do considerably worse to that company, and both directions of the check
have to agree, so controlling only the reverse zone for one's own address is not enough. What
remains unbounded is nothing this product can see from a web server.

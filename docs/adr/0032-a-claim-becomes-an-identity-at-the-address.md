# 0032 — A claim becomes an identity at the address it arrived from

- **Status**: accepted
- **Date**: 2026-08-27
- **Applies to**: both editions. Adds one column to `events`, one background service, and one
  observation to the engine's vocabulary. Collects nothing new about a visitor and narrows the
  retention envelope rather than widening it.
- **Completed by**: [0033](0033-an-identity-from-the-name-an-address-answers-to.md), which builds
  the half this one records as unbuilt.

## Context

Every identity this product could report was, until now, a claim. The engine recognises a crawler by
finding its operator's published token inside the user agent — and a user agent is one line of text
whoever is driving the browser writes for itself. So the catalogue always paired a recognised name
with `identity.unverified_claim`, the interface always rendered "says it is" rather than "is",
`TrafficCategory.KnownAiCrawler` and its three siblings were unreachable from any decision path, and
`EvidenceStrength.Verified` was a band with no route to it. `EvidenceScorecard` said so in a comment:
_"Confirming the claim is what moves this to KnownAiCrawler, and only address verification can do
that."_

An audit of seven days of production traffic put numbers against the cost. Two findings decided the
design:

- **The largest single mislabelled group could never be reached by name.** Seventy-nine of eighty-
  three visits from Microsoft's crawler declared nothing but `HeadlessChrome`. No catalogue of tokens
  can find them, however complete; they were weighed as anonymous automation from a rented network.
- **The rest were named but unconfirmable.** After the catalogue was widened from four operators to
  thirteen, sixty visits changed category — and every one of them arrived at `SuspectedAiCrawler` or
  `GenericWebCrawler` carrying the caveat, because nothing could take the caveat off.

Meanwhile the material for a real check already exists and is already free. Twelve companies publish
a machine-readable file of the addresses their crawlers connect from, all of them in one format:
Google defined it, and OpenAI, Anthropic, Perplexity, Microsoft, Apple, DuckDuckGo, Mistral and
Common Crawl all publish the same shape. None needs an account, a key, or a paid tier.

The hard constraint is `ip_address`, which `0001_events.sql` clears seventy-two hours after the event
(ADR 0005). Anything derived from an address must be derived while the address exists, or it cannot
be derived at all — which is exactly the reasoning already written against the country, town and
network columns in `0004_visitor_context.sql`.

## Decision

**The check runs when the activity is collected, and what is kept is the answer rather than the
address.** `ICrawlerAddressDirectory` is asked once per accepted event, beside `INetworkLookup` and
under the same contract: it answers from memory, it never reaches the network, and an address in
nobody's file is an ordinary answer rather than a failure. What it returns is written to one new
`LowCardinality(String)` column, `confirmed_operator`, and the address it was derived from is erased
seventy-two hours later as before.

Deriving it at judging time instead was rejected. A ruleset bump re-judges a site from its start, so
every visit older than three days would silently lose an identity it once had — and a verdict that
depends on when it happened to be reached is not a verdict. Storing the conclusion is also the
better answer for privacy: "this was Microsoft's crawler" outlives the address, and is not personal
data.

**What an address establishes is the company, and never the crawler.** Google publishes four files
covering all of its crawlers together, so membership cannot distinguish `Googlebot` from
`Google-Extended`; OpenAI publishes one per product, but all four are OpenAI's. The honest unit is
therefore the operator, and `identity.confirmed_crawler` carries the company alone. What the visitor
called itself remains a separate observation with its own sentence, so a reader sees the two facts
apart: this named itself GPTBot, and this arrived from OpenAI's own addresses.

**A company whose crawlers all do one thing lends that purpose to a visit that named nothing.**
Microsoft runs one catalogued crawler and it indexes, so a confirmed Microsoft visit is a search
crawler whether or not it introduced itself — which is what reaches those seventy-nine visits.
Google runs crawlers for four different purposes, so a confirmed Google visit that named nothing is
an ordinary crawler and the purpose is left unstated. The difference between being indexed and being
collected as training material is the one a publisher actually cares about, so it is never guessed.

**An established identity is not weighed.** It decides the category before the probing rules are
consulted and it sets the band outright. A confirmed search crawler asking for pages that are gone is
a search engine following links somebody published years ago, not a scanner; and how much else was
observed about a visit changes nothing about whether it was that company, so letting corroboration
decide the band would make the firmest statement this product can make depend on how talkative the
visit happened to be. This is the only route to `EvidenceStrength.Verified`, and behaviour has none:
the engine performs no I/O, so it could not check an address if it wanted to.

**Absence is never a denial.** `identity.false_claim` is reported only on a positive contradiction —
one company's name over another company's published addresses, both halves published facts. An
address in nobody's file is the ordinary case: the company may publish nothing, the installation may
have fetched nothing, and a machine commissioned this morning is in no file until its operator
republishes. Concluding "impostor" from silence would be exactly the overreach this product exists
to argue against.

**Only files whose every address belongs to the company are used.** Google publishes a fifth list
covering fetchers that run on shared App Engine infrastructure; it is deliberately excluded, because
those addresses are shared with everyone else who runs an app there and arriving from one proves
nothing. Amazon publishes its crawler addresses as a web page rather than as a file, so Amazon is
absent too, and Amazonbot stays a claim. Two guards sit under that: a block broader than a `/16` is
refused outright, so a file that had somehow come to say `0.0.0.0/0` cannot hand every visitor on the
internet one company's name at the verified band; and a block two companies both claim establishes
neither.

**Which files exist is part of the catalogue, not part of the configuration.** A file decides whose
name goes beside a customer's traffic, which is not a deployment setting. What an operator may choose
is whether this installation fetches them at all, how often, and where the copies live — and an
installation with no way out to the internet works identically from files put there by hand.

## Consequences

**Five categories become reachable and one band becomes attainable.** `KnownSearchCrawler`,
`KnownAiCrawler`, `KnownAutomatedService` and `MonitoringOrSynthetic` now have decision paths, and
`Verified` now has one. `LikelyAnalyticsSpam` remains unreachable; nothing here was going to reach it
and forcing it would have been the sort of guess rule 12 forbids.

**The ruleset moves to 5.0, so history is re-judged and both answers stay on record.** Visits
collected before this shipped carry no answer to the address question and keep the verdict 4.0
reached, which remains the honest one for evidence that never included the check. Re-judging cannot
recover it: there is nothing left to recover it from.

**One company's web server being slow costs that company's addresses and nothing else.** Each file is
fetched in its own attempt with its own allowance, nothing is put into service without being parsed
first, and a failed refresh leaves the copy already on disk working. A file that fails every time
simply stops being refreshed and the company keeps being recognised from the last good copy, which is
a warning in the log rather than a loss of function.

**Verification is a snapshot and cannot be re-taken.** A visit checked against a file that was a day
stale is recorded as unconfirmed for ever, because the address is gone by the time anyone could
notice. The refresh interval is twelve hours for that reason, and a company that has just commissioned
a machine will have some of its traffic recorded as a claim.

**An installation that fetches nothing is not degraded, it is quieter.** Every visit is still
measured and every crawler is still recognised by the name it gives. What stops is the confirmation,
and every such visit reads as a claim — which is what the product said about all of them until now.

**Forward-confirmed reverse DNS is the other half and is not built.** Four of the companies in the
catalogue — Yandex, Baidu, Ahrefs, and Apple alongside its file — publish a hostname suffix rather
than a list, and the production audit confirmed two more crawlers that way. `OperatorProof` already
carries `ConfirmingHosts` for them. It is a separate decision because it is a separate mechanism: a
name lookup is network I/O, so it cannot run under the contract above, and the design that fits is a
resolver keeping an in-memory answer per address. Until it exists those companies' crawlers are
recognised by name and reported as claims.

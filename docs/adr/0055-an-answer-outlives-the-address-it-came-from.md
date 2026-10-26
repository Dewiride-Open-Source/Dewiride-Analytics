# 0055 — An answer outlives the address it came from

- **Status**: accepted
- **Date**: 2026-10-25
- **Applies to**: both editions. Adds one table to the telemetry store (`0010_name_confirmations`),
  one migration that fills it from the verdicts already stored (`0011_answers_already_reached_by_name`),
  one port the engine writes through (`INameConfirmationStore`), and one join to the reconstruction
  the engine judges from, and gives the telemetry store's migrations a client of their own. Collects
  nothing new, stores no address, changes no rule and does not move the ruleset. Nothing in
  `integrations/` changes, and what is happening now is read as before.
- **Revises**: [0033](0033-an-identity-from-the-name-an-address-answers-to.md), which expected the
  hand-back to the collector to make an identity found by name durable. It does that only for reports
  that arrive while the collector still holds the address.
- **Follows**: [0032](0032-a-claim-becomes-an-identity-at-the-address.md), which keeps the conclusion
  rather than the address, and [0035](0035-a-visit-is-judged-on-all-of-itself.md), which judges a
  visit on all of itself however its history is walked.

## Context

0033 settles a crawler whose company publishes no list of addresses by asking a name server about the
visit's address when the visit is judged. It expected the answer to last because a settled address is
handed back to the collector, which stamps the next reports from that machine with the company as they
arrive, exactly as a published address is stamped. Two things stop that from covering the visit that
was settled. The collector keeps such an address only until the published lists are next reloaded,
twelve hours at most; and the visit that prompted the question has already arrived, so its own reports
are never stamped. Its answer lives in its verdict and nowhere else.

A verdict is not the last word on a visit: improving the rules judges each site's history again from
its beginning ([0035](0035-a-visit-is-judged-on-all-of-itself.md)). The address is cleared 72 hours
after it was collected (`0001_events.sql`), so a visit judged again after that has nothing left to
ask about, and it is judged on the claim alone — "says it is Yandex's crawler" where it had been
"Yandex's crawler · confirmed".

Measured read-only on the hosted installation on 2026-10-23, under ruleset 9.0:

| What                                                             | Visits |
| ---------------------------------------------------------------- | -----: |
| Confirmed by name alone and older than 72 hours: Baidu           |    259 |
| … Yandex                                                         |    106 |
| … Ahrefs                                                         |     38 |
| Confirmed by name under an earlier ruleset, lost in the 9.0 walk |     78 |

The 403 would all have lost their identity at the next walk. The 78 already had: Yandex 39 now read
"A crawler", Baidu 32 and Microsoft 2 "An automated browser" — Baidu's renderer declares that it is
driven by automation, and with the confirmation gone the declaration decides — and Ahrefs 5. Companies
that publish a file of addresses (Microsoft for most of its traffic, DuckDuckGo, OpenAI, Anthropic)
were not affected, because their visits carry the stamp on their own reports.

## Decision

**The answer is kept beside the visit it was established for.** `name_confirmations` holds the site,
the visit's derived name, when it began, the company, and when the answer was settled. It holds no
address and no user agent: the conclusion, never what it was drawn from. It is a `ReplacingMergeTree`
on `settled_at`, so writing an answer twice leaves one row and a later answer for the same visit
replaces an earlier one; sorted by site and then by when the visit began, because every reading asks
for one site's visits over a stretch of time; and kept for twelve months, as the verdicts are.

**Keyed by the visit, never by the visitor.** On a rented network one visitor key can stand for a whole
network's day ([0017](0017-a-rented-address-is-a-lease-not-a-visitor.md)). An answer spread to every
visit under the key would vouch for activity whose own address was never checked.

**Written before the verdict.** A pass that settles visits by name keeps their answers first and then
stores the verdicts, both stamped with the moment the pass began. A verdict is therefore never stored
that a later reading of its visit could not reach again. Either write failing ends the run before the
bookmark moves, so the next run reads the same visits and reaches the same conclusions.

**What the activity says is read first.** The reconstruction takes the company from the visit's own
reports where the collector marked one — from a list the company publishes, or from an address a name
check had already settled and handed back — and otherwise from the answer kept for the visit, bounded
by the window's own site and instants. That is the order 0033 already gives the two when a visit is
first judged: the activity's answer was reached report by report as the reports arrived, and the kept
one speaks for the single address the visit was asked about. A visit with no answer kept reads as
nobody's, which is not a denial.

**An answer already kept is not asked for again.** It comes back in the evidence, and a visit whose
evidence already names its company is never sent to a name server, while its address is still kept or
after.

**The answers the stored verdicts already carry are copied once** (`0011`). Every verdict reached on a
settled address names the company: as the `operator` of `identity.confirmed_crawler`, or of
`identity.false_claim` where the visitor named a different company. Only one detector writes either
code, and only when the evidence carried a settled company, so every verdict carrying one records an
answer the installation reached. The migration reads all of them, across every ruleset, so an answer
that later rules lost is found in the earlier verdict that still holds it. Per visit, the newest
verdict naming a company is copied, stamped with when the first of them was reached, so a second run
writes the same rows. The `operator` of `identity.declared_crawler` is never read: that is the
company the visitor named itself after.

Answers the collector marked on the activity are copied with the rest, whether a published list or an
address a name check had settled before the visit arrived was behind the mark. They are on the
activity already and are read first, so the copy changes nothing about them, and telling them apart
would mean reading every visit's reports back — where, since
[0034](0034-a-visit-begins-where-somebody-arrived.md), a visit's span can hold another visit's
reports.

**Removed with the site.** The table is on the list the telemetry purge empties, so from this release
on removing a website and closing an account both take its answers.

**What is happening now is not touched.** The live reading counts rather than judges
([0036](0036-what-is-happening-now-is-counted-not-judged.md)) and is keyed by visitor, so it never
reads this table.

## Consequences

**The privacy envelope is unchanged.** What is kept is the conclusion 0032 already treats as not
personal data, and the address is still cleared at 72 hours. A visit read again while its address is
still kept is now asked about less, not more.

**A kept answer holds whatever the asking policy is now.** `0011` copies answers reached under rules
that asked about more visits than [0037](0037-a-person-is-not-concluded-from-one-thing.md) allows.
Those addresses were sent already, and what was established is still forward-confirmed reverse DNS.
An installation that switches name checks off keeps believing what was established before, as it
does with the stamps on its activity.

**An answer a later verdict under the same rules replaced is recovered only while the store still
holds it.** Verdicts replace each other within a ruleset, and a visit was judged again under the same
rules — behind a visit still in progress before
[0054](0054-a-visit-still-in-progress-holds-back-only-itself.md), and since then when a later report
made it longer — with its address asked about again. Where that second question settled nothing (the
lookup timed out; it failed, and a failure is remembered as nobody for a day; or the address fell past
the most a pass asks about), the claim it produced replaced the confirmed verdict. `0011` copies the
earlier answer while both verdicts are still held. Where the store's merges have already discarded the
earlier one, those visits stay claims, and nothing left in the store says how many there are. From
this release on the question is not asked twice: the kept answer comes back with the visit.

**A domain withdrawn from the catalogue does not withdraw what it confirmed.** Taking a company's
answers back would be a migration naming the company, touching the marks on the activity as well as
this table.

**A change that renames visits must carry a repair for this table.** A visit is found by its derived
name. A code change to how visits are cut, or an operator changing
`Dewiride:Classification:IdleTimeout`, renames them: a part split off a visit drops back to a claim,
and a visit merged from several inherits its first part's answer.

**Rolling back keeps the answers, stops keeping new ones, and stops removing them.** The older
release neither reads, writes nor purges the table. Visits it judges by name in the meantime are kept
only in its verdicts, and `0011` does not run again when the newer release returns. A website removed,
or an account deleted, while the older release runs keeps its answers here — unreadable, because
every reading is scoped through a site that no longer exists — until the twelve-month expiry takes
them; the newer release does not revisit a removal already finished.

**Migrations are given an hour a statement.** `0011` is one statement over the verdicts'
twelve-month retention, applied by the migration step before the engine starts judging. Every
question a screen asks is held to the HTTP client's default of a hundred seconds; a script is recorded
as applied only once it has finished, so a migration held to the same limit would fail every start-up
of an installation large enough, each attempt beginning the statement again. The migrations
therefore run over a client of their own that waits up to an hour, which covers every migration and
not only this one. On the hosted installation `0011` takes under a second. While a long one runs the
engine is not yet listening, and the stack's readiness check gives up on it after about two and a
half minutes (half a minute's grace, then twenty-four failed checks five seconds apart):
`docker compose up --wait`, and a release with it, then report the engine unhealthy and leave the
dashboard unstarted, while the engine carries on and reports healthy once the migration has finished.
Running `docker compose up --wait` again then completes. An installation with
`Dewiride:Schema:ApplyOnStartup` switched off applies `0011` in whatever supervised step applies its
other migrations, before the engine runs.

**It is released together with ruleset 10.0
([0056](0056-somebody-can-be-reading-from-a-rented-computer.md)), never on its own.** What the
engine is shown is what a ruleset is, so an installation part-way through a walk would otherwise
judge the rest of its history with answers the first part lacked. Verdicts reached under 9.0 are left as they are. After
that release, the 403 visits above stay confirmed and the 78 are confirmed again when the walk
reaches them.

**Measured after the release.** The 10.0 walk had read every website by 2026-10-26, and the newest 9.0
and 10.0 verdicts were compared visit by visit that day. Every visit begun before the release day
whose identity 9.0 had settled — confirmed, or a claim shown false — is settled the same way under
10.0: 619 visits, the 403 that rested on the name alone among them; 484 are a known search crawler's,
71 an automated service's, 37 a web crawler's and 27 an AI crawler's, all `Verified`. The 78 the 9.0
walk had lost are confirmed again, 73 as a known search crawler's and 5 as a web crawler's, `Verified`.
The release filled the table with 702 answers: Baidu 352, Yandex 148, Microsoft 118, Ahrefs 42,
DuckDuckGo 26, Anthropic 8 and OpenAI 8.

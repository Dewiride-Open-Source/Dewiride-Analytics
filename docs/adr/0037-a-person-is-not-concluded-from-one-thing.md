# 0037 — A person is not concluded from one thing

- **Status**: accepted
- **Date**: 2026-09-14
- **Applies to**: both editions, and the whole of it is in the free product. Moves the ruleset to
  9.0, adds one observation and one catalogue sentence, grows both catalogues, collects nothing new,
  retains nothing new, and needs no migration. Nothing in `integrations/` changes, because nothing
  about what is captured changes.
- **Follows**: [0035](0035-a-visit-is-judged-on-all-of-itself.md), which settled what a visit is
  judged on; this one changes what the judgment concludes from it.
- **Follows**: [0036](0036-what-is-happening-now-is-counted-not-judged.md), whose live screen names
  a visitor only from observations that cannot be withdrawn; the observation added here is one of
  those.
- **Revises**: [0033](0033-an-identity-from-the-name-an-address-answers-to.md), which asked the name
  server about every visit not concluded to be a person. Under this ruleset a visit the product
  could not rule out as a person is not asked about either.

## Context

Thirty days of one installation's traffic — six sites, 1,978 finished visits judged under ruleset
8.0 — were read back against the verdicts the engine gave them. Every figure below comes from that
reading; the queries selected aggregates only, never an address, and reduced each user agent to a
bounded word.

**One visit in seven was called a person on the strength of one thing.** 268 visits (13.5%) were
`LikelyHuman` at `Weak`, and every one of them rested on `browser.script_executed` alone, with
nothing pointing the other way. 227 of them never sent an engagement report at all, 39 reported
under two seconds of engaged time, none scrolled, none moved a pointer. They arrived overwhelmingly
on desktop browsers with no referrer, and their networks were a data-centre operator in Beijing
(21), a rented server company (9), three carrier data centres (9), corporate security proxies (7) —
and, in smaller numbers, ordinary household connections. The mechanism was arithmetic:
`RenderingDetector` reports the tracker having run at weight 35 toward a person,
`EvidenceScorecard.Weighed` asked only that something heavier than 25 point that way and nothing
heavier point the other, and a browser that executes a script is the commonest thing an automated
browser does too. A screen labelled these "A person · slight signs", and the owner reading it
disputed the word "person" — correctly.

**A person could never be stated firmly.** Of the 1,175 visits called a person at `Moderate`, at
least 905 (77%) had three or more independent observations agreeing — read for a while, scrolled,
used a pointer, typed — with nothing contradicting. They stayed at `Moderate` because `Corroborated`
reached `Strong` only through one observation weighing 65 or more, and nothing a person does is
allowed to weigh that much, deliberately: each of those things can be produced by a script. So the
band structure had a ceiling that no combination of a person's own behaviour could pass, while a
visit with a single observation sat one band below it.

**Crawlers that said what they were, in a name the catalogue did not hold, were called people.** A
search engine's spider from Sogou made 60 visits, and 43 of them read "A person" — 24 at `Moderate`,
because it renders pages and scrolls them. Yisou's spider made 25 and 12 read "A person". Eight
smaller programs — `hanaleibot`, `sebot`, `promptingbot`, `exasearchbot`, `bitsightbot` and the rest
— were spread across "Browser automation" and "Suspicious automation". Each of them carried the word
`spider`, `bot` or `crawler` in its user agent; the engine read a user agent only for the names it
knew, so a self-description in an unknown name weighed nothing at all.

**The owner's own sign-in read as a break-in.** `ProbingDetector` counted a request for `/wp-admin`
or `/wp-login` as `probing.sensitive_paths` whatever the site answered, so a site owner opening
their own administration pages, served 200 by their own plugin, would be "Probing for a way in". No
production verdict carried the signal — every site on this installation reports from the browser
tracker alone, so no request carries a status code and the probing rules never fire — but the rule
was wrong on its face and would fire the day a server-side reporter is installed.

**"A search engine" meant two things.** All 115 `KnownSearchCrawler` verdicts arrived with no
referrer, as a crawler does, and every one read "A search engine · Came straight here" on the
journeys screen, where the same three words also name a visit that arrived _from_ a search. That is
copy rather than judgment and is repaired where the copy lives; it is recorded here because it is
the sentence the owner saw first.

## Decision

**A person is concluded only from corroboration.** `EvidenceScorecard.Weighed` calls a visit
`LikelyHuman` where what points toward a person is _firm_ — one observation weighing 55 or more, or
two that each weigh 25 or more — and outweighs what points away. One passing remark is a lean, and a
lean is answered as `Unknown`, which rule 12 says is a correct answer and which was never given to a
visit from the tracker before. Somebody who scrolled, or read for two seconds with the tracker
running, is still two things agreeing and still a person; somebody who read for fifteen seconds is
substantial on their own. A page opened and left, with nothing else observed, is something the
product could not tell. `LikelyHuman` at `Weak` is therefore impossible by construction rather than
rare.

**Three observations agreeing reach the firmest behavioural band.** `Corroborated` returns `Strong`
where three or more counted observations agree, whatever the heaviest weighs, and the Troubling cap
still holds it to `Moderate` where anything weighing 50 or more points the other way. A reader who
read, scrolled and clicked has done three things that each had to be produced, and that is a firmer
position than one loud thing. `Verified` remains what it was: an identity established from an
operator's published addresses, never behaviour.

**A program that calls itself a crawler is a crawler.** `CrawlerWords.AppearIn` reads the first
kilobyte of a user agent for `crawler`, `spider`, `scraper`, `fetcher` or `bot` ending a word — the
word may begin mid-token, because operators write `PetalBot` and `Bytespider` — without a regular
expression, which nothing in this product runs over a user agent. A phone maker whose name ends in
one (`CUBOT`) is excused by name, and the review found no other of the kind in thirty days. The
observation is `identity.declared_generic_crawler`, weighing 60 toward automation, the same as a
named tool: both say what kind of thing was fetching and neither says whose. It carries no
parameters, deliberately — the name is text the visitor wrote. `Decide` reads it after a catalogued
name and a declared web driver and before the weighing, so it settles `GenericWebCrawler` whatever
else the visit did: a self-described crawler that also scrolled is a crawler, on the same principle
as a scanner that also scrolled. The live screen names it at once, because a description that
travels with every request cannot be withdrawn.

**Probing is a request the site refused.** `ProbingDetector` counts a request for a sensitive path
only where its status is 400 or above. A status of `null` is not refused: a status is absent only
when no request-path surface saw the page, which means the only sighting came from the page's own
browser, which means the page was served and rendered far enough to run the tracker — an owner on
their own administration page, never a sweep. This also closes a contradiction between the live
reading and the finished visit: the two read a page's status from different sightings, and under a
rule where `null` counted, a dual-surface owner opening `/wp-admin` would have been named "Probing
for a way in" live and excused once the visit finished.

**A visit that might be somebody reading is not asked about.** 0033 sent the address of every visit
not concluded to be a person to a name server. With the first rule above, every one-page visit whose
browser ran the tracker becomes `Unknown`, and the letter of 0033 would send all of them.
`SessionClassifier.WorthAsking` now also excludes an `Unknown` verdict any of whose signals point
toward a person. The price is the one 0033 accepted, slightly larger: a crawler that ran the tracker
and did nothing else, in no name the catalogue holds, stays unrecognised by that route.

**Both catalogues grew from what the review showed.** Sixteen crawler names were added, each read
from its operator's own page and never from a third-party list: Semrush, Majestic, Moz, Pinterest,
Yahoo, Seznam, Cốc Cốc, UptimeRobot, Pingdom, StatusCake, Better Stack, DataForSEO, Babbar and
Google's feed fetcher. Two more published range files are fetched twelve-hourly (Seznam, Babbar) and
five more companies are recognised by the name their addresses answer to. Six data-centre networks
were added to `HostingNetworks`, each confirmed by number in the routing registry data the product
already holds: Sinnet, HostRoyale, Volcano Engine, and three carrier data centres numbered apart
from the household backbones they belong to. The backbones themselves — the networks most of China
browses from — stay out, and the test names them so the absence is a decision rather than an
oversight.

**Two crawlers the review found are deliberately not catalogued.** Sogou's own page confirms its
spider is a search crawler but never states the token it sends; Shenma's has no reachable operator
page at all. Neither publishes addresses or a domain to confirm against, so a catalogue entry would
give the same verdict as the crawler-word rule — `GenericWebCrawler` — under a name this product
could not cite. The rule names them "A crawler", which is what is known.

**This is ruleset 9.0.** Categories move, so it is a major version, and `ClassificationWorker`
re-judges every site's history from where it began. Verdicts are kept per ruleset; nothing is
deleted, and the newest ruleset wins per visit.

## Consequences

**On the day 9.0 finishes re-judging this installation**, measured against the same thirty days: 268
visits leave "People" for "Couldn't tell", 41 of them for "Suspicious automation" instead because
their networks are now catalogued; at least 905 visits called a person with fair signs are called
one with strong signs; about sixty visits by self-described crawlers become "A crawler". The People
figure on the overview falls by roughly an eighth, and that is the figure being honest. An 8.0 → 9.0
transition matrix per visit is to be run once the walk is complete, in the shape of the review's
queries.

**"Couldn't tell" is now a band a customer will see often**, for one-page visits that never scrolled
or stayed. It keeps the band arithmetic — "Couldn't tell · slight signs" says how much was seen —
and it is the honest reading of a bounce, which this product has no way to tell from a headless
browser that rendered one page. A rule that forced these into either category to avoid showing the
band is exactly the logic rule 12 forbids.

**A person can never be reported on slight signs**, which removes a sentence from the product rather
than tuning it. The screens change no copy for it; the row simply never occurs.

**Re-judging costs what 0035 recorded**: a site is walked from the day it was added in six-hour
stretches, about half an hour per year of history at the least, and screens mix rulesets per visit
while the walk runs, newest winning. Nothing is removed.

**Residuals accepted.** A credential-stuffing program hitting a login page that answers 200 is no
longer a scanner on that alone; it is caught, if at all, by what else it did. The tracker has no
business on administration pages, and a site that puts it there is measuring its own staff. A
crawler that describes itself in a word this list does not hold — `agent`, `client`, a bare product
name — is weighed as before. Rented-server visits that also read — 264 in the review, 120 of them
reading, and on Microsoft's cloud 31 of 32 — stay "Suspicious automation" with the reading
shown against the verdict; the instrument for a corporate proxy is the hosting catalogue's exclusion
list, and Microsoft's cloud is not a proxy. `Unknown` reaching `Moderate` or `Strong` when
observations agree in both directions is unchanged.

**A catalogue entry after this ships is the next major.** A crawler name, a network or a
`CUBOT`-shaped exception added later moves visits between categories, and `RulesetVersion` says so
in its own remarks; it is a 10.0 rather than a footnote to this one.

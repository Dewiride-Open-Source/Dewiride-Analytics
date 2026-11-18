# 0077 — Where a visit ran and what it said can outweigh how it read

- **Status**: accepted
- **Date**: 2026-11-18
- **Applies to**: both editions, and the whole of it is in the free product. Moves the ruleset to
  13.0, adds a catalogue of networks companies keep for their own computers, three networks to the
  catalogue of rented ones, three observations and three catalogue sentences, and reads for the first time
  three columns already stored: the clock a browser reported, and the browser and system the collector
  named. Collects nothing new, retains nothing new, and needs no migration. Nothing in `ee/` changes:
  the hosted website explains verdicts by category and band, not by reason. Nothing in `integrations/`
  changes, because nothing about what is captured changes.
- **Revises**: [0011](0011-visitor-location-and-network.md), under which classification was untouched
  by the browser and system named there.
- **Revises**: [0016](0016-where-a-visit-came-from-counts.md), under which where a visit ran was read
  against one catalogue, of networks that rent out computers.
- **Revises**: [0037](0037-a-person-is-not-concluded-from-one-thing.md), under which a page another
  site sent counted as seen a quarter of the way down whether or not it was ever on screen.
- **Revises**: [0056](0056-somebody-can-be-reading-from-a-rented-computer.md), whose release of a
  visit over a rented network that was then used does not reach a network nobody rents.
- **Revises**: [0059](0059-a-page-left-open-is-not-a-page-read.md), which refused any rule on what a
  browser said about itself.
- **Revises**: [0065](0065-somebody-who-came-straight-in-has-to-have-stayed.md), which refused a rule
  on Linux, and judged a visit another site sent exactly as 11.0 did.

## Context

The operator of the hosted installation reported, for a WordPress blog measured since 2026-11-15, many
machines counted as people: visits from Facebook at no time at all, and repeated direct traffic on tag
pages. Measured read-only there between 07:15 and 08:10 UTC on 2026-11-18, over the newest verdict of
each visit, the blog had 81 people:

| Who                            | People | What they did                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ------------------------------ | -----: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Browsers on Meta's own network |     42 | The blog shares each new post to its Facebook page seconds after publishing it. Seven to eleven seconds after each share, browsers on Meta's network opened the post, two to five in the same second under different visitor keys, Facebook named as the sender, a clock eight hours behind UTC, American English, placed in the United States, Ireland and Sweden. Phones at 412 × 915 stayed no time at all with a quarter of the page on their first screen; desktops at 1050 × 1893 stayed 2 to 25 seconds. |
| One fleet's account of itself  |     11 | Chrome on Linux, 945 × 972, American English, a clock eight hours ahead of UTC, nothing named as a sender, nothing pressed, 17 to 62 seconds on tag and series pages, some loaded twice. The same description had opened the same pages from DigitalOcean, where the network made it automation, before arriving over household and mobile networks in China, where nothing pointed the other way.                                                                                                              |
| Another group of browsers      |      8 | Chrome 143 on Linux, 1280 × 800, a clock at UTC, American English, nothing named as a sender, 15 to 59 seconds. One arrived over Limestone Networks; seven over four small networks that carried nothing else on any website.                                                                                                                                                                                                                                                                                   |
| A cloud network                |      1 | UpCloud's: straight to the contact page, keys pressed, 59 seconds, a clock at UTC while placed in the United States.                                                                                                                                                                                                                                                                                                                                                                                            |
| Readers                        |     19 | Sent by search engines or a messaging app, or with nothing named as a sender; 13 pressed something.                                                                                                                                                                                                                                                                                                                                                                                                             |

Two rules made the first four groups people. A visit another site sent, with the tracker having run
and the first screen holding a quarter of the page, is two things agreeing (0037), at no time at all;
and a visit that came straight in and stayed fifteen seconds is a person over any network the rented
catalogue does not hold (0065). None of the networks those groups came over was in a catalogue.

The blog's own host counted the same loads. Its statistics credited Facebook with 52 views that day,
exactly the 52 loads from Meta's network with a desktop Facebook address, post by post.

Across the installation's whole record, at the same reading: Meta's network carried 1,211 visits and
42 were people, all on the blog, because elsewhere its browsers named one of Meta's crawlers and read
"Says it's an AI crawler". The fleet's account of itself — Linux, Chrome, American English, eight
hours ahead, straight in, nothing pressed — matched 18 people (11 on the blog, 5 on another website in
October at 1904 × 1000, 2 on a third at 1350 × 1286) and no reader: the readers on Linux with that
clock and language had a search engine send them, or named Brave or Firefox. The second group's account
— Linux, Chrome, American English, a clock at UTC, a window of exactly 1280 × 800, straight in, nothing
pressed — matched its 8 people, 2 visits on the blog that read "Couldn't tell", and 20 visits on two
other websites, nine or ten pages each about once a week over Amazon's network, already automation; without
the window it also reached one person on each of those two websites. Away from Meta's network,
visits called people whose every report said no time on screen at all numbered 18, all on another
website, 10 of them in the last thirty days, each a page another site sent whose first screen counted
as seen.

Meta's page for its crawlers (https://developers.facebook.com/docs/sharing/webmasters/web-crawlers,
read 2026-11-18) publishes no list of addresses, no network number and no name its machines answer to.
Nothing can confirm a visit as Meta's, so nothing here says one is: it says where the visit ran.

## Decision

**A network a company keeps for its own computers** is weighed as heavily as a rented one, 65 against
the 60 fifteen seconds carry, and is never answered by what the visit then did (`CompanyNetworks`,
`network.company_machines`). Meta's network (32934) is the one entry, listed because real traffic
showed these browsers arriving over it, and read from the same routing registry data as the rented
catalogue. Nobody rents a computer there, so the release 0056 gives a reader on a desktop rented in a
cloud has nothing to answer, and its visitors are still told apart by the address each one used. A
visitor naming one of that company's own crawlers is left to what it called itself: the name is a claim
the company publishes no way to confirm, and the network beside it would read as that confirmation. A
visitor naming another company's crawler over it has the network among its reasons.

**Three more rented networks.** Limestone Networks (46475) sells bare metal, cloud and colocation, and
UpCloud (202053 and 25697) is a cloud platform; each says so of itself. A visit that came straight in
over one is weighed as over any rented network, and one another site sent over one goes through 0056's
release like any other. Kept out, and pinned out by a test: Datacamp's server network (212238) and M247
Europe (9009), whose addresses carry consumer privacy services' exits, and CDN77 (60068), a delivery
network that carries some too.

**Two fleets' accounts of themselves.** The engine is told what clock a visit's page views agreed on
and which browser and system the collector named, as the visit list names them, and reads the language
from the first report to name one, so a visit is judged on one account of it whatever order its
reports arrived in. A visit that came straight in, with pointer and keys both watched for and neither
pressed, from a browser saying it is Chrome on Linux, asking for exactly `en-US`, with its clock exactly
eight hours ahead of UTC, and not saying it is a phone, carries `browser.fleet_description` toward
automation at 65 — above the 60 a stay carries, which is what made the fleet's visits people; at 60 the
two would tie and the visit would read "Couldn't tell". The same visit with its clock exactly at UTC
and a window of exactly 1280 × 800 carries `browser.fixed_window_fleet_description` instead, at the
same weight, under a sentence of its own so the reason names the account the visit gave. The window is
part of the second account because without it the account reaches visits nothing else ties to the
group. The operator chose that both read "Something automated", overruling 0065's refusal of a rule on
Linux for these two descriptions and nothing wider.

**A page never on screen.** How far down a page came into view counts only where some page of the
visit was on screen at all. The tracker counts time only while a page is visible and reports the share
its first screen would hold whether or not it ever was, so a page opened behind another tab and closed
unseen counted as seen a quarter of the way down. It no longer does. Such a visit is not thereby
called silent: somebody opening tabs to read later does that too, so the depth still keeps a visit of
several pages from reading as one on which nothing happened, and the operator chose that it read
"Couldn't tell".

**Thirteen.** Categories move, so this is a major version, and every earlier answer stays on record.

## What this deliberately does not do

- **The four small networks the Chrome 143 group came over are not listed as rented.** Nothing their
  holders publish says what business they are in, their own websites do not answer, and their address
  space carries a published location file of the kind leased address blocks use. The sentence a rented
  network carries says its company rents out computers, which nobody has established of them. The
  group is recognised by its own account of itself instead.
- **No rule on Linux, on American English or on a clock alone,** and none on a clock that disagrees
  with where a network is placed: that catches travellers and people on privacy services, and on
  Chinese networks the fleet's clock agrees with its place.
- **No rule across visits.** The same window opened from several networks within a minute caught 6 of
  the fleet's 11 people and dozens of readers on common screens; judging one visit by others was
  refused in 0065 and stays refused.
- **No rule on archive pages.** Only one website has them, with three days of record.
- **No identity for Meta.** No `Verified`, no category with "known" in its name, and no service named:
  where a visit ran is established, which of the company's services ran it is not.
- **Nothing on a browser string that names Windows while the browser's own platform says Linux,** which
  Meta's desktops did; the network already decides them, and seven visits elsewhere were all it added.
- **What another site sent is otherwise judged as before.** A moment on a page, a page shown and a press
  count where another site sent the visit, exactly as 12.0 counted them, except over Meta's network,
  over the three newly listed rented networks under 0056's release, and on a page never on screen.
- **Nothing about reports filed under a key that never announced its page,** which Meta's browsers and
  some others produced; they belong to no visit, as 0034 decided, and are another piece of work.

## Consequences

**Simulated before release**, read-only on the hosted installation at 11:39 UTC on 2026-11-18, by
replaying each visit's newest 12.0 evidence with the changes applied, beside a control that reproduced
every one of the 25,092 stored 12.0 categories and bands exactly:

| Website          | People under 12.0 | Under 13.0 | Why they move                                                                                     |
| ---------------- | ----------------: | ---------: | ------------------------------------------------------------------------------------------------- |
| The blog         |                84 |         20 | 44 over Meta's network, 11 the first fleet's account, 7 the second's, 2 over Limestone or UpCloud |
| A developer blog |             7,244 |      7,221 | 18 never on screen → "Couldn't tell", 5 the first fleet's account                                 |
| A third website  |             1,040 |      1,038 | 2 the first fleet's account                                                                       |
| The rest         |               108 |        108 |                                                                                                   |

The blog's 84 against the 81 in Context are three visits that began between the two readings: two over
Meta's network, and one that stays a person. The Limestone visit gave the second account too, and is
counted once, under its network. Besides the people: 77 visits over Meta's network, 15 in the first
fleet's account and 2 in the second's move from "Couldn't tell" to "Something automated"; the 20 visits
over Amazon's network that gave the second account keep their category and band, and now give that
account among their reasons. The 1200 × 3000 fleet of 0065 said the first fleet's four things about
itself, so where its window already made it automation the first account now agrees with it, and 3,131
visits read "strong signs" rather than "some signs"; 3 already automated over Meta's network read
"strong signs" rather than "slight signs". The one reader over UpCloud another site sent, who pressed
and read, stays a person, held to "some signs"
by the network. No visit that named a crawler changes category. Visits nothing could see pressing on
are not described at all, which leaves 984 visits on the developer blog and 10 on the blog, none of
them a person, as they were.

**Name checks.** A visit that comes to read as machinery is asked about while its address is kept, as
every such visit is; at the simulation 146 visits on the blog and 1 elsewhere that 12.0 left as people
or undecided were inside those 72 hours, well within the default `MostNameChecksPerPass` of 500.

**Keys on the three networks.** From the release, a visitor on one of them is recognised by the network
rather than the address, as on every rented network (`VisitorConnection`). Visits already stored keep
the keys they were given, and going back does not undo keys written in the meantime.

**Stated, not hidden.** The first fleet's account is one setting from defeat: a fleet that changes its clock,
language, system or browser name escapes it, and this one has already moved between networks. A reader
whose browser says all four things, who comes straight in — typing the address, from a bookmark, or
from a mail or chat app that names no sender — and presses nothing is called automated, wherever their
clock reads eight hours ahead of UTC; the record holds none. The second account is as fragile and as
blunt: a reader on Linux and Chrome in American English, in a window of exactly 1280 × 800, whose clock
reads UTC — as every clock in Britain, Ireland and Portugal does from late October to late March, and
in Iceland and much of West Africa all year — who comes straight in and presses nothing, is called
automated; the record holds none either. Somebody browsing over Meta's own network is called automated
however they arrived; one of the 42 pressed something. Everybody totals still count
the loads over Meta's network, as machinery.

**Rolling back** to 12.0 leaves the 13.0 verdicts on record beside the 12.0 ones. Reads take the newest
ruleset each visit has, so a visit judged under 13.0 keeps that verdict, and one judged only after
going back is judged under 12.0 until 13.0 returns and reaches it. Kept months stay as 13.0 counted
them (`MonthsToCount`). An older dashboard has no sentence for the three new observations, so where it is
shown one it says "Something else about this visit pointed that way."

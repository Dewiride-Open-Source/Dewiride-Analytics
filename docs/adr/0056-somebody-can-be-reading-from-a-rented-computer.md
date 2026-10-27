# 0056 — Somebody can be reading from a rented computer

- **Status**: accepted
- **Date**: 2026-10-25
- **Applies to**: both editions, and the whole of it is in the free product. Moves the ruleset to
  10.0, adds one observation and four catalogue sentences, rewrites two sentences, and tells the
  engine for the first time what kind of site sent a visit. Collects nothing new, retains nothing new,
  and needs no migration. Nothing in `integrations/` changes, because nothing about what is captured
  changes.
- **Revises**: [0014](0014-where-traffic-comes-from.md), under which a change to the catalogue of
  sending sites was a code change and nothing else. The kind of site is now part of the evidence a
  verdict keeps, so such a change ships with a new ruleset.
- **Revises**: [0016](0016-where-a-visit-came-from-counts.md), under which a visit over a rented
  network was machinery whatever it did.
- **Revises**: [0017](0017-a-rented-address-is-a-lease-not-a-visitor.md), whose stated cost — several
  programs on one rented network counted as one visitor — can now also put a program's pages inside a
  person's visit.
- **Revises**: [0037](0037-a-person-is-not-concluded-from-one-thing.md), which held that Microsoft's
  cloud is not a proxy. It carries people too; whether a key was pressed, or whether another site
  sent the visit and the pointer was then pressed or its pages read, is what tells them apart.
- **Follows**: [0055](0055-an-answer-outlives-the-address-it-came-from.md), whose kept answers are
  part of what 10.0 is walked with.
- **Revised by**: [0059](0059-a-page-left-open-is-not-a-page-read.md). Read means fifteen seconds in
  front of somebody that the pages said as they were left; a page left open is not read, so one
  another site sent that was left open is "Couldn't tell" unless something was pressed or typed.

## Context

A visit over a network in `HostingNetworks` carries `network.hosting` at 65, above anything a person
is observed doing (reading at most 60), and the weighing compares the heaviest observation each way.
So a visit from a listed network could never be a person, nor even something the product could not
tell, whatever it read, scrolled, pressed or typed. 0016 chose that on purpose, and 0037 kept it.

Read-only on the hosted installation, ruleset 9.0, the thirty days to 2026-10-23 — 3,778 judged
visits on six sites:

| What                                                                          | Found                                                                       |
| ----------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| Visits over a rented network called a person                                  | 0 of 568                                                                    |
| Called "Something automated" by the network alone                             | 357; without the network: 272 person, 85 couldn't tell, 0 automated         |
| Named automation (976 crawlers, services and declared drivers) sent by a site | 1 (0.1%)                                                                    |
| Named automation that pressed a key / the pointer                             | 0 / 3 of the 976, all automated browsers that said so                       |
| People on ordinary networks arriving from a search engine                     | 76% (Google 57%, Bing 14%); pointer 52–68%, keyboard 14–22%                 |
| Alibaba Cloud's 62 visits                                                     | all "macOS Chrome", none sent by a site, no pointer, no key: a fleet        |
| Microsoft visits a search sent                                                | pointer 41%, a quarter seen 73%, a key 23%, median read 74 s — as people do |

Microsoft's network also holds a shape that matches what its own mail protection is documented to
do — Safe Links checks a link when it is clicked and the address "is asynchronously sandboxed to
update its reputation"
([Microsoft Learn](https://learn.microsoft.com/defender-office-365/protection-stack-microsoft-defender-for-office365)).
Straight in, Chrome on Windows, no key: 28 of 43 hands-off visits and 8 of 11 that pressed the
pointer read for 20–29 seconds (people on ordinary networks: 15 of 408); their reading times spread
over 33 seconds against people's 1,286; and 49% arrived a median 20 seconds behind somebody else
reaching the same page (people: 3–9%). Nothing about one visit establishes that it was a scanner, and
nothing here says so.

Microsoft documents three ways people reach the web from its own addresses: Windows 365 Cloud PCs
([Microsoft Learn](https://learn.microsoft.com/windows-365/enterprise/windows-365-network-deployment-options)),
Azure machines including Azure Virtual Desktop hosts, which reach the internet from Microsoft-owned
public addresses
([Microsoft Learn](https://learn.microsoft.com/azure/virtual-network/ip-services/default-outbound-access)),
and Entra Internet Access, which sends employees' browsing out from 128.94.0.0/19 and 151.206.0.0/16
([Microsoft Learn](https://learn.microsoft.com/entra/global-secure-access/reference-egress-ip-ranges)) —
both originated by AS8075 according to RIPEstat on 2026-10-23. Amazon WorkSpaces reach the internet
from AWS addresses
([AWS documentation](https://docs.aws.amazon.com/workspaces/latest/adminguide/amazon-workspaces-internet-access.html)).
0037's "Microsoft's cloud is not a proxy" no longer holds.

## Decision

**What sent a visit becomes evidence.** `SourceChannel` moves to the domain, unchanged, and
`SessionEvidence.SentBy` carries it: settled exactly as the visit list settles it — the first report
whose referring address names a site other than the measured one, through the catalogue of sending
sites — by the same expression in the engine's reconstruction, the visit list and the live reading.
The site's own address now travels with every reading (`SiteRegistration.Domain` →
`SessionWindow.SiteDomain`), so a reader moving between its pages, or onto its own subdomains, is no
arrival. Only the kind reaches the engine: the referring address and the site it names stay inside the
statement. Because a verdict keeps the kind it was judged with until history is judged again, a
correction to the catalogue of sending sites now ships with a new ruleset rather than on its own.

**One new observation, too light to count.** `arrival.from_another_site` points toward a person at
20, below the 25 at which an observation counts toward any conclusion, with the kind of site as its
only parameter — `search`, `assistant`, `social` or `link` — and never the site. On an ordinary
network it changes no verdict and no band about a visit anything else was observed about. A visit it
is the only observation of reads "Couldn't tell" rather than "Too little to go on", and so is not sent
to a name server. This build's own collection never produces one: the image a browser fetches without
scripts carries no referring address, and every other way of reporting a visit adds an observation
about the browser. It takes reports whose capture surface the running build does not recognise, as
rows a later release wrote would be after going back to this one.

**Where the rented network is the only objection, what the visit did decides.** At the head of the
fallback weighing, where `network.hosting` is the only observation that counts pointing toward
automation:

| …and the visit                                                                                                                      | Answer                           |
| ----------------------------------------------------------------------------------------------------------------------------------- | -------------------------------- |
| had a key pressed, with one more observation that counts                                                                            | A person, with some signs        |
| was sent by another site and then had the pointer pressed, with one more that counts                                                | A person, with some signs        |
| was sent by another site and its pages were in front of somebody 15 seconds or more in all, with one seen a quarter of the way down | A person, with some signs        |
| was sent by another site and did neither                                                                                            | Couldn't tell                    |
| came straight in with no key pressed                                                                                                | weighed as any other: automation |

The network stays among the reasons, pointing the other way, so a person concluded here is never
stated more firmly than some signs. The second observation is ordinarily the tracker having run at
all, so what the rule rests on in practice is one key pressed, an arrival and one press of the
pointer, or an arrival, 15 seconds in all in front of somebody and one page seen a quarter of the way
down — and the code says so rather than describing more corroboration than there is. Any other
observation that counts toward automation — no user agent, no language asked for, no script run,
nothing happening on watched pages, pages taken too fast or too widely, pages asked for that were
never there, a tool naming itself — is a second objection, and the visit is weighed as any other.

**How far down a page was seen is not, alone, somebody reading.** The tracker reports the deepest
point of a page that came into view, and that counts the first screen: a page no taller than four
screens is seen a quarter of the way down by being shown. Counted alone, it would make a person of a
made-up link to a short page opened and left. Of the 54 visits in the simulation below that the rule
would release with the depth counting alone, 16 rested on it with nothing pressed or typed, and five of
those had their pages in front of somebody for less than 15 seconds in all. So the depth counts here
only beside 15 seconds or more in front of somebody, summed across the visit's pages — where reading
already counts by itself — and a reader who stays is released without having to click anything. Both
figures are the visit's rather than one page's: the time is added up across its pages and the depth is
the deepest any of them came into view, so the 15 seconds need not have been spent on the page seen a
quarter of the way down.

**The network's sentence stops saying more than is known.** "It came from a computer rented at
{operator}, not from a home or office connection" was false for staff behind a provider's security
edge, and it now sits under some people's verdicts. It reads "It came over a network where {operator}
rents out computers."

**So does the depth's.** "Somebody scrolled {percent}% of the way down a page" claimed a scroll the
reading cannot show. It reads "A page was seen {percent}% of the way down."

**Ten.** Categories move, so this is a major version, re-judging history as every major does. It is
walked with the answers 0055 keeps.

## What this deliberately does not do

- **No person from a referring address alone,** nor, over a rented network, from a referring address
  and a page merely shown. An arrival is a faint sign, and 0037's rule that a person is never
  concluded from one thing still holds; on a rented network it opens the question rather than
  answering it.
- **No rule for one vendor's scanner.** The hands-off shape on Microsoft's network stays automation
  because it came straight in and typed nothing, not because of what it resembles.
- **No device inferred.** "Chrome on Windows" is a string the visitor writes.
- **Nothing in the catalogue or its weight moves.** Microsoft and Amazon stay listed: leaving them out
  would release the hands-off shape on Microsoft's network with them.
- **How firmly an open answer is stated is unchanged.** A visit another site sent that did neither
  reads "Couldn't tell · strong signs" where enough else was seen, by the band arithmetic 0037 records
  for an open answer: the band says how much was seen, not how sure the answer is.

## Consequences

**Simulated in one run** on 2026-10-25 over the 9.0 verdicts of visits that began from 2026-09-23 to
2026-10-22 — 3,723 judged visits, 2,105 of them people: of the 351 visits where the rented network was
the only objection, 49 become people (20 on a key, 18 on an arrival and a press of the pointer, 11 on
an arrival and a read), 30 become something the product could not tell, and 272 that came straight in
with no key pressed stay automation. Visits that other evidence also holds there stay automation, as
before. People go from 2,105 to 2,154. No visit with the hands-off shape is released. The table above
was read two days earlier over a window ending at the moment it was read, and counted the visits the
network made automation however much else pointed that way, which is why its 357 is not this 351.

**What sent a visit now narrows the name check.** A visit concluded to be a person, or left open with
something pointing toward one, is never asked about (0033, 0037). So a visit this rule answers — a
person, from a key, or from an arrival and a press or a read, or left open after an arrival — is no
longer sent to a name server; one held to automation by a second objection is asked about as any
automation verdict is.

**What is happening now is not named differently.** The live reading names a visitor only from
observations that cannot be withdrawn (0036), and none of the observations this rule reads is one.

**Stated, not hidden:**

- One key press releases a visit that came straight in. Named automation almost never typed — none of
  the 976, on any network — and next to none of the unnamed visits on Tencent Cloud's, Google Cloud's,
  Sinnet's or China Unicom's networks did, but almost never is not never.
- A forged referring address and one press of the pointer, or 15 seconds in all on pages one of which
  is short enough to be seen a quarter of the way down by being shown, or a single key, over a rented
  network reads "A person · some signs".
- People another site sent who read only the first screens of long pages, or whose pages were in front
  of them for less than 15 seconds in all, without pressing anything, read "Couldn't tell" rather than
  "A person".
- On every other network the depth still counts beside the tracker, as 0037 left it: a page short
  enough to be seen a quarter of the way down by being shown, opened and left, reads "A person · some
  signs" whatever sent it, and "A person · strong signs" once it has been on screen for two seconds.
  In the run above, 22 of the 2,105 people rest on the depth that way and on nothing else. Whether it
  should count there only beside a read, as it does here, is not decided here.
- Search arrivals over the smaller hosting networks in the catalogue, HostRoyale's and Latitude.sh's
  among them, are released on the same terms as Microsoft's.
- Eighteen straight, key-less visits on Microsoft's network without the 20–29 second shape stay
  automation, and people on a cloud desktop who arrive from a mail or chat application, or a
  bookmark, and never type stay automation too.
- Everything on a rented network describing itself identically is one visitor for the day, and one
  visit while its activity is never quiet for as long as the idle timeout (0017). Where that visit is
  judged a person, whatever the others did in that stretch is counted in that person's visit; a busy
  company gateway reaching 25 pages trips the breadth observation, which blocks the rule, so heavy
  egress stays automation.

**Refused:** taking Microsoft and Amazon out of the catalogue, which releases the hands-off shape;
lowering 65, which brings back 0016's scraper reading as a person; counting the referring address as a
person's observation, which makes a glance from a search a person against 0037; counting how far down
a page was seen as somebody reading on its own, which makes a person of a made-up link to a short page;
requiring a press or a key of every visit another site sent, which leaves people who read without
clicking as couldn't-tell — between a third and a half of the people on ordinary networks never pressed
the pointer; a rule on "Chrome on Windows", a string the visitor writes;
and a rule on following somebody else to a page, which the engine cannot see, because it judges one
visit at a time.

**Measured after release.** The 10.0 walk had read every website to within the idle timeout of the
present by 2026-10-26 11:25 UTC, and the newest 9.0 and 10.0 verdicts were compared visit by visit
that day. Every one of the 6,811 visits begun before the release day has both.

- The 351 went exactly as simulated. 49 are people (`LikelyHuman · Moderate`; a key was pressed in 20,
  another site sent 42), 30 could not be told (`Unknown · Strong`; another site sent all 30, and none
  pressed anything), and 272 stay automation (`SuspiciousAutomation · Moderate`; another site sent
  none). By the network's owner:

  | Network             | Visits | Person | Couldn't tell | Automation |
  | ------------------- | -----: | -----: | ------------: | ---------: |
  | Microsoft           |     82 |     20 |             5 |         57 |
  | Alibaba Cloud       |     61 |      0 |             0 |         61 |
  | Tencent Cloud       |     49 |      2 |             0 |         47 |
  | Amazon Web Services |     49 |     11 |             2 |         36 |
  | Google Cloud        |     45 |      3 |             0 |         42 |
  | Sinnet              |     11 |      0 |             0 |         11 |
  | HostRoyale          |     10 |      0 |             9 |          1 |
  | Latitude.sh         |      9 |      2 |             7 |          0 |
  | China Unicom        |      8 |      0 |             0 |          8 |
  | Eleven others       |     27 |     11 |             7 |          9 |

- Over the run's window people went from 2,105 to 2,168, not to 2,154. The 14 beyond the simulation
  are not this rule's: none is on a rented network. 9.0 judged each 30 to 51 minutes after the last
  report it had, before the rest of the visit arrived — every report 10.0 read beyond where 9.0's
  reading ended came afterwards, and eleven were judged on their opening page view alone — and could
  not tell. 10.0's walk read each once all of it had arrived, 36 minutes to more than nine hours from
  first report to last, and found it read, seen or pressed.

# 0059 — A page left open is not a page read

- **Status**: accepted
- **Date**: 2026-10-27
- **Applies to**: both editions, and the whole of it is in the free product. Moves the ruleset to
  11.0, adds two observations and three catalogue sentences, and reads more out of activity already
  stored: which kind of report each reading came from, and the time of day the tracker's own reports
  carry. Collects nothing new, retains nothing new, and needs no migration. Nothing in
  `integrations/` changes, because nothing about what is captured changes; the tracker gains a
  comment and a test that holds its waits to the engine.
- **Revises**: [0037](0037-a-person-is-not-concluded-from-one-thing.md), under which somebody who
  read for fifteen seconds was substantial by itself. The fifteen seconds now count only where the
  pages said them as they were being left.
- **Revises**: [0056](0056-somebody-can-be-reading-from-a-rented-computer.md), whose "read" — fifteen
  seconds in all in front of somebody with a quarter of a page seen — now counts the same time 0037's
  reading does.

## Context

The tracker reports how long a page has been in front of somebody twice over. While the page stays
open it says how it is going — fifteen seconds after the page opened, then thirty seconds later, then
every minute — so that a browser shut down outright is still measured. As the page is left — closed,
navigated away from, or put behind another tab — it says so, with the whole of the page's time so far.
Both kinds of report were added up the same way, and fifteen seconds in front of somebody is enough,
with the tracker having run, to call a visit a person.

The operator of the hosted installation reported visits called a person that came straight in, stayed
exactly fifteen seconds, said they were Chrome on macOS, and came from many places at once. Measured
read-only there on 2026-10-27, over the 10.0 verdicts of visits that began in the previous thirty days
and that the tracker reported on:

| What                                                                                     | Found                                                                                                                                         |
| ---------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| People who came straight in on a computer, by how long their pages were in front of them | every reading under two minutes that did not end with a page being left stopped on one of the tracker's marks: 15 s (26), 45 s (7), 105 s (6) |
| Visits whose last report said a page was being left                                      | people another site sent: 1,577 of 1,623 on computers, 224 of 230 on phones                                                                   |
| … the same, among people who came straight in                                            | 269 of 314 on computers, 50 of 118 on phones                                                                                                  |
| Chrome 125 on macOS, an 800 × 600 window, a clock eight hours ahead of UTC               | 14 people, over ten networks in four countries, each page left open at 15, 45 or 105 s, nothing pressed                                       |
| Chrome 145 on macOS, a 1366 × 1366 window, the same clock                                | 8 people, six of them from six countries within one minute, each page left open at 15 s, nothing pressed                                      |
| Windows and Linux shapes of the same thing                                               | 8 people, clocks set four hours behind or eight ahead of UTC in countries on neither                                                          |

A browser that opens a page, waits and is switched off never says the page was left: the last thing
the page reported was its running account at one of the tracker's marks. Fifteen seconds were then
counted as reading, and the visit was a person.

Looking for that shape turned up a second one. The tracker never sends its first report of how a page
is going before fifteen seconds have passed on the browser's own clock, and that has been true of
every version that sent one. Yet 1,461 of the 27,151 such reports stored since claimed less. Read
report by report, they came in two kinds:

- **Both of the report's clocks short.** The time the page said it had been in front of somebody and
  the page's own time of day, read against the time of day its page view gave, agreed: the wait had
  taken a second or two, and the next report followed a second or two after that. That is a page
  whose timers were run ahead of its own clocks. Over thirty days: 64 people and 7 visits the product
  could not tell about, all on one carrier's networks in China, phones reporting an old Chrome; and 62
  visits already called automation — 60 over Microsoft's network that came straight in, 2 over
  Amazon's. Not one visit another site had sent was among them.
- **Only the reading clock short.** The report arrived on time by the page's own clock, but the time
  in front of somebody ran at a fixed fraction of it — between about a half and three quarters, the
  same fraction on every page of the visit. Eleven people another site had sent, on long visits with
  presses: real readers in a browser that reports elapsed time slowly.

## Decision

**Time counts as reading only up to the last moment a page said it was being left.** For each page
of a visit, the reading is what the page reported the last time it said so — a report of a page being
left carries the whole of its time so far, so the last one stands for every earlier one — and nothing
where it never said so. The visit's reading is the sum, and what the pages reported beyond it is
`SessionEvidence.LeftOpenMs`. Where nothing could say how much was left open — a visitor still on the
site, whose pages are open because they are still there — all of the time is reading, as before.

**Only where the leaving could have been heard.** A page in a visit is the stretch between two
arrivals at the same address, cut in the order the collector received them, and a visitor's key is
renewed at midnight. So two pages count whole rather than as left open. One whose address was arrived
at again later in the visit: a reload, a jump to another section of the same page or a second tab
sends the old page's leaving and the new arrival back to back, and the leaving can reach the
collector second and be filed with the new stretch. And one whose last report reached the collector
within 75 seconds of midnight — the tracker's longest gap between reports, with time for one to
arrive — because whatever it reported afterwards went under the next day's key, which never arrived at
it. A test holds the tracker's longest gap below that allowance.

**A page left open is said so, too lightly to count.** `engagement.left_open` points away from a
person at 20, below the weight at which anything counts toward a conclusion, carrying the seconds
across the visit's pages. A reader whose laptop is shut mid-page leaves a page open as surely as a
program switched off does, and a report that a page was being left can be lost on its way, so it
never makes a visit automation and never outweighs anything that counts. It is there because a reader
shown fifteen seconds for a visit is owed the reason they were not taken for reading: "Your pages were
open for 15 seconds with no sign they were ever closed, so that time isn't counted as reading."

**Silence still weighs all of the time.** The observation that nothing was read, scrolled or touched
across several pages keeps counting time left open, because a page kept open in front of a browser is
something happening even where it is not somebody reading.

**A page whose wait was cut short is automation.** For the first page at each address in a visit, the
first report of how it was going is read on whichever of its two clocks says longer: the time it says
the page was in front of somebody, and its own time of day against the time of day the tracker's page
view gave. All three readings have to exist, and the report has to have been sent after the page
opened — a page whose opening only a server saw has no time of day the tracker's report can be read
against, because a server keeps a different clock. Only the first page at an address is read because
nothing earlier at that address can have spilled a report into it. The soonest across the visit's
pages is `SessionEvidence.SoonestProgressMs`. Under fourteen seconds, a whole second short of the
tracker's wait and ten times the coarsest clock a browser hands a page, it is
`automation.wait_cut_short` at 75: above anything a person does, because what such a page reports
happened while its timers ran ahead of its clocks. The tracker's wait and the engine's are held
together by a test, since a tracker that reported sooner would make every honest browser look like
this.

**The longer of the two clocks, never either alone.** The reading clock alone would call the eleven
real readers above automation. The page's own clock alone would read a report from one tab against a
second tab's page view of the same page. Neither shortens both at once.

**The time of day a client claims is read for the first time.** It was stored to see how far a
client's clock is from the collector's, and is still never taken for when anything happened. It is
now also read within one page, report against page view, as one of the two clocks above. The comment
on the column in `0001_events.sql` says it is kept for the clock difference alone and cannot be
edited, because the migration is checked against its checksum; `RawEvent` and the ingest command say
both.

**Eleven.** Categories move, so this is a major version, re-judging history as every major does.

## What this deliberately does not do

- **A page left open is not, on its own, called automation.** Measured, it almost always is a browser
  switched off from outside, but a reader whose laptop was shut on a page looks the same, and from one
  visit the honest answer is that nothing was settled. Its time is simply not reading: where
  something else already pointed toward automation and only the reading outweighed it, the visit is
  weighed without that time and can be automation.
- **No rule on what the browser said about itself.** An old Chrome, a square or default-sized
  window, a clock set to a time zone the address is not in: each is a string the visitor writes or a
  setting it chooses, and the last describes a traveller or anybody behind a private network as well
  as a fleet.
- **No rule across visits.** Six visits from six countries in one minute, identical in every respect,
  are plainly one operator, but the engine judges one visit at a time, purely.
- **How far down a page was seen is unchanged.** It still counts the first screen, as 0037 left it and
  the operator kept on 2026-10-25, so a page left open whose first screen held a quarter of it is
  still two things agreeing.
- **The visit list's reading time is unchanged.** It says how long a visit's pages were open in front
  of a browser; the verdict beside it now says how much of that had no sign of the pages being closed.
- **What is happening now is not named differently.** The live reading names a visitor only from
  observations that cannot be withdrawn (0036). A visitor still on the site has pages open because
  they are still there, and neither observation reaches it.

## Consequences

**Simulated** on 2026-10-27 over the 10.0 verdicts of visits that began in the previous thirty days
and that the tracker reported on, with both rules as they ship — pages taken by address within a
visit, which also gives a page arrived at again its whole time: people go from 2,301 to 2,200. 37
become something the product could not tell — 30 that came straight in on a computer and 7 that
another site sent — and 64 become automation; 7 visits the product could not tell about become
automation too. Of the operator's two Mac shapes, 21 of the 22 visits stop being people. Nothing that
was automation changes category. The table above was read a few hours earlier over the thirty days
before that moment, which is why its counts differ from these by a few visits.

**What the name check is asked about follows the verdicts.** A visit that may be somebody reading is
never asked about (0033, 0037). The first rule moved every one of the 37 above to something the
product could not tell, each still with the tracker having run pointing toward a person, so none of
them is asked about. It can make a person automation instead — where something else pointed toward
automation and only the reading outweighed it, which none of the thirty days held — and a visit
whose wait was cut short is automation; while its address is still kept, either is asked about as
any automation verdict is, the 64 people and 7 open visits above among them.

**Stated, not hidden:**

- A program that closes the page as it goes — or navigates away from it — says the page was left, and
  its wait is read as before. The fleets measured here did not; nothing stops one that does.
- A page left open whose first screen holds a quarter of it is still "A person · some signs": one of
  the operator's eight 1366 × 1366 visits stays a person that way.
- A program whose timers keep to its clocks is not caught by the second rule, whether it runs in real
  time or moves its clocks forward together with its timers, as a virtual source of time — a headless
  browser's, a testing tool's — does.
- A reader whose laptop is shut on a page, who did nothing on it but read, reads "Couldn't tell", and
  so does one whose report of leaving was lost, or went under a new key because their device moved to
  another network mid-page. Seven of the 1,868 people another site sent in the thirty days were moved
  that way, and some of those share the fleets' shape behind a made-up referring address.
- A page whose last report came within 75 seconds of midnight is read as let go of, so a program
  switched off in that minute and a quarter is read as before.
- On a rented network, a page another site sent that was left open is not read, so it is "Couldn't
  tell" unless something was pressed or typed (0056). And everything on a rented network describing
  itself identically is one visitor for the day (0017): where a page whose wait was cut short shares a
  visit with a reader, the whole visit is automation.
- Reports are matched to pages by address and by the order the collector received them. A report a
  page opened in an earlier visit under the same key sends can still, rarely, land in a later visit's
  first page at that address; only a browser whose reading clock runs slow could then look cut short.
- Only the first page at an address is read for its wait. A page that changes its own address as it
  loads, or is reached through a redirect to the same address that a reporter on the site's server
  saw, opens a second page at that address before its first report of how it is going, and a program
  on it is not caught by the second rule. Nor is one that opens the same page twice in a visit, whose
  first opening is also never taken for a page left open.

**Refused:** counting how far down a page was seen only where the page was left as well, which moves
three more visits that came straight in and twelve readers another site sent; reading the reading
clock alone, which makes automation of the eleven readers above; reading how soon after the visit's
first report the collector received the first report of how it was going, which delays through a
proxy disturb, catching the fleets' slow connections and people's alike; calling a page left open
automation; and rules on the browser's version, its window or its time zone.

**Rolling back** to 10.0 leaves the 11.0 verdicts on record beside the 10.0 ones. Reads take the
newest ruleset each visit has, so a visit judged under 11.0 keeps that verdict, and one judged only
after going back is judged under 10.0 until 11.0 returns and reaches it.

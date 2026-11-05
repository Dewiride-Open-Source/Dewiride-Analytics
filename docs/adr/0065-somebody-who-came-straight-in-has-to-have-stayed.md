# 0065 — Somebody who came straight in has to have stayed

- **Status**: accepted
- **Date**: 2026-11-05
- **Applies to**: both editions, and the whole of it is in the free product. Moves the ruleset to
  12.0, adds two observations and two catalogue sentences, rewords three, and reads one column already
  stored for the first time: how tall a window was. Changes what a visit nothing named a sender for can
  be called; a visit another site sent is judged exactly as it was. Collects nothing new, retains
  nothing new, and needs no migration. In `ee/` only the hosted website's explanation of its verdicts
  changes. Nothing in `integrations/` changes, because none exists yet and nothing about what is captured
  changes.
- **Revises**: [0016](0016-where-a-visit-came-from-counts.md), as 0056 left it, under which a key
  pressed on a visit over a rented network answered the network however the visit arrived.
- **Revises**: [0035](0035-a-visit-is-judged-on-all-of-itself.md), which refused a settling pass on
  what it measured under 8.0, four bands a month. A late report has been able to move a category since
  0037, as 0056 and 0059 record. On a visit that came straight in more now rests on one: a first ten
  seconds that 11.0 called a person stays "Couldn't tell" until the visit is read again.
- **Revises**: [0037](0037-a-person-is-not-concluded-from-one-thing.md), under which somebody who
  scrolled, or read for two seconds with the tracker running, was two things agreeing wherever they
  came from.
- **Revises**: [0052](0052-a-row-says-on-its-first-line-how-somebody-arrived.md), which left a visit
  opening on a page reached from the site's own link reading "Came straight here" as a matter of what
  a row says. A verdict on any network now rests on the same answer.
- **Revises**: [0054](0054-a-visit-still-in-progress-holds-back-only-itself.md), for the same reason
  as 0035.
- **Revises**: [0056](0056-somebody-can-be-reading-from-a-rented-computer.md), and settles what it
  left undecided: whether how far down a page was seen should count on other networks only beside a
  read. On a visit that came straight in, it now does.
- **Revises**: [0059](0059-a-page-left-open-is-not-a-page-read.md), under which how far down a page
  was seen was unchanged, so a page left open whose first screen held a quarter of it stayed a person;
  and which refused any rule on what the browser said about itself, its window named among them, on the
  ground that it is a setting the visitor chooses. On a visit that came straight in, a window now counts.

## Context

The operator of the hosted installation reported a great deal of direct traffic marked as people,
most of it on Linux, staying no time at all and yet having scrolled. Measured read-only there as of
2026-11-05 11:30 UTC, over the 11.0 verdicts of visits that began in the previous thirty days:

| What                                         | Found                                                                                                                          |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| People on Linux who came straight in, by day | never more than 6 a day through 11-01, then 17, 26, and 112 on 11-04                                                           |
| One browser shape behind it                  | 1200 × 3000 window, Chrome 148 on Linux, en-US, a clock eight hours ahead of UTC                                               |
| Its page views                               | 246 since 10-27, 153 of them on 11-04, in 245 visits, all but one on one website                                               |
| Where they came from                         | 138 networks in 20 countries, mostly household connections in South America; never a referring address                         |
| What they did                                | nearly always one page and a report of it being left, nothing pressed, ever; 483 ms on screen at the median, 9.9 s at the most |
| How far down their pages were seen           | at the median, 34%                                                                                                             |
| What they were called                        | 177 "A person" (153 on the tracker and a page seen, 24 with a short read, 22 of them with a page seen too), 68 "Couldn't tell" |

The window is the mechanism. The tracker's depth is how far down the bottom of the window has been,
so the first screen counts, and a window three thousand pixels tall shows a third of a page without
anybody scrolling. The tracker having run and a quarter of a page seen were two things agreeing, which
0037 calls a person. Both 0056 and 0059 recorded this as a gap, and it was left alone at 22 of 2,105
people; a fleet of browsers now fills it. The 68 are pages whose first screen held less than a quarter
of them, and visits whose report of leaving reached the collector before their page view, which drops it
(0034), so no time or depth survived them: 52 of the 244 that sent such a report arrived in that order, and one visit sent none.

People who came straight in, the same thirty days:

| Stayed, by what the pages said as they were left | Nothing pressed | … of them the fleet | Pressed |
| ------------------------------------------------ | --------------: | ------------------: | ------: |
| Under 2 seconds                                  |             164 |                 153 |      21 |
| 2 to 14 seconds                                  |              85 |                  24 |      24 |
| 15 seconds or more                               |              85 |                   0 |     174 |

Windows over 2,560 pixels tall across the installation's whole record: 1200 × 3000 (246, the fleet), 1024 ×
12140 (43, a crawler its company vouches for), 1748 × 3495 (2, another such crawler, saying it is a phone) and 1280 ×
3000 (1). None of those 292 page views was sent by another site, and on none was anything pressed. The tallest window of
anybody called a person outside them is 1440 × 2445, 1.70 times as tall as wide.

The operator's report was about direct traffic and nothing else, and it is direct traffic this changes.

## Decision

**What another site sent is judged as before.** Every observation it reports, what each weighs, what
is concluded and how firmly, is what 11.0 did. Tests pin the answers by value across how long a visit
stayed, how far down, what was pressed, whether it came over a rented network, time left open, several
pages with nothing happening, pages taken fast, widely or missing, and a window taller than a screen, so a
later change to this rule that reaches those shapes fails a test. The arrival is the one thing such a visit has that a visit that
came straight in does not.

**A visit that came straight in is a person only where its pages stayed.** With nothing named as
sending it, a visit has only its time to speak for it. Fifteen seconds in all, said as its pages were
left in the terms of 0059, is what 0037 already called substantial by itself, and half of what 0056 took
for reading, which also needs a quarter seen. From two seconds up to that, the time is reported at 20,
below the weight at which anything counts.

**How far down a page was seen counts on such a visit only beside that stay.** It is reported whatever
the visit did, at 45 from fifteen seconds and at 20 before them, because it counts the first screen.
A page seen a quarter of the way down, opened and left, is therefore something the product could not
tell, stated on slight signs because only the tracker counts.

**A press counts for nothing on such a visit.** A pointer pressed or a key pressed is reported pointing
nowhere and weighing nothing: a program can press as easily as anybody, and the operator's judgement
is that it is not what makes somebody a person. It stays on the record as something seen, and a reader
of the verdict still sees it. It does not hold an automation verdict back, as what it weighed used to.

**Over a rented network, such a visit is weighed as any other, whatever was pressed.** The rule 0056
added for the network being the only objection now needs another site to have sent the visit, where it
reads exactly as it did. A key pressed on a visit that came straight in no longer answers the network.

**A press can still stop a visit being called silent, and so can a page merely shown.** Across two or
more pages with under two seconds in all, the observation that nothing was read, scrolled or touched
cannot be said of a visit on which something was. So either keeps such a visit from being called silent,
and neither makes it a person; anything else pointing toward automation still counts. A press has done so
at most three times in this installation's history.

**A window taller than a screen, on such a visit, points toward automation.** A new observation,
`browser.oversized_window`, weighs 55 toward automation where a visit nothing named a sender for reported a
window over 2,560 pixels tall and more than 2.2 times as tall as it is wide, and its visitor does not say it
is a phone. Beside the tracker having run and anything too light to count, that settles it: "Something
automated", on some signs. Fifteen seconds said as the pages were left weigh 60 and outweigh it: "A person ·
some signs", with the window shown against the answer. Neither a press nor time left open stands against it.
Over a rented network it is a second heavy thing pointing the same way, so short of a stay it is stated on
strong signs; a stay does not outweigh the network, so with one it reads "Something automated · some signs",
with the stay shown against it. A crawler that named itself keeps its name, and one in the catalogue is stated
on strong signs beside it. An identity a company vouches for is not weighed, so it stays Verified. Reported with
the height and shown as "Its browser reported a window 3,000 pixels tall, taller and narrower than an ordinary
screen."

**Height alone would have caught screens held upright, so the shape is part of the rule.** Page zoom and a
display's scaling change what a browser reports without changing its shape. Computed from the common sizes, a
browser filling a 16:9 or 16:10 screen held upright stays at or below 1.78 at any zoom: 1440 × 2445 at 90% is
1600 × 2717, 1.70; an upright 4K screen at 125% is 1728 × 2957, 1.71; a 16:18 screen of 2560 × 2880 is 1.13. A
browser need not fill it, though, and anything docked beside the page narrows the window: 320 pixels of side panel
turn that 1728 × 2957 into 1408 × 2957, 2.10. So the line is 2.2, which spares a panel of about a fifth of the
screen's width, 383 pixels on that screen. Nothing measured lies near it: the tall windows here that did not say
they were phones are 2.50, 2.34 (one visit, nothing else known about it) and 11.86. The margin costs little. A
visit this misses under fifteen seconds still is not a person; it reads "Couldn't tell". A reader it wrongly
caught would be called automated and have their address asked about. The collector keeps any size a browser
reports, so the comparison is made in long integers, 5 × height against 11 × width, and the tallest report anything
could send cannot overflow it.

**A phone is left out, by the mark MDN gives for a mobile device.** Chromium on Android lays a page too wide for it
out at a smaller scale, and what it then reports is a figure from that layout rather than from its screen: 412 × 800
at a scale of 0.31 would report 1320 × 2563, and Safari on an iPhone has reported 1500 × 2148 here. Those stop short
of the line, at 1.94 and 1.43, but a long narrow phone does not: 360 × 840 is 2.33. "Mobi" anywhere in the user agent
leaves them all out. That text is the visitor's own, like everything else a browser says about itself.

**How tall a window was is read for the first time.** It has been stored since the first migration and was
read by nothing. It is now carried on the visit, as the tallest any of the visit's reports gave, in the same
place the width is and from the same page views, so a visit whose report of being left reached the collector
ahead of its page view, and so has no time or depth (0034), still has its window. Nothing reads it in a live
reading, whose statement does not change: the observation cannot name anybody there (0036), because it can be
outweighed by what happens next.

**A reader is told why.** Without it a verdict of "Couldn't tell" would stand above sentences that all
read like somebody reading, and 0059 holds that a reader is owed the reason. A new observation,
`engagement.brief_stay`, pointing nowhere and weighing nothing, is reported where a visit that came
straight in and is over showed anything beyond the tracker having run and did not stay: "It came straight
here and your pages were on screen for under 15 seconds, which is too short to count as somebody reading."
It stands among the reasons only where the answer is "Couldn't tell": beside an answer something else
settled, it would be giving the wrong reason. Where two seconds or more were left open and would have made
the fifteen seconds it is not reported, because the sentence about that time gives the reason. Less than two
seconds left open has no sentence of its own, so it is still reported, and there its "under 15 seconds" counts
only what was said as the pages were left.

**The sentences for reading and pressing stop saying somebody was there.** "Your pages were open in front
of somebody for 5 seconds" now stands above that reason, and a program can press as easily as anybody. They
read "Your pages were on screen for 5 seconds." and "The page was clicked or touched." Time left open was on
screen too, so its sentence says so rather than leave it to be read as off screen: "Your pages stayed on
screen for 15 seconds with no sign they were ever closed, so that time isn't counted as reading." This is
wording, for every visit, and moves no verdict. The trail above those sentences counts whole seconds too, so a
page left at 14.6 seconds is written 14s rather than the 15 it fell short of. It counts each page where the
sentences count the visit, so on several pages its figures can add up to a little less than the sentence beneath
them, and it includes time the sentences leave out where under two seconds were left open, so such a stay can
read 15s above "under 15 seconds".

**Twelve.** Categories move, so this is a major version, re-judging history as every major does.

## What this deliberately does not do

- **No rule here changes how a visit another site sent is judged.** A page another site sent, opened and left inside
  two seconds with a quarter of it seen, is still "A person · some signs", which 41 of the thirty days'
  visits are; a press on such a visit still counts, and 43 would be something else if it did not. A
  referring address can be made up, and that is stated below rather than worked around.
- **No rule on Linux, or on any operating system.** Measured, a visit saying it runs Linux is far likelier
  to be automated than one saying Windows, but outside this fleet 163 of the 300 such visits were
  people, 135 of them sent by another site and 125 staying fifteen seconds or more, in the same
  proportions as people on Windows and macOS. It is also a string the visitor writes.
- **What time counts is unchanged.** It is still 0059's: what a page reported the last time it said it was
  being left.
- **The tracker is unchanged, and what it collects.** Presses stay collected. They are what "Readers who
  did something" counts, what a visit another site sent is judged on, and what stops a visit being
  called silent.
- **What is happening now is not named differently.** The live reading names a visitor only from
  observations that cannot be withdrawn (0036), and none of these is one. The band beside a live name
  can differ for a visit another site sent: one whose arrival fell before the half hour reads there as
  having come straight in, until the finished visit is judged with its sender. The category does not
  move and nothing is kept from it. Nor is it said of a visitor still on the site that they stayed under
  fifteen seconds: `engagement.brief_stay` needs a visit that is over, which the live reading never holds.
  A visitor named there in a window taller than a screen, whether a crawler that named itself or a browser
  saying it is driven, can read a band lower than once its visit is over, because the live reading does not
  read the height. Its category does not move.
- **A window does not decide a visit another site sent, however tall.** It is not reported on one at all.
- **Nothing new is recorded about anybody.** The size of the window is already recorded and already listed
  among what is recorded about a visit. What is new is that the engine reads it, and that a visit it makes
  automation has its address asked about while the address is kept, as set out below.

## Consequences

**Simulated.** Replaying 11.0's rules over the evidence stored with the newest verdict of every visit in
the installation reproduced every category and every band, with no difference anywhere, so the figures
below rest on the rules and not on the replay. Read as of 2026-11-05 11:30 UTC with this rule as it ships:

- Over the thirty days, people go from 3,031 to 2,733. 183 become automation: the fleet's 177, and 6 that came
  straight in over a rented network and pressed something. 115 become something the product could not tell
  (`Unknown · Weak`). 69 that could not be told about become automation, all of them for the window: the fleet's
  other 68, and the 1 at 1280 × 3000. 21 stay people on some signs rather than strong ones, and 2 automated
  browsers that pressed something are stated on strong signs rather than some, because the press no longer holds
  them back. **No rule here moves a visit another site sent**, and the replay shows none moving, in category or in
  band. At the walk two things outside these rules can still move one: a report that reached the collector after
  its 11.0 verdict is read at last, as has happened before (0059), and an address asked about because a visit that
  came straight in now reads as automation settles every visit from it in that pass.
- Over the whole record, people go from 5,482 to 5,020: August 477 to 446, September 1,632 to 1,522, October 2,490
  to 2,362, and November so far 883 to 690, with 314 to 197 on 11-04. Older than the thirty days, 7 more become
  automation: 6 over a rented network, and 1 that took pages faster than anyone reads, which only a press had
  outweighed.
- Of the people who came straight in, the fleet aside, the thirty days lose about 3 in 10: 97 of 306 on computers
  and 24 of 70 on phones. 49 of those 121 pressed something, 15 had time left open, and 3 opened on a page reached
  from the site's own link.

**What the name check is asked about follows the verdicts.** A visit that may be somebody reading is
never asked about (0033, 0037). Every visit this moves to something the product could not tell still has the
tracker pointing toward a person, so it is not asked about. The 183 and the 69 that become automation are,
while their addresses are kept, and that is a real consequence: most of the fleet arrives over household
connections in South America, so those addresses now go to a name server, as the address of anything that
reads as automation does. At the walk that is the last three days of them, which is as long as an address is
kept, and after it each new visit of that shape. A pass asks about no more than 500 addresses
(`MostNameChecksPerPass`). An answer settled that way settles every visit from the address in that pass, one
another site sent included, as it always has.

**Stated, not hidden:**

- Somebody who came straight in, opened a bookmark or a link from a mail or chat application, and read
  for less than fifteen seconds reads "Couldn't tell". A website whose readers mostly arrive that way
  loses far more of its people than this installation does, where about four in five people come from a search.
- Somebody who came straight in and whose page never said it was left reads "Couldn't tell" however
  long they stayed and whatever they did, or "Something automated" over a rented network or in a window this rule reports: a laptop
  shut mid-page, a phone that moved to another network and filed its leaving under a new key. Of those who
  leave people, 15 in the thirty days had time left open. 0059's promise that such a
  reader who pressed something stays a person now holds only where another site sent them. Where two
  seconds or more were left open and would have made the fifteen seconds, their verdict says that time
  was not counted, and nothing says why how far down a page was seen, a short read beside it or a press
  did not count.
- Kept to people, "Came straight here" looks smaller than it is beside the kinds of site that send visits,
  so a newsletter and a search cannot be compared on that view.
- A first page reached from the site's own link, after a half-hour gap or a change of key, reads as having
  come straight in (0052). 3 of the 121.
- More now rests on a report that arrives after a verdict. Somebody who came straight in, reads for ten
  seconds, locks their phone for half an hour and reads on may stay "Couldn't tell" until the next major
  re-judges them. Measured over a slightly wider set than this moves, of 244 visits judged since the last
  major, 3 gained reports afterwards and 2 of those reached fifteen seconds.
- Somebody who came straight in, clicks through five pages and has left inside a quarter of a minute reads as automation,
  having nothing beside the rate that outweighs it (`retrieval.rate`). So does a visit whose only other observation is
  that it took 25 pages (`retrieval.breadth`), or asked for pages that were never there and did not stay
  (`probing.missing_paths`).
- **This is one setting from defeat.** A program that stays fifteen seconds and closes the page reads
  as a person, as one always has: with a quarter of a page seen, on strong signs. And one that writes a
  made-up referring address is judged as a visit another site sent, which is the cheapest route of
  all, because this leaves those alone: the fleet's visit with a made-up link in front of it is "A
  person · some signs" today and after this.
- **So is the window.** It is a number the browser reports, and the program that opens a window of 1200 × 3000
  can open one of 1200 × 2560 or 1400 × 3000, neither of which is caught, or write "Mobi" into what it says it is.
  Nor is the same program waiting fifteen seconds, which reads "A person · some signs" with the window shown
  against it, or writing a made-up referring address, which is judged as a visit another site sent. The line stops
  this fleet today and promises nothing about the next. On the hosted installation each of those is looked for by
  hand after this release, rather than the fleet's disappearance being read as success.
- Some people look the same as the fleet to this rule. A screen held upright at 21:9 (1440 × 3325 is 2.31), a window
  narrower than an upright 16:9 screen by more than about a fifth, such as beside a wider side panel or sharing
  the screen with another window, and a page in a tall frame on the same website all report
  this shape; so does a long narrow phone asked for the desktop version of a site, which stops saying it is a
  phone. Outside the fleet, nobody called a person in this installation's whole record reported a window over 2,560
  pixels tall, and any of them whose pages say fifteen seconds as they are left, off a rented network, is still a
  person.
- Time runs from when the tracking code starts, and some browsers count it slowly (0059), so a stay of
  fifteen seconds on the clock can be up to twice as long in the room.
- A band on an open answer counts only what weighs enough to count, so a "Couldn't tell · slight signs"
  can list more sentences than a verdict stated more firmly.
- People and every figure kept to them fall by the amounts above, a best day can fall (0064), kept months
  count themselves again once the walk has passed them, and months that have frozen keep their verdicts
  (none has here). No customer is told any of this on a screen, as with every earlier major. During the
  walk, 15 to 20 minutes here, screens fall gradually and visits finishing then wait for it.
- Nothing about billing moves. What is counted against a plan is read from reports and never from
  verdicts (0025, 0047), so these visits stay on the allowance whatever they are called.

**Refused:** a rule on Linux or any operating system, on the figures above; applying any of this to visits
another site sent, which the operator declined; requiring a press of anybody, which leaves people who
read without clicking out, and which the operator rejected from the other side; having the tracker report whether a
page actually moved, which is new collection and a later decision; reading the gap between a page view
and its leaving at the collector, for the reason 0059 refused timing a first progress report there: a
slow connection disturbs it; a rule across visits, because the engine judges one at a time; and settling
a visit a second time after its late reports, on the figures above.

**Rolling back** to 11.0 leaves the 12.0 verdicts on record beside the 11.0 ones. Reads take the newest
ruleset each visit has, so a visit judged under 12.0 keeps that verdict, and one judged only after going
back is judged under 11.0 until 12.0 returns and reaches it. Kept months stay as 12.0 counted them,
because an older engine never counts a newer one's month again (`MonthsToCount`). An older dashboard has no
sentence for the two new observations, so where it is shown one it says "Something else about this visit
pointed that way."

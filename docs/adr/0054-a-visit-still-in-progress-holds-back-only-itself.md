# 0054 — A visit still in progress holds back only itself

- **Status**: accepted
- **Date**: 2026-10-24
- **Applies to**: both editions. Adds one column to the control plane's `classification_progress`
  (migration `AddClassificationReadThrough`), changes which visits the engine's reconstruction hands
  back, changes where a series counted from verdicts says it stops being complete, and adds one
  sentence to the dashboard for a period not one bucket of which has settled, and one heading for an
  overview kept to people that has counted none yet. Collects nothing new, changes no rule and no
  verdict, and does not move the ruleset. Nothing in `integrations/` changes.
- **Revises**: [0019](0019-a-window-that-opens-inside-a-visit.md), which left the bookmark alone on
  the grounds that stopping at the earliest unfinished visit is "the reason the engine never stalls
  behind one long reader". It stalls behind any reader whose visit outlasts one stretch.
- **Revises**: [0041](0041-one-population-for-the-whole-screen.md), whose series of people is
  complete to "an idle timeout behind the present". That is where judging can have reached at the
  most, not where it has.

## Context

The engine judges a visit once it has been quiet for an idle timeout, and works forward through
each site keeping one bookmark: every visit that began before it has been judged. The bookmark
stopped at the earliest visit still in progress, so that the visit would be found again, and every
pass read from the bookmark to one stretch (`LongestPass`, six hours) past it. A visit still in
progress six hours after it began therefore pinned every pass to the same six hours. Nothing that
began after them was read until that visit ended, an idle timeout after its last report.

A visit stays in progress for as long as its reports are less than an idle timeout apart, and the
tracker reports every minute while a page is in front of somebody, and again each time they come
back to it. A page left on a screen that never locks — a second monitor, a display on a wall — is one
visit until the visitor key changes at midnight UTC. So is a documentation page somebody keeps open
at work and returns to every quarter of an hour. At worst, a page on screen all day, everything on
that site that began after 06:00 UTC waited until about 00:30 UTC the next morning.

Nothing said so while it waited. The User journey list stopped growing, and every series counted
from verdicts — who came, and everything with only people switched on — dipped, because a series
declared itself complete to an idle timeout before the present whatever the engine had reached. A
reader was shown fewer people rather than a delay.

Measured read-only on the installation this product runs on, over everything it holds (nine sites,
6,849 judged visits since 2026-08-19):

|                                                           |                                       |
| --------------------------------------------------------- | ------------------------------------- |
| Visits judged to have lasted six hours or more            | 12                                    |
| …never quiet for an idle timeout (longest silence ~1 min) | **2**                                 |
| 2026-09-25, 8.6 hours, 524 reports                        | 3 visits judged up to 2.6 hours late  |
| 2026-10-20, 6.6 hours, 402 reports                        | 5 visits judged up to 40 minutes late |
| The other ten                                             | a tab hidden for hours, then closed   |

The other ten held 2 to 76 reports with silences of 3 to 11½ hours: each was over long before its
last report, and held nothing back. Both of the two were people, both fell at night for the sites'
readers, and both were on sites seeing a few hundred visits a week. The rate is not a property of
the product: on a busier site, and above all on documentation read at work, a page open past six
hours is a daily event, and it falls in the working day.

## Decision

**The bookmark keeps two instants.** `classified_through` keeps its meaning — every visit that
began before it has been judged — and waits where the earliest visit still in progress began.
`read_through` is new: every visit that began before it has been read, and judged if it had finished
then. A run goes on until it has read up to its horizon. Each pass reads from where judging waits to
one stretch past where the last reading stopped, so judging waits on the open visit and reading
waits on nobody. The store moves a bookmark in one statement that takes, for each instant, the later
of what it holds and what it is offered, so forward only holds across instances of the engine and
not merely within one.

**A reading leaves out what the same rules have already judged.** Reading from the open visit would
otherwise judge everything after it again on every run until it closed — the "little work" 0019
accepted, which for a site held since the morning is every visit of the day judged twice a minute. A
visit is left out where a verdict under the same ruleset names it and ends where it ends. One that a
later report has made longer — a page announcing it is being left, hours after it was opened — no
longer matches, so it is read again and judged on all of itself once it is over. That is what the
re-reading behind a held bookmark used to do by accident; now it happens where something changed and
nowhere else. It is not the settling pass [0035](0035-a-visit-is-judged-on-all-of-itself.md)
declined: nothing is read that the run was not reading anyway.

**A series counted from verdicts is complete to the earlier of an idle timeout before the present
and how far judging has got.** That is as far as the rules being run have read, or as far as any
earlier rules judged, whichever is further. Every ruleset walks a site from the moment it was added,
and the screens show the newest verdict each visit has, so a visit any of them reached has its answer
on the screen; a visit earlier rules found still in progress is judged by nothing until the rules
being run reach it, so earlier rules count only as far as they judged. A site judging has never
started on is complete to nothing. The instant is read before the series it qualifies, because the
engine stores a pass's verdicts before it moves its bookmark past them: read first, it can only be
behind what the series holds. The dashboard already marks every bucket past that instant as still
being judged; it is now told where that is, says the whole period is still being judged where not one
bucket has settled rather than "the newest", and an overview kept to people that has counted none says
"No people yet" while any of the period is still being judged, keeping "No people in this period" for
a period judged to the end — and says what went wrong, rather than either, when it could not find out.

**The ruleset does not move.** No category, band, weight or rule changed and no visit is renamed.
What changed is when a visit is read, not what is concluded from it.

## Consequences

**A page left open costs only its own visit.** Everything else on the site is judged within a run of
having been quiet for an idle timeout, whatever is open elsewhere.

**One reading per run, as before.** In steady state a run has half a minute of new ground, and reads
it in one statement together with whatever is still open. A site with a visit open since the morning
reads up to a day more stored activity per run than one without — on top of the day either side
that every reconstruction reads already (0035) — and judges nothing twice.

**The verdicts a reading checks against are read a month at a time.** The table is kept in order of
visit name, which says nothing about when a visit began, so the window's bounds narrow the reading
only to the month (or two) it falls in, and the rules a verdict was reached under come after the
name in that order: each pass reads the site's verdicts for those months under every ruleset the
table still holds. That is a few thousand rows on the installation measured, and would be about a
million for every ruleset held on a site with a million visits a month, every half minute. It is the
first thing to give way if judging is ever the bottleneck on a large site, and keeping the table in a
second order by when visits began is the remedy.

**A visit is judged once per ruleset, and again only if it grew.** A late report about a visit that
began before the reading starts is still counted only at the next re-judging of history, as 0035
records.

**The charts say "still being judged" wherever judging is behind**, not only over the last half
hour: after the engine has been stopped, while it works through a backlog, and in the moments after
a site is added. A series that dips now dips because the traffic did — except that a visit still in
progress is counted in the buckets it spans only once it ends, as it always was. On an installation
that has switched judging off the sentence is not true — nothing is being judged, nor going to be —
and the dashboard has no way to say otherwise yet; it is no further from the truth than the dip it
replaces.

**The User journey list is not told yet.** It lists judged visits, and an empty list kept to people
still says nothing matches what was picked, or that the first visits arrive about half an hour after
they end, wherever judging has got to. The visit list's answer carries no instant to tell it by;
giving it one is the change that would let it say what the overview now says.

**One column on the control plane.** `read_through` is added empty, filled with
`classified_through` — the furthest every bookmark already kept is known to have read, since how far
one held by an open visit read past it was never recorded, and the first run reads that stretch again
and leaves out what was judged — and only then made required, in the order Microsoft documents for a
column computed from existing data. The first run after it reads each site from its open visit to
the present in stretches and judges what was waiting.

**Rolling back keeps working.** An engine from before this change moves `classified_through` alone,
and starts a bookmark without naming the column it does not know, which then takes the column's
default: the start of 1970, before anything that can be judged. A reading behind the judging is read
as having got as far as the judging, since everything judged was read, so a bookmark kept or started
by that engine reads correctly once this one is back, and no default is ever taken for a reading
point anybody established.

**Instances of the engine still need no coordination.** Each instant of a bookmark only moves
forward, enforced where both instances meet, and the reading leaves out what either of them has
judged. Two instances reading the same stretch at once can still judge one visit twice, which stores
one verdict.

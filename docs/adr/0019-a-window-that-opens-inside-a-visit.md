# 0019 — A window that opens inside a visit

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column and collects nothing new; it widens the stretch of stored
  activity one statement reads, and removes rows that name visits which never happened.
- **Follows**: `0018-a-page-somebody-reported-reading-is-a-page-they-visited.md`, which fixed the
  other reason a live installation was full of visits with no pages in them.

## Context

The engine works forward through a site in windows and keeps a bookmark. The bookmark does not
stop at the end of the window just read — it stops at the **earliest visit still in progress**, so
that a reader who has been on the site all afternoon is picked up whole on the next run rather than
holding the entire site up until they leave. Everything after that point was judged all the same
and is simply judged again next time, which costs a little work and is meant to cost nothing else.

It cost something else. The statement that rebuilds visits read activity from the window's own
start, so a visit that had already been judged, and which happened to still have activity after the
bookmark, was rebuilt from whichever of its reports fell first inside the next window. That
remainder looked like a whole visit beginning at that instant, it had been silent long enough to
count as finished, and it was judged and stored under an identity of its own — a second visit, by
the same person, at the same moment, usually with one page in it or none.

Read against a live installation, two of its 215 stored visits were remainders of this kind. Both
were second copies of a real reader:

|               | Stored as           | Started      | Ended        | Pages |
| ------------- | ------------------- | ------------ | ------------ | ----: |
| The visit     | `…f0:1787212831001` | 08:00:31.001 | 08:16:38.226 |     0 |
| Its remainder | `…f0:1787213798226` | 08:16:38.226 | 08:16:38.226 |     0 |

The remainder is the visit's last report, on its own, judged as though it were somebody's whole
time on the site. Both were answered "not enough to go on", which is the honest answer to the
question it was asked and the wrong question to have asked.

The same defect was reproducible on a second installation at the same rate — two visits in
forty-two — so it is a property of the design rather than of one site's traffic.

## Decision

**Activity is read from a full idle timeout before the window as well as a full idle timeout after
it.** The statement already reached past the end, which is what makes "this visit is over" a fact
rather than an artefact of where the reading stopped. It now reaches back by the same amount, which
makes "this visit began here" a fact on the same terms. A visit already under way when the window
opens is then rebuilt from its own beginning, that beginning falls before the window, and the
filter that was always there drops it.

**One idle timeout back is exactly enough, and provably so.** A visit is a chain of reports each
less than an idle timeout apart. A visit with a report on both sides of the window's start must
therefore have one within an idle timeout _before_ it — and that report carries the whole chain
back, putting the rebuilt beginning outside the window. Reaching back further would find nothing
new; reaching back less could leave a remainder that still looks like a beginning.

**The bookmark is left alone.** Stopping at the earliest unfinished visit is the right rule and the
reason the engine never stalls behind one long reader. What was wrong was that re-reading a stretch
of time was not idempotent, and that is what has been fixed — re-reading now yields the same visits
whatever instant the window opens at.

**The remainders already stored are deleted, in migration
`0006_truncated_visit_fragments.sql`.** Their identities name visits that never happened, so nothing
will ever supersede them: every count they appear in is one too many, for as long as the data is
kept. A remainder is recognisable without knowing anything about the windows that produced it — it
is a visit by a visitor who already had a visit that started earlier and ended no sooner. Two
genuine visits by one visitor cannot overlap, because a silence longer than the idle timeout is
what makes them two, so an overlap of that shape is always this defect.

**The ruleset is not bumped.** No verdict changes. The detectors are shown exactly what they were
shown before for every visit that actually happened; what stops happening is a second visit being
invented beside it.

## Consequences

**A visit is counted once.** Two per hundred is not a rounding error on a screen whose whole
proposition is that its numbers can be explained, and it landed disproportionately in the one
category nobody can act on: a remainder is short by construction, so it is nearly always answered
"not enough to go on".

**Re-reading a stretch of time is idempotent, and now says so.** That was already the claim made
for interrupting a run and for two instances of the engine working the same site without
coordinating. It was true of the verdicts and not of the visits.

**Slightly more activity is read per window.** One idle timeout of it, rebuilt and then discarded.
Against a window that is six hours wide, that is a few per cent of the work for a guarantee that
was previously assumed.

**It reads only what was already stored.** No column, no new report from the tracker, and nothing
collected that was not collected before.

# 0035 — A visit is judged on all of itself

- **Status**: accepted
- **Date**: 2026-08-27
- **Applies to**: both editions. Moves the ruleset to 8.0, changes how far the reconstruction reads
  and what decides that a visit is over, bounds the account of a visit shown beside its verdict, and
  drops a column nothing ever wrote. Collects nothing new and narrows nothing about what is retained.
  Nothing in `integrations/` changes.
- **Follows**: [0034](0034-a-visit-begins-where-somebody-arrived.md), which made a report about a page
  belong to the visit that page was arrived at in, and named this as the consequence it left open.
- **Revises**: [0029](0029-what-a-visit-was-is-rebuilt.md), whose stated invariant — that a rebuilt
  visit is the same visit the engine judged — the journey panel did not keep.
- **Revises**: [0019](0019-a-window-that-opens-inside-a-visit.md), which proved one idle timeout was
  enough to reach back by, from a premise 0034 removed.

## Context

The engine judges a visit once it has been quiet for an idle timeout, records the verdict against a
key derived from where the visit began, and never returns to it. Its bookmark only moves forward.
Two things follow that were not intended, and 0034 made both of them larger.

**A verdict depended on where a boundary happened to fall.** A backlog — and every ruleset bump is a
backlog, because a bump re-judges each site from its beginning — is walked in stretches of six hours,
reading a further idle timeout past the end of each. Whether a visit was over was then decided by
comparing its last report against the end of the stretch. Under 0034 a visit can hold a gap of hours:
a page announcing that it is being left belongs to the visit that page was arrived at in, however
long the silence before it. A visit lying across the end of a stretch was therefore declared finished
and judged on the part that fitted, while the rest of it sat in the store, already written, ignored.

Measured by reconstructing seven days of a live installation twice — once as one window, once as the
six-hour walk the engine actually performs:

|                                          |       |
| ---------------------------------------- | ----- |
| Visits, one window                       | 443   |
| Visits, six-hour walk                    | 443   |
| Visits the walk invents, or never judges | **0** |
| Visits whose page count differs          | **0** |
| Visits whose reading time differs        | **8** |

Nothing is miscounted and nothing is fabricated; what the walk loses is evidence. One visit is judged
on 38 minutes of reading where the store holds 90.

**The account of a visit and the reasons given for it were two different answers.** The visit list
reads a stored verdict. Opening a row asks a second question, which rebuilds the visit from activity
— and that rebuild carried no bound at all, so it answered from everything present at the moment of
the request, including reports that reached the collector after the verdict was reached. Over thirty
days of the same installation, 40 of 583 visits gained a report after the moment they were judged,
and **37 of them would show a trail disagreeing with the sentences beside it**: a rail of pages
adding up to 19 minutes under a sentence saying the visit was read for 1 minute 15 seconds; another
at 39 minutes against 17 minutes 40; one where the verdict records no reading at all and the rail
shows seven minutes, ninety per cent scrolled, with pointer use. Neither number is wrong. They answer
different questions and are printed six lines apart.

**What this is not.** Replaying the real engine over both evidence sets, only 4 of those 583 visits
would reach a different verdict, every one of them `A person / some evidence` becoming `A person /
good evidence`, and none changing category. The engine is not mislabelling traffic here. It is
under-stating its own confidence in about one visit in a hundred and a seventh of them, always in the
modest direction, and contradicting itself on screen far more often than that.

## Decision

**Whether a visit is over is asked of the moment nothing more can arrive.** The caller passes that
instant — the present moment less an idle timeout — and it is separate from the window deciding which
visits the reading is answerable for. A visit still belongs to the window its first report falls in,
so a caller working forward covers a site once and only once; but a visit that finished last Tuesday
is over in every window it might land in, and is judged on all of itself.

**Activity is read a full day either side of the window.** The bound is derived rather than chosen: a
visitor key mixes in the day the report was observed, so every report under one key arrived on the
same day and a visit cannot run longer than one. A day either side reads whole every visit that could
touch the window. The reach used to be an idle timeout, on the reasoning that a visit is a chain of
reports each less than one apart — true until 0034, and left in place unrevised when 0034 landed.

The same correction applies to the fragment the dashboard rebuilds visits with, for the same reason.
It costs a fifth more time on a six-hour stretch of the busiest site measured — 0.083 s to 0.098 s —
and in steady state it costs nothing, because the widened range is in the future and empty.

**The account of a visit stops where its verdict's evidence stopped.** The rebuild reads up to the
last instant the stored verdict was reached from, taken from the verdict itself rather than from
anything a caller wrote. A visit nothing has judged is shown whole, because there is no verdict for it
to disagree with. What a reader is shown beside a verdict is now the visit that verdict was about.

**The ruleset moves to 8.0, and re-judging is where this is repaired.** No category, band, weight or
threshold moved; what changed is how much of a visit the engine is shown. By the time history is
walked again a departure sent hours after the page it names has long since arrived, so a visit judged
in the moment on half its reading is judged on all of it now. Verdicts are kept per ruleset, so every
earlier answer stays on record. No migration is needed to remove anything: a visit keeps its name, so
the new verdict supersedes the old one under the same key.

**The flag for a verdict reached before a visit finished is removed.** `0003` added it against a way
of judging that was going to exist — a verdict reached as activity arrived, shown as not yet final,
replaced when the visit closed. It was never built, and the engine that was built forbids it: a visit
is read only once it is over. No value other than false has ever been written, on any installation.
It was carried from the column to the browser and displayed nowhere. `0009_a_verdict_is_reached_once`
drops the column.

## Consequences

**A verdict no longer depends on when the engine last ran.** That was the property `RulesetVersion`
exists to provide and did not: the same visit judged during a catch-up and during steady state could
be reached from different evidence. It now cannot.

**Customers will see nothing move except a handful of confidence bands.** Four visits in 583 gain a
band on the traffic measured here, all of them readers the product was already calling readers. The
visible change is that opening a visit no longer shows a trail its own explanation does not account
for.

**A verdict on the last stretch before the present can still be reached from less than the store will
eventually hold**, and this is the residual rather than a fix. A departure that has not arrived yet
is a report nothing can account for, and waiting for one that may come eleven hours later would mean
a dashboard that is a day behind. Re-judging repairs it for everything older, which is to say for
everything except the newest verdicts, and only at the next bump.

**A settling pass was considered and is not being built.** Detecting which visits grew is genuinely
cheap — one aggregate over a trailing window of activity, 0.023 s across thirty days of the whole
installation — so this is not a decision about cost. It is that the thing it would buy has largely
been bought twice over: bounding the account removes what a reader can see, and re-judging repairs
the evidence. What would remain is four confidence bands a month, on the modest side, until the next
bump. It is also structurally partial: 0034 measured a fifth of late departures arriving under a key
that has since rotated, which no amount of re-reading can attach to anything. The fix that reaches
those is the one 0034 already names — a report carrying the identity of the page view it is an
account of — and a settling pass is the expensive way to buy part of what that buys whole.

**One measurement in this file must not be read as a general property of the product.** The rates
here come from one installation of a few hundred visits a week, in steady state, over one week and
one month. They size a decision; they do not describe how often this happens to anybody else.

**A defect found alongside this one is recorded rather than fixed.** The classifier asks a name server
which operator an address belongs to, learns the answer, and applies it only to the visits in the pass
that asked. A visit judged in an earlier pass keeps a verdict reached without an identity the process
is still holding, and a visit that looked like a reader is never asked about at all. It is a wrong
label in the direction [0005](0005-privacy-envelope.md) and rule 12 guard hardest — machinery reported
as a person — and it is cheap to fix. It is left here because nothing has measured how often it
happens, and because judging a fix by how small it looks is how the thing being corrected in this file
got in.

**A verdict whose visit has since been renamed is stranded, and this is the third time it has come
up.** Reads take the highest ruleset present _for each name_, so a verdict under a name nothing
produces any more is never superseded and is counted for ever. `0006` cleaned up one instance and
`0008` another; this one appeared while verifying the change. A development installation holds two
verdicts under a server-side visitor key, and the same two visits — same site, same instant, same
category — again under the browser key the reconciliation has folded that key onto since. The
reconciliation arrived after those verdicts were written, so both are shown and both are counted.

It is not being cleaned up here. The installation this product runs on has none: every name carrying
an old verdict there also carries a current one. And the rule that would find them — the same site
and the same starting instant under a different name — deletes a real verdict the first time two
visitors begin in the same millisecond, which on a busy site is a matter of when rather than whether.
The general answer is that renaming visits should carry its own repair, written with the change that
renames them and while it is still obvious which name replaced which. Recorded here so that the next
one is not discovered the same way.

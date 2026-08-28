# 0029 — What a visit was is rebuilt, not stored

- **Status**: accepted
- **Date**: 2026-08-25
- **Applies to**: both editions. Adds no column, no migration and no index; collects nothing new and
  widens no retention envelope.
- **Followed by**: [0034](0034-a-visit-begins-where-somebody-arrived.md), which changes what counts
  as one visit in the grouping this rebuild derives a visit's name from.
- **Revised by**: [0035](0035-a-visit-is-judged-on-all-of-itself.md), which bounds the rebuild at the
  verdict's own evidence so that it is the same visit the engine judged, as this one intended.

## Context

The list of judged visits can be narrowed by three things the verdict itself holds — what the engine
concluded, how much weight stood behind it, and how many pages the visit went to. Everything a
reader actually recognises a visit by is missing: the kind of device, the browser and the system
under it, the country and the town, the network, the site that sent them and what sort of place that
is, and the page they landed on.

Every one of those is already derived from `events`. The card that ranks a period's networks derives
one; the panel a reader opens on a single visit derives eight more. What does not exist is any way
to ask a _list_ about them.

The obvious move is to write them onto `session_classifications` when a visit is judged, and rewind
the classification bookmark to fill in what is already there. Nine `LowCardinality` columns, one
extra condition each, no reconstruction at read time. It was the first design and it is wrong twice
over, for reasons this codebase had already written down elsewhere:

- **It would freeze both catalogues at judging time.** The catalogue that turns a referrer into
  "Google" and the catalogue that turns a routing number into "Alibaba Cloud" are deliberately
  applied when the question is asked rather than resolved into a column when the traffic arrives —
  so correcting an entry re-answers every period a site has already recorded, instead of leaving the
  same visit filed one way before the correction and another way after it. A stored `source_site` or
  `network` would put the cards and the list into permanent disagreement about visits judged before
  the last correction, and nothing would ever reconcile them.
- **It would create a second answer to a question that already has one.** The panel a reader opens
  from a row of this very list derives these values from `events`. Storing them means the row and
  the panel can disagree about the same visit, and only one of them can be right.

A verdict is stored precisely because it cannot be worked out again — the rules that produced it
move on. These nine can be worked out again exactly, from data that is already there.

## Decision

**What a visit was is rebuilt from activity when somebody asks, and never stored.** No migration.
The rebuild lives in one place, `JudgedVisitDetails`, and every expression in it is deliberately one
already used elsewhere, so a value a reader picks off a card is the value that narrows the list.

**The two are joined on the identity the engine derives, which is reproduced rather than looked
up.** A visit is named by its visitor's key and the instant it began. Reproducing that needs four
things together: activity read a full idle timeout either side of the period, the reconciliation of
the browser's half of the measurement with the server's — which rewrites the visitor key, and so
writes half the name — the same grouping into visits every other statement uses, and the earliest
report of the whole visit rather than the earliest that announced a page. The reach back is what
makes the rebuilt visit the same visit the engine judged: it keeps a visit that was already under
way from being handed an invented beginning, and therefore an invented name.

**The list has two shapes, chosen by what was asked for.** A narrowing the stored verdicts answer on
their own compiles to exactly the statement it always did. One that asks what the visit itself was
compiles to that statement with the rebuild in front of it and one further condition on the same
outer selection — so the narrowing still applies after each visit has been reduced to one verdict,
and the count of the period still describes the list the reader is looking at. Within each shape the
text does not vary: an empty set is the question "all of them", so there is one plan for the store to
reuse and one statement to read.

**The values a reader may choose from are the ones their own traffic holds.** Six of the nine are
open sets with no list anybody could write down in advance — nobody can guess whether a search
engine is recorded as `Google`, `google` or `google.com` — so a separate question reports what each
detail held, counted per visit, commonest first, capped per detail. It counts the whole period
rather than what is left after the rest of the narrowing: conditioning it would give the answer as
many shapes as the question and double the cost of every change a reader makes.

**The empty text is a value, not a gap.** It is what the rebuild produces where no report ever said
anything, so asking for it asks to see the visits nothing was established about — a fair question,
and a different one from asking for all of them. The two closed vocabularies map it back to the word
they already use for the same state: a device nothing said reads as unknown, and a visit nothing
sent reads as direct, which is not "nobody sent them" but "nothing said who did".

## Consequences

**The ordinary list pays nothing.** The rebuild is a second reading of every event in the period,
and it is built only when somebody actually narrowed by something it answers.

**A narrowed list over a long period is materially slower than an unnarrowed one**, and deliberately
so. The window a question may cover is already capped, and the tuning shipped for small machines
already decides what happens beyond that: somebody asking a year-wide question on a small machine
waits rather than receiving an error. No further cap was added.

**A drifted identity fails silently.** The two reconstructions meet in a text rather than in a
foreign key, so a difference of one millisecond matches nothing and returns an empty list with no
error anywhere. Four things guard it: an assertion that both compilers contain the same identity
expression, an assertion that the rebuild reads past both ends of the period, an assertion that
every detail the answer offers is one the list can be narrowed by, and — the one that would actually
catch a drift — an end-to-end test that judges real activity and then narrows to every value the
answer offered, expecting the visit back each time.

**Anything else a reader might narrow by has an obvious home and an obvious set of obligations**: an
expression taken from wherever that value is already shown, a name in the rebuild, a bound array, a
line in the offered set, and a place in the round trip.

**One value is still spelled two ways, and it predates this.** A network is named from the hosting
catalogue on the card that ranks networks, and now in the filter; the panel that opens a single visit
still shows the registry's own description of it. The two should agree, and the panel is the one to
change.

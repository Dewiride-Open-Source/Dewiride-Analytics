# 0062 — A day's visitor key is ready before the day begins

- **Status**: accepted
- **Date**: 2026-10-30
- **Applies to**: both editions, and the whole of it is in the free product. Changes when a day's
  salt is made, not what it is, how long it lives or what is derived from it; collects nothing new,
  retains nothing new and needs no migration. Nothing in `integrations/` or in the tracker changes.
- **Follows**: [0017](0017-a-rented-address-is-a-lease-not-a-visitor.md) and
  [0034](0034-a-visit-begins-where-somebody-arrived.md), which describe the visitor key as
  `HMAC(the day's salt, site ‖ day ‖ connection ‖ user agent)` under a salt that rotates daily.

## Context

A visitor key is derived from the salt of the UTC day a report arrives on, and where that day has no
salt the factory returns no key at all, by design: hashing without one would be a stable,
reversible identifier. A report with no key is nobody. `VisitGrouping` and every reading about the
present moment leave it out, so it is in no visit, under no verdict and on no screen. And the
collector cannot remember such a visitor as one to turn away
([0060](0060-a-visit-from-an-ignored-website-is-never-kept.md)).

`VisitorKeySaltRotationService` made the day's salt at start-up and then once an hour, timed from
whenever the engine last started. Nothing tied a run to midnight, so each day began without its
salt until the next run, up to an hour later. Measured read-only on the hosted install on
2026-10-30, per UTC day from 16 to 30 October:

| What, per day                          | Measured                                             |
| -------------------------------------- | ---------------------------------------------------- |
| Days with reports stored without a key | 13 of 15                                             |
| Reports stored without a key           | 0 to 74, up to 18 of them page views                 |
| When                                   | from 00:00 until the first keyed report, 00:38–01:07 |
| From                                   | the browser tracker, every one                       |

On the other two days nothing arrived before the day's first keyed report, at 00:43 on the 18th
and 00:06 on the 25th. The eight page views from aocr.org that no visit held, found while removing
that website's visits under 0060, were all inside this window.

## Decision

**Every rotation also makes the next day's salt.** `VisitorKeySaltStore.RotateAsync` ensures salts
exist for today and the `DaysAhead` days after it — one — and caches every salt from yesterday on.
A day's salt is therefore made during the day before, at the first rotation of that day, and is
held many runs before its first report arrives. A rotation that fails is followed by about twenty
more before the salt is needed.

**Nothing else about the salt changes.** It is still one random salt per UTC day, never derived
from a secret and never reused. It is still deleted once the day after its own is over — the first
rotation of the day after that deletes everything older than yesterday — which is what makes a key
unrecoverable by anybody, the operator included. No key is derived from a salt before its day
begins, because a key is derived from the day a report arrives on.

## Consequences

- **The first hour after midnight is counted.** A visitor arriving then has a key, a visit and a
  verdict like anybody else, and appears on the screen of who is here.
- **Three salts are held at once instead of two**: yesterday's, today's and tomorrow's. Tomorrow's
  identifies nobody until tomorrow, and the deletion that makes history unrecoverable is unchanged.
- **Reports already stored without a key stay without one.** Their days' salts were deleted as
  designed, so no key can be recomputed for them, and it would not be right to try.
- **Two engines racing to make the same day's salt still make one**: the day is the table's key, so
  one write fails, is logged, and the next run reads the salt the other made. With the salt made a
  day ahead, that next run is still hours before it is needed.
- **Rolling back** to an earlier release reopens the gap within a day or two: it never makes a salt
  ahead, though it uses one this release has already made. Nothing breaks either way.

# 0020 — The screens live under one segment

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column, collects nothing new, and widens no envelope.

## Context

Every screen sat at the root of whatever address the dashboard was published on: `/` was the
overview, `/journeys` the visit list, and `/sign-in` and `/set-up` the two doors into them. An
address naming no screen was redirected to `/` by `proxy.ts`, which is correct for an application
nobody arrives at from a search result.

That arrangement leaves nothing at the root for anything else to occupy. A deployment that wants
to put a page in front of the product — anything describing what it is, on the same name people
sign in on — has no room to do it, because the first address it would need is the one the overview
already answers. The alternative is to publish the two on separate names, which is worse than it
sounds here: the address the dashboard is read on is the address written into the beacon that
somebody pastes into their website, taken from `window.location.origin` and never from
configuration, and once pasted it can never change. Choosing that name badly is a decision with
no second attempt.

Moving the screens after people are using the product means changing every address at the moment
the cost of getting it wrong is highest. Moving them while nothing outside the repository depends
on them costs a rename.

## Decision

**The four screens move under `/app`.** `DASHBOARD`, `JOURNEYS`, `SIGN_IN` and `SET_UP` in
`lib/routes.ts` become `/app`, `/app/journeys`, `/app/sign-in` and `/app/set-up`, and the files
move to a matching `app/` segment under `src/app/[locale]/`. `SCREENS` is still the single list,
and the test that fails when a screen has a file but no entry now walks the new segment.

**The root is not a screen, and is redirected rather than rendered.** `screenPath` answers a bare
language prefix with `/` rather than with the dashboard, so `/`, `/en` and anything else naming no
screen all fall to the same branch in `proxy.ts` and are redirected to `/app` before anything is
rendered. That keeps one rule where there was one rule: a screen that does not exist is answered
by putting the person on the dashboard, decided before a response has begun and not patched over
the top of one already streaming.

**No `basePath`.** Next.js would then never see `/api`, `/collect` or the beacon, and those would
have to be routed by whatever sits in front of the process instead — splitting a table that is
deliberately kept in one file, and making the same-origin sign-in cookie depend on a reverse-proxy
configuration rather than on code that is tested. `proxy.ts` remains the only router.

**`/dw.js` and `/collect` do not move.** They are the engine's addresses and the beacon's, not
screens. Every snippet already pasted into a measured page keeps reporting to exactly the address
it was given.

**This applies to both editions.** A self-hosted install serving its dashboard at `/app` and
redirecting `/` there costs nothing and behaves identically, and one code path is worth more than
a root that differs by edition.

## Consequences

Somebody who bookmarked `/journeys` is redirected to `/app` rather than to the visit list, because
the redirect is to the dashboard rather than to a guessed equivalent. That is the same answer a
mistyped address has always received, and the visit list is one press away.

The root of the address is now free. What goes there is not this decision.

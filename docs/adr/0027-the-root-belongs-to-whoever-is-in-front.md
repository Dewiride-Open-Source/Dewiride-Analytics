# 0027 — The root belongs to whoever is in front

- **Status**: accepted
- **Date**: 2026-08-22
- **Completes**: [0020](0020-the-screens-live-under-one-segment.md), which moved the screens under
  `/app` so that the root could be occupied, and then left it empty.
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.

## Context

[0020](0020-the-screens-live-under-one-segment.md) moved every screen under `/app` for one reason:
the address the dashboard is read on is the address written into the beacon somebody pastes into
their website, taken from `window.location.origin` and never from configuration — so it can never
change, and it had better have room for whatever else the deployment needs to put on it. Until now
nothing did. An address naming no screen was redirected to `/app`, which is the right answer when
there is nothing else there.

Something now needs to be there. The hosted service has a public website, and it has to be on the
same name as the product rather than on one of its own — a customer who reads the pricing page on
one address and signs in on another has been shown two products, and every link between them is a
place for the two to disagree about which is which.

The obvious ways of arranging that are all worse than they look:

- **A second name for the website.** Splits the product in two for a reader, and wastes the one
  short memorable name on whichever half was chosen first.
- **`basePath` on the dashboard.** Would stop `/api`, `/collect` and the beacon ever reaching the
  dashboard at all, since every one of them is forwarded by it. Routing them at the edge instead
  splits the table `proxy.ts` deliberately keeps in one file, into a file nothing tests.
- **Routing at the edge.** Same objection, and it makes the arrangement a property of whichever
  proxy is deployed rather than of the product — so an installation somebody runs themselves gets
  a different answer from the hosted one, and the difference is invisible in the repository.

## Decision

**The dashboard is the single router for its address, and forwards the root to whatever is behind
`DEWIRIDE_SITE_ORIGIN`.** `proxy.ts` gains a fourth job, tried after the engine and after the
screens: an address that names neither, with a website configured, is forwarded there untouched.
The order matters and is the whole rule — the engine's addresses, then the screens, then everything
else is somebody else's.

**Unset is a valid setting, and it is the ordinary one.** `siteOrigin()` answers nothing rather than
throwing, which is the difference between it and `engineOrigin()`: a dashboard with no engine cannot
draw a single screen and says so loudly, while a dashboard with nothing in front of it is an
installation somebody runs themselves. With nothing configured the root leads to `/app` exactly as
it did before.

**Read per request, never at build time.** For the reason the engine's address is: `rewrites()` in
`next.config.ts` are compiled into `routes-manifest.json`, which would bake one website's address
into the image and make the setting a lie.

**Everything the website publishes that is not a page lives under `/site-assets`.** Two Next.js
applications on one address both claim `/_next`, and the first to answer hands the other's pages the
wrong bundle. The website sets `assetPrefix` to that prefix, keeps its own served-as-is files in a
directory of the same name — the prefix is not applied to those automatically — and the dashboard
forwards the whole prefix untouched.

**`/robots.txt` and `/sitemap.xml` are named individually.** They cannot move behind a prefix,
because a crawler looks for them at the root and nowhere else. Both are shaped like files, and the
screen rule deliberately passes over anything with a full stop in it so that a request for a file is
never given a language prefix — so both are named in the proxy's matcher and recognised by
`isSiteFile()`.

**An address shaped like a file, with no website behind it, is answered as missing rather than
redirected.** Sending a page of markup back to something that asked for a stylesheet is a stranger
failure than the one that actually happened.

## Consequences

One address serves the marketing pages, the screens, the engine, the collector and the beacon, and
the browser cannot tell that three separate services are behind it. The sign-in cookie works with no
cross-origin arrangement to get wrong, because there is no second origin.

An installation somebody runs themselves gains the same capability, and it is worth having: putting
your own front page on the address your dashboard is on now costs one setting.

**The matcher is now the load-bearing part of that file, and it is a regular expression inside a
string.** A backslash that does not survive being a string escape changes the meaning without
changing the shape — `.*\..*` is "has a full stop in it", and `.*..*` is "is at least two characters
long", which excludes very nearly every address there is. That mistake was made while writing this,
and it fails as a page that cannot be found rather than as an error anybody would look for in a
proxy. `proxy.test.ts` in both applications reads the matcher back as a regular expression and holds
it to a list of addresses that must reach it and a list that must not.

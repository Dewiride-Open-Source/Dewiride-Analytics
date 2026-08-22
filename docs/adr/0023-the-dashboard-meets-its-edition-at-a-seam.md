# 0023 — The dashboard meets its edition at a seam

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column and collects nothing new.

## Context

The engine has had an edition seam since the beginning: exactly one composition module is compiled
into any build, the host discovers it rather than naming it, and neither edition's code appears in
the other's binary. Conditional compilation was rejected for it because analyzers do not read an
inactive branch.

The dashboard had no such thing, and until now needed none — every screen exists in both editions.
The hosted service changes that. Creating an account on somebody else's server is an act with no
meaning on a server you own, and the screen for it is commercial code that must not appear in the
open-source product. A runtime flag would not do: the screen would still be in the bundle, and a
bundle is readable.

## Decision

**`@edition` is a module specifier the bundler resolves, and that is the whole mechanism.** The
open-source module is the default and the commercial one is selected by `DEWIRIDE_EDITION=cloud`,
which points the alias at `ee/frontend/edition`. Whichever module is resolved is the only one
compiled in.

**The contract carries what an edition contributes, never what it is.** Nothing branches on the
edition's name; a screen an edition does not offer is `null`, so the route that would show it can be
written once, publicly, and send somebody somewhere sensible instead. Today that is the sign-up
screen alone.

**Every address the product publishes is the same in both editions.** The sign-up route exists in
the open-source build and redirects to signing in, because an installation somebody runs themselves
has nothing to sign up to. That keeps the list of screens a plain fact written once, and keeps the
one place that decides where somebody belongs a pure function of what is known.

**An edition brings its own wording.** Its catalogue is laid over the product's at the point the
language is resolved, and is kept in whichever repository owns the screens that read it — so the
open-source repository carries no copy for a screen it does not have, and adding a language to the
commercial edition stays a translation job in the repository that owns those screens. An edition
must not use a name the product already uses, because the two are merged one level deep.

**Both aliases are written relative to the application directory.** The bundler treats what is
configured as something to import rather than as a place on the disk and refuses an absolute Windows
path, which would fail on one contributor's machine and nobody else's. The application's own short
name for its source is stated there as well as in the TypeScript configuration, because the
configuration's version applies only to files inside the application and the commercial edition's
screens are not inside it.

**The commercial screens are a workspace member that is usually absent.** They import React and the
data-fetching library, and a second copy of React in one bundle produces hooks that fail at run
time — so they must resolve to the same installed copy the dashboard uses, which is what workspace
membership means. The member is named rather than matched by a pattern, a pattern at the repository
root having been rejected for exactly this directory, and the package manager passes over a member
that is not there. The lockfile carries an entry for it in both repositories: an entry with nothing
behind it is ignored, so one lockfile satisfies a frozen install in either checkout.

## Consequences

The open-source build has one route whose only behaviour is to redirect, and a module whose only
content is two absences. Both are the edition's answer rather than a gap in it.

The commercial screens are outside the reach of the open-source repository's linting and formatting,
which ignore that directory deliberately — it is absent from an ordinary clone, so a check that ran
against it would pass or fail by luck. They are held to the same rules by the repository that owns
them.

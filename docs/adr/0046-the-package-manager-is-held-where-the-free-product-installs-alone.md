# 0046 — The package manager is held where the free product installs alone

- **Status**: accepted
- **Date**: 2026-09-28
- **Applies to**: both editions, and only how they are built. Adds no column, collects nothing new,
  and widens no envelope.
- **Revises**: [0023](0023-the-dashboard-meets-its-edition-at-a-seam.md), whose "an entry with
  nothing behind it is ignored, so one lockfile satisfies a frozen install in either checkout" holds
  for the package manager up to 11.27 and not after it; and
  [0043](0043-what-is-out-of-date-is-proposed-not-applied.md), whose proof that "a frozen install on
  a checkout without the two members has nothing to disagree with" was made on a release that still
  tolerated it.

## Context

One lockfile serves both repositories. It names the two workspace members that live in the private
repository, and every checkout of the public one lacks them. Until now a frozen install passed over
their entries, which is the whole of what let the free product install on its own from a lockfile
the commercial build also accepts.

pnpm 11.28.0 and 12.7.0 stop passing over them. Both release notes say the same thing, for
[pnpm#7667](https://github.com/pnpm/pnpm/issues/7667): "`pnpm install --frozen-lockfile` now fails
with `ERR_PNPM_OUTDATED_LOCKFILE` when `pnpm-lock.yaml` lists a workspace project whose directory or
manifest file is missing." The change is
[pnpm/pnpm#15427](https://github.com/pnpm/pnpm/pull/15427), and it treats the old behaviour as the
defect: an install that reported success while installing nothing for a project.

It was proved on this repository before this record was written. The public members, the
workspace file and the lockfile, copied without the private members, install frozen under 11.27.1
and are refused under 11.28.0 with `The lockfile records importers["ee/frontend/apps/site"], but
that project's directory or package.json is missing`.

Three things would go red under the newer releases, and none of them is where the change is made:

- the public build's install step, which runs on a checkout that never holds the private members;
- the free edition's dashboard image, whose build copies only the public manifests before it
  installs — and that image is what `docker compose up` builds for everybody running the product
  themselves;
- a contributor's frozen install on an ordinary clone.

The commercial build and the release hold both repositories, so they stay green. A version bump
proved on a maintainer's machine, where the private clone is present, passes every check there.

## Decision

**The package manager is pinned to 11.27.1, the newest release on pnpm's own stable channel for 11
that still passes over an absent member, and it does not move to 11.28 or later, or to 12.7 or
later, until the public install paths no longer depend on that.** The pin is `packageManager` in the
root `package.json`; the workflows and every image that installs JavaScript read it from there, so
it is the only line that moves.

**The way out is a decision of its own and is not taken here.** Two candidates are known, and
neither is free:

- An install filtered to the members a public checkout holds. The change keeps a filtered frozen
  install tolerant, but states it as a limitation rather than a promise — "a filtered frozen install
  cannot tell a deleted project from an unselected one" — and the public build needs every member
  it holds, which is the whole workspace as the package manager sees it on that checkout, so the
  check runs anyway unless the install is split.
- A lockfile per repository. That reopens the reason 0023 made the commercial screens a member of
  this workspace at all: they must resolve to the same installed copy of React as the dashboard,
  because a second copy in one bundle produces hooks that fail at run time.

## Consequences

Fixes the package manager ships after 11.27.1 are not taken until the way out is decided. No
published advisory affects 11.27.1 at the time of writing, and the 11.x line still receives fixes,
so the hold costs nothing today; it becomes a cost the day an advisory names a version this pin
cannot leave.

pnpm 12 waits on the same decision, since 12.7.0 carries the same change.

A refresh that moves the pin past the hold without the way out is refused loudly and in the right
place: the public build fails at its install step, naming the private member it could not find.

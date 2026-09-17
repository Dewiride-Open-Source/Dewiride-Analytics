# 0043 — What is out of date is proposed, not applied

- **Status**: accepted
- **Date**: 2026-09-17
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.
- **Follows**: [0031](0031-a-change-is-checked-before-anybody-reads-it.md), which is what makes a
  proposal from a machine worth reading: it is compiled and put through every suite before
  anybody looks at it, exactly as a contributor's change is.

## Context

Every dependency in this repository is pinned to an exact version — in one table for .NET, in one
catalogue for JavaScript, by tag for the two stores and the base images, by major for the four
actions the build runs on — and nothing told anybody when a pin had fallen behind. The versions
moved when somebody remembered to look, which on a repository this size is how a security fix that
shipped in March is installed in June.

That is the ordinary case for an automated updater, and three things about this repository make the
ordinary configuration wrong:

- **The lockfile describes two repositories, and a public checkout holds one of them.** The
  workspace names two packages that live in the private repository and are absent from an ordinary
  clone. The package manager passes over a member that is not there, which is what lets the free
  product install on its own — and it also writes the lockfile from the members it can see. An
  install that is not frozen, run on a checkout without them, drops their entries, and the
  commercial build then refuses the file as out of date. That rewrite is exactly what an automated
  update produces, and exactly what a contributor without the private clone produces with
  `pnpm add`. It was proved on this repository before this record was written: the public build
  stays green, because a frozen install on a checkout without the two members has nothing to
  disagree with, and the failure appears in the other repository after the merge.
- **Some pins are chosen together with files no updater can move.** The Node major is chosen with
  the runner in `build.yml` and `engines.node`; the .NET major with `global.json` and the target
  framework; each store's version with the integration fixture that tests against it, and on the
  machine serving traffic only after a backup. A tag such as `24-alpine` can only ever receive a
  new major, so watching it would propose precisely the moves that are not taken this way.
- **Two packages in the version table belong to the other repository.** Stripe.net and the client
  for the mail service are pinned here, because the table is the single central one for the whole
  tree, and referenced by no project in this solution. An updater working from this checkout never
  restores them and never sees them.

## Decision

**Dependabot proposes; the build decides; a person merges.** A `.github/dependabot.yml` watches
the four actions, the .NET version table, the SDK pin and the JavaScript catalogue, once a week,
and opens a pull request for each thing that has fallen behind. Nothing merges on its own. A
proposal that turns the build red is the point of the arrangement: that is the release which would
otherwise have failed on a machine with real traffic behind it.

Four things follow, and each is a decision rather than a default:

**Nothing is proposed until a week after it was published.** A package taken over at the registry
is usually withdrawn within days. The package manager already waits a day before installing
anything, and the proposal is held back for a week on top of that — long enough that the withdrawn
release is never proposed at all, short enough that a security fix is still proposed the week it
ships.

**The base images, the two stores, and the two packages that belong to the other repository are
not watched.** Each is named in the file itself, with the reason. An entry whose every possible
proposal is one that is never taken by that route is not coverage; it is a green mark that means
nothing, and the file says instead where those moves are made.

**The lockfile is checked on the proposal, not discovered after the merge.** `pnpm verify` now
asserts that every member named in the workspace file has an entry in the lockfile, so a lockfile
rewritten without the private half fails the public build with a sentence saying what to do —
regenerate it with `ee/` cloned — rather than passing here and failing there. It catches the
contributor's `pnpm add` on the same terms.

**Related packages move together.** The platform packages from Microsoft and the PostgreSQL
provider are one proposal, as are the telemetry packages, the sign-in packages, the test
packages; on the JavaScript side the framework, the stylesheet compiler, the language and its lint
bridge, the linters, and the test tools. Everything else that is a minor or patch release arrives
as one proposal a week, and any new major arrives alone. Two things are kept out of the groups on
purpose: the analysers, because a new rule set can turn the build red on code that was fine
yesterday and that is a proposal to read by itself; and esbuild, which is pre-1.0 and treats a
minor release as allowed to break the beacon.

## Consequences

The versions this product is built on now fall behind by at most a week plus the cooldown, and
every move is proved by the same build a contributor's change goes through.

**Every JavaScript proposal needs one manual step before it can merge.** The machine that writes
it has no private clone, so its lockfile is missing the private half, and the check above says so.
A maintainer with both repositories fetches the proposal's branch, runs `pnpm install
--lockfile-only` with `ee/` present, and pushes the result. Two minutes a week, and the reason
is structural: the only machine that could regenerate the lockfile correctly is one holding a
credential for the private repository, and the public build holds none by design. The updater
also has, at the time of writing, an open defect of its own for a workspace whose root manifest
uses the catalogue — the lockfile it writes can disagree with the manifest — and the same step
corrects that too.

**Stripe.net moves by hand, in the private repository's own change**, because nothing in this
checkout can prove a new version of it compiles. The private repository carries a Dependabot file
of its own, and it watches the actions its workflows run on and nothing else: its projects compile
into this repository's project graph and take their versions from the table and the catalogue
here, so an updater working from that checkout alone has nothing it can restore.

**A proposal that is red stays open until somebody reads it.** The limit of five open proposals
per ecosystem means a week of ignored red marks stops further proposals for that ecosystem rather
than piling them up, which is the right failure: it makes neglect visible on the list rather than
in the count.

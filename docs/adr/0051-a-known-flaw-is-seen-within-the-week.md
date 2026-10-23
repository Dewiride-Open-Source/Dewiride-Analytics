# 0051 — A known flaw is seen within the week

- **Status**: accepted
- **Date**: 2026-10-23
- **Applies to**: both repositories, and only how they are checked. Adds no column, collects nothing
  new, and widens no envelope.
- **Revises**: [0043](0043-what-is-out-of-date-is-proposed-not-applied.md), whose week of waiting is
  "short enough that a security fix is still proposed the week it ships". It is not.

## Context

Microsoft published .NET 10.0.13 on 2026-10-13, a security release whose fixes include
`System.Security.Cryptography.Xml` and `System.Security.Cryptography.Pkcs`. From that day every
change to either repository failed to compile: restore reports a package with a known advisory as a
warning (NU1902, NU1903), and warnings are errors here. Nothing said so for ten days.

- **No build ran.** Neither repository's main had been pushed to since 2026-09-29.
- **No proposal arrived.** Dependabot's Monday run on 10-19 found 10.0.13 five days old, inside the
  seven-day hold 0043 set, so the earliest it could have been proposed was 10-26.
- **No alert was raised.** GitHub's dependency graph reads project files, which under central
  package management name no version and no transitive package, and automatic dependency
  submission, which would resolve them, is not turned on. GitHub never learns which NuGet versions
  are in use.
- **The proposals that did arrive went red** — two unrelated NuGet updates on 10-19 failed on the
  advisories, which is how they were found.

JavaScript is different, and needs nothing here. GitHub reads `pnpm-lock.yaml`, transitive entries
included, so an advisory against anything in it becomes an alert without a build or a proposal —
not at once, since the two critical Next.js advisories GitHub published on 2026-10-22 had raised no
alert here eighteen hours later, but without anybody having to look.

## Decision

**Each repository's Build also runs once a week on what its main already is**, on Monday at 00:30
UTC — 06:00 Asia/Kolkata, beside the dependency proposals. Restore audits every package the
solution resolves, transitive ones included, so a .NET package that gains an advisory turns that run
red within the week whether or not anybody has pushed. The private repository's run is needed in its
own right: Stripe.net and what it brings with it are restored by no build of the free product.

**GitHub's own security updates stay off.** Their proposals would rewrite the lockfile without
`ee/`, as the version proposals do, and fail the same check; and for NuGet they would have nothing
to act on, since no alert is ever raised.

**The week's hold on proposals stays.** A fix is proposed one to two weeks after its release: seven
days of waiting, then the next Monday's run. What makes a flaw visible sooner is the build, not the
proposal.

## Consequences

A red scheduled run is the signal, and it is answered by a dependency refresh, as 10.0.13 was.

GitHub switches a scheduled run in a public repository off after 60 days in which the repository
saw no activity, and does not say what counts as activity. Should the free repository's run be
switched off, nothing goes unseen: the private repository's weekly Build compiles the free solution
as well as its own, against the free repository's main, and GitHub states that rule for public
repositories only.

An advisory with no patched release cannot be closed by any refresh. `braces`, reached only through
a lint plugin, is the one at the time of writing; GitHub dismissed its alert as development-only,
and the refresh that leaves it behind names it.

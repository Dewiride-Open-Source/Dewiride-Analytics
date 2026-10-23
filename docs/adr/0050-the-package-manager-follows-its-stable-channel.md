# 0050 — The package manager follows its stable channel

- **Status**: accepted
- **Date**: 2026-10-23
- **Applies to**: both editions, and only how they are built. Adds no column, collects nothing new,
  and widens no envelope.
- **Revises**: [0046](0046-the-package-manager-is-held-where-the-free-product-installs-alone.md),
  whose hold below 11.28 is lifted because the behaviour it waited for is back; and, through it,
  [0023](0023-the-dashboard-meets-its-edition-at-a-seam.md) and
  [0043](0043-what-is-out-of-date-is-proposed-not-applied.md), whose statements about a checkout
  without the two private members hold again from pnpm 11.28.4.

## Context

0046 held the package manager at 11.27.1 because 11.28.0 and 12.7.0 began refusing a frozen install
whose lockfile names a workspace member the checkout does not hold — and every checkout of the
public repository lacks the two members that live in the private one. The way out was left as a
decision of its own, between a filtered install and a lockfile per repository.

Neither was needed. pnpm 11.28.4 and 12.9.0 restored the earlier behaviour in
[pnpm/pnpm#16462](https://github.com/pnpm/pnpm/pull/16462). The 11.28.4 release notes say:
"`pnpm install --frozen-lockfile` again succeeds when a workspace project recorded in
`pnpm-lock.yaml` has no directory, such as a project left out of a Docker build context. It still
fails if the project's directory exists without a `package.json`."

It was proved on this repository before this record was written, on the committed tree with no
`ee/` directory, in a clean Linux container:

- `scripts/assert-lockfile-members.mjs` passes — the lockfile still names all four members;
- pnpm 11.28.3 refuses the frozen install with `ERR_PNPM_OUTDATED_LOCKFILE`, naming
  `ee/frontend/apps/site` — the control, without which a green result proves nothing;
- pnpm 11.28.7 installs frozen, and `pnpm verify` then passes both as written and with `CI=true`,
  leaving nothing changed in the tree but the `packageManager` line itself.

The hold was withholding more than that. 11.28.4 to 11.28.7 each carry security fixes by their own
notes: credentials forwarded on a redirect or printed in an error (11.28.4); package archives, git
dependencies and configuration dependencies (11.28.5); a CI identity token sent to a registry it was
not issued for (11.28.6); and registry metadata naming a different package from the one requested
(11.28.7). None has a published advisory naming 11.27.1, so nothing had flagged what the hold kept
out.

## Decision

**The package manager moves again, and it moves by hand.** `packageManager` in the root
`package.json` names the newest 11.x release without a prerelease suffix that is at least a day old
— 11.28.7 at the time of writing, which is what `pnpm self-update 11` resolves under the default
one-day release age. pnpm moves its `latest-11` tag on no fixed schedule and some way behind its
releases, so the tag is not what the pin follows. No updater proposes the pin: Dependabot does not
edit that field
([dependabot/dependabot-core#4830](https://github.com/dependabot/dependabot-core/issues/4830)), so
it is moved in a dependency refresh — the change that also regenerates the lockfile with `ee/`
present — and `.github/dependabot.yml` names it among the things that move by hand. The workflows
and every image read it from there, so it is still the only line that moves.

**What made the hold necessary is now what keeps the pin honest.** The public build installs from a
checkout that never holds the private members, so a release that stops passing over an absent member
is refused there, at the install step, naming the member it could not find. The free edition's
dashboard image builds from the public manifests alone and fails the same way. Neither needs a rule
of its own.

**The lockfile check stays.** `scripts/assert-lockfile-members.mjs` still asserts that the
lockfile names every member, because a machine without the private clone — Dependabot's among
them — still rewrites it without the private half.

**pnpm 12 stays out, for a different reason than before.** 12.9.0 carries the same fix, so it is no
longer held for the same reason. It is held because it is distributed differently: a native binary
placed by the package's own install scripts, which corepack does not run, so each of the three
images that enable corepack would fetch a binary on first use instead. How every image obtains its
package manager is a decision of its own.

## Consequences

Fixes the package manager ships are taken at the refresh that next moves the pin, from 11.28.4's
onwards; nothing proposes one sooner.

A checkout whose `ee/` directory exists but holds a member without its `package.json` — a partial
copy, not a missing one — is refused by a frozen install. That is the case the release notes keep
refusing, and the right answer to it is to finish or remove the copy.

0046's way-out candidates are not pursued. If a later release refuses an absent member again, the
public build says so on the change that moves the pin, and the choice between them is reopened then
rather than now.

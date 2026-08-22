# 0021 — A standing in an organisation

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Collects nothing new and widens no envelope.

## Context

Authorisation had one shape: `site_memberships` grants a person a role on one website, and
`ITenantScopeProvider` turns a grant into the scope every telemetry query is bound to. That is
enough for an install with one person and one website, which is what a self-hosted copy usually
is on its first day.

It is not enough for an install with a team, in either edition. A person added to an account has
to be granted every website separately, and every website added afterwards has to be granted to
every person again. Nothing anywhere records that somebody belongs to the account at all, so the
only way to answer "who is in this organisation" is to gather the distinct people named on its
sites — which is a different question wearing the same clothes, and answers nothing about someone
who was added before any site existed.

Organisations are already in the shared schema and both editions write one. What was missing was
the grant that says who belongs to it.

## Decision

**`organization_memberships` joins the shared schema, and both editions write it.** The Community
edition creates one on first run for the account that claims the install; there is no edition in
which the table is unused, and no version of this in which the two editions carry different
authorisation tables. Divergent schemas are what make an upgrade path between editions impossible.

**`OrganizationRole` is its own enumeration, not `SiteRole` reused.** `Member`, `Admin` and `Owner`
answer a different question from `Viewer`, `Editor` and `Owner` — one is a standing across an
account, the other a role on one website — and the first organisation-level standing with no
meaning on a single site would otherwise be a migration rather than an addition.

**A standing is translated to a role on a site by `OrganizationRoles.OnItsSites`**, and a value
outside the enumeration throws rather than falling back to the narrowest role. Such a value can
only arrive from a cast or from a row this product did not write, and answering it with a reader's
access would silently admit a stranger.

**Where somebody holds both claims, the wider applies.** An owner of an account who was never named
on one of its websites still owns that website; somebody granted editing on a single site does not
lose it by joining the account as a reader. The alternative — taking the narrower — produces an
account owner who can see nothing, which reads as a defect to everyone who meets it.

**The migration backfills.** Every existing grant on a site is translated into the widest standing
it implies in that site's organisation. Migrations here are forward-only and run against a
self-hoster's live data with nobody to call, so a table that arrives empty beside grants that
already exist would leave two records of the same fact disagreeing from the day it shipped.

**One resolution, both editions.** `MembershipTenantScopeProvider` reads both claims and takes the
wider, wherever it runs. An installation with one organisation and a service with many are asked
the same question about a single site — which organisation owns it, and what does this caller hold
in that one — so there is nothing here for an edition to answer differently. Two implementations
would be two chances for one of them to be more generous than the other while both drove the same
screens.

## Consequences

Nothing a person can reach changes in the open-source edition today: the same grants produce the
same scopes. What changes is that the account now records who belongs to it, which is the fact
invitations, roles and cross-site access are all expressed in terms of.

Removing a person from an organisation removes the grants on its sites with them, in one act, which
is what this table made possible.

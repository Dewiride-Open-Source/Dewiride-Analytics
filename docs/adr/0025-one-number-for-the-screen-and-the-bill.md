# 0025 — One number for the screen and the bill

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no telemetry column and widens no retention envelope.
- **Applies to**: both editions. The count is open-source; what anybody is charged for it is not.

## Context

An installation somebody else runs has to decide how much each account has used, and it has to be
able to say so. The obvious way to build that is a second count: a metering job with its own query,
tuned for summing rather than for showing.

That is the arrangement to avoid. A customer whose dashboard reports one figure and whose allowance
reports another has no way to tell which of them is wrong, and neither has anybody answering their
message about it. The two would not disagree on the day they were written — they would drift, one
of them corrected and the other not, and the drift would be invisible until it was expensive.

There is a second, quieter constraint. Only one project in this product may reach the telemetry
store, and an architecture test enforces it. Whichever edition wants a usage figure, the statement
that produces it has to live in the open-source repository.

## Decision

**One fragment produces both figures.** The arithmetic that turns raw activity into pages delivered
is written once, in `ReconciledEvents.DeliveredPageViews`, and both the statement behind the
dashboard's headline total and the statement behind the accounting are built from it. An
integration test asserts the two against one set of events, through the address the dashboard
really asks and the port the accounting really uses.

**The count is a port in the open-source product.** `ISiteVolume` is declared in the Application
layer and answered in the project that holds the telemetry driver, because it has to be — no other
assembly may issue a statement against that store. It is a system-side port on the same terms as
`ISiteRoster`: the sites come from the control plane rather than from a request, so it takes no
`TenantScope` and nothing reachable from a request may use it. The questions a person asks are the
ones on `ITelemetryQueries`, every one of which demands proof of a role on the site it reads.

**It is compiled by a second entry point, not a new case.** `AnalyticsSqlCompiler.CompileVolume` is
separate from `Compile` deliberately: every statement reachable from a request takes its site from
the authorisation decision, and a case that took one from the question instead would put an
exception to that rule somewhere a caller could reach.

**Identity is settled inside each site.** A correlation identifier is minted by the reporting site's
own server, so two sites can mint the same one. Reconciling across all of them at once would read
that coincidence as one visitor seen twice and move one site's activity onto another's key —
charging the wrong account for it. The reconciliation is therefore written once and rendered two
ways, with and without the site as part of the key.

## Consequences

A change to how pages are counted reaches the dashboard and the accounting in the same commit, and
the test that holds them together fails if it reaches only one.

An open-source installation carries a port nothing in it calls. That is the price of the storage
boundary and it is the right price: the alternative is a commercial project issuing its own
statements against the telemetry store, which is the arrangement the boundary exists to prevent.

Metering asks one statement per organisation rather than one per site, which is why the port takes a
set of sites rather than one. An organisation that has added no sites yet costs no statement at all.

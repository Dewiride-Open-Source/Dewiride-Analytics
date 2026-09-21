# 0045 — Closing an account

- **Status**: accepted
- **Date**: 2026-09-21
- **Applies to**: both editions. Adds three nullable columns to `organizations`, one nullable
  foreign key on `organization_invitations`, and a one-row `installation_claims` table; collects
  nothing new; **narrows** the retention envelope, because nothing about a closed account outlives
  thirty days in the running stores.
- **Follows**: [0013](0013-removing-a-website.md), whose ordering of the two stores this applies to
  a whole account and whose "refused for ever" this keeps true by other means; and
  [0026](0026-somebody-else-on-the-account.md), which left a person able to belong to two
  organisations without saying what happens when one of them is closed.

## Context

An account could be added and never taken away. The hosted service's terms promised something
the product did not do — _"after an account is closed we keep its traffic data for 30 days so that
it can be restored if the closure was a mistake, and then delete it"_ — and a self-hosted install
had no way to be rid of its last website's history at all, because [0013](0013-removing-a-website.md)
refuses to remove the only website somebody owns.

"Account", in the terms and in the code, is the organisation: the thing that owns websites and
that people belong to. So closing an account is closing an organisation, and every question this
decision answers is a question about what happens to the websites, the people, the telemetry and —
on the hosted service — the money when an organisation is closed and, thirty days later, deleted.

Three facts about the code as it stood shaped the answers more than anything else.

- **Per-site access already crosses organisations.** The list of websites somebody can open, and
  the scope every telemetry read is bound to, are resolved per site from every organisation the
  person holds a grant or a standing in. Only the account screens, the choice of organisation for
  a new website and the hosted service's money endpoints are pinned to the single organisation
  `StandingForAsync` chooses. A person invited into a second organisation reads both today, so a
  closure that walled everybody with a closed organisation would take away screens the engine
  still serves.
- **Telemetry is keyed by site, never by organisation.** A site row is the only handle the
  telemetry store has, which is why removing a website purges telemetry before the row goes. An
  organisation's purge is therefore one deletion per website, and the websites must still exist
  when it runs.
- **The first-run claim is "are there any accounts".** Deleting an account was never possible, so
  the question never had a second answer. Once it is possible, an install whose accounts are all
  deleted would look exactly as it did before anybody claimed it.

## Decision

**Closing is the organisation's act, and everything stays for thirty days.** An owner closes the
account; measurement stops for every website in it at once, nobody in it can open its screens, and
a clock starts. Nothing is deleted. `organizations` gains `closed_at`, `closed_by_user_id` and
`deletion_reminder_sent_at`, all absent while the account is open, so that bringing it back is
clearing them and nothing else. The state is derived from those columns rather than kept in one of
its own, as an invitation's is, and the mutators are idempotent rather than throwing, as a key's
withdrawal is: two owners pressing the button at once, or one request retried, must not move the
instant the deletion date and the announcement hang off.

**A closed account is invisible on every read and write path, and explained on one screen.** Its
websites resolve to no scope, are absent from the list, from the collector's catalogue, from the
classification roster, from the choice of organisation for a new website and from the guard that
protects somebody's last website; an invitation into it is answered like a spent one; every
account endpoint answers as though the person belonged to no account. `OpenSites` and
`OpenOrganizations` on the context are the one spelling of "not closed", so there is one place for
a closed account to leak through and it is the place every reader starts. Four things see a closed
account: the session, which has to explain it; the two acts that close and restore; the hosted
service's plan and invoice pages, so that a paying customer can still read what they paid; and the
sweep.

**Where somebody is, an open organisation comes first.** `StandingForAsync` orders open
organisations ahead of closed ones, then widest standing, then oldest grant, and carries the
closure instant back with the standing. Closure is reported separately: the session describes the
closed organisation a person belongs to, chosen among the closed ones by the same widest-and-oldest
rule, and says whether they have anywhere open to be — a standing in an open organisation or a
grant on one of its websites. Somebody with nowhere open is walled on the closed screen; somebody
with an open account keeps their dashboard and sees one line about the closed one. The act of
closing takes the organisation `StandingForAsync` chooses, so it is an open one whenever the person
has one; the act of restoring takes the closed organisation the person holds the widest, oldest
standing in, so the account the closed screen describes is the account its button acts on.

**Restoring is an owner signing in and pressing one button.** Every user row is kept for the thirty
days, so sign-in and password reset keep working; no emailed secret, no new anonymous endpoint.
Any owner may close and any owner may restore, with a typed confirmation of the account's name on
the way in: reversible for thirty days, but it stops measurement for every website and takes the
whole team's screens away, so a reflex press must not be possible.

**Three messages, to every owner, each one message per owner.** That the account was closed, with
the deletion day; a reminder a week before deletion; that it was brought back. The keys carry the
organisation and the instant of the closure or the restoration — the event reported on, never the
moment of sending — so a pass that fails after sending sends nothing new and an account brought
back and closed again is told about the second time. The mailbox is folded into every key at the
one place messages are composed, because the service that delivers the hosted edition's mail
treats one key as one message and refuses the same key sent to a second address; the four owner
messages the hosted edition already sent reached only the first owner, and now reach all of them.
The messages are composed in the free product through `MailTemplate`, which has no edition seam,
so they say the same on both editions and the reminder promises nothing that only the hosted
service does.

**The sweep is hourly, its first tick waits, and every account is its own transaction.** A closed
account a week from deletion is reminded once: one guarded update, under the account's lock, stamps
the row, and only the pass that stamped it sends — which makes it once against a restore that lands
between the choosing and the sending, and across two engines sweeping the same hour, without
leaning on a mail server to notice a repeat, which the free product's cannot. A closed account whose
thirty days have run is
deleted in a scope of its own, under the account's advisory lock, re-read under that lock first:
telemetry for each website, then the organisation row and everything that cascades from it, then
the people who belonged nowhere else, then whatever the edition has to do, then the commit. The
lock is shared with closing and restoring, so a restore that arrives while the sweep holds it
waits and then finds nothing to restore. An account that fails is logged and left for the next
pass, and nothing of it can ride along into the next account's transaction, because the scope that
held it is gone.

**The edition's part runs inside the act, and last at the purge.** `IAccountClosureObserver` is
resolved as any number of observers, none being the ordinary outcome, and the free product
registers none. Closing and restoring call it before their commit, so a refusal there is a refusal
here: an account whose arrangement with a payment company could not be stopped is not closed. The
purge calls it after the rows and the telemetry are gone and before the commit, so that the one
thing it does which cannot be undone is the last thing attempted. An observer's own writes commit
on their own connection; it must be idempotent, and must not assume a failed purge undid them.

**People go only at the purge, and the last purge empties the install.** The account's own members
who hold no standing anywhere else and no grant on any website are deleted with it; anybody who
also belongs to an open organisation is kept. When the purged organisation was the last on the
installation, every remaining account goes too — an installation with no organisation has nothing
for any account to belong to.

**The door stays shut.** `installation_claims` holds one row, written by the first-run claim, by
the migration for every install that already had accounts, and by every purge, in one statement
that does nothing where the row exists: an account being deleted is an account that existed, so
the installation is claimed whatever a neighbouring engine has or has not yet committed, and an
install whose accounts all arrived by signing up is recorded before the last of them goes.
`IsClaimedAsync` reads it as well as the accounts. A purged install is finished rather than new.
Reopening the claim window was
considered and refused for the reason [0013](0013-removing-a-website.md) gave: an unattended
window on a server the internet can reach hands a stranger a working engine on somebody else's
hardware, and the operator has been told in writing that it cannot happen. Starting over is the
documented wipe.

**An invitation outlives its sender.** `invited_by_user_id` becomes nullable and the foreign key
sets it null, because a purged member may have invited somebody into another, open organisation,
and that invitation is the other organisation's history rather than the sender's.

## Consequences

- **A person in a closed organisation and an open one is not walled**, and cannot be: the engine
  keeps serving the open one's websites and the dashboard keeps showing them. An owner of the
  closed one restores it from the line in the header. An owner who belongs only to the closed one
  restores it from the closed screen, which is the only screen they have.
- **The same address cannot open a second hosted account until the first is restored or gone.**
  Signing up with it says the address is registered and points at signing in, which lands on the
  closed screen and its one button. That is the designed path rather than a dead end.
- **Cache eviction is best-effort across instances.** The collector's cache is per process, and
  closing or restoring empties only this process's; on a second instance the one-minute lifetime
  of an entry is the guarantee, exactly as it is for removing a website.
- **The hosted service's purge has a residual.** Its arrangement with the payment company is
  cancelled outright and its billing rows are deleted on their own connection before the
  control-plane commit. If that commit then fails, the organisation is back, still closed, with
  its telemetry gone, its arrangement gone and its billing rows gone; the next pass completes the
  deletion, but a restore inside that window brings back an empty account that the hosted service
  starts afresh on a trial dated from the account's creation and therefore already over, so a plan
  has to be bought again; its invoices remain with the payment company and are obtained by writing
  to the billing address. It is recorded rather than prevented, because preventing it would put
  the payment company's answer inside a database transaction, and the window is one failed commit
  after everything else succeeded.
- **A closed account's server key still answers.** The keyed collector accepts the key and then
  refuses every observation, because the website is not found; a reporter sees a batch with every
  observation counted as rejected, and the wire-format document now says so. Refusing the key
  instead would have made a withdrawn key and a closed account distinguishable, and would have
  required emptying a cache with no eviction handle.
- **A backup taken while the account was open holds its rows until the backup ages out.** The
  running stores keep nothing past thirty days; how long a copy survives beyond that is the
  operator's backup retention, not this decision's.
- **Removing a website is unchanged.** It is still immediate, still refuses the last website
  somebody owns, and still cannot be undone; closing the account is how a self-hosted install with
  one website is rid of its history, with thirty days to change its mind.

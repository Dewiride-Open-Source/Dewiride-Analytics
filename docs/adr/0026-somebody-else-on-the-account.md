# 0026 — Somebody else on the account

- **Status**: accepted
- **Date**: 2026-08-21
- **Supersedes**: nothing. It completes [0021](0021-a-standing-in-an-organisation.md), which added
  the standing but left nothing able to grant one.
- **Applies to**: both editions. Adds no telemetry column and widens no retention envelope.

## Context

Until now an installation had exactly one account on it: the one created when somebody claimed it.
The table that records who belongs to an organisation existed, and nothing could write a second row
into it. A self-hosted installation with three people was a thing the schema allowed, the
authorisation checked for, and no screen could bring about.

Adding a second person is not one decision but several, and each of them has a wrong answer that
looks reasonable:

- **Creating the account for them.** The obvious approach, and the one that lets anybody who can
  name an address claim it. On a service running many organisations it is worse: whether the
  attempt succeeded says whether that address already had an account somewhere on the service,
  which is the disclosure [0024](0024-signing-up-says-nothing.md) exists to prevent.
- **Making invitations a commercial feature.** The free product would then have roles, a membership
  check, and no way to use either. "Several accounts on one install" would be a claim about a
  database rather than about a product.
- **Letting a standing be granted without letting it reach anything.** A person added to an account
  who then opens an empty dashboard has been added to nothing they can see.

## Decision

**An invitation is an offer, and nothing exists in the invited person's name until they take it
up.** A row records the address, the standing offered, who sent it, when it runs out, and the digest
of a secret. Only the digest is stored, for the reason a server key's is: a stolen copy of the
control plane must not hand over a way into somebody's account.

**Taking it up creates the account, or joins the one that is already here.** The screen is told which
of the two it is, so somebody with no account chooses a password and is signed in on the spot, and
somebody who already has one is granted the standing and signs in the way they always do. The
address is taken from the invitation and never from the request, so a link cannot be used to create
an account under a different address.

**Every link that will not do is answered identically.** Spent, withdrawn, expired and never issued
produce one refusal with one code. Telling them apart would say whether somebody else had already
used it, and what to do about it — ask for another — is the same in all four cases.

**Sending a second invitation to the same address replaces the first.** One row per address per
organisation, whatever state it is in, so the list nobody has taken up never shows an address twice
and the older secret stops working the moment a newer one is sent. It is also how somebody who left
is asked back.

**Invitations are in the free product, not in the commercial edition.** The rule that accounts and
roles are free is worth nothing if there is no way to make a second one, and every reason above for
treating an invitation as an offer applies to an installation somebody runs themselves as much as to
the service. What the commercial edition adds around them — signing in with a work account,
directory-driven joining and leaving — is untouched by this.

**A standing reaches every website the organisation owns, in both editions.** Resolving what
somebody may do on a site reads the grant made on that site and the standing held in the owning
organisation, and takes the wider. Listing the websites somebody may open does the same. Without
both, a person invited to an account would be admitted by the authorisation and shown an empty list.

**The last owner cannot be removed or moved to anything narrower.** An account with nobody who can
manage it cannot be repaired from inside the product, and the two ways of reaching that state leave
it in exactly the same condition, so both are refused with the same code. Nobody is offered the
control on their own row either: taking yourself out of the account you are reading would leave you
signed in with nothing to look at.

**Removing somebody takes their grants on the account's websites with them, in one transaction.**
Half of it leaves somebody out of the account and still able to read one of its websites, which is
worse than not having started.

## Consequences

The open-source product can be used by a team, which is what it claimed and could not do. A
self-hosted installation with no mail server still works: the link is written to the log, exactly as
a password reset is, and configuring a mail server is the documented step that changes that.

`MembershipTenantScopeProvider` is now the only implementation of the tenancy port, and the
commercial edition's copy is deleted. The two had converged on the same query the moment the free
edition honoured organisation standings, and keeping two would have been keeping two chances for
one of them to be more generous than the other while both drove the same screens.

An invited person who never opens the link leaves a row behind. It expires after seven days and is
gone from every list at that moment; nothing prunes it, because who was asked to join an account is
part of the account's history and the row is the only record of it.

What is still not expressible: belonging to more than one organisation. The standing somebody is
answered about is the widest they hold, and the oldest of those where they hold the same standing in
several. That is a rule rather than a choice anybody makes, and the screens for making it a choice
arrive with the edition that needs them.

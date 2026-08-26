# 0030 — Settings kept somewhere with a door

- **Status**: accepted
- **Date**: 2026-08-26
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.

## Context

Every secret this product holds lives in one file on one machine. `.env` on the server carries the
two store passwords, the key the hosted service hands its mail over with, and — once payments go
live — the key that moves real money and the secret every event from the payment provider is
verified against. The file is untracked, owned by one account, and readable by anyone who can
become that account.

That arrangement has three costs, and they get worse as the product does:

- **Changing a key means being on the machine.** Rotating the mail key is a text edit over SSH
  followed by a restart. So the person who can rotate a key is exactly the person who can read
  every other one, and the operation is a hand edit of a file the whole stack refuses to start
  without.
- **There is no record of a change.** Nothing says who changed a key, when, or what it was before.
  For a payment credential that is the wrong answer to give an auditor, and it is also the wrong
  answer to give yourself at two in the morning.
- **A key that leaks leaks completely.** It is in a file, in a backup of that file, and in the
  shell history of whoever last edited it. The mail key has already been through a place it should
  not have been, which is what prompted this.

The alternatives considered:

- **Leave it as it is.** Defensible while one person runs one machine, and indefensible the moment
  a live payment key is in the file.
- **A file mounted from somewhere else** — a secrets file per key, an encrypted file decrypted at
  boot. Moves the problem without a record of changes and adds a decryption step that can fail at
  the worst moment.
- **A managed identity**, which is the arrangement with no credential at all. It needs the machine
  to be one Azure issues, and this one is not — it is a rented virtual machine, deliberately, on
  the same principle that keeps the telemetry store self-managed rather than hosted. Reaching a
  managed identity from outside Azure means Azure Arc, which is a large piece of machinery to
  install for one credential.

## Decision

**A vault may be named in configuration, and when it is, it is the last place settings are read
from.** `Dewiride:KeyVault` carries an address, a directory, a sign-in and that sign-in's password;
with all four present the host adds Azure Key Vault as a configuration source before anything else
is registered, so a value kept in the vault wins over the same value in the environment.

Four things follow from that, and each is the reason for the shape rather than a consequence of it.

**It is a configuration source, not a feature, and it is therefore in the open-source product.**
This was the one genuinely contested point. Every secret it will hold on the hosted service is
commercial — the payment keys, the mail key — and the instinct is to put the mechanism beside them
in `ee/`. It cannot go there, and the reason is structural rather than a matter of taste: a
configuration source has to be added before the container is built, and an edition is discovered
after. `IEditionModule.Register` runs long after `AddControlPlane` has already read a connection
string. Putting the vault behind the edition seam would mean the vault could not supply the one
setting most worth protecting. It also happens to be the right answer on the merits — somebody
running this themselves on Azure gets the same benefit, and the code contains nothing about what
the secrets are for.

**It is read once, at start-up, and nothing polls it.** The provider caches for the lifetime of the
process by default and that default is kept. A value changed in the vault reaches a running engine
when that engine is next started. This is deliberate: several of these settings decide whether
money is taken from somebody, and a price identifier that changes underneath a checkout already in
flight is a worse failure than one that changes at a moment somebody chose. It also means an
outage at the vault cannot disturb a running engine — only a restart during one.

**Naming a vault that cannot be opened stops the engine.** Half of `Dewiride:KeyVault` filled in is
refused at start-up with the missing keys named. The alternative — carrying on with the environment
alone — produces a service that starts, passes its health checks, sends no mail and takes no money,
which is the failure this product can least afford to have look like success. The same reasoning
already governs `Dewiride:MailService`.

**The door is shut, and the standing is read-only.** The vault refuses every network address but
the server's and the operator's, and the sign-in the engine uses is granted `Key Vault Secrets
User` over that one vault: it can read a secret and cannot write, list versions of, or delete one,
and has no standing anywhere else in the subscription. Writing a secret is a person's job, done
where the change is recorded.

## Consequences

- **One credential replaces many.** The machine still holds a secret — the password that opens the
  vault — so this is a reduction in what is exposed rather than an elimination of it. The
  difference that matters is that this one credential only reads, only from one vault, and its use
  is recorded.
- **That credential expires.** It is issued for two years. When it lapses the engine will refuse to
  start, with the vault named in the failure. It is recorded where the deployment itself is
  documented, because nothing in the product can warn about it: the engine holds a password and
  not its expiry.
- **The two store passwords stay out of the vault.** The database containers are created from them,
  by Compose, before the engine exists to read anything. Putting them in the vault as well would be
  two records of one fact, and the one that drifts is the one nobody looks at.
- **A change now takes a restart.** Editing a secret in the vault and restarting the engine is the
  whole operation, and it costs the few seconds of collection that any restart costs.
- **Nothing changes for an installation that names no vault.** The section is absent, no library is
  asked for an opinion and no connection is opened. That is the ordinary case and the one anybody
  self-hosting is in.

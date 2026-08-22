# 0024 — Signing up says nothing

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no telemetry column and widens no retention envelope.
- **Applies to**: the commercial edition only. The open-source product has no sign-up form and must
  not: an installation somebody runs themselves is claimed once by whoever put it there.

## Context

A sign-up form is open to anybody who can reach the service, which makes it the obvious way to ask
which addresses have accounts on it. Most products answer that question openly — "that email is
already in use" — and accept the disclosure as the price of a form people can use.

This product treats a uniform answer to a failed sign-in as non-negotiable, and a sign-up form that
gave the same fact away by a different route would make that worthless.

Answering uniformly is harder than it looks. A form that refuses a weak password only for a free
address has disclosed the address. A form that creates a usable account has disclosed it too:
whoever filled it in can simply try the password they just chose, and learn from whether it worked
whether the address had been taken.

## Decision

**The details are checked before the address is looked at.** Whether the address is shaped like one,
whether the password is one this product will take, whether the organisation and the site can be
built — all of it is decided without reference to who exists, so every refusal the form makes is
about what was typed and can be shown to anybody.

**After that, nothing reaches the caller.** An address in use and one that is free produce the same
status and the same empty body. What differs is which message arrives in the mailbox, which is the
one place only its owner can read. A mail server that will not take the message is caught rather
than allowed to escape.

**Nothing is usable until the address is confirmed.** The commercial edition alone refuses to sign
anybody in before that, which is what makes the paragraph above worth anything. An unconfirmed
account is refused in the same words as a wrong password, so the screen that follows signing up says
plainly that the inbox is the next step.

**Filling the form in again is how somebody asks for another link.** An account that has never
confirmed is sent a fresh one; one that has is sent a message saying it already exists and where to
sign in. Both are the same answer on the screen. Somebody who takes the other route — asking for a
new password — is confirmed by that instead, because receiving a link at an address is the whole of
what confirming it attests.

**Everything is created in one transaction, or nothing is.** A half-created account is worse than
none: the address is then taken and the person who took it can neither use it nor sign up again.
Two people filling the form in at the same moment produce one account, and the one who loses is
answered exactly as if the look-up had found them.

**Confirming signs the account in and answers with nothing.** Signing in changes who the
proof-of-origin value belongs to, so the interface reads the session again and gets a fresh one —
one round trip on an act that happens once per account, and one wire contract fewer to keep in step
with the open-source product.

## Consequences

Somebody who signs up cannot use the service until they open the message, and a sign-in attempted
before that is refused in words that do not explain why. That is the cost of the uniform answer and
it is paid deliberately; the screen that follows signing up carries the explanation instead.

The commercial edition now configures one thing about the shared account store — that a confirmed
address is required — which the open-source edition does not. It is a stricter rule rather than a
weaker one, which is the only direction an edition may differ in.

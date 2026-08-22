# 0022 — A way back into an account

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no telemetry column, collects nothing new about a visitor, and
  widens no retention envelope.

## Context

An installation had exactly one way in: a password typed on the sign-in screen. Nothing sent mail,
and nothing could — there was no port for it and no implementation behind one. An owner who forgot
their password was locked out of their own analytics permanently, with traffic still being
collected and nobody able to look at it. That is a defect rather than a consequence of not running
a mail server.

Password reset is also the first thing in this product that has to send somebody a link, and a link
in an email is where a specific and well-known mistake lives: building it from the hostname on the
request that asked for it. That hostname is written by whoever sent the request, so a reset link
built that way can be aimed at a server the attacker controls, and the person who clicks it has
done nothing wrong and no way to tell.

## Decision

**`IEmailSender` is a port in the Application layer, and both editions send through it.** Every
message this product sends is a consequence of a use case, and the use case must not depend on how
mail leaves the building.

**MailKit, not the framework's own client.** Microsoft does not recommend
`System.Net.Mail.SmtpClient` for new development — it does not speak enough of the modern protocol
to be secured properly — and names MailKit as the alternative. MailKit is MIT, which the licence
gate requires. A client is created per message and disposed with it: MailKit's holds one connection
and is not safe to use from two places at once.

**An installation with no mail server still has a sender.** `LoggingEmailSender` writes the message
to the log, including the link. That is a real consequence rather than an oversight — whoever can
read the log can use the link — and it is recorded as a warning so that it reads, in an ordinary
log, as something to fix. It widens who can reach the account by nobody: anyone who can read the
log can already read the database password out of the same machine's environment. The alternative,
a self-hoster with no way back into their own installation, is worse.

**The address links point at is configured and never derived from a request.**
`Dewiride:Dashboard:PublicAddress` is read as text, because every way of not setting it — absent,
blank, a variable that expanded to nothing — has to mean the same thing, and is turned into an
address by one property that answers nothing at all for a value that is not a whole address. Setting
mail without setting it is a refusal to start rather than links that go nowhere.

**Asking for a link says nothing.** An address that has an account and one that does not are
answered with the same status and the same empty body, and a mail server that will not take the
message is caught rather than allowed to escape — an answer that varied would be a way of listing
who has an account on somebody's installation. What remains is timing, which the account allowance
bounds.

**Following a link can fail honestly, but only about the link.** Expired, already used, tampered
with, and naming an address nobody has registered are one answer with one code. A password the
product will not accept is a different answer, because the link was good and the person holding it
was sent it.

**A completed reset ends the lockout and confirms the address.** Somebody who has just proved they
hold the mailbox should be able to sign in at once, and would otherwise meet the lockout their own
forgotten attempts caused. Receiving a link at an address is also the whole of what confirming that
address attests, so an account that had never confirmed one is confirmed by this — which is what
stops a hosted-service account that lost its confirmation message becoming a dead end.

**Sign-ins are re-checked against the account every five minutes** rather than the framework's half
hour. The stamp that a reset rotates is what ends other sessions, and somebody resetting a password
is often doing it precisely because they think somebody else is holding one.

## Consequences

The open-source edition gains a complete way back into an account with no mail server configured,
and a better one with. The engine now holds one address that names a screen — the path a reset link
opens — which is unavoidable: a link has to point somewhere, and the alternative is the mistake
described above.

Rate limiting on the account endpoints is now one allowance shared by signing in, claiming an
installation, asking for a way back and creating an account, because from the point of view of
somebody working through addresses they are the same act.

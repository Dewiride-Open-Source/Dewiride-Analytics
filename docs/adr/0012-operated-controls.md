# 0012 — What a visitor operated

- **Status**: accepted
- **Date**: 2026-08-19
- **Supersedes**: nothing. Widens the envelope recorded in ADR 0005.

## Context

The product records that a pointer was used on a page and nothing about what it was used on. That
answers "was anybody actually there", which is what the classification needs, and answers nothing a
site owner asks: which of two calls to action people press, whether anyone finds the search box,
which outbound links readers follow.

Recording a press is a genuine widening. A control's visible text is written by the site, and a
site may template a person's own details into it — "Delete account for jane@example.com" is a
button label on plenty of real products. So the question is not whether to record presses but what
a press may consist of.

## Decision

**A press is a fourth kind of report, described structurally, and never by anything a visitor
entered.** `EventKind.Action` joins the three existing kinds, and migration
`0005_operated_controls.sql` widens the `kind` enumeration and adds four columns. What is kept is:

- **What sort of control it was**, from the closed set `Unknown | Link | Button | Field`. The page's
  own word for its control — a declared role, or failing that the element — is resolved into that
  set at ingest and then discarded. A column holding whatever other people's markup happened to say
  is a column nothing can be built on and nothing can safely be shown from.
- **What the control said**, cut to 64 characters: its accessible name, or failing that the text it
  reads as on the screen. The site's own writing about its own control.
- **Where it pointed**: the path for somewhere on the same site, the host alone for anywhere else,
  and nothing at all for an address to write to or ring.

**A field's value is never read.** Not truncated, not hashed, not sampled — the code that builds a
report has no path that reaches one. A submit button whose only wording is its value therefore
reports no name at all, and that is the right trade: a bright line is worth more than a label.

**A destination off the site keeps the host and drops the rest.** The path and query of an outbound
link are written by whoever wrote the link and can carry anything, including who followed it. An
address to write to or ring is recorded as having been used and is never kept, because the address
itself names a person.

**Nothing is recorded about the press itself.** No coordinates, no button, no modifier keys, no
duration, no element identifier and no selector path. None of them answers the question, and every
one of them describes the reader rather than the reading. The complete payload is pinned by a test
so a field added later has to be argued for.

**Only presses that landed on something operable are reported.** The tracker walks up from whatever
was under the finger to the nearest link, button, field or thing with a declared role, and reports
that. A press that reaches none of them landed on the page rather than on anything in it: it names
nothing anybody could act on, and reporting it would fill the store with whitespace. It also
removes a double count — a browser answers a press on a field's own name by raising a second press
on the field, and counting both would report one tick of a box as two.

**A site may mark part of a page as never reported.** Anything inside an element carrying
`data-dw-ignore` is left alone entirely, which is how a site keeps a signed-in area or an
administration panel out of the measurement without turning the feature off everywhere.

**The switch is per site, it lives in the control plane, and it is enforced by the collector.**
`sites.capture_clicks` gates ingest: a press reported for a site that has it off is dropped where
it arrives rather than stored and filtered out of later questions. Saving it evicts the site from
the collector's cache, so the change takes effect on the next report rather than up to a minute
later — a setting that lies for a minute is worse than no setting.

**It is on by default, including for sites that existed before this.** The migration backfills
`true`. Two sites on one install differing in what they collect, because of when each happened to
be added, is the kind of difference nobody would ever think to look for. It is also less personal
than what the install already keeps: the town and network operator recorded under ADR 0011, and the
address held for 72 hours.

## Consequences

- **Presses are browser-only, and always will be.** Every capture surface other than the tracker
  sits in the request path and cannot observe a press. `docs/server-reporting.md` states this beside
  the other browser-only fields rather than leaving the omission unexplained.
- **A control with no name is a row rather than an omission.** It reads as "Unnamed" on the
  dashboard, which is honest and is also exactly the prompt a site needs to go and name the thing.
- **The beacon costs more.** It is 1.95 KB compressed against a 2 KB budget, and the budget does not
  move. Anything added after this has to be paid for out of what is left or out of savings
  elsewhere.
- **A press is not a page view and never counts as one.** Every statement that counts traffic
  already predicates on `kind = 'PageView'`, so nothing in the existing numbers moves.
- **Classification is untouched.** Presses are recorded and not weighed. Adding a signal is a
  ruleset version change, and this change must not be able to move a verdict.
- **A site that templates personal details into a button label will have that label recorded.** The
  opt-out attribute is the answer, and the setting is the blunter one. This is stated plainly rather
  than implied, because a site owner cannot make that call without knowing it exists.

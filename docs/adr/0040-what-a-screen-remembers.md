# 0040 — What a screen remembers

- **Status**: accepted
- **Date**: 2026-09-14
- **Applies to**: both editions, and the whole of it is in the free product. Dashboard only: the
  engine answers the same questions in the same shapes, no column or migration is added, and
  nothing new is collected — what is remembered is written into the reader's own browser and never
  leaves it. Nothing in `integrations/` changes, because nothing about what is captured changes.
- **Follows**: [0039](0039-what-is-being-read-is-where-the-visitors-are.md), the last of three
  records drawn from the same thirty days of real use, of which this is the fourth.
- **Revises**: [0028](0028-what-a-link-carries.md), whose decision that "the period every screen
  opens on leaves no trace" is narrowed here to the built-in default: a choice the reader made is
  remembered by the browser, and an address that says nothing is given it.
- **Revised by**: [0041](0041-one-population-for-the-whole-screen.md), which makes the population
  the whole screen's rather than the picture's, so the cards and the picture add up.

## Context

0028 drew a line through what a screen remembers. A choice about what is being looked at — the
period — goes in the address, so a link carries it and the browser's own way back undoes it. What
is true of the machine somebody is sitting at — which website, and later how the chart is drawn —
stays in the browser's own storage. The line was the right one and still is. What thirty days of
use showed is that it left the choices a reader makes most often with the weakest memory of the
three: an address carries a choice for the length of a link and forgets it the moment a screen is
opened plain, which is how every screen is opened every morning.

**The owner chose the same things every day.** The same stretch of days, the same view of the
chart, the earlier period behind it or not — each chosen again on arrival, because the address the
dashboard opens on says nothing and nothing else was listening. The drawing style, which lives in
the browser on 0028's reasoning about the chosen website because it changes only appearance, was
the one control on the chart that stayed as it was left. The dashboard therefore had two kinds of
memory, and the reader's habits fell on the side without one.

**The picture draws everybody, and there was no way to see the people.** That the picture draws
the honest whole is not in question: what the website recorded is what it recorded, and a chart
that quietly left the machinery out would be the over-claim rule 12 exists to forbid. But the
question most owners bring to the chart is how many people came, and a week swept by one crawler
puts the people under a band three times their height. The list beneath the chart names the people
as a category; the picture could not be asked to draw them alone. The engine's answer already
carried what was needed — visits and pages read, per category, per bucket — so this is a question
the dashboard was declining to ask rather than one the engine could not answer.

## Decision

**A deliberate choice is remembered by the browser, and an address that says nothing is given
it.** The period, the view of the chart, whether the period before is drawn behind it, and whether
the picture is kept to people each have a key in the address, as 0028 requires, and now also a
place in the browser's storage under a `dewiride.` name. What a screen shows is what the address
names, or failing that what the browser remembers, or failing that the built-in default. When the
address says nothing and the browser remembers a choice, the choice is written into the address on
first paint — quietly, replacing the entry the reader is already on rather than adding one, because
nothing they pressed put it there — so the link in the bar goes on saying what is on the screen.
Written once: an address that falls silent after that is one the reader went back to, and it means
the built-in default it meant before the choice, so the browser's own way back still undoes a choice
made on the plain address of the screen. Going back is remembered as a choice of the default, so
that the bar's links agree with the screen. The built-in default is neither written into the address
nor distinguishable from an address that never mentioned the control, which is what 0028 said and
still says.

**The address wins.** A link names what its sender was looking at, and the reader's own habit gives
way to it for as long as the link is being followed. Nothing read out of an address is trusted any
more than before, and a word naming no population or no period is read as an address that says
nothing: `only=machines` draws whatever the browser remembers, or everybody where it remembers
nothing, exactly as `period=whenever` opens on the remembered period or the usual week — and the
seed then writes the remembered choice over the junk, in place.

**Only the screens a choice is the subject of write it into their address.** The overview and the
journeys screen are about a period, and each seeds its address with the remembered one. The bar
across the top reads the period on every screen so that its links between the two can carry it,
and writes none, because the live screen and the settings are not about a stretch of days and an
address for them that named one would be a lie. The test that holds the bar to this is
`app-header.test.tsx` "hands the remembered period between the screens without writing it into
this one".

**What is remembered is the reader's own, on the machine.** Nothing is kept on the account and
nothing is sent to the engine, for the reasons 0028 gave for the chosen website: a preference is a
property of the machine somebody is sitting at, keeping it centrally would let one person's tab
change what another sees, and the server knows nothing about the browser's storage while it renders
a screen. Every remembered thing is therefore read as an external store — the server and the
browser agree on the default for the first paint and the browser corrects it — and a second tab
that changes a choice tells the first. One store shape, `remembered()`, serves all of it; the
chosen website and the drawing style ride on the same shape rather than on two of their own.

**The picture can be kept to the people.** With `only=people` in the address, the view of who came
draws the people band alone, with the period before as the people before it; the view of how much
draws pages read by people and visits by people, taken from the judged answer rather than from the
counts of everything recorded, because "people" is a verdict and only judged visits have one. That
makes the figures in that view finished visits, which lag the present by the judging, and the
caption under the picture says so, where the everybody view's caption names only where its days are
cut; the buckets still being judged are washed the same way the who view washes them. The cards
above the chart are
untouched and keep counting everything the website recorded — the chart is being asked a narrower
question, not the screen. When the period holds no visits judged to be people the picture says so
and offers everyone back; when nothing in the period has been judged at all it says that instead,
because "nobody was a person" and "nobody has been looked at yet" are different facts — and the
one way out of the second is the picture that waits on no verdict, everybody's how much, reached
in one press from wherever the picture was kept, because a button whose press lands on the same
sentence under a different button is not a way out. In the table, the people are the whole once
the picture is kept to them, so they take the whole's place in front and the period before follows.

**The control wears the colour a person is drawn in.** Pressed, it is green rather than the accent,
because green is the tone a person carries everywhere else on the dashboard and the pressed
control should read as "the green band" rather than as a second accent competing with the one
beside it.

## Consequences

**The dashboard opens on what the reader last chose.** A reload, the next morning, and the move
from one screen to the other all land on the remembered period, view, comparison and population,
and the address says so. A link still opens on what its sender saw for every control it names.

**Absence in a link is no longer the default; it is the reader's habit.** Somebody who sends the
plain overview address sends "your overview, however you usually read it", where before it meant
"the last seven days, who came, this period alone". That is a change in what a link means and is
recorded here as one. The way to send exactly what is on screen has not changed: the address in
the bar carries it, and the seed on first paint is what keeps that true.

**A cold load paints the default once before the remembered choice.** The server cannot read the
browser's storage, so the first paint is the built-in default and the browser corrects it a frame
later — the same behaviour the chosen website has had since 0028. In the worst case the screen has
asked the engine for the picture it is about to switch away from before the correction lands;
that is one answer discarded on a cold load and never on a warm one.

**A remembered choice is a fact about a browser, not about a person.** It is not carried between
machines, it is lost when the browser's site data is cleared, and it is never seen by the engine.
A reader who wants their habits to follow them would need them kept on the account, which is a
different decision with a different privacy shape and is not this one.

**The people-only figures are judged figures on the how-much axis.** They will not add up to the
page views and visitors in the cards above, and neither pretends to: the cards count everything as
it happened, the people-only picture counts finished visits a verdict was reached about. The
caption names the population under each of the two judged drawings, which is the discipline 0036
and 0039 already hold the live screen to; the everybody drawing's caption names only where its
days are cut.

**Tests that make this true.** The store and its pairing with the address are pure and tested
without a screen: `remembered.test.ts` and `use-remembered-address.test.tsx`, whose "the way back
from a choice lands on what was there before" is what holds the seed to first paint. That the overview
asks the judged question rather than the recorded one under people-only is
`dashboard.test.tsx` "asks for judged visits rather than page views when only people are wanted";
that the picture draws the people alone is `traffic-chart.test.tsx` "draws only the people where
somebody asked for them alone".

**What stays as it was.** The filters on the journeys screen are not remembered: a narrowing is a
question about one period rather than a habit, and a list that opened already narrowed would hide
visits from somebody who had forgotten they asked. The drawing style and the chosen website behave
exactly as they did, on the shared store. Nothing here changes what a screen shows when the
address names everything.

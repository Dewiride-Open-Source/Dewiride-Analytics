# 0028 — What a link carries

- **Status**: accepted
- **Date**: 2026-08-25
- **Applies to**: both editions. Adds no column, collects nothing new, and widens no envelope.

## Context

The dashboard has two screens about a website — the numbers, and the visits behind them — and they
are two questions about the same stretch of days. Until now each held that stretch as its own state,
so moving between them started again, reloading started again, and a screen somebody wanted to show
a colleague could only be described rather than sent.

Fixing it by lifting the state into a provider shared by both screens fixes the smallest of those
three and none of the rest: state a React tree owns dies with the tree, so a reload still loses it
and a link still carries nothing. The browser already has a place for what a reader is looking at,
it survives a reload, the Back button already operates on it, and it is what people paste into
messages to each other. Nothing else on offer has any of those properties.

What is less obvious is that not everything a screen remembers belongs there. Which website the
dashboard is showing is remembered too, and putting it in the address would be actively wrong:
it is a property of the machine somebody is sitting at rather than of what they are looking at, a
colleague following the link may not be able to see that website at all, and the only spelling
available for it is an identifier that would then be on screen in the address bar.

## Decision

**What a reader chose about what they are looking at goes in the address. What is true of the
machine they are sitting at stays in the browser's own storage.** Today that line falls between the
period, which travels, and the chosen website, which does not.

**A period is written as what it is called, or as the two days it runs between.** `last-7-days`,
`yesterday`, `2026-08-01..2026-08-14`. Two full stops separate the ends rather than a dash, which
already separates the parts of a date; neither character has to be escaped, so a link still reads as
the days it names. There is one key, so a named period and a chosen stretch cannot both be half
present.

**The period every screen opens on leaves no trace.** An address that says what it would have said
anyway is one more thing in the bar for nothing, so an ordinary address stays ordinary and only a
deliberate choice appears.

**Nothing read out of an address is trusted.** An address is typed, edited and forwarded by people,
so a word naming no period and a pair that is not two real days are both refused and the screen
opens on the period it would have opened on anyway. A stretch that is readable but outside what a
website can answer for is pulled into range rather than refused. There is no address that produces
a screen somebody cannot use, and none that reaches the engine as a question it will reject.

**Changing it leaves an entry in the history.** The way back from a period somebody landed on by
mistake is then the button they already reach for, rather than working out what they were on before
and picking it again.

**The links between the two screens carry it, and the link to the account does not.** Which of them
does is a property of the screen recorded in `SECTIONS`, not a rule spelled out at each link.

The mechanism is `nuqs`, whose adapter sits in `Providers` above everything else. It updates the
address through the History API rather than by navigating, so changing the period does not re-run
the server. Query strings pass through `proxy.ts` untouched — every rule there reads the path alone
— so nothing about this arrangement depends on how the dashboard is deployed.

## Consequences

A period survives a reload, a Back, a Forward and a move between the two screens, and a link opens
on what its sender was looking at. `usePeriod()` is the only way a screen reaches it, so the two
screens cannot disagree about what they are showing without disagreeing about the address, which is
visible.

Anything else a reader chooses about what they are looking at now has an obvious home and an obvious
set of obligations: a key, a written form that survives being pasted, a fallback for junk, and an
absence when nothing was chosen.

**A screen may now be entered on a state it did not put itself into.** Anything a screen derives
from the period and holds separately — which page of a list somebody is on, most obviously — has to
be reconciled when the period changes underneath it, because the browser's own Back button changes
it with nothing on the screen having been pressed. Watching for it where the derived state lives is
the only place that catches both.

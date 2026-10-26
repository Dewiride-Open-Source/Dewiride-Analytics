# 0057 — A text box is never read, and clicks start switched off

- **Status**: accepted
- **Date**: 2026-10-25
- **Applies to**: both editions, and the whole of it is in the free product. Changes the tracker, the
  collector and the default for a website, adds two ClickHouse migrations that remove what followed
  a referrer's path and the names stored for fields, and one control-plane migration that switches
  recording off for every website.
- **Revises**: [0012](0012-operated-controls.md), under which recording what people press was on for
  every website and a press on any field could be named by its text.
- **Revises**: [0014](0014-where-traffic-comes-from.md), under which a referrer was kept as it was
  reported.
- **Revised by**: [0058](0058-a-browser-is-told-whether-its-website-records-presses.md). A page
  opened on a website that records none sends no presses: the reply to its first page view says
  whether to listen for one.

## Context

A site owner's team read the tracker before installing it, and reported two ways in which what a
visitor wrote reached the store.

**Search words, through the address somebody came from.** The tracker sends a page's address whole,
and the collector keeps what follows its path only for a website that asks it to — none on the hosted
installation does. But the referrer was kept as it was reported. Somebody who searched a site and
opened a result had the search carried on the result's page view; on a site that moves itself between
pages, the tracker passed the previous address itself, query and hash and all. Switching the tracker
off on a search page changes nothing: the search travels with the page after it. Read-only on the
hosted installation, 53 stored referrers carried a query — 3 from other sites, each a search — and 91
a hash.

**Typing, through the name of a press.** A press was named by the control's accessible name, its
title, or its text, and a text area is a control. React keeps a controlled text area's default value,
which is its text, in step with what is typed into it, so a press in one was named with up to 64
characters of the typing. A list was named by the text of every choice it offered.

Click names on the hosted dashboard were then found to hold customers' own messages and names. On a
page that shows visitors their own things, the words on its buttons and links are those things.
[0012](0012-operated-controls.md) saw this — "Delete account for jane@example.com is a button label
on plenty of real products" — and answered it with an attribute that keeps a region out, which no
website had used. Read-only, counts only: 5,410 named presses on 6 websites since 2026-08-19, and
per website 281, 183, 105, 30, 16 and 11 names that a single visitor produced.

## Decision

**A text box is never read.** The tracker reports a press in a text area or in a part of the page made
editable, on a control inside one, or on a control holding one, as the kind of control pressed and
nothing more: no name, and no address, because a link somebody wrote there points wherever they
wrote. The collector drops a name reported for a `textarea`, `textbox`, `searchbox` or `combobox`
whatever sent it, because a browser keeps an older copy of the tracker for up to an hour
(`Cache-Control: public, max-age=3600, must-revalidate`). Every other control is named as before — by
the accessible name or title the site gave it, or else by its words on the screen — except that a list
is never named by the choices it offers, and nothing typed into a field is ever read.

**A referrer is kept up to its path.** The tracker cuts the referring address, and the address a page
that moves itself has just left, at the first `?` or `#`. The collector cuts it again, for every way a
visit can be reported (`EventIngestor.PathOnly`), so no reporter has to be trusted to have done it.
Everything ever read from a referrer — the site it names, and the page on it — is unchanged.

**Recording what people press starts switched off.** `Site.CaptureClicks` is false for a new website,
and `StopRecordingClicksUntilAsked` switches it off for every website that already exists, so an
upgraded installation starts this release recording no presses anywhere. The same migration makes
the column's own default false: it was true, and a row written by anything but the product — a
script, a restore, an insert by hand — would have started recording. The clicks card says when a
website records none, and offers the settings to somebody who may change them; the switch says what
it keeps and when to leave it off. A website switched on again gets the words on its buttons and
links as before. `data-dw-ignore` remains the way to keep a region out.

**What followed a referrer's path, and the names of presses on fields, are removed on every
installation.** `0012_came_from_without_what_followed` cuts
every stored referrer at its first `?` or `#`. `0013_presses_on_fields_without_names` removes the name
of every stored press on a field: a stored name cannot be told from typing without reading it, so
every one goes, and the press, its page and its kind stay. Both are updates the store runs in the
background, so the engine starts without waiting for them.

## On the hosted installation

Done by hand on 2026-10-25, with the operator's approval, before this release:

- 10:53:18 UTC — recording switched off for all eleven websites.
- 10:54 UTC — every stored press name removed (5,410) and every stored referrer cut at its first `?`
  (53). The 5,934 presses keep their kind, page and destination.
- 10:54:46 UTC — a fresh backup completed (`telemetry-20261025-105445`), after which the nineteen older
  nightly backups, which held the names, were deleted.

No press arrived between the switch and the removal. The stored referrers that still carry a hash —
90, the 91st having lost its hash with its query — are cut when this release runs 0012, and a website
added before the release ships is switched off by the control-plane migration when it runs.

## What this deliberately does not do

- **No pattern filter on names.** A filter for addresses or numbers misses whatever it was not written
  for, and would claim a safety it does not have.
- **No names only from an attribute the site adds.** Considered. Recording that starts off, switched on
  by somebody who knows what their pages show, was chosen instead, so a website switched on keeps the
  names it would have had.
- **The page's own query is untouched.** It is still kept for a website that asks for it, and no
  hosted website does.
- **Names and destinations already stored for links and buttons stay** on an installation that
  recorded presses before this release — those inside, or holding, a part of the page made editable
  among them, because nothing stored says where on a page a press was. They are the words and places
  a website switched on keeps. On the hosted installation every name stored by 10:54 UTC on
  2026-10-25 was removed by hand; one recorded after that, by a website added or switched back on
  before this release, stays as it would anywhere else, and the destinations stay there too.

## Consequences

- Presses on fields recorded before the release are listed without names on every installation; from
  the release on a press in a text area or an editable region has no name, and other fields are named
  as above.
- 0012 and 0013 remove what cannot be recovered. Nothing on any screen read the part of a referrer they
  cut, and a press on a field is still counted.
- A page's own address still travels to the collector whole, and is cut there as before.
- **A press still travels from a website that records none.** The tracker sends every press, with what
  the control says and where it points, and the collector drops it where it arrives without storing
  it ([0012](0012-operated-controls.md) enforces the switch at the collector). The switch decides
  what is kept, not what a visitor's browser sends; only `data-dw-ignore` keeps a press on the page.
  A browser that learns from the collector whether its website records presses, and otherwise
  listens for none, is the next change.
- The beacon is 2,034 bytes compressed against its 2,048-byte budget. The next change has to make
  its own room, or move the budget and every page that states it.
- A website switched on gets the words on its buttons and links. On pages that show visitors their own
  messages, names or accounts those words are theirs, which is why the switch says so and why
  `data-dw-ignore` stays.
- Going back to an earlier release would name text areas again, keep referrers whole for anything
  reported while it ran, and start every website added while it ran with recording on; the removals
  stay done.

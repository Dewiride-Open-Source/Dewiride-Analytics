# 0053 — A time zone is called by the name it goes by now

- **Status**: accepted
- **Date**: 2026-10-23
- **Applies to**: both editions, and the whole of it is in the dashboard. Changes which identifier
  the dashboard offers, starts a picker on, shows and saves; adds no column and no migration,
  rewrites no stored zone, and changes nothing the engine accepts. Nothing in `integrations/`
  changes, because nothing about what is captured changes.
- **Revises**: [0013](0013-removing-a-website.md), whose consequence handled "the same place is
  `Asia/Calcutta` on one platform and `Asia/Kolkata` on another" by offering the stored zone beside
  the browser's own, which put one place in the list twice under two names.

## Context

The zones a picker offers are the platform's: `Intl.supportedValuesOf('timeZone')`, labelled by
place and by today's offset. Where the engine is V8 — Chrome and Edge, and Node rendering on a
server — those are CLDR's stable identifiers, and CLDR keeps the name a zone had when it was first
listed: `Asia/Calcutta`, `Europe/Kiev`, `Asia/Saigon`, `Asia/Rangoon`, `Asia/Katmandu`,
`America/Godthab`, `Atlantic/Faeroe`, `Pacific/Enderbury`, `Africa/Asmera`, three more renamed
outright (`America/Coral_Harbour` for `America/Atikokan`, `Pacific/Ponape` for `Pacific/Pohnpei`,
`Pacific/Truk` for `Pacific/Chuuk`), and seven whose place is the same but whose name has gained a
region (`America/Buenos_Aires` for `America/Argentina/Buenos_Aires`, `America/Indianapolis` for
`America/Indiana/Indianapolis`, and five like them). The time zone database names them otherwise
now, and Firefox, whose engine corrects ICU's identifiers to the database's, answers with those
names. The device's own zone comes back the same way: a browser in India reports `Asia/Calcutta`,
and resolves `Asia/Kolkata` to it.

So somebody in Kolkata was offered "Calcutta (GMT+5:30)" when setting the product up, a website
they then created was stored under `Asia/Calcutta`, and the screens said its times were "the clock
in Calcutta". A website stored as `Asia/Kolkata` — created in Firefox, say — had the stored zone
added beside the browser's, so the add-website and settings dialogs listed "Calcutta (GMT+5:30)"
and "Kolkata (GMT+5:30)" as two choices. The same held for Kyiv, Ho Chi Minh City and the rest.

## Decision

**A zone is offered, started on, shown and saved under the name it goes by now.** One function,
`currentZoneName` in `lib/time-zones.ts`, turns each of the nineteen identifiers into its current
one and leaves every other identifier as it is. It is applied to every zone the platform offers, to
the device's own, to a stored zone a picker has to include or start on, and to a zone named in a
sentence. Two names a platform offers for one place become one choice.

**The pairs are CLDR's own.** They are every zone in CLDR's `common/bcp47/timezone.xml` whose stable
identifier differs from the IANA name its `iana` attribute records — nineteen as of this record, each
written into the map as CLDR states it. Nothing is renamed on judgement: a place CLDR does not
record under another IANA name is left alone, however else it is spelled elsewhere, and so is a zone
the database has folded into another.

**A stored zone is never rewritten.** A website stored under `Asia/Calcutta` keeps that identifier
until somebody chooses a different zone. Its settings hold, offer and compare the zone under the
current name, so the picker shows Kolkata and leaving it alone sends nothing; only an actual change
sends a zone, and that zone is the current name. A new website, and a new installation's or
account's first one, is created under the current name.

**The engine already knows both names.** It refuses a zone `TimeZoneInfo.TryFindSystemTimeZoneById`
does not recognise, and all nineteen current names were checked against .NET 10 on Ubuntu 24.04 —
the engine image's base, with its tzdata — and on Windows. The telemetry store cuts a website's days
with the same identifier, and ClickHouse 26.3, the version every compose profile runs, lists all
nineteen current names and all nineteen earlier ones. The browser accepts them too: V8 maps a
current name back to its CLDR identifier internally and formats it identically.

**0013's widening stays for what it was for.** A stored zone that a browser does not offer at all —
one the engine's copy of the rules has and the browser's lacks — is still added under its region.
It is no longer the way two names for one place are reconciled.

## Consequences

**The map follows CLDR, by hand.** When CLDR records another renamed place, the pair has to be
added; until it is, that place is offered under its older name, exactly as before this record, and
nothing breaks. When Chrome and Edge answer with the current names themselves, the map stops
matching anything a browser offers — and is still what shows every website stored under an earlier
name, which is never rewritten, as the place it is, so it stays.

**Stored identifiers of both generations coexist.** The engine holds whatever each website was
created or last changed under, and every screen shows either under the current name. Nothing
compares two websites' zones by their identifier, and the one decision a zone makes — the currency
the hosted service quotes in (0048) — already reads both of India's names alike.

**The labels for the seven regrouped zones gain their region.** "Buenos Aires (GMT-3:00)" becomes
"Argentina — Buenos Aires (GMT-3:00)", the way every other Argentine zone is already labelled, and a
sentence naming the zone still says "Buenos Aires". The three renamed outright read as their places
now do: Atikokan, Pohnpei and Chuuk.

**What holds it.** `time-zones.test.ts` covers the offered list, a place a platform names twice, a
device reporting an earlier name, a stored earlier name being neither added a second time nor
offered under its old name, and every current name being one the platform can read; the settings,
add-website, set-up, header and sign-up form tests cover what each form starts on and what it
sends.

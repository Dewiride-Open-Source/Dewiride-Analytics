# 0047 — An allowance says what it counts

- **Status**: accepted
- **Date**: 2026-09-28
- **Applies to**: the commercial edition's plan screen, its allowance and payment messages, and the
  public pricing page; one additive helper in the free product's mail template. Adds no column,
  collects nothing new, and widens no envelope.
- **Follows**: [0025](0025-one-number-for-the-screen-and-the-bill.md), which made the figure an
  account is held to the dashboard's own count. This record is about saying which count it is.

## Context

0025 settled that the number on the plan screen and the number an account is billed against are
one number. It did not settle how that number is described, and the description turned out to be
where it went wrong. Read against a website's own dashboard, the figure looked too low, and nothing
on the screen could say why:

- **The window was invisible.** The figure covers the account's billing month — the period it has
  paid for, or a month counted from the organisation's creation before it has — across every website
  on the account. The label said "this month", which a reader takes to mean the calendar month, and
  compared it with one website's calendar month. The two are different sums over different days, and
  both were correct.
- **The unit was a different word.** The screen said "pages" where the dashboard, the pricing page's
  own questions and the terms say "page views". A reader who sees two words assumes two things.
- **Its days were Greenwich's.** Every date on the plan screen and in the allowance and payment
  messages was written in universal time, while the screens about a website write dates in the zone
  that website counts its days in. A month turning over at half past midnight in Kolkata was said to
  reset on the day before.
- **The month was not quite the bill's.** Once an account paid, its allowance month was worked out
  here by stepping calendar months from the start of the paid period. The payment processor steps
  from its own anchor instead, so a period anchored on the thirty-first runs to the last day of
  February and then back to the thirty-first of March; stepping from the twenty-eighth reset the
  count three days before the bill and again when the renewal arrived, and each reset forgot that
  the account had already been told it was approaching its allowance.
- **An uncounted month read as nought.** The figure is written by the hourly accounting. Between a
  month turning over and the next run there was no row for it, and the screen showed nought — a
  figure, and a wrong one.
- **The first run waited an hour.** The accounting's first pass after start-up waited a whole
  interval, so an account whose month turned over across a release read nought for up to an hour
  after it.

## Decision

1. **One unit, "page views", wherever a customer reads it** — the plan screen, the list of plans,
   the strip above every screen, the allowance messages and the pricing page. The wire and catalogue
   keys follow (`pageViews`), so the next person to add copy finds the right word by its name.
2. **Every date about an account is written in the account's zone**: the zone of its first website,
   ordered by creation, and universal time for an account with no website. It is resolved once, by
   `AccountStandings.TimeZoneForAsync`, sent on the plan answer as a wire value that is never
   rendered, and used by the plan screen, the renewal date on the closure card, and the allowance
   and payment messages through a zoned `MailTemplate.Day`. The dates of closing an account stay in
   universal time: they belong to the free product and match the messages it sends about closing.
   The plan describes the whole account and needs one calendar, so for an account whose websites
   sit in different zones its dates can fall a day apart from a later website's own charts.
3. **The allowance month is the paid period, exactly.** While a paid period runs, the month is that
   period's two instants as the processor reports them, recorded when it reports the renewal. After
   the last paid period ends with no newer one reported — a renewal whose report is late or never
   arrived, collection that has been paused, an arrangement that has ended — months step on from its
   end, so the count restarts when the period would have rather than staying on the old month.
   Before anybody has paid, months step from the organisation's creation.
   `Subscription.AllowanceMonth(now)` answers all three; the anchor it replaced is gone. A usage row
   takes its month's end again on every run, so a month counted on from a period's end before the
   renewal was reported is given the processor's end when it is.
4. **The screen says which days and how many websites.** The days run from the local day the month
   began on to the day before the local day it starts again — "Sep 20 – Oct 19" — so that two months
   side by side never share a day, followed by "across your 6 websites", "on your website", or
   nothing for an account with none.
5. **A month not yet counted says so.** The plan answer carries no figure until the month has a
   row, and the screen reads "Counting" and says the figure appears within the hour, rather than
   showing nought. The bar then carries no reading. When the figure was last computed is not shown:
   it is normally less than an hour old, and a timestamp would be a caveat nobody asked for.
6. **"Starts again from zero on …" appears only where it will happen**: on a plan being paid for,
   that is not set to end on or before the month's end, and that is either measuring or paused for
   going over its allowance. "Renews on …" additionally needs no cancellation at all. A trial, a
   plan ending with its month, a plan that has ended and a card whose failure has paused measurement
   get no date, because each would be a promise the account cannot keep — and a plan paused for
   going over that ends with its month is told the day it ends rather than a day measuring comes
   back.
7. **What an account is told first is decided once**, by one pure function the plan screen and the
   strip above every other screen both read, from the same instants the collector compares. The
   plan answer carries the one it lacked, `measuringUntil` — when access runs out unless a renewal
   or a purchase extends it — and a screen names it only for a plan that has ended, which no renewal
   will extend. Going over pauses measurement only where the week of grace runs out before the month
   does: the new month's first count ends a week still running, so no pause is announced for it.
   While measuring, the order is a payment not made; a plan that has ended, told when measuring
   stops; a trial over its allowance, told the earlier of its end and the pause, and that any plan
   carries it on; a paid plan going over, unless it is set to end first, when its end is the news;
   a plan set to end; and a trial. Once paused, the order is a plan that ended; a month that went
   past its allowance on a plan being paid for, whether or not its card is behind — told the day
   the month turns over, or the day the plan ends where it ends with the month, or, before the new
   month's first count, that it is inside its plan again; a payment not made; a paid plan that has
   lapsed, sent to where its card lives; and otherwise a trial that ended. Every sentence that sends
   somebody to a plan below, or to a bigger plan, is said without that pointer where there is none.
8. **The first run of the accounting comes five minutes after start-up**, then hourly. Five minutes
   is past the window in which the service's health probe is timing its start, so the first pass —
   which reads the telemetry of every account — never competes with it.
9. **A plan set to end with its period is dated by the period** where the processor names no end
   date, because its reference describes ending at the period's end and ending on a date as separate
   settings and does not promise to fill the second in for the first. Every path that records the
   date — an event arriving, closing an account, the hourly check of closed accounts — reads it
   through one rule, `StripeArrangements.EndsAt`, so one of them cannot undo what another wrote.

## Consequences

The figure is unchanged; what changes is that the screen says what it is. An owner who compares it
with one website's calendar month is told on the same card that it is every website over different
days.

A paused account resumes at the first run after its month turns over, so up to one interval late —
and in that interval the screen says so rather than repeating that it went over.

Paying an overdue invoice does not move the month. The month is the period the processor is billing
for, paid or not, and an invoice paid late pays for the period it was issued for.

The dates a closed account's screen shows can sit in two zones: the closure's in universal time, the
plan's renewal in the account's. That is deliberate and follows from where each date comes from.

The figure may be up to an hour behind the traffic it counts. That is not shown, for the reason given
in 5.

Tests hold each decision: `SubscriptionTests` for the three kinds of month, including the
thirty-first; `PaymentEventTests` for the processor's end, a row counted before its renewal was
reported taking that end, and the undated period-end cancellation; `StripeArrangementsTests` for the
one rule every path reads that date through; `AccountStandingTests` for the account's zone and the
uncounted month; `UsageRollupTests` and `PaymentChasingTests` for the zoned day in each message;
`UsageRollupServiceTests` for the first run; `MailTemplateTests` for the zoned day; and the
dashboard's `plan-standing.test.ts` for every state the first thing said is decided for, on fixed
dates, and `plan.test.tsx` and `plan-facts.test.ts` for the window, the counting state and every
case in which the reset date is or is not shown.

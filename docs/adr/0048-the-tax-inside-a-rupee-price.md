# 0048 — The tax inside a rupee price

- **Status**: accepted
- **Date**: 2026-09-29
- **Applies to**: the commercial edition only. Adds no column and widens no envelope. Collects one
  new fact from a buyer paying in rupees — the state or union territory they are billed in — which
  travels to the payment processor with the purchase and is not stored here.
- **Follows**: [0025](0025-one-number-for-the-screen-and-the-bill.md), which made the figure on the
  screen and the figure on the bill one number. This record keeps a rupee price one number in the
  same way: what the plan screen quotes is what leaves the buyer's bank, with the tax inside it.

## Context

The hosted service sells each plan at two prices: in rupees to an account whose first website
counts its days in India, and in US dollars to everybody else. The rupee prices — ₹799, ₹2,499 and
₹6,999 a month — are what the buyer pays, and Goods and Services Tax is inside that figure rather
than added to it. A dollar sale is treated as an export and carries no tax.

Inside India, which tax the invoice states depends on where the buyer is billed. A sale within the
supplier's own state carries Central GST and State GST, half the rate each; a sale to any other
state or to a union territory carries Integrated GST at the whole rate. One rate stated on every
rupee invoice cannot express the first case, and an invoice that states the wrong kind of GST is
one the buyer's accountant cannot use. The business behind the hosted service is registered in
Rajasthan.

Four facts narrowed what could be built:

- **It will not work the tax out.** Stripe's own tax calculation is not offered to a business
  registered in India, so the tax has to be stated with rates created by hand.
- **Its way of choosing a rate from the address is deprecated.** `line_items.dynamic_tax_rates`
  matched a rate to the billing address typed on the checkout page. Stripe's documentation marks it
  deprecated and does not list India among the countries it supports.
- **A subscription's first invoice is final when it is paid.** Checkout creates the arrangement and
  charges its first invoice in one step, so the rates that invoice carries are the ones the session
  was created with. The processor's documented alternative — adjusting each draft invoice as a
  renewal creates it — never reaches that first invoice.
- **Nothing had checked the settings.** When the live keys replaced the sandbox's, the rate setting
  kept an identifier that existed only in the sandbox. Stripe refused every rupee purchase, and the
  buyer read "Something went wrong": the refusal escaped as an unhandled error, and nothing at
  start-up had asked whether the configured objects existed in the key's mode.

Two inclusive rates on one price raise a question Stripe's documentation does not answer: whether
they are backed out of the price together or one at a time. Together, 18 % in two halves leaves the
same taxable amount as 18 % in one; one at a time, each half is taken out of the whole price, the
tax comes to more than 18 % of what remains, and the invoice misstates both. It was settled by
asking the processor for a preview invoice — which creates nothing — of the ₹799 plan with the two
halves attached: a total of ₹799.00, ₹60.94 of Central GST and ₹60.94 of State GST, on a taxable
amount of ₹677.12 — 79,900 paise, 6,094 and 6,094, and 67,712. The whole rate alone gives ₹121.88
of tax on the same ₹677.12. The two rates are backed out together, so the prices need no other
shape.

The alternatives considered:

- **Stripe's tax calculation.** Not offered to this account.
- **Dynamic tax rates.** Deprecated, without India among the supported countries, and matching on an
  address rather than on anything the buyer is asked.
- **Editing each invoice as it is drafted.** Never reaches the first invoice, which is the one paid
  at the moment of purchase.
- **Collecting a card first and creating the arrangement here afterwards.** The first charge would
  then be taken with nobody present, and an Indian card needs its holder present to approve the
  first payment of a recurring mandate.
- **The time zone or the network address as the place of supply.** The time zone picks the currency
  and nothing else; neither is the address on the invoice, which is what the tax follows.
- **One combined rate, or a percentage chosen to compensate for the back-out.** The first cannot
  show the two halves a sale within the state has to show; the second answers a question the
  preview settled the other way.
- **Prices written net of GST and marked exclusive.** Correct under either back-out, and needless
  once the back-out was observed; the invoice would also have shown ₹677.12 as the price of a plan
  every screen quotes at ₹799.
- **Keeping the buyer's state here.** Considered below and refused.

## Decision

1. **Where the buyer is billed decides which GST lines appear.** A rupee buyer billed in the
   installation's home state is charged Central GST and State GST, at 9 % each and in that order; a
   buyer billed in any other state, or in any union territory, Integrated GST at 18 %. A dollar
   purchase carries no rate, and neither does anything on an installation with no GST configured.
   Every rate is inclusive, so the prices do not move and ₹799 is still what is paid. `GstRates.For`
   is the single place the choice is made.
2. **The state is asked before the buyer is handed over.** Choosing a plan on a rupee account opens
   a short dialog — _"Where are you billed?"_ — listing the thirty-six states and union territories
   of ISO 3166-2:IN by the names the reader sees, sorted in the reader's language. The purchase goes
   to the engine as the plan and the two-letter code together. A dollar account is never asked. The
   engine refuses a code it does not recognise (`NoSuchState`, 400), a rupee purchase without a
   state (`StateRequired`, 409) and a dollar purchase naming one (`StateNotApplicable`, 409). From
   the dashboard the last two mean a screen still showing the other currency, and the buyer is told
   the page is out of date. A code not on the list is refused before anything else is looked at;
   an installation that cannot take payment says so before either conflict between the state and
   the currency.
3. **The state travels with the purchase and is not kept here.** The chosen rates become the
   subscription's default tax rates, from which the processor copies them onto every invoice, and
   the code is written as `tax_state` into the session's metadata and the subscription's. A
   returning customer's saved address and name are updated from what they type on the checkout page,
   so the processor's record of the customer and the invoice agree. There is no column: the
   processor holds the state three times over, decision 5 reads it from there at the moment it
   needs it, and a copy written from events that arrive out of order drifts from the record it
   copies.
4. **The settings are all four or none.** `Dewiride:Stripe:IndiaGst` holds `HomeState`, a two-letter
   code, and `CentralRate`, `StateRate` and `IntegratedRate`, identifiers of rates created by hand
   at the processor. Some set and not others, a home state that is not a state or union territory,
   or the same rate named twice, and the engine refuses to start, naming the setting — as it does
   for a key with a space or a line break in it, which no key has and from which Stripe's client
   cannot be built. None set is a valid installation whose rupee invoices carry no GST. They replace
   a single rate stated on every rupee invoice and a switch asking the processor to work tax out,
   both of which are gone.
5. **The processor's record is kept right after the purchase, decided from that record.** When a
   purchase completes, the state on the billing address the buyer typed is compared with the state
   they chose, both read from the completed session; where they agree the processor is asked
   nothing. Two states both outside the home state are taxed alike, and a buyer who chose one and
   typed the other is left as they are. Where the difference changes the lines — one of the two is
   the home state — a warning names the first invoice: it was final when paid, states the wrong
   lines, and is corrected by hand. What the renewals are taxed for is then decided from the
   arrangement as the processor holds it at that moment, never from the address the purchase was
   paid from, because the report of a purchase can arrive after the customer has already corrected
   their address. A purchase naming no account here — another sale through the same processor
   account — is left alone. When a customer changes their details on the billing pages, the
   arrangement is read back from the processor together with the customer's address, and moved the
   same way where its rates are not the ones that address calls for; a move between two states taxed
   alike changes no line on any invoice and is left as it is. Only a rupee arrangement on an
   installation with GST configured is ever moved, and what is recorded is country and state codes,
   never the address. The address is read leniently — current codes, superseded ones, full names,
   older spellings — because it is the processor's wire value, written by a form this product does
   not control; an address naming no recognisable state leaves the rates as chosen and says so. A
   refusal while reading or moving them leaves the event for the processor to deliver again, and
   moving them twice changes nothing.
6. **What the settings name is checked at start-up, and payment stops only where the charge would be
   wrong.** Once the engine has started, each configured price, each rate and the named billing
   pages are read once from the processor, in the background, holding up neither the start nor the
   health probes. A currency stops taking payment only for a defect that would change what is
   charged or guarantee a refusal: a price or rate that is missing or archived, a price in the wrong
   currency, for the wrong amount or not one flat charge a month, or a rate that adds tax instead of
   containing it. A price stops its own currency and a rate stops rupees. A rupee price not marked
   as including tax, a rate recorded for the wrong place or of the wrong kind, and two halves that
   do not add up to the whole are reported loudly and the service goes on selling, because none of
   them changes the amount. The rates are checked by their structure — two equal halves making the
   whole — and never against a percentage written in code. A defect in the named billing pages —
   missing, inactive, or not offering exactly the configured prices, as pages that let nobody switch
   plans offer none — is reported the same way and never stops a purchase, which does not pass
   through them; so is naming no billing pages at all on an installation that sells. The log names
   the setting and never its value. A setting the processor will not answer about draws no
   conclusion, and a processor out of reach ends the check with what it had already found standing.
   What the check finds stands until the engine next starts. It never stops the engine, and the plan
   screen tells somebody not yet paying that plans cannot be bought just now in a stopped currency.
7. **A page the processor will not open is answered, not thrown.** A refusal, a processor out of
   reach and one that has not answered within 20 seconds all reach the buyer as `503`
   `PaymentProcessorFailed` — the page could not be opened, nothing on the account changed, and
   where to write — with the processor's request identifier in the log and never its message.

## Consequences

- **A first invoice declared for one state and addressed to another stays as it was issued.** Its
  renewals are corrected by decision 5. Where the two states are taxed differently, the invoice
  itself is corrected by hand at the processor, which does not let a paid subscription invoice be
  edited — a credit note and a reissued invoice — and the buyer is written to from the billing
  address.
- **Arrangements bought before this decision carry Integrated GST whatever their address**, until
  somebody points their default rates at the right ones. Nothing here does that on its own for an
  arrangement whose customer never changes anything.
- **A change in the rate of GST is not a change of setting alone.** A rate's percentage cannot be
  edited at the processor, so it means new rates, new values for the three settings, and every
  existing arrangement pointed at them, since an arrangement keeps the rates it was given.
- **A dollar buyer billed in India carries no GST**, as every dollar purchase does. A sale to
  somebody in India is not an export, so each one is reported as a warning for an accountant to
  look at rather than decided here.
- **A rupee customer must never be marked tax-exempt at the processor.** For an exempt or
  reverse-charge customer Stripe backs an inclusive tax out of the price and charges what remains:
  ₹677.12 for the ₹799 plan. Only somebody editing the customer by hand can cause it.
- **The two halves can differ from half the whole rate by a paisa**, because each is rounded on its
  own. The total never moves.
- **A rupee buyer takes one more step before paying**, and a dollar buyer none.
- **The processor has to send one more event**, the one that says a customer's details changed, so
  the endpoint that receives events subscribes to nine. Nothing fails without it: an endpoint that
  lacks it never moves a customer's tax after the purchase, and says nothing about it.
- **A setting put right is taken up at the next start.** The check runs once each time the engine
  starts, so a currency it stopped stays stopped until the setting is corrected and the engine is
  restarted, and a processor out of reach at that moment means nothing is checked until the next.
- **The invoice prints only the tax identifiers it is given.** The supplier's own GSTIN is a setting
  of the account at the processor rather than anything this product sends.

Not settled here, and questions for an accountant rather than for this product: whether the invoice
must print the place of supply, whether a business buyer's GSTIN is to be collected at checkout,
and what a dollar invoice should say about an export made under a letter of undertaking.

Tests hold each decision: `GstRatesTests` for the choice of rates, union territories included;
`StripeIndiaGstTests`, `StripeOptionsValidatorTests` and `PaymentSettingsTests` for all four or none
and for the engine refusing to start otherwise; `IndianStatesTests` for the list the dashboard
offers being exactly the list the engine reads, and for reading a code strictly and an address
leniently; `PaymentHandoffTests` for the three refusals, their order after an installation that
cannot take payment, the rates and `tax_state` sent to the processor, and a returning customer's
address kept in step; `SandboxPaymentPagesTests`, against a real sandbox, for every rupee price
including its tax, for the two halves and the whole rate carrying the same tax on the same price,
and for the check at start-up finding nothing to report; `TaxAlignmentTests` for decision 5, and
`StripeArrangementTaxTests` for an arrangement being read with its customer's address in one request
and moved with its rates and `tax_state` together; `CatalogueRulesTests` and
`PaymentCatalogueTests` for decision 6, and `PaymentSettingsTests` for the check being made once the
engine has started; and the dashboard's `plan.test.tsx` and `indian-states.test.ts` for the dialog,
the order of its list, a name for every state, and the notice that plans cannot be bought just now.

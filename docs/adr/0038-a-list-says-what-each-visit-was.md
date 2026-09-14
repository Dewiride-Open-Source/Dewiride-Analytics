# 0038 — A list says what each visit was

- **Status**: accepted
- **Date**: 2026-09-14
- **Applies to**: both editions, and the whole of it is in the free product. Changes the shape of
  both forms of the visit-list statement and one wire record; adds no column, no migration and no
  index; collects nothing new and widens no retention envelope. Nothing in `integrations/` changes,
  because nothing about what is captured changes.
- **Follows**: [0037](0037-a-person-is-not-concluded-from-one-thing.md), which recorded the sentence
  "A search engine · Came straight here" as the first thing the owner saw, and left the words to
  where the words live.
- **Revises**: [0029](0029-what-a-visit-was-is-rebuilt.md), whose consequence "the ordinary list
  pays nothing" is given up here: every row now carries what its visit was, and the list pays for
  the page it is showing.

## Context

The journeys screen listed a badge, a strength, a page count and a time. Everything a reader
recognises a visit by — where it came from, roughly where it was, whose network it arrived over,
what it was read on — was a click away, on the panel opened from the row. That is the account 0029
built and never stored, and it was correct; it was also invisible until asked for.

Two things made the invisibility a defect rather than a tidiness.

**The badge and the arrival used the same three words for two things.** A verdict of
`KnownSearchCrawler` was labelled "A search engine". The kind of place a visit was sent from, when
that was a search, was also labelled "A search engine". A confirmed crawler arrives with no referrer,
as crawlers do — every one of the 115 such visits in thirty days of one installation did — so the
row read "A search engine · Came straight here", and the owner read it as an organic search that
had somehow come from nowhere. Neither word was wrong. The pair was.

**The name a confirmed crawler was called was on the panel, never on the row.** Googlebot,
bingbot and Yandex's crawler each carry a token the operator publishes and this product confirms
from the operator's own address ranges, and the row said only which kind of thing it was.

**The account of a visit already existed, and 0029 had decided it exists in one form.** The
opened panel settles its eight facts with expressions the rebuild in `JudgedVisitDetails` settles
its own with, held in step by an approved statement rather than by a shared body. What the row
lacked was therefore not a new fact but a second reading of a fact the product already had — the
cheapest kind of change to make and the most dangerous kind to make twice.

## Decision

**Every row of the list carries what its visit was.** `JudgedSession` gains a `VisitContext`, the
wire record `VisitSummary` gains a `context` object, and both are the same eight fields, spelled the
same way, as the opened visit's account and the live row's. The list and the opened visit read
them through one method, `ClickHouseTelemetryQueries.Established`, from the same eight columns in
the same order — only where the eight begin on the row differs — and every one of the three
reaches the wire through the one mapping in `SiteEndpoints`, so one visit is described in one
vocabulary wherever it appears.

**The ordinary list rebuilds the page it is showing, not the period.** The slice of verdicts is
taken first, as it always was, with the count of the whole narrowed window worked out inside it.
The activity read is then bounded by that slice's own span — from a day before the earliest visit on
the page began to a day after the latest one ended, through `SendingSites.ThePageAndTheVisitsAcrossIt`
— and rebuilt into what each visit was by the same body that rebuilds a period,
`JudgedVisitDetails.OfThePage`. The store settles the two ends of the span once, as scalars, and
reads the activity as a range of its primary key. A page of twenty-five visits on a busy site costs
about two days of that site's activity however long the period is; the store writes the page out
once for the rows and once for each end of its span, and those are bounded reads of verdicts, not
of events.

**A narrowed list rebuilds the period once and takes each row's account from that rebuild.** It had
to rebuild the whole period to narrow at all. The verdicts are now joined to the narrowed rebuild —
an inner join on the identity the engine derives, which both keeps the visits asked for and
describes them — rather than tested for membership in it, so the period is rebuilt once and named
once. The three conditions a verdict answers on its own stay on the outer selection, after each
visit has been reduced to one verdict, so a visit is still kept or dropped on the verdict a reader
would be shown and the count still describes the narrowed list.

**A visit whose activity has aged out still lists, with an account that establishes nothing.** The
ordinary list joins the rebuild with a left join, and nothing established reads as eight empty
texts — which the two closed vocabularies map back to the words they already use for the same
state, a device nothing said and a visit nothing sent. On the row that reads "Came straight here"
and nothing more, exactly as the panel opened from such a visit reads it, because the store holds
no difference between a visit that named no referrer and one whose reports are gone. It is also why
the count at the foot of the list and the rows above it cannot disagree.

**The row names a confirmed crawler, and never a claimed one.** Where a verdict carries
`identity.confirmed_crawler`, the row shows the token the crawler declared — a catalogue token the
evidence sentences already render, never text from a user agent — or the operator's name where the
confirmed crawler declared no token this product recognises. A visit that only claimed a name, however
loudly, shows none on the row: rule 12 forbids attributing traffic to a named company on the strength
of an inference, and a name on a row is an attribution.

**"A search engine" means one thing.** The category reads "A search engine's crawler". The row
names the site that sent the visit — "From Google", or the sending address where the catalogue has
no name for it — beside "Came straight here". The filter that narrows by how visitors arrived,
where a kind of arrival stands on its own, reads "From a search engine", "From an AI assistant",
"From a social network" or "From a link on another site". The opened panel, which already labels
the fact "Came from" and names the site under it, keeps the apposition "A search engine" beneath
the name, because a second "from" on the line below the label reads as a stutter. And the category
the engine answers with when it could not tell reads "Couldn't tell", which is what 0037 made it
mean.

**The row says where, roughly where, whose network, and on what**, in that order, on the same line
the live row already uses for two of them, from one shared component. It is hidden while the row is
open, because the panel lays the same facts out in full an inch below. The credits the two
catalogues' licences require appear at the foot of the list exactly as they do under the live list,
and only where a row was placed or a network named.

## Consequences

**The list pays for a page.** An unnarrowed list page now reads about two days of the site's
activity through eight window functions on every page turn. Every visit on the page is already
bounded by its verdict's own instants, so the page's span alone would be enough for correctness;
the day either side is the reach 0029 chose so that a visit already under way at the edge keeps its
own beginning, and it is kept here for the same reason. If the store's query log shows the reach
mattering, tightening it is the first lever, and one scalar returning both ends of the span at once
is the second.

**Two wire shapes change together.** The list's rows gain `context`, and the dashboard's schema for
a row requires it, so the engine and the dashboard land in one release — which they do, being
published as one. There is no shim, on the same terms as every other change of shape here.

**The guard on a drifted identity now covers the page-bounded rebuild too.** 0029's round trip
proved that every value offered narrows the list to the visit it came from; a second round trip
proves that the unnarrowed list, the narrowed list and the opened visit give one visit one account;
a third that a page of several visits hands each row its own, at whatever offset the page begins;
and a fourth that a verdict stored with no activity behind it lists with `VisitContext.Nothing`
rather than failing on a null. A page-bounded rebuild that cut off a visit's first report, or whose
reconciliation wrote the key differently, would derive a different identity from the one the
engine stored, match nothing and hand back eight empties with no error anywhere, and those tests
are what would catch it.

**A name on a row is a claim this product makes**, so it is made only from a confirmed identity.
Suspected AI crawlers, self-described crawlers and everything else that says what it is stay
described by their badge alone until the row is opened.

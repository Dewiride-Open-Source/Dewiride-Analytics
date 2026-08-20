# 0014 — Where traffic comes from

- **Status**: accepted
- **Date**: 2026-08-20
- **Supersedes**: nothing. Adds no column and widens no envelope.

## Context

The referring address has been collected since the first migration: `events` has carried
`referrer` and `referrer_domain` from `0001_events.sql`, the browser beacon sends
`document.referrer` on the first report of a visit, and the collector derives the host from it.
Nothing has ever read either column back. A site owner watching a week rise on the chart could see
that it rose and had no way to find out what caused it.

This is therefore a read path over data already held, not a new thing to collect. What had to be
decided is what the answer means, because the obvious arithmetic gives a wrong one.

## Decision

**A source is counted once per visitor, not once per page.** Only a visit's first page names
anywhere else; every page after it was reached from the site being measured. Counting each report's
own answer would file one arrival from a search engine as one arrival from the search engine and a
dozen arrivals from the customer's own website. So the source is settled once for the whole
visitor, in the same shape and for the same reason the country and the device class already are —
and by the same aggregate, `anyIf` over a per-visitor grouping.

The cost is stated rather than hidden: a visitor who leaves, follows a link back from somewhere
else and returns within the same day has two sources and is credited to one of them. That is a
limit of counting people rather than arrivals, and it is the trade every other per-visitor figure
on the dashboard already makes.

**The measured site is never one of its own sources, and neither is anything below it.** The
exclusion is the site's registered address and any subdomain of it — the same rule `EventIngestor`
applies when it decides whose traffic a report is, so one notion of "this site" holds across ingest
and reporting. Comparing against each event's own `host` instead was rejected: a site reachable at
both its bare address and a `www` or `docs` subdomain would list one half of itself as a source of
the other.

That address is the only value the SQL compiler binds which comes from the control plane rather
than from a fixed table of identifiers in its own source. It is bound as a parameter, and the
endpoint reads it from the site catalogue rather than from the request, so a caller cannot decide
whose traffic gets left out of somebody else's list.

**Arrivals naming nowhere are a row, not an omission.** Typing an address in, opening a bookmark,
following a link from an application, and arriving from a site that withholds the address are
indistinguishable here, and together they are usually the largest single row. Dropping them would
take every share on the screen against a total that excluded most of the audience. The screen calls
the row "Came straight here" and says what it covers when it is more than half the period.

**A sending page keeps its path and drops everything after the question mark.** The list can be
read two ways: by sending site, and by the page the link was on — which article sent the readers is
the question somebody asks second, and it is answerable because the whole referring address is
stored. What is shown is `host` plus `path`. The query string is somebody else's site carrying
somebody else's state, it can hold a token or an identifier belonging to a person, and it is not
needed to answer the question. This narrows what is displayed below what is retained, which is the
right direction; it does not change what is retained, and so does not touch ADR 0005.

**Sources are shown as text and never as links.** The address is written by whoever visited the
site. A clickable one would put an attacker-chosen destination one mis-click away from the person
reading their own numbers.

**One site is one row whatever address it answered on, and each is named.** A search engine answers
on hundreds of addresses — `google.com`, `www.google.com` and `google.co.in` are one place — so a
list keyed on the hostname reports the busiest source of a site's traffic at a fraction of its size
on each of a dozen rows, and may not show it near the top at all. A leading `www.` is cut and the
rest is reduced to the label in front of the public suffix, which is then looked up in a catalogue
that gives it a name and a kind.

That reduction is what makes the lookup safe. A referrer is written by whoever visited the site
(rule 11), so matching a catalogue entry against any label in the address would let somebody who
registers `google.attacker.test` file their traffic under Google's name on a stranger's dashboard.
Taking the label in front of the suffix gives `attacker`, and the entry does not match. An
approximate public-suffix list of a dozen second-level labels is carried for this; the real list is
a published file that changes weekly, and depending on it to improve the spelling of a row on a
chart would be a dependency to keep current for the rest of the product's life.

**The catalogue is applied when the question is asked, not when the traffic arrives.** It is bound
into the statement as three parallel arrays rather than resolved at ingest into stored columns.
That is the decision most likely to look wrong later, so the reason is recorded: a catalogue of
search engines and social networks is never finished, and a stored column freezes each visit's
classification at the moment it happened to arrive. An analytics product that answers the same
question two ways depending on when the traffic was recorded is broken, and correcting a catalogue
would otherwise need a rewrite of history rather than a deployment. Read-time also means it works
on every period a site has already recorded, with no migration and nothing to backfill.

**Kinds are five, and one of them is that nothing said.** `Search`, `Assistant`, `Social`, `Link`,
and the same empty name every other grouping uses for an arrival that named nowhere. Conversational
assistants are kept apart from search rather than folded into it: somebody who arrives having been
told about a page did not read a list of results and choose it, and this is not the product to
blur that distinction. Anything uncatalogued is a link from another website, which is what it is —
there is no list of every website, and a product that pretended otherwise would be inventing
precision.

**An address whose job differs from its site's is catalogued separately.** `mail.google.com` is
somebody sending a link to somebody, not a search. Counting it under search engines would overstate
the one figure this card exists to give honestly, so whole-address entries are checked before names.

## Consequences

Nothing to migrate and nothing to backfill: every period already recorded answers this question the
moment the code ships, which is the whole advantage of the column having existed from the start.

There is now a catalogue to keep current. That is a real ongoing cost and it is accepted knowingly:
the alternative was a list of hostnames that answers "how much of my audience does search bring"
only for a reader who already knows which of the names are search engines. Because it is applied
when the question is asked, adding an entry is a code change and nothing else — no migration, no
backfill, and every period a site has ever recorded is re-answered by it.

Its imprecision is bounded and stated rather than hidden. A site absent from it is a link, which
is honest. A site present in it is named, and a site whose subdomains do different jobs needs an
entry per address or it inherits its parent's kind — `mail.google.com` has one for that reason, and
another such address will eventually be found the hard way.

Two things remain deliberately absent.

**What people searched for is not shown, and cannot be.** Search engines stopped passing search
terms in the referring address years ago; they say they sent somebody and nothing more. Any figure
this product showed for a search term would be an invention, which rule 12 forbids outright.

**Campaign tags are not read.** The beacon does not send them and no column holds them, so a link
tagged for a campaign is attributed to the site that carried it. Adding them is a tracker change, a
migration, and a decision about the per-site query-string retention setting they would interact
with.

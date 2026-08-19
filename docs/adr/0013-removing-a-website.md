# 0013 — Removing a website

- **Status**: accepted
- **Date**: 2026-08-19
- **Supersedes**: nothing. The first operation that takes data out of the telemetry store.

## Context

A website can be added and cannot be taken away, and almost nothing about it can be changed once
it is there. Its name and the time zone its days are counted in are settled at the moment it is
added and settled for good; the only thing the Settings panel offers afterwards is whether presses
are recorded. So a name typed in a hurry stays, a website added by mistake stays, and a website
that no longer exists keeps its year of measurements and its place in the picker at the top of
every screen.

Removal is the harder half of that, and it is not a row deletion. The two stores fail in different
ways and in different places. The control plane deletes cleanly: memberships, ingest keys and
classification bookmarks all carry a cascading foreign key to the site, so one `DELETE` takes the
site and everything that refers to it. The telemetry store has never had a row deleted from it at
all — everything written there leaves on a schedule the engine enforces without being asked,
`ip_address` after 72 hours and the whole row after 12 months. Nothing in the product has ever
issued a statement that removes telemetry on demand.

That asymmetry is the whole of this decision. Two stores, no transaction across them, one of them
slow and forgiving and the other fast and final — so the order the two halves are attempted in is
what decides what a half-finished removal leaves behind, and it has to be chosen deliberately
rather than discovered.

There is also a claim to honour. The product tells people it collects the minimum that answers the
question and drops what it holds on a published schedule. "Remove this website and everything you
hold about it" is the request that claim invites, and until now the honest answer was to wait
twelve months.

## Decision

**The telemetry goes first, and the control-plane row goes second.** A removal loads the site the
person owns, counts the sites they own, purges the telemetry store, deletes the site row, and
finally throws away what the collector has cached for it. The ordering is the one whose failures are
recoverable, and the argument is entirely about which half is reachable after the other one has
gone:

- **Purge first and fail.** Nothing has changed. The site is still in the picker, its numbers still
  answer, its memberships are intact, and pressing the button again does exactly the same work. A
  lightweight delete of the same rows a second time is harmless, so a retry needs no bookkeeping and
  no half-way state to resume from.
- **Delete the row first and fail.** Every telemetry read in this product goes through a
  `TenantScope`, and a scope can only be produced by checking a membership against a site that
  exists. The site does not exist, so no scope can ever be built for it again — the rows cannot be
  read, cannot be shown, and cannot be purged by repeating the removal, because the removal has
  nothing left to load. They are unreachable, undeletable, and still on disk for twelve months.

One of those is an operation that can be tried again and the other is a permanent leak of exactly
the data somebody asked to be rid of. Deleting the control-plane row last is what keeps the site
identifier — the only handle the telemetry store has — alive until the moment it is no longer
needed.

**A lightweight `DELETE`, not a mutation, and not a partition drop.** ClickHouse's own guidance is
that `DELETE FROM ... WHERE` is the way to remove rows matching a condition on a MergeTree table,
and both telemetry tables — `events` and `session_classifications` — are MergeTree-family with
`site_id` as the first column of the sorting key. That is worth more here than it looks: the
primary index skips whole granules belonging to other websites, so the statement reads the site's
own rows and very little else. Two alternatives were considered and rejected.

- **`ALTER TABLE ... DELETE` rewrites parts, and the parts are not this website's.** A heavyweight
  mutation reads and rewrites every column file of every part the predicate touches. `site_id`
  orders rows _within_ a part; it does not put a website in parts of its own. So every part in
  every month the website was ever active in belongs to every other website on the installation
  too, and removing one would rewrite all of their data column by column. The lightweight form
  writes the `_row_exists` mask and hardlinks the rest.
- **A partition drop is not available, and buying one would cost every read.** Both tables are
  partitioned by month — `toYYYYMM(server_ts)` and `toYYYYMM(started_at)` — so a partition is a
  calendar month of the whole installation and dropping one would take every website's month with
  it. Partitioning by site instead was rejected twice over: the month partition is what makes the
  12-month retention nearly free, because whole parts age out together rather than being filtered
  row by row; and partitioning on a column with one value per website multiplies parts by the
  number of websites, which is a tax on every query an installation ever runs in exchange for an
  operation performed once in a website's life.

Table names in the statement are literals from a static array in the compiler and the site
identifier is bound as a `{site_id:UUID}` parameter, in the same style the analytics compiler uses.
Nothing a caller supplied reaches the text of the statement — which matters less here than on the
read path, since a site identifier is a `Guid` before it gets anywhere near this code, but the rule
in the codebase is that no statement is ever assembled from anything but literals and bound values,
and an exception argued for once becomes an exception argued for again.

**The default synchronous behaviour is kept, so a successful return is worth something.**
ClickHouse's `DELETE` waits until the rows have been marked deleted before returning, and
`lightweight_deletes_sync` is left where it is. Turning it off would make removal feel faster and
would make the ordering above meaningless: the purge would return on a promise, the control-plane
row would be deleted on the strength of that promise, and a failure afterwards would produce
precisely the unreachable rows the ordering exists to prevent. Waiting is what lets the code treat
a returned call as a fact and move on to the irreversible half.

**Nobody may remove the only website they own.** Adding a website takes its organisation from one
the person already owns a site in; somebody who owns none has no organisation for a new one to join
and is refused. So an owner who removed their last website would be left signed in to an
installation they could no longer add anything to — a locked door with no key on either side, and
no self-service way back, because first-run claiming is refused for ever once an installation has
been claimed and widening it would be a way to take over somebody else's install. Removal is
therefore refused with its own reason, and the dashboard's sentence for it says what to do: add the
replacement first, then remove the old one. The alternative — letting a person with no websites
create an organisation — is a change to how organisations come into being, and inventing a second
route into that for the sake of one refusal is how a product ends up with two.

The site is loaded before the count is taken, so this refusal is a statement about the website that
was named. Counting first would answer a question about a website the caller may hold no role on at
all, and would tell somebody who owns exactly one website that a stranger's website is their last
one.

**That rule is a count acted on, so it is taken under a lock.** The whole removal runs in one
control-plane transaction that opens by taking a PostgreSQL advisory lock keyed on the person
asking. Without it the rule is a check-then-act on shared state and defeats itself the moment it
matters: an owner of two websites who removes both at once — two tabs, a double-tapped button, a
retried request — has both calls read two owned websites, both pass the guard, and both delete. The
result is an account owning none, which is the exact state the rule exists to prevent, reached
through the rule rather than around it, and it is the one outcome here with no way back. The lock is
keyed per person rather than taken globally, because the invariant is per person and making every
customer's removal queue behind every other customer's would be a cost paid on an operation that is
already rare. It is taken in the two-key advisory space, which PostgreSQL keeps separate from the
single-key space the first-run claim locks in, so neither has to know the other's number. The
account identifier is folded to the 32 bits a key is addressed by, and a collision between two
accounts costs one of them a wait and can never let a removal past the guard.

Bringing the purge inside that transaction settles a second question the ordering does not answer on
its own: the telemetry has to be destroyed **after** the guard, not before it. A purge that ran
ahead of the count would wipe a website's entire history and then refuse the removal for being
somebody's last website — the worst of both halves, and silently, since the refusal says nothing
about what it has already done on the way to saying no.

**An editor may change a website; only an owner may remove one.** Renaming and changing the zone go
through the settings endpoint, which already admits `Editor` and above: both are corrections to how
a website is described and reported, and both can be undone by making the change again. Removal is
gated on `Owner` alone, on top of the proof-of-origin pair every state-changing endpoint carries and
a confirmation on the screen that will not proceed until the person has typed the website's own
address. That is not belt and braces for its own sake — a website identifier is printed in the page
source of every page it measures, so the identifier in the path is not a secret and can never be
the safety. The role, the origin proof and the typed address are.

The directory checks the ownership again for itself rather than trusting that it was checked, which
is how adding already works: whether a person may add a website is decided where the addition
happens, because it is a question about what they already hold. A removal is the same question and
gets the same treatment, so a second caller reaching the port later cannot take away a website its
caller does not own.

**Removal is a dashboard capability and there is nothing to deliver on any capture surface.** The
standing rule is that a capability ships everywhere it belongs, so the accounting is written down
rather than left as an omission: removing a website changes nothing about what is collected or how,
so no surface has anything to implement. What happens to a surface still pointing at a removed
website happens for free — every surface reports to the same collector, the collector resolves the
site through the cached catalogue, and an unknown identifier is refused there. A tracker still on
the pages, a Worker still deployed, a plugin still installed: all of them stop being accepted by the
same mechanism, and none of them needs to know why. Nobody is asked to go and take the snippet off
their site, because the product does not need them to.

## Consequences

- **A returned purge means no query can reach the rows again; the bytes leave disk at the next
  merge.** ClickHouse marks rows deleted and physically removes them when the affected parts are
  next merged. Saying "deleted" of the visible state is honest, and saying it of the storage would
  not be, so nothing in the interface promises the second thing.
- **There is a narrow window between the purge and the control-plane delete, and it is accepted.**
  Two things can still write during it. The collector holds a site's snapshot for up to a minute, so
  a report already in flight or arriving before the cache is evicted is still accepted. And the
  classification worker takes its roster of sites at the start of a run, so a website removed
  mid-run can have a verdict written for it after its verdicts were purged. Both write rows that
  nothing can ever read, for the same reason a failed removal would: no scope can be built for a
  site that no longer exists. Closing the window properly means putting coordination on the busiest
  endpoint in the product and paying for it on every beacon, and a second sweep after the row is
  gone would only narrow it — while reintroducing the exact failure the ordering was chosen to
  avoid. The row TTL takes them twelve months after the event, with nothing having to remember they
  are there.
- **The cache eviction is what makes the refusal immediate.** Without it the collector would keep
  accepting reports for a removed website for up to a minute — a website absent from the dashboard
  while its traffic was still being written is a worse state than either end of the operation.
- **Changing the time zone re-counts every day already counted.** Days are cut at query time, with
  the zone carried on the scope from the row on the website, so there is no stored day column to
  migrate and no rewrite to perform. The honest consequence is that yesterday's total changes and
  the daily graph shifts the moment the zone is saved. This is why the form reads the stored
  settings back before it renders, sends only the fields that actually differ, and offers the stored
  zone in the picker even when this browser does not list it: platforms disagree about zone names —
  the same place is `Asia/Calcutta` on one and `Asia/Kolkata` on another — and without that, opening
  Settings to fix a name would silently move a website's day boundary as a side effect of saving.
- **A name or an address that will not fit is refused rather than truncated.** The aggregate states
  both widths — 253 characters for an address, the ceiling DNS puts on a name, and the same for the
  name a website is shown under — so either coming in too long is a refusal the dashboard has a
  sentence for instead of a database error nobody can act on. The two are one number on purpose: a
  website is shown under its address until somebody renames it, so allowing a name less room than an
  address would refuse a website whose address is perfectly legal, at the moment of adding it and
  for a reason nobody could see. The name column is widened to match, which PostgreSQL performs
  without rewriting the table.
- **A removed website cannot be brought back, and re-adding the same address does not bring it
  back.** Addresses are deliberately not unique across an installation, so the address can be added
  again — as a new website, with a new identifier, starting empty. The old snippet carries the old
  identifier and reports to nothing. Nothing about this is recoverable, which is the reason the
  confirmation asks somebody to type the address rather than press a second button.
- **An installation with exactly one website cannot remove it.** That is the only-one rule seen from
  the other side, and it is the state a fresh install is in. Adding the replacement first is the
  route, and it is the route the refusal names.

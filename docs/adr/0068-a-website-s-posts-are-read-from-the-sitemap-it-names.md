# 0068 — A website's posts are read from the sitemap it names

- **Status**: accepted
- **Date**: 2026-11-06
- **Applies to**: both editions, and the whole of it is in the free product. Adds one column to
  `sites`, the tables `sitemap_reads`, `sitemap_parts` and `site_posts` (PostgreSQL migration
  `AddSitemapReading`), one background service, one outbound client, one endpoint, one section of
  Settings and one package (AngleSharp, MIT). Collects nothing from visitors and widens no retention
  envelope. Nothing in `integrations/` or in the tracker changes, because nothing about what is
  captured changes. In `ee/` only the hosted service's terms and privacy policy change.
- **Extends**: [0013](0013-removing-a-website.md), whose removal of a website now also removes
  everything read from its sitemap, by cascade in the database.
- **Follows**: [0032](0032-a-claim-becomes-an-identity-at-the-address.md), whose lists of crawler
  addresses come from a catalogue of pages chosen here, where this is the first fetch from an
  address a customer supplies; [0045](0045-closing-an-account.md), since a closed account's websites
  are not read and deleting the account deletes what was read from them; and
  [0067](0067-work-in-the-background-ends-when-the-host-does-and-not-before.md), whose rule for a
  timed service the reader keeps.

## Context

Nothing in the product knew what a post is. The collector knows a page by its path, and a path does
not say whether it is an article, what it is called or when it was published, which is what a count
of posts, the latest of them and a calendar of publishing all rest on. A sitemap is where a website
already says what it has published: WordPress writes one itself, its SEO plugins and Ghost write
their own, and static generators such as Hugo produce one with the site.

Reading one is the first time the engine fetches from an address a customer supplied. Everything
else it fetches, the place and network data of [0011](0011-visitor-location-and-network.md) and the
crawler lists of 0032, comes from addresses chosen here. A sitemap's address is typed by somebody
with an account. What the file lists, where its redirects lead and what its host name resolves to
are decided by whoever runs that server, and nothing checks that a website's domain is the
customer's: any account can add any domain. So naming a sitemap points the engine at something, and
most of what follows is about what it can be pointed at.

The owner settled the bounds on 2026-10-30. Each page is read once, and again only when its
sitemap's date for it advances. A website gets at most one request a second, on its own addresses
only. robots.txt is obeyed. All of it is free.

This record is the reading, and what Settings says about it. What Insights makes of the posts comes
under a later record.

## Decision

**The address is a setting, and what reading finds has tables of its own.** `sites.sitemap_address`
is saved by the request that saves a website's name and zone, by an editor or an owner, and is kept
in one spelling: scheme, host in lower-case ASCII, path and query, with no port and no fragment.
Everything the engine writes is elsewhere: `sitemap_reads` holds one row per website, with the
claim, when it is next due and how the last reading went; `sitemap_parts` one row per file;
`site_posts` one row per page, keyed by the path the collector would give it (`PagePath.Of`, now the
one definition both use). Where the website redirected a page to another of its own paths, the path
it answered at is kept beside it (`landed_path`), because that is where a browser following the link
ends and what a visit is recorded under, and a page is read only once. So a settings save and a
reader never write the same row, and the collector's cached copy of a website never carries any of
it.

Changing the address deletes the read row and the parts in one transaction, and keeps the posts: the
new sitemap lists each one again, or a complete reading of it takes it off the list. Removing the
address deletes the posts too, because nothing could ever do either. A save that leaves the address
alone writes none of this.

**An address has to pass two rules before anything is fetched from it or kept.** That goes for the
address typed, every file an index names, every page a file lists and every redirect.

The first is about the website. `Site.Publishes` takes an absolute `http` or `https` address with no
credentials, on the default port, whose host is a name and not a number, in its ASCII form too, no
longer than a name server can be asked about, on a host the website is published at. That last is the rule the
collector holds reports to, moved from `EventIngestor` to `PublishedHosts` with its meaning
unchanged, so that a website is never described by two rules. A domain or a permitted origin with no
dot in it covers nothing, so a website registered as `com` publishes nothing. It answers rather than
throws, whatever it is given, because `Uri` accepts hosts it then cannot turn into ASCII. Some
characters become, in ASCII, ones no name may hold or the dots of a number, such as a full-width
less-than sign or an ideographic full stop, and an address kept in that spelling could not be read
back, so every pass over it would fail before asking anything. `Uri.CheckHostName` has to find the
ASCII form a name, and a pass that meets an address it cannot read leaves the website alone as it
would after a failure.

The second is the [sitemaps.org](https://www.sitemaps.org/protocol.html) location rule: a sitemap
speaks for its own host. `PublishedHosts.SameWebsite` takes the sitemap's host or its `www` twin and
nothing else, neither a sibling nor an address below it. A domain is typed by whoever adds the
website, so it can name a whole platform, and without this rule one file on `github.io` would be a
list of everybody's pages on it. A platform's name cannot be refused by counting its labels, since
`github.io` has as many as `example.com`, and refusing it would buy nothing: nothing verifies a
domain, so an account could as well register `somebody.github.io` and reach that one website, under
the limits here. The protocol's rule also names the protocol and the folder a file sits in. Those
are not held to, because neither changes whose server is asked. Whatever leads elsewhere is skipped
and counted, and Settings says so (`left-the-website`); a sitemap that leads nowhere else has
failed, and takes nothing off the list.

**Only the public internet, and no setting can change that.** `PublicAddress.IsPublic` refuses the
older family by list: every block in IANA's special-purpose registry, whatever that registry says
about reachability, and multicast. It permits the newer family by list: `2000::/3`, less what IANA
has set aside from inside it. Both were read from the registries on 2026-11-06. An address in the
mapped form is judged as the older address it reaches, and is taken apart before any block is asked
about it, because `IPNetwork.Contains` answers for a mapped address by what it maps to, which puts
`::ffff:10.0.0.1` inside `2000::/3`. The translated form `64:ff9b::/96` is judged by its last four
bytes, so a network with only the newer family can still read. One address is refused by name:
[`168.63.129.16`](https://learn.microsoft.com/azure/virtual-network/what-is-ip-address-168-63-129-16),
the virtual public address on which every Azure machine reaches the platform's WireServer, on port
80 among others and outside any network security group. It is in no registry and looks public.

`PublicInternetEndpoints` applies that as a connection opens: only ports 80 and 443, and a name is
refused whole if any answer is not public, rather than connected to by the ones that are. The
handler's `ConnectCallback` connects to the endpoints that were judged and to nothing else, so a
name is looked up once for a connection and there is no second lookup for it to answer differently
to. The one seam is `IPublicEndpoints`, because the tests read from a website on this machine. Only
code compiled into a test can stand in for it: an architecture test holds the product to one
implementation that takes no settings, and another test proves that the client the engine registers
refuses this machine. An installation that wants its own private network read cannot have it,
because the same setting on the hosted service would let any account read that service's network.

**The client does nothing on its own.** It follows no redirect, keeps no cookie, sends no
credential, asks no proxy, which would resolve the name for itself out of the check's reach, and
unpacks nothing. It tells nobody what the engine was doing when it asked: no trace header goes to
somebody's website, and the client factory's own log lines, which carry the address of every
request, are off. It speaks HTTP/1.1 exactly, because a newer protocol can open its connection some
other way than through the callback that does the checking. Each address a name answers with has its
share of the ten seconds allowed to connect, so one that stays silent does not use up the time of
the rest. A request the website hangs up on over a connection already open is sent once more, on a
new one, and no more.

Certificates are validated as the framework validates them, but nothing is fetched to do it: no
missing certificate of a chain is downloaded and revocation is not checked
(`DisableCertificateDownloads`, `X509RevocationMode.NoCheck`). A certificate names addresses of its
own to fetch from, and following them would reach wherever they lead, a private network included,
outside every check here. A website serving an incomplete chain therefore reads as a certificate
that cannot be trusted, where a browser might cope.

A request carries `Host`, `User-Agent`, `Accept` and `Accept-Encoding`, and `If-None-Match` or
`If-Modified-Since` where there is something to ask with. The User-Agent names the product first,
says it fetches sitemaps and says where to read about it:
`DewirideAnalytics/{version} (sitemap fetcher; +https://github.com/Dewiride-Open-Source/Dewiride-Analytics#reading-a-websites-sitemap)`.
"Fetcher" is a word this product's own list of automated software knows, so a website reporting
requests from its own server sees the reader judged a crawler, however many pages it reads, and not
somebody copying the website. robots.txt knows it as `DewirideAnalytics`.

Redirects are followed by the reader: at most five, each held to both rules above, each waiting its
turn, with a loop caught by remembering what was asked. Each is put to the robots.txt of the host it
leads to before it is followed, within the same deadline, because where an answer sends the reader
is still the website's to refuse. A redirect robots.txt disallows is not followed: a page comes to
being turned away with no attempt spent, and the address or a file says `robots-disallowed`. A
robots.txt there that cannot be read is trouble at the website, as it would be before a first
request. A gap between requests that would leave no time for five redirects inside the deadline of a
page or a file is refused as the installation starts.

**A website gets one deadline and two limits.** The client's timeout stops at the headers once a
body is being streamed, and a website can begin its answer at once and then drip it out for as long
as it likes, which would pin a reader and outlast its claim. So each asking has one deadline over
the headers and every byte of the body, redirects included: 60 seconds for a file of a sitemap, 20
for a page or a robots.txt. Every read goes under it whatever it was asked under, and when it passes
the response is disposed as well, which unblocks a reader with no way of being cancelled. A website
that began answering a file and did not finish in that time has been too slow, and Settings says
that rather than that it did not answer. Only a 200 or a 203 is the answer asked for. A firewall's
challenge commonly answers 202 with a page of its own, and 204, 205 and 206 hand over something
other than the whole of what was asked, so each is read as being turned away, its attempt spent and
the rule of three in a row applying; a robots.txt is read from any success.

A body is held to two limits, because what arrives and what unpacks can be wildly apart in either
direction: what arrives is counted, and what comes of it is counted again. A file of a sitemap may
be 50 MiB either way, the protocol's own limit, and a gzipped file served as what it is, with no
word about it in its headers, is found by its first bytes and gets a limit of its own. A page is
read to its first 512 KiB, with 1 MiB allowed to arrive to get there. A robots.txt is read to 512
KiB either way, and what was read by then stands. Headers stop at 64 KiB. A body that cannot be
unpacked is an answer and not a fault. Brotli reports one as `InvalidOperationException`, contrary
to its documentation, on Windows and on Linux, and that is turned into the same answer as anybody
else's. A body compressed any way but the two asked for is refused as it is opened. An entity tag
holding anything but ASCII is not kept, because it could not be sent back.

**A sitemap is read with what it can cost held down.** The document type is prohibited and there is
no resolver. A file is read a little at a time and a value a piece at a time, so a value too long to
be an address is skipped without being held. At most 50,000 entries are read from a file, 500 files
from an index and 50,000 pages for a website, and an index inside an index is not followed. What the
XML reader keeps goes not with how long a file is but with what is in it: on 50 MiB files made to
find out, it allocated 3.1 GiB for open elements, 1.4 GiB for attributes and 0.5 GiB for names. So
nesting stops at 32 levels, and a file may use 1,024 names of at most 1,024 characters. A file doing
anything else is not a sitemap. The XML reader's own `MaxCharactersInDocument` is not used: asked
for its data asynchronously it checks that setting against the first buffer and never again, so the
stream is what limits how long a file may be. A file holding a stretch of 1 MiB with no `<` in it,
or a marked-out section running that long, is not a sitemap either, and is refused before the XML
reader has taken it in (`MarkupGuardStream`). The reader holds the whole of a tag before it hands
anything over and goes back over it each time it needs more, so one tag made of spaces, which
compresses to a thousandth of its size, cost 301 MiB and 62 seconds of a processor in a test run; it
costs 2 MiB and 0.4 seconds now.

**A page is read by its tokens, and no document is ever built.** AngleSharp 1.9.1's tokenizer reads
it, and nothing else of AngleSharp runs, because building a document is where the cost was. Parsed
whole, an `<iframe srcdoc>` inside another was parsed as nested documents twice at each level, out
of reach of any deadline, so a page of 2 KB held a reader without end; `<pre>` and `<listing>`
recursed until the stack overflowed, which no catch can stop and which ends the whole engine,
collector included; formatting elements were cloned to between 1.4 and 3 GiB; and each new block
walked back over every element still open. An architecture test forbids the parser, a browsing
context, a configuration and a document anywhere under `Infrastructure.Posts`, and another pins the
tokenizer methods that call themselves. Those recurse on runs of `</>` and `--!`, so a run of more
than 16 has one character replaced before the page is read, which alters a literal run that long in
a title.

The walk keeps a small stack of open elements of its own, at most 4,096, with a count for each name,
so an end tag with nothing of its name open is ignored at once; past that, the deepest stays open. A
tag keeps the 11 attributes the walk reads, the first of each name. U+FFFF, which the tokenizer
takes for the end of the text, becomes U+FFFD. Cancellation is asked at every token. Measured on
Linux with two processors, 512 KiB of the most costly markup tried takes at most about 0.17 seconds
of a processor and 23 MiB, in step with its length from 64 KiB to 2 MiB, and an ordinary page of 150
KiB takes 17 ms. Nesting is read plainly, as tokens and not as a browser's tree: no end tag is
implied and nothing misnested is repaired, an end tag closes the nearest open element of its name,
and inside `svg` and `math` nothing is read as HTML. Across real pages spliced together, at most
0.17% of what was read differs from a full parse.

Characters are decoded as a browser decodes them: a byte-order mark, then the `Content-Type`
charset, then a `<meta>` within the first 1,024 bytes, and with nothing stated UTF-8 where the bytes
are valid and windows-1252 where they are not. A page naming UTF-16 is read as UTF-8, as browsers
do. A title is cut to four times its longest before it is normalised, the most Unicode composes.
Structured data is read from at most ten blocks of at most 128 KiB, nested at most 32 deep, and a
malformed one says nothing. No loader is configured and there is no scripting engine, so nothing a
page points at is fetched and a script is only ever text.

**What counts as a post is what a sitemap and a page say.** A publisher that splits its sitemap has
a file of posts, by names read from each publisher's own code and documentation: WordPress's
`wp-sitemap-posts-post-1.xml`, the `post-sitemap.xml` of Yoast, Rank Math, All in One SEO and
SEOPress, and Ghost's `sitemap-posts.xml`, each numbered as it grows. An index naming one is read
for that alone, which spares the website a request for every other page it has. An index naming none
is read whole, as is a sitemap that is a single list, and what each page says about itself decides.
`site_posts.is_post` is worked out by the database, so no statement that counts posts can get it
wrong: a page saying it is anything but an article is not a post, nor is an address whose server
says what it sent is not a web page, nor a page that has gone, and of the rest a page counts if it
is listed in a file of posts or has said it is an article. A website's front page is never a post
and is not kept.

What a page says it is goes in this order. An `og:type` that is anything but an article makes it
something else, and so does being a list: structured data naming a `CollectionPage`, WordPress
marking the page as a blog, an archive or search results, or, on any other website, several entries
on a page not marked as a single post. Then a schema.org type of the article family makes it an
article, a `Product` makes it something else, and a stated publish date makes it an article. An
`og:type` of `article` with nothing else says too little, because themes print it on lists, so such
a page counts only in a file of posts. A list states no date of its own.

**A title and a date come from what a page states, and nothing is made up.** The title is
`og:title`, then the headline of the first schema.org article in the page's structured data, then
`twitter:title`, then the document's own title. The date is `article:published_time`, then that
article's `datePublished`, then microdata, then microformats, then WordPress's block, below. A date
without an offset is read on the website's calendar, and a date is believed only from 1990 to a day
ahead of now, in the forms of ISO 8601 and a few common near misses. A title has its control and
invisible format characters taken out, the two joiners excepted, its runs of space brought to one,
and is cut to 300 characters at the last whole character with nothing added. A page that says
nothing keeps nothing, which is a correct answer: it is counted if it is in a file of posts, it has
no date, and Settings shows the gap. A sitemap's `lastmod` says when somebody last touched a page,
and is never shown as when it was published.

**WordPress's own markup is read where plain WordPress says nothing else.** Plain WordPress states
no date for machines: no Open Graph tags and no structured data. A block theme prints dates through
WordPress's Post Date block, so `.wp-block-post-date time[datetime]` is read as the last resort. A
date inside `.wp-block-post-template` or `.wp-block-query` is another post's, in a list, and is
never read; of the rest, a page holding exactly one has it read, and the same block's
`__modified-date` never is. Twenty Twenty-Five, the default theme, prints no date on a post, and its
posts are honestly undated; the next default prints a post's own date above a list of others, which
is why lists are left out. WordPress's body classes `blog`, `archive`, `search` and `single` are
read to tell a list from a post. No theme's own markup is read, because that would be a guess for
each theme.

**Each page is read once.** A page is asked for once, and falls due again only when the sitemap's
date for it moves forward. A date that stands still or goes backwards is not a change, so a sitemap
that gives no dates has each page read once and never again. Asked again, a page is asked whether
anything has changed, where it gave something to ask with. The newest come first, at most 120 in one
pass.

Once is a limit on pages, for each time a sitemap is named. The sitemap itself is read from the top
every hour, so that a new post is counted within about the hour while the readers keep up. That too asks whether anything has changed, where the website gave
something to ask with, and being listed again does not make a page due. An index answering that
nothing has changed still has its files asked for, and its own dates are not used to skip one,
because a post can move into a file whose date has not changed.

**One request a second to a website, however many ask.** `Pacer` keeps turns per website for the
whole process, robots.txt and every redirect included, each turn booked ahead. Domains are not
unique across an installation, so two accounts can name one website, and a website answers at both
of a host's names: `PublishedHosts.WebsiteName` gives a host and its twin one name, which the pacer
and the claim both key on. Across engines the claim holds it: a claim is refused while another row
for the same website holds one. That is a question about one row and a change to another, and two of
those side by side would each find the way clear, so the two statements run under
`pg_advisory_xact_lock` on the website's name, released before the pass begins. A claim let go of
stays in the way for one more gap, with no token anybody could write under, because another engine's
pacer knows nothing of this one's last request. A validator refuses a gap under a second. The limit
is each website's and not an engine's: a reader goes straight from one website to the next, so an
engine reading two at a time makes about two requests a second to websites that answer, and more
where passes end at once. One server behind 300 names whose robots.txt answered with a server error
was asked 18 times a second in a test run, each name no more than once a second.

**A claim is a lease, and every write names it.** A pass goes off to somebody's website for minutes
at a time. An advisory lock for that long would hold a transaction and a connection open across all
of it, and a session lock would need a connection of its own for each website and is cleared when
the pool resets it. So a claim is a token and a time on the website's row: it holds nothing,
survives a crash by running out, and names every write. The time is set and compared by the
database's `clock_timestamp()` and never an engine's clock, because a pass ending before its claim
can be taken is the whole of what keeps two passes apart. A pass stops starting new work after ten
minutes and a claim lasts fifteen, and a claim shorter than the budget, a robots.txt and a file
begun at the last moment, their gaps and a minute's margin is refused at start.

A write to the claim's row names the claim in its `WHERE`. A write to any other row asks for the
claim's row first, `FOR KEY SHARE`. A plain `EXISTS` looks at a snapshot and could land just after a
change of address commits. With the key lock, a deletion of that row waits for the write and a write
waits for the deletion and then finds nothing, so a write either finishes before the address changed
or changes nothing. Changing the address, removing the website and purging a closed account all
delete the read row first, which voids any pass on any engine. A write that had to change something
and did not stops the pass. A claim that has run out counts as lost whether or not anybody has taken
it. Removal and the purge delete that row ahead of the website for a second reason: deleting the
website first would reach a write's rows while that write was waiting on the website, and the
database would end one of them.

**An attempt is written before it is made.** Before the address, a file or a page is asked for, a
fenced write uses up the attempt and says when the next falls due. So neither a body that cannot be
read nor an engine dying can bring the same request back half a minute later. A page has three
attempts, an hour apart and then two, and after those waits for its sitemap date to advance. That
date is recorded at each attempt, or a failing page would have its attempts reset every time its
file changed. Each file and each page is handled in a catch of its own: a fault here writes event
1306 with the website's identifier and the name of the exception's type, what was written first
stands, and the reading moves on. An engine stopping in good order gives back the attempt at the
address or the file it had in hand, and lets go of the website as due at once, or when a wait the
website asked for runs out, because a deploy should neither make a sitemap read as failed nor leave
it idle for an hour: the next engine to start carries on where this one stopped. A page's attempt is
not given back, because nobody sees it and the page comes round again.

**A website in trouble is left alone.** Three files or pages in a row turned away, in a server
error, unreachable or with a certificate that cannot be trusted end the attempt: for files, too slow
and too large count as well, the files not yet asked for settle with the same outcome and the
reading closes; for pages, this pass ends. The website is left alone for an hour. A website whose
every file drips out at the last moment cannot keep a reader for itself, and an outage costs a few
pages' attempts rather than every page's. Asked for a file or a page, a 429 or any other answer
short of success that carries `Retry-After` is heard: this pass stops and the row is not claimed
again until the wait is over, at most seven days, because a page should not be able to park itself
for decades by saying so. A 429 with no header waits an hour. A 304 answering a request that carried
nothing to compare is not believed, because whoever asked has nothing it could still be the same as.

**What a reading leaves behind is bounded.** A file is stamped only once all of it has been read,
and a page comes off the list only at the end of a complete reading, where no file's stamp vouches
for it any more. So a file that failed or was cut short can never unlist anything, and a file
answering that nothing has changed goes on vouching for what it listed. A page off the list is
forgotten after a year, or sooner once more than 50,000 are off it, the oldest first.

A sitemap that breaks at the end of the file, listing different pages each time, never completes a
reading and so never takes anything off the list. So a website may hold three times what it may
list, counted in pages and in what their paths and addresses take up: 1 KiB a page on average across
the 50,000 it may list, several times what an ordinary post's take and enough for a path spelling a
title in a script an address writes in nine characters a letter. Past either, a file stops storing
pages and the reading says `too-many`. Every reading that closes then forgets the pages no file
vouches for, those found most recently kept longest, down to twice what a website may list, so a
sitemap put right has room for a whole list at its next reading and the ceiling is never a trap. A
website holds at most about 150 MB of paths and addresses, about 400 MiB of PostgreSQL with its
indexes by the ratio measured on real rows, against about 1.6 GiB before what pages take up was
counted, and a trial account's five websites about 2 GiB. Nothing bounds it across accounts.

**robots.txt is obeyed.** RFC 9309, for the product token `DewirideAnalytics`, or for `*` where no
group names it. The token is compared without regard to case on its leading letters, hyphens and
underscores, so a version after the name does not hide it. Every group naming the reader is read as
one, the longest matching rule decides, and an allow decides a tie. By that rule the RFC's own
example in its section 5.1 permits `/publications/picture.gif`, whatever its prose suggests, and a
test says so. Paths are compared in one escaped form, by a matcher that takes linear time, with at
most 10,000 rules kept. A file with more for the reader permits nothing, because there is no telling
which of those left out would have decided. A rule over 6,144 characters is left out, because it
could not match any address read here. A file cut off at the limit loses its unfinished last line,
because an allow cut short would permit more than was written. `/robots.txt` itself may always be
asked for. The file is read once for each exact host in a pass and never held longer.

A file that is not there, or that the server will not give out, permits everything: the 4xx range,
as the RFC says, except a 429 or any answer asking for a wait. That leaves the file undefined, as a
server error, an unreachable host, a certificate that cannot be trusted or a body that cannot be
unpacked do: nothing is read this pass, a wait asked for is heard before anything else is asked, and
a sitemap stopped that way reports what stopped it, `turned-away` for a 429, and not
`robots-disallowed`, because the website has not asked for anything to be left alone. A robots.txt
that redirects off the website, or through more than five redirects, says `robots-unreadable`. The
RFC advises following a redirect across hosts, and that reason does not apply to something that has
undertaken to stay on the website. A sitemap or a file refused says `robots-disallowed`. A page
refused is settled as turned away, with no attempt spent. `Crawl-delay` is not read: the reader
keeps to one request a second whatever it asks.

**Settings says how it went.** `GET /api/sites/{id}/sitemap` answers any role on the website, and a
missing and a forbidden website alike with 404. It gives the address, a `state` (`none`, `off`,
`waiting`, `read`, `partly-read`, `failed`), a `problem` (one of fifteen words, the first thing that
went wrong), when a reading last finished, when one last got through, how many posts, dated posts
and pages still to read, and how many pages turned the reader away the last time they were asked for
(`turnedAway`). A reading where nothing got through has failed. One where a file failed, was cut
short or listed something off the website is partly read; its address answered, so its problem is
told in sentences about the files the sitemap lists and never about the address in the box. Pages
that turned the reader away are said in place of the advice given about a sitemap listing no posts.
Where reading is switched off the state is `off` whether or not an address is named, so nobody is
asked to add a sitemap that would not be read. The section called Posts turns every word into a
sentence from the message catalogue. It asks every five seconds while the first reading is under way
and every thirty while pages are left, and never where reading is switched off. An owner or an
editor can name, change or remove the address. A viewer sees it as text, never as a link, and the
same line about how it is going. The box refuses only what could never be right, something that is
not a whole web address; whether the address is on this website is for the engine, which knows hosts
the dashboard does not, and refuses with `SiteSitemapRejected`.

**The reader is a timed service like the others.** `SitemapReadingService` starts two readers unless
told otherwise, its first look waits half a minute, and each reader takes whichever website has
waited longest. A sitemap newly named, or named again, takes its place as of that moment, behind
every website that has waited longer: saving an address costs nothing and can be done as often as
anybody likes, and a new one going to the front let one account keep every reader for itself. A
website being read, at either of its names, is left out of what a look tries, so rows naming it
cannot fill the eight tried while others wait.

**Only a website being measured is read.** Before a pass asks anything, it asks the edition's
`IMeasurementAllowance`, which the collector asks about every report. A website whose account is not
being measured, a trial that ended or an account past its allowance and its grace, is left unread
and looked at again an hour later, and reading carries on by itself once measurement does. The free
edition measures everything.

The reader keeps to 0067: whether work was cancelled is asked of the token, a pass that
fails is written down (1304) and that reader waits, and once the host is stopping it says nothing of
what the work in hand ended in. Every wait a timer keeps is refused at start over 49 days.
`Dewiride:Sitemaps:Enabled=false` switches reading off for an installation: the status says `off`,
and what was read stays and is still reported. `Dewiride:Sitemaps:SitesAtOnce=0` leaves one engine
out where several share a control plane, and that engine still says how reading is going.

**What a website writes stays data.** Titles, addresses, paths, entity tags and everything else read
here were written by a stranger. They reach PostgreSQL only as parameters, a batch of pages as one
JSON parameter, and each is fitted before it is written: an entity tag over 256 characters is
dropped and an address over 2,048 is skipped, so that a fenced write cannot fail on a value too
long. They are never logged. What a website could not or would not do is an outcome, written where
its owner can read it, and an exception from parsing can quote the document, so a fault leaves the
name of its type and nothing else. They are rendered only as text, and never placed in a model
prompt as instructions. No regular expression runs over any of it.

**Nothing on any capture surface changes.** The standing rule is that a capability ships everywhere
it belongs, so the accounting is written down rather than left as an omission. This reads a website
and measures no visit, so nothing collected changes, and there is nothing to deliver in the tracker,
the image fallback or server-side reporting. It serves every hosting stack that publishes a
sitemaps.org sitemap, WordPress and static sites on Netlify, Vercel or Cloudflare Pages alike, with
nothing to install. Every surface's reports reach `EventIngestor`, which reduces a page to its path
by the same `PagePath.Of`.

## Considered and refused

- **A setting to read a private network, or a way round the check for development.** Either would be
  a way of pointing the hosted service at its own network. A local run reads a real public sitemap,
  or the tests' own website through `LoopbackEndpoints`, which only test code can reach.
- **Connecting to the public answers of a name that also gives private ones.** Nobody's website
  legitimately answers with both, and a name that does is misconfigured or somebody finding out what
  happens when it tries.
- **Folding the case of percent-escapes in `PagePath`.** It would change what the collector stores
  and split the twelve months of history of every page it touched, at the release that did it. It is
  a decision of its own.
- **A limit on how often an address may be saved.** Four saves a minute held both readers when a new
  address went to the front of the queue, and that is within what a person changing their mind does.
  Placing a new address as of the moment it was named is what holds.
- **Taking `lastmod` for a publish date where a page states none.** It says when somebody last
  touched the page. A post corrected yesterday would appear on a calendar as published yesterday,
  and that is exactly the kind of confident wrong answer the rest of this product refuses to give.

## What it does not do

- **Read anything but a sitemaps.org XML sitemap.** RSS, Atom and plain-text sitemaps, what a news
  or image sitemap adds to an entry, and the WordPress REST API are not read.
- **Read a theme's own markup.** WordPress's own Post Date block and body classes are read, because
  plain WordPress states a post's date and kind no other way.
- **Read `Crawl-delay`.** One request a second is kept whatever robots.txt asks.
- **Reach a private network, or go through a proxy.** A machine whose only way out is a proxy cannot
  read sitemaps, and Settings says the website did not answer.
- **Tell posts apart by a query string,** because the collector cannot.
- **Read again a page whose sitemap gives it no date.**
- **Check whose website it is.** Nothing does, in this product, and this does not begin to.
- **Show posts anywhere but Settings.** That is for the later record.

## Consequences

- **A WordPress website on plain links (`/?p=123`) cannot be matched.** The collector keeps only a
  page's path, so every such post has the path `/`. Entries told apart only by a query are skipped
  and counted, and where nothing else was found Settings says `addresses-with-queries` and what to
  do about it.
- **An escape spelled differently can be another path.** `Uri` upper-cases an escape that decodes to
  valid UTF-8, so WordPress's `/caf%c3%a9` and a browser's `/caf%C3%A9` are one path. An escape of a
  reserved character, or of bytes that are not valid UTF-8 (`%2f`, `%e4`), keeps the case it was
  written in, so a link spelling one differently from the sitemap is another page and its views are
  not credited to the post.
- **A firewall, bot protection or robots.txt can stop reading.** Settings says which (`turned-away`,
  `robots-disallowed`) and names the token to allow, and the README's section on
  [reading a website's sitemap](../../README.md#reading-a-websites-sitemap) says the same to whoever
  finds the reader in their logs.
- **A page refused by robots.txt, or whose three attempts ran out, stays unread once the cause is
  fixed.** It comes round only when its date in the sitemap advances, or when the sitemap is removed
  and added again, which starts again from nothing.
- **Trouble reading pages reaches the status only as pages turned away.** A sitemap read in full
  says `read` however its pages went. Pages that turned the reader away are counted and said; other
  trouble with them shows only as pages still to read and posts with no date.
- **On the hosted service any account can name any website.** Nothing verifies a domain. What that
  buys is bounded by robots.txt, one request a second, the one host, and a User-Agent that says what
  this is and where to read about it. Each page is read once for each naming: removing a sitemap and
  naming it again, or another account naming the same website, starts again from nothing. Several
  names on one server are several websites as far as anything here can tell, and each has its own
  second.
- **A wait a website asks for holds for the row that heard it.** Another account naming the same
  website is still kept to one request a second, and does not inherit the wait.
- **Nothing caps a reading's bytes as a whole.** The worst it can fetch is 500 files of 50 MiB, and
  a website giving nothing to ask with is asked in full each hour.
- **"Too slow" also covers what went wrong here.** A file whose attempt was written and never
  finished, by an engine dying or a fault of this product's, reads as too slow until the next
  reading.
- **Addresses can reach a trace exporter.** The HTTP client's spans carry a website's address,
  escaped and with its query redacted, wherever an exporter is configured. Nothing leaves without
  one.
- **A website reporting from its own server reports the reader too, and it counts towards the
  plan.** It runs no script, so the tracker never sees it, and a server's report of it is judged, as
  a crawler, and counted like any other request's: on the hosted service a first reading of 50,000
  pages is 50,000 reports against the account's allowance. A reporter can leave out user agents
  beginning `DewirideAnalytics/`, as [server reporting](../server-reporting.md) says. The allowance
  does not leave them out by itself, because anybody could write that name.
- **A post reached through a redirect keeps both paths.** The listed path is what the next listing
  finds it by and the path it answered at is what its visits are recorded under. A website serving
  both spellings with no redirect splits its views between them, and only the listed one is the
  post's.
- **A generator that stamps every page with the time it ran has every page read again after each
  run,** at 120 a pass, and pages never read wait behind those.
- **A new post is counted within about the hour while the readers keep up.** Each website ahead of
  it that uses its whole ten-minute budget costs half a budget with two readers, so a dozen slow
  websites in their first reading add about an hour. `SitesAtOnce` is what an operator raises.
- **A website whose account is not being measured stands where it was.** Settings shows its last
  reading, and a sitemap named meanwhile reads as waiting until measurement resumes.
- **A large website's first reading takes a while.** At 120 pages in a pass and half a minute
  between passes, fifty thousand take most of a day, the newest first, and Settings counts the pages
  left.
- **An older engine leaves all of it alone.** Going back past this release leaves the column and the
  tables in place, and it reads and writes none of them. Removing a website or deleting a closed
  account still takes them, because the cascades are in the database. Reading carries on when the
  newer engine returns, any claim having run out.
- **The hosted service says so.** Its privacy policy says what is read and what is kept, and its
  terms say that naming a sitemap lets the service read it and the pages it lists, where they spoke
  before only of the right to install measurement.

## Tests that make this true

The engine:

- `SiteTests`: `Constructor_Leaves_The_Sitemap_Unnamed`,
  `A_Sitemap_On_The_Websites_Own_Address_Is_Kept`,
  `A_Sitemap_On_An_Address_Below_The_Website_Is_Kept`, `A_Sitemap_Elsewhere_Is_Refused`,
  `Only_A_Web_Address_Can_Be_A_Sitemap`, `An_Address_Carrying_Credentials_Is_Refused`,
  `An_Address_On_Another_Port_Is_Refused`, `The_Default_Port_Written_Out_Is_Accepted`,
  `An_Address_Written_As_A_Number_Is_Refused`, `A_Website_Registered_As_A_Number_Publishes_Nothing`,
  `A_Sitemap_Address_Is_Kept_Without_Its_Fragment_And_With_Its_Host_Lower_Cased`,
  `A_Query_String_Is_Kept_On_A_Sitemap`, `An_International_Address_Is_Kept_In_Its_Ascii_Form`,
  `An_Empty_Address_Removes_The_Sitemap`, `An_Address_As_Long_As_Accepted_Is_Kept`,
  `An_Address_Longer_Than_Accepted_Is_Refused`,
  `An_Address_That_Only_Overruns_Once_Escaped_Is_Refused`,
  `An_Address_Too_Long_As_Typed_Is_Refused_However_Little_Of_It_Would_Be_Kept`,
  `An_Address_Is_Measured_Without_The_Space_Around_It`,
  `Nothing_Unprintable_Is_Kept_In_A_Sitemap_Address`,
  `A_Refused_Address_Leaves_The_Sitemap_As_It_Was`, `Allowed_Origins_Decide_Where_A_Sitemap_May_Be`,
  `A_Website_Registered_As_A_Bare_Suffix_Publishes_Nothing`,
  `A_Bare_Suffix_Among_The_Permitted_Hosts_Covers_Nothing_And_Brings_Nothing_Back`,
  `A_Name_With_No_Ascii_Form_Is_Refused_And_Nothing_Is_Thrown`,
  `A_Host_Longer_Than_A_Name_Server_Can_Be_Asked_About_Is_Refused`,
  `What_Cannot_Be_A_Sitemap_Is_Not_Published_Either`,
  `A_Host_That_Is_No_Name_In_Ascii_Cannot_Be_A_Sitemap`,
  `A_Page_On_The_Websites_Own_Hosts_Is_Published`, `An_Address_That_Is_Not_Whole_Is_Not_Published`.
- `PublishedHostsTests`: `A_Host_Is_Published_At_Its_Domain_And_Below_It`,
  `A_Look_Alike_Suffix_Is_Not_The_Website`, `Hosts_Are_Compared_Exactly_As_They_Are_Given`,
  `Allowed_Origins_Replace_The_Domain`, `A_Sitemaps_Own_Host_And_Its_Twin_Are_One_Website`,
  `How_A_Host_Is_Spelled_Does_Not_Make_It_Another_Website`,
  `Anything_Else_A_Sitemap_Leads_To_Is_Another_Website`,
  `A_Bare_Suffix_Is_Not_The_Twin_Of_Anything`, `A_Host_And_Its_Twin_Go_By_One_Name`.
- `PagePathTests`: `A_Page_Is_Known_By_Its_Path_Whichever_Name_It_Was_Reached_At`,
  `What_Follows_The_Path_Is_No_Part_Of_It`, `A_Path_Is_Kept_As_An_Address_Escapes_It`,
  `Text_Escaped_In_Either_Case_Is_One_Path`,
  `An_Escape_Of_Anything_Else_Keeps_The_Case_It_Was_Written_In`.
- `PublicAddressTests`: `Every_Address_Set_Aside_From_The_Public_Internet_Is_Refused`,
  `Addresses_On_The_Public_Internet_Are_Permitted`.
- `PublicInternetEndpointsTests`: `A_Name_Answering_With_Any_Private_Address_Is_Refused_Whole`,
  `A_Name_That_Resolves_To_Nothing_Is_Unreachable`, `Only_The_Default_Ports_Are_Reached`,
  `A_Name_On_The_Public_Internet_Is_Connected_To_By_What_It_Answered`,
  `An_Address_Written_As_A_Number_Is_Judged_As_It_Stands`.
- `NetworkBoundaryTests`: `Only_The_Public_Internet_Is_Reached_From_A_Websites_Pages`,
  `The_Check_Takes_No_Settings`.
- `WebsiteHttpTests`:
  `An_Address_That_Stays_Silent_Has_Its_Share_Of_The_Time_And_The_Next_Is_Tried`,
  `An_Address_That_Refuses_Is_Passed_Over_At_Once`, `The_First_To_Answer_Is_The_Last_Asked`,
  `Every_Address_Staying_Silent_Is_A_Failure_To_Connect`, `Whoever_Stops_Waiting_Stops_All_Of_It`,
  `No_Address_At_All_Is_A_Name_That_Leads_Nowhere`,
  `A_Website_Is_Reached_At_Its_Next_Address_When_The_First_Goes_Nowhere`.
- `WebsiteFetcherTests`: `A_Redirect_Within_The_Website_Is_Followed`,
  `A_Redirect_To_The_Websites_Other_Name_Is_Followed`, `A_Redirect_Off_The_Website_Is_Refused`,
  `An_Address_Off_The_Website_Is_Not_Asked_For_At_All`, `Five_Redirects_Are_Followed`,
  `A_Sixth_Redirect_Is_Refused`, `A_Redirect_Loop_Is_Refused`,
  `A_Redirect_To_Another_Port_Or_An_Address_Literal_Is_Refused`,
  `A_Redirect_That_Leads_Nowhere_Is_A_Fault_At_The_Website`, `Every_Hop_Waits_Its_Turn`,
  `Nothing_Is_Sent_But_The_Product_Name`, `The_Reader_Says_What_It_Is_And_Where_To_Read_About_It`,
  `A_Website_Is_Asked_For_By_Its_Name`, `A_Website_Is_Told_What_Is_Wanted`,
  `An_Answer_Says_What_To_Ask_With_Next_Time`, `A_Website_Is_Asked_Whether_Anything_Has_Changed`,
  `A_Tag_That_Can_Not_Be_Sent_Back_Is_Left_Out`, `A_Tag_Longer_Than_Is_Kept_Is_Not_Kept`,
  `A_Tag_Holding_More_Than_Ascii_Is_Not_Kept`, `What_A_Website_Answered_Is_What_Came_Of_Asking`,
  `A_Success_That_Hands_Over_Nothing_Asked_For_Is_Only_Read_As_A_Robots_Txt`,
  `A_Wait_A_Redirect_Asks_For_Is_Not_Heard`, `A_Redirect_Robots_Txt_Refuses_Is_Not_Followed`,
  `Robots_Txt_Is_Asked_About_Where_Each_Redirect_Leads`,
  `What_Stopped_Robots_Txt_Being_Read_Is_What_A_Redirect_Comes_To`,
  `A_Redirect_Off_The_Website_Is_Refused_Before_Robots_Txt_Is_Asked`,
  `Asking_Robots_Txt_About_A_Redirect_Goes_Under_The_Time_The_Asking_Has`,
  `Whoever_Stops_Waiting_While_Robots_Txt_Is_Asked_Gets_A_Cancellation_And_No_Outcome`,
  `A_Website_That_Hangs_Up_Is_Asked_Once`,
  `A_Request_Hung_Up_On_Over_A_Connection_Already_Open_Is_Sent_Once_More_And_No_More`,
  `A_Website_That_Hung_Up_Is_Asked_For_The_Next_Thing_As_Usual`,
  `A_Website_That_Resets_The_Connection_Is_Asked_Once`,
  `A_Website_Asking_To_Be_Left_Alone_For_A_While_Is_Heard`,
  `A_Website_Asking_To_Be_Left_Alone_Until_A_Time_Is_Heard`, `A_Page_Has_To_Say_It_Is_One`,
  `A_Page_That_Says_Nothing_About_What_It_Is_Is_Given_The_Benefit_Of_The_Doubt`,
  `A_Sitemap_Can_Be_Said_To_Be_Anything`,
  `A_Compressed_Sitemap_Arrives_Unpacked_However_It_Was_Sent`,
  `A_Corrupt_Body_Is_Not_What_Was_Asked_For`, `A_File_That_Is_Not_A_Sitemap_Fails_As_Not_A_Sitemap`,
  `A_Sitemap_Past_The_Limit_Is_Too_Large`,
  `A_Compressed_Sitemap_That_Unpacks_Past_The_Limit_Is_Too_Large`,
  `A_Page_Longer_Than_Is_Read_Is_Read_As_Far_As_That`, `A_Page_Shorter_Than_Is_Read_Is_Read_Whole`,
  `A_Compressed_Page_Is_Read_As_Far_As_Is_Read_However_Far_It_Unpacks`,
  `A_Page_That_Arrives_Past_The_Limit_While_Unpacking_To_Nothing_Is_Too_Large`,
  `Headers_Past_The_Limit_Are_Too_Large`,
  `A_Website_Whose_Encryption_Can_Not_Be_Trusted_Is_Insecure`,
  `A_Certificate_Sends_The_Reader_Nowhere`, `A_Website_Nobody_Is_Listening_At_Is_Unreachable`,
  `A_Name_That_Leads_Into_A_Private_Network_Is_Refused_And_Nothing_Is_Sent`,
  `A_Name_That_Resolves_To_Nothing_Is_Unreachable`,
  `The_Website_Redirecting_To_Its_Twin_Does_Not_Get_Its_Twin_Past_The_Check`,
  `A_Slow_Website_Is_Abandoned_At_The_Timeout`,
  `A_Website_That_Drips_Its_Sitemap_Is_Abandoned_At_The_Timeout`,
  `A_Website_That_Drips_A_Page_Is_Abandoned_At_The_Timeout`,
  `A_Website_That_Stalls_Partway_Through_A_Compressed_Sitemap_Is_Abandoned_At_The_Timeout`,
  `A_Blocking_Read_Nobody_Can_Cancel_Is_Ended_By_The_Deadline`,
  `Whoever_Stops_Waiting_Gets_A_Cancellation_And_No_Outcome`,
  `Whoever_Stops_Waiting_While_The_Body_Is_Being_Read_Gets_No_Outcome_Either`,
  `What_The_Client_Reports_Is_What_Came_Of_Asking`,
  `A_Failure_Reading_A_Body_Is_What_Came_Of_Reading_It`,
  `An_Address_Refused_Inside_What_The_Client_Throws_Is_Still_Refused`,
  `The_Client_The_Engine_Registers_Refuses_This_Machine`,
  `The_Client_The_Engine_Registers_Does_Not_Reach_A_Website_On_This_Machine`,
  `The_Client_Follows_Nothing_By_Itself`,
  `The_Handler_The_Engine_Registers_Does_Nothing_On_Its_Own`, `A_Request_Leaves_Nothing_In_The_Log`,
  `An_Installation_Does_Not_Start_On_A_Reading_Setting_It_Could_Not_Keep`,
  `An_Installation_Does_Not_Start_On_A_Gap_That_Leaves_No_Time_To_Follow_A_Redirect`,
  `An_Installation_Starts_On_A_Gap_That_Leaves_Time_To_Follow_A_Redirect`,
  `A_Name_With_No_Form_A_Name_Server_Could_Be_Asked_For_Is_Not_On_The_Website`,
  `A_Platform_Registered_As_A_Website_Speaks_Only_For_The_Host_Its_Sitemap_Is_On`.
- `CompressedBodyTests`: `A_Gzipped_Sitemap_Is_Recognised_By_Its_First_Bytes`,
  `A_Body_Is_Not_Looked_Inside_Unless_It_Is_Asked_To_Be`,
  `A_Body_Compressed_In_Transit_Is_Expanded`, `A_Body_Compressed_Twice_Is_Expanded_Twice`,
  `A_Body_That_Expands_Past_The_Limit_Is_Refused`,
  `Endless_Empty_Gzip_Members_Are_Refused_By_The_Compressed_Limit`,
  `A_Plain_Body_Past_The_Limit_Is_Refused`, `A_Body_Exactly_At_The_Limit_Is_Not_Past_It`,
  `A_Corrupt_Body_Is_Refused_As_Corrupt`, `A_Corrupt_File_Inside_A_Body_Is_Refused_As_Corrupt`,
  `A_Body_Compressed_With_Anything_Else_Is_Refused`,
  `A_Body_Looked_Inside_That_Is_Compressed_With_Anything_Else_Is_Refused_As_It_Opens`,
  `Disposing_A_Body_Compressed_With_Anything_Else_Disposes_What_It_Was_Opened_From`,
  `A_Very_Short_Body_Comes_Back_Whole`, `A_Body_Read_A_Byte_At_A_Time_Comes_Back_Whole`,
  `A_Read_Goes_Under_The_Deadline_Whatever_It_Was_Asked_Under`,
  `A_Blocking_Read_Past_The_Deadline_Does_Not_Begin`,
  `Disposing_A_Body_Disposes_What_It_Was_Opened_From`.
- `MarkupGuardStreamTests`: `What_Goes_Through_Comes_Out_As_It_Was`,
  `A_Stretch_May_Be_As_Long_As_The_Limit_And_No_Longer`, `A_Stretch_At_The_Very_Beginning_Counts`,
  `Each_Opening_Starts_The_Count_Again`,
  `A_Section_Full_Of_Openings_Is_Held_To_The_Limit_All_The_Same`,
  `A_Section_That_Has_Closed_Is_Not_Counted_Any_Longer`,
  `A_Section_Closes_However_Many_Brackets_Come_Before_It`,
  `A_Section_Does_Not_Close_On_Something_That_Only_Looks_Like_It`,
  `Something_That_Only_Looks_Like_A_Section_Opening_Is_Not_One`,
  `A_Section_Opening_Straight_After_A_False_Start_Is_One`,
  `Charsets_That_Spread_Their_Characters_Out_Are_Looked_Past`, `A_Blocking_Read_Is_Watched_As_Well`,
  `What_It_Reads_From_Stays_Open`.
- `SitemapCostTests`: `A_File_Made_To_Be_Expensive_Costs_What_It_Takes_To_Go_Through_It`.
- `PacerTests`: `A_First_Request_Goes_At_Once`, `A_Second_Request_Waits_Until_A_Second_Has_Passed`,
  `Requests_Waiting_Together_Go_One_At_A_Time`, `A_Request_Long_After_The_Last_Goes_At_Once`,
  `Another_Website_Has_A_Turn_Of_Its_Own`, `A_Website_And_Its_Twin_Share_A_Turn`,
  `Whoever_Stops_Waiting_Stops_Waiting`,
  `Websites_No_Longer_Being_Asked_Are_Forgotten_And_Those_Still_Being_Asked_Are_Not`.
- `SitemapDocumentTests`: `Reads_The_Pages_A_Sitemap_Lists`, `Reads_The_Parts_An_Index_Lists`,
  `A_Document_Type_Declaration_Is_Refused`, `An_External_Entity_Is_Never_Resolved`,
  `An_Entity_Nobody_Declared_Fails_The_File`, `A_Document_Longer_Than_The_Limit_Is_Refused`,
  `No_More_Than_Fifty_Thousand_Entries_Are_Read`,
  `A_File_Built_To_Use_Up_Memory_Is_Refused_Before_It_Does`,
  `A_First_Element_Built_To_Use_Up_Memory_Is_Not_A_Sitemap`,
  `What_Publishers_Add_To_An_Entry_Is_Passed_Over_Without_Complaint`,
  `Exactly_Fifty_Thousand_Entries_Is_Not_More_Than_Is_Read`,
  `An_Entry_With_Nothing_In_It_Counts_Towards_The_Limit`,
  `A_Value_Too_Long_To_Be_An_Address_Is_Skipped_Without_Being_Held`,
  `What_The_Xml_Reader_Would_Hold_Whole_Is_Refused_Before_It_Has`,
  `What_The_Xml_Reader_Would_Hold_Whole_Is_Refused_In_Any_Charset_It_Reads`,
  `A_Value_Longer_Than_Anything_In_A_Sitemap_Stops_The_File_And_What_Came_Before_Stands`,
  `A_File_With_Nothing_Unusual_About_It_But_Its_Length_Is_Read`,
  `The_Longest_Address_Kept_Is_Read_And_Anything_Longer_Is_Not`,
  `Lastmod_In_Every_W3C_Form_Is_Read`, `A_Lastmod_That_Can_Not_Be_Read_Is_The_Same_As_None`,
  `Something_That_Is_Not_A_Sitemap_Reads_As_Not_A_Sitemap`,
  `Entries_Escaped_As_The_Protocol_Requires_Are_Read_Unescaped`,
  `A_Sitemap_Without_A_Namespace_Is_Still_Read`,
  `What_Else_An_Entry_Holds_Is_Not_Read_As_The_Entry`, `An_Entry_With_No_Address_Is_Passed_Over`,
  `A_File_Listing_Nothing_Is_Still_A_Sitemap`,
  `A_File_That_Breaks_Hands_Over_What_Came_Before_The_Break`,
  `A_File_That_Names_Another_Charset_Is_Read_In_It`,
  `Whoever_Stops_Waiting_Stops_It_Between_Entries`.
- `SitemapEntriesTests`: `A_Page_On_The_Website_Is_Kept_By_Its_Path`,
  `An_Address_Is_Kept_In_One_Spelling`, `What_Leads_Off_The_Website_Is_Counted_And_Not_Kept`,
  `Addresses_Told_Apart_By_A_Query_Are_Counted_And_Not_Kept`, `An_Empty_Query_Is_No_Query`,
  `The_Front_Page_Is_Not_Kept_And_Is_Not_Counted_As_Anything`,
  `An_Address_That_Would_Not_Fit_Is_Not_Kept`,
  `A_Host_That_Has_No_Ascii_Form_Leads_Off_The_Website`,
  `An_Index_With_A_File_Of_Posts_Is_Read_For_That_Alone`,
  `An_Index_With_No_File_Of_Posts_Is_Read_Whole`, `A_Part_Keeps_Its_Query_And_Is_Named_Once`,
  `A_Part_Off_The_Website_Is_Noted_And_Not_Kept`, `Being_Cut_Short_Is_About_The_Kind_That_Is_Read`,
  `More_Files_Of_Posts_Than_Are_Read_Are_Cut_Short`.
- `PostsPartTests`: `The_Posts_Part_Of_Each_Publisher_Is_Recognised`,
  `Pages_Categories_And_Other_Post_Types_Are_Not_Posts`.
- `PageMetadataTests`: `The_Open_Graph_Title_Is_Preferred`,
  `The_Structured_Data_Headline_Comes_Next`, `The_Twitter_Title_Comes_After_That`,
  `The_Document_Title_Is_The_Last_Resort`, `The_First_Title_Is_The_Only_One_There_Is`,
  `A_Title_With_Nothing_In_It_Gives_Way_To_The_Next`, `A_Headline_Is_Decoded_Once`,
  `The_Article_Publish_Time_Is_Preferred`, `A_Date_That_Can_Not_Be_Believed_Gives_Way_To_The_Next`,
  `A_Date_With_No_Offset_Is_Read_In_The_Websites_Zone`, `Structured_Data_In_A_Script_Block_Is_Read`,
  `A_Block_That_Names_Its_Charset_Is_Still_A_Block`, `Structured_Data_In_A_Graph_Is_Read`,
  `A_Later_Block_Describing_An_Article_Is_Found`,
  `The_First_Article_Described_Is_The_One_That_Counts`, `No_More_Than_Ten_Blocks_Are_Looked_At`,
  `A_Block_Larger_Than_Is_Read_Is_Not_Read`, `Malformed_Structured_Data_Is_Skipped`,
  `Values_That_Are_Not_Text_Say_Nothing`, `Structured_Data_Nested_Too_Deeply_Is_Skipped`,
  `Microdata_And_Microformat_Dates_Are_Read`,
  `A_Date_Marked_As_Published_Outside_An_Entry_Is_Not_Read`,
  `The_Date_WordPress_Prints_Is_Read_As_The_Last_Resort`,
  `Anything_A_Page_States_For_Machines_Is_Believed_Over_What_WordPress_Prints`,
  `The_Date_A_Post_Was_Modified_Is_Not_The_Date_It_Was_Published`,
  `A_Post_Printing_Both_Its_Dates_Has_The_Date_It_Was_Published_Read`,
  `A_Page_Printing_Several_Post_Dates_Has_None_Read`,
  `A_Page_Whose_Open_Graph_Type_Is_Not_An_Article_Is_Not_A_Post`,
  `A_Page_Saying_Only_That_It_Is_An_Article_Has_Not_Said_Enough`,
  `A_Page_Saying_It_Is_An_Article_And_When_It_Was_Published_Is_One`,
  `A_Page_Whose_Structured_Data_Says_It_Is_An_Article_Is_One_With_No_Date`,
  `A_Page_Saying_Nothing_About_Itself_Keeps_No_Title_And_No_Date`,
  `The_First_Tag_Of_A_Name_Is_The_One_That_Counts`, `A_Tag_Is_Found_However_It_Is_Named`,
  `The_Charset_The_Server_Names_Is_Honoured`,
  `The_Charset_A_Page_Names_Is_Honoured_Where_The_Server_Names_None`,
  `A_Byte_Order_Mark_Is_Believed_Over_The_Server`,
  `A_Page_Is_Read_Whatever_Is_Said_About_Its_Charset`, `What_A_Page_Holds_Stays_What_It_Is`,
  `Nothing_A_Page_Points_At_Is_Fetched`, `A_Page_Nested_However_Deep_Is_Read_On_A_Small_Stack`,
  `A_Run_The_Tokenizer_Would_Call_Itself_On_Is_Read_On_A_Small_Stack`,
  `A_Short_Run_Is_Left_As_It_Is_Written`,
  `A_Character_That_Means_The_End_To_The_Tokenizer_Ends_Nothing`,
  `A_Page_Of_Hostile_Markup_Is_Read_In_Good_Time`,
  `A_Page_Is_Read_However_Many_Tags_Come_Before_What_It_Says`,
  `A_Page_In_A_Wider_Charset_Is_Read_Where_The_Server_Names_It`,
  `A_Page_Cut_Short_Or_Badly_Made_Is_Read_For_What_It_Says`,
  `A_Corrupt_Page_Says_Nothing_And_Does_No_Harm`, `An_Empty_Page_Says_Nothing`,
  `A_Reading_Nobody_Is_Waiting_For_Stops`,
  `A_Collection_Is_Not_An_Article_And_States_No_Date_Of_Its_Own`,
  `An_Article_Holding_A_Gallery_Is_An_Article`, `A_Product_Is_Not_An_Article`,
  `An_Article_About_A_Product_Is_An_Article`,
  `A_Page_Of_Several_Entries_Is_A_List_And_States_No_Date_Of_Its_Own`,
  `A_Page_WordPress_Says_Lists_Posts_Is_A_List_And_States_No_Date_Of_Its_Own`,
  `A_Post_Listing_Others_Keeps_Its_Own_Date`,
  `A_Page_Stating_That_It_Is_An_Article_Is_Not_Taken_For_A_List`,
  `An_Entry_Inside_An_Entry_Is_Not_Another_Entry`, `What_Ipsum_Prints_Is_Read`,
  `What_Twenty_Twenty_Four_Prints_Is_Read`, `What_Twenty_Twenty_Five_Prints_Is_No_Date_At_All`,
  `A_List_Of_One_Post_States_No_Date_Of_Its_Own`,
  `A_Date_In_A_List_Of_Posts_Is_Not_Read_Even_Where_It_Is_The_Only_One`,
  `A_Tag_In_An_Element_Of_Plain_Text_Is_Not_A_Tag`, `Everything_After_Plaintext_Is_Plain_Text`,
  `A_Tag_In_A_Comment_Is_Not_A_Tag`, `A_Tag_In_A_Drawings_Marked_Out_Section_Is_Not_A_Tag`,
  `A_Title_In_A_Drawing_Is_The_Drawings`, `A_Drawing_Closing_Itself_Is_Closed`,
  `A_Tag_Of_The_Page_Marked_As_Closing_Itself_Stays_Open`,
  `What_Is_In_A_Template_Is_No_Part_Of_The_Page`,
  `A_Date_In_An_Elements_Text_Is_Read_However_It_Is_Written`,
  `An_Element_That_Holds_Nothing_Has_No_Date_In_Its_Text`,
  `A_Date_After_An_Entry_Has_Closed_Is_Not_The_Entrys`,
  `Whatever_Follows_The_End_Of_A_Body_Is_Still_In_It`,
  `An_End_Tag_Closes_What_It_Names_And_Nothing_Else`,
  `The_First_Of_An_Attribute_Named_Twice_Is_The_One_That_Counts`,
  `The_Charset_A_Page_States_Is_Believed`, `A_Charset_That_Was_Not_Stated_Is_Not_Believed`,
  `The_Server_Is_Believed_Over_The_Page`, `A_Mark_On_The_Bytes_Says_Which_Way_Round_They_Are`,
  `A_Page_Saying_It_Is_In_UTF_16_Is_Not`, `A_Charset_Stated_Too_Far_Down_Is_Not_Believed`,
  `A_Page_Saying_Nothing_Of_Its_Charset_Is_In_What_Its_Bytes_Can_Be`,
  `The_Charset_The_Server_Names_Is_Found_However_It_Is_Written`.
- `OpenElementsTests`: `Nothing_Is_Open_To_Begin_With`,
  `What_Is_Known_Of_Being_Inside_Is_What_The_Deepest_Says`,
  `An_End_Tag_Closes_The_Nearest_Of_Its_Name_And_Everything_Opened_Since`,
  `An_End_Tag_With_Nothing_Of_Its_Name_Open_Closes_Nothing`,
  `What_Has_Been_Closed_Can_Not_Be_Closed_Again`,
  `Only_So_Many_Are_Held_And_Whatever_Is_Deepest_Then_Stays_Open`.
- `PageCostTests`: `Nothing_Else_Runs_While_These_Measure`,
  `A_Page_Made_To_Be_Expensive_Costs_What_It_Takes_To_Read_It`,
  `What_One_Tag_Holds_Has_A_Limit_Whatever_Is_Written_On_It`,
  `A_Title_Made_To_Be_Expensive_To_Normalise_Costs_Next_To_Nothing`,
  `A_Headline_Made_To_Be_Expensive_To_Normalise_Costs_Next_To_Nothing`,
  `A_Title_In_A_Charset_Of_One_Byte_Marks_Costs_Next_To_Nothing`.
- `PageReadingBoundaryTests`: `No_Document_Is_Ever_Built_From_A_Websites_Page`,
  `The_Tokenizer_Calls_Itself_Only_Where_The_Reader_Knows_It_Does`.
- `PublishedDateTests`: `A_Date_Without_A_Zone_Is_Read_In_The_Websites_Zone`,
  `A_Date_With_An_Offset_Means_The_Moment_It_Says`,
  `A_Time_The_Clocks_Skipped_Is_Read_On_The_Far_Side_Of_The_Skip`,
  `A_Day_The_Calendar_Skipped_Is_Still_Read`,
  `A_Date_Before_1990_Or_Tomorrow_Is_Not_A_Publish_Date`,
  `A_Date_At_Either_End_Of_The_Calendar_Does_No_Harm`, `Anything_Else_Is_Not_A_Date`,
  `A_Value_Longer_Than_Any_Date_Is_Not_Read_At_All`.
- `PostTitleTests`: `Control_And_Direction_Characters_Are_Removed`,
  `The_Joiners_Words_Are_Spelled_With_Are_Kept`, `Runs_Of_Space_Become_One`,
  `A_Title_With_Nothing_Left_In_It_Is_No_Title`,
  `Half_A_Character_Comes_Out_As_The_Replacement_Character`,
  `An_Accent_Written_Separately_Is_Written_As_One_Character`,
  `A_Title_Is_Cut_On_A_Character_Boundary`, `A_Title_Just_Long_Enough_Is_Left_Alone`,
  `A_Title_Too_Long_Is_Cut_To_What_Is_Kept_With_Nothing_Added`, `A_Cut_Leaves_No_Space_On_The_End`,
  `One_Character_Too_Long_To_Keep_Leaves_No_Title`,
  `Text_Is_Cut_Before_It_Is_Normalised_Only_Past_What_A_Title_Can_Hold`,
  `What_Comes_Out_Of_A_Title_Does_Not_Count_Towards_What_Is_Kept`,
  `A_Title_Cut_Short_Leaves_No_Half_Of_A_Character`, `Markup_In_A_Title_Stays_Text`.
- `RobotsRulesTests`: `The_Example_In_The_Specification_Means_What_It_Says`,
  `The_Longest_Rule_That_Matches_Decides`, `The_Order_Rules_Are_Written_In_Does_Not_Matter`,
  `An_Allow_Wins_A_Tie`, `The_Readers_Name_Is_Found_However_It_Is_Written`,
  `Another_Agents_Name_Is_Another_Agents`, `Every_Group_Naming_The_Reader_Is_Read_As_One`,
  `Where_No_Group_Names_The_Reader_The_Groups_For_Everybody_Are_Read`,
  `A_Group_Naming_The_Reader_With_No_Rules_Still_Takes_Precedence`,
  `A_Group_Naming_The_Reader_With_An_Empty_Rule_Permits_Everything`,
  `A_File_With_Nothing_To_Say_To_The_Reader_Permits_Everything`, `Blank_Lines_Do_Not_End_A_Group`,
  `Lines_About_Something_Else_Do_Not_End_A_Group`, `A_Rule_Before_Any_Group_Is_Nobodys`,
  `However_A_File_Is_Laid_Out_Its_Rules_Are_Read`, `The_Comparison_Is_Case_Sensitive`,
  `Wildcards_And_End_Markers_Mean_What_They_Mean`, `Two_Spellings_Of_One_Address_Are_One_Address`,
  `Different_Addresses_Stay_Different`, `A_Percent_Sign_That_Starts_No_Escape_Is_A_Percent_Sign`,
  `The_Query_String_Is_Part_Of_What_A_Rule_Is_Matched_Against`,
  `The_File_Itself_May_Always_Be_Asked_For`,
  `Where_A_File_Was_Cut_Off_Its_Unfinished_Last_Line_Is_Left_Out`,
  `Where_A_File_Was_Cut_Off_At_The_End_Of_A_Line_That_Line_Stands`,
  `More_Rules_Than_Are_Kept_Permit_Nothing`, `As_Many_Rules_As_Are_Kept_Are_Followed`,
  `Rules_For_Somebody_Else_Do_Not_Count_Towards_What_Is_Kept`,
  `A_Rule_Longer_Than_Any_Address_Is_Left_Out`, `A_Pathological_Rule_Does_Not_Take_Long_To_Answer`,
  `Reading_A_File_The_Size_Of_The_Limit_Does_Not_Take_Long`.
- `RobotsPassTests`: `What_The_File_Says_Is_Followed`,
  `The_File_Is_Asked_For_Once_However_Many_Addresses_Are_Asked_About`,
  `Each_Host_Has_A_File_Of_Its_Own`, `A_New_Pass_Reads_The_File_Again`,
  `The_File_Waits_Its_Turn_With_Everything_Else`,
  `A_File_The_Server_Says_Is_Unavailable_Permits_Everything`,
  `A_Server_Saying_It_Is_Being_Asked_Too_Often_Permits_Nothing_This_Pass`,
  `A_Wait_Asked_For_With_The_File_Comes_With_The_Verdict`,
  `A_File_Is_Followed_Whichever_Success_It_Arrives_As`,
  `A_Success_With_Nothing_In_It_Permits_Everything`,
  `A_Server_In_Trouble_Permits_Nothing_This_Pass`,
  `A_Server_That_Can_Not_Be_Reached_Permits_Nothing_This_Pass`,
  `Once_It_Could_Not_Be_Read_It_Is_Not_Asked_For_Again_This_Pass`,
  `A_Redirect_Off_The_Website_Permits_Nothing_This_Pass`,
  `A_Redirect_That_Never_Lands_Permits_Nothing_This_Pass`,
  `A_Redirect_Of_The_File_Is_Not_Itself_Asked_About`,
  `A_Redirect_To_The_Websites_Other_Name_Reaches_Its_File`,
  `A_File_That_Can_Not_Be_Unpacked_Permits_Nothing_This_Pass`,
  `An_Address_Off_The_Website_Is_Not_Asked_About`, `A_Compressed_File_Is_Unpacked`,
  `The_File_Is_Read_As_Far_As_The_Protocol_Requires_And_No_Further`,
  `A_File_That_Unpacks_Past_The_Limit_Is_Read_As_Far_As_The_Limit`,
  `A_File_That_Arrives_Past_The_Limit_Is_Read_As_Far_As_It_Got`.
- `SitemapReaderTests`: `Reading_A_Sitemap_Stores_Its_Posts`,
  `What_A_Reading_Found_Is_What_The_Status_Reports`,
  `A_Pass_Lets_Go_Of_The_Website_And_Says_When_It_Next_Has_Anything_To_Do`,
  `Only_The_Posts_Part_Of_An_Index_Is_Read`, `An_Index_With_No_Posts_Part_Is_Read_Whole`,
  `What_WordPress_Itself_Writes_Is_Read`, `What_Yoast_Writes_Is_Read`, `What_Hugo_Writes_Is_Read`,
  `What_Hugo_Says_Of_An_About_Page_Does_Not_Make_It_A_Post`,
  `A_Page_Saying_Only_That_It_Is_An_Article_Counts_In_A_File_Of_Posts`,
  `The_Page_Yoast_Lists_First_Is_Not_A_Post`, `A_Website_Under_Yoast_With_No_Posts_Has_None`,
  `A_Shops_Products_Are_Not_Posts`, `A_List_Of_Posts_Read_Beside_Them_Adds_Nothing_To_Them`,
  `A_Posts_Own_Date_Is_Read_Above_A_List_Of_Others`,
  `The_Posts_Of_A_Theme_Printing_No_Date_Are_Counted_And_Not_Dated`,
  `A_Page_Made_To_Bring_Down_The_Reader_Is_A_Page_Saying_Nothing`,
  `A_Page_Is_Read_Once_And_Again_Only_When_Its_Lastmod_Changes`,
  `A_Page_A_Sitemap_Gives_No_Date_For_Is_Read_Once`,
  `An_Unchanged_Page_Is_Answered_From_Its_Validators`,
  `A_Sitemap_That_Has_Not_Changed_Vouches_For_What_It_Listed`,
  `An_Index_That_Has_Not_Changed_Still_Has_Its_Parts_Asked_For`,
  `A_List_Not_Read_To_Its_End_Is_Asked_For_In_Full_Next_Time`,
  `A_Post_No_Longer_Listed_Stops_Counting`, `A_Read_That_Did_Not_Finish_Unlists_Nothing`,
  `A_Post_Moved_Between_Parts_Stays_Listed`, `A_Post_In_A_Part_No_Longer_Named_Stops_Counting`,
  `A_Page_Off_The_List_For_A_Year_Is_Forgotten`, `Pages_Are_Read_At_Most_One_A_Second`,
  `A_Page_Never_Fetches_Anything_It_Links_To`, `Two_Engines_Never_Read_One_Website_At_Once`,
  `Engines_Looking_At_The_Same_Moment_Do_Not_Both_Get_A_Website`,
  `A_Lease_Left_By_A_Stopped_Engine_Expires`,
  `A_Claim_Runs_Out_By_The_Databases_Clock_And_Not_An_Engines`,
  `Writes_From_A_Pass_Whose_Claim_Was_Lost_Change_Nothing`,
  `Writes_From_A_Pass_That_Holds_Its_Claim_Are_Written`,
  `A_Write_Arriving_During_A_Change_Of_Address_Waits_And_Changes_Nothing`,
  `Changing_The_Address_During_A_Pass_Voids_It`, `Removing_A_Website_During_A_Pass_Voids_It`,
  `Changing_The_Address_Takes_Its_Turn`, `Clearing_The_Address_Forgets_The_Posts`,
  `A_Website_Whose_Account_Is_Not_Being_Measured_Is_Not_Read`,
  `A_Closed_Accounts_Websites_Are_Not_Read`, `Two_Websites_On_One_Host_Are_Never_Read_At_Once`,
  `Another_Engine_Waits_Out_The_Gap_After_A_Pass_Lets_Go`,
  `A_Claim_Let_Go_Of_Cannot_Be_Written_Under`,
  `Claims_On_Different_Rows_Of_One_Website_At_The_Same_Moment_Go_To_One_Reader`,
  `A_Website_Named_Many_Times_Does_Not_Hold_Up_The_Next_Website`,
  `A_Corrupt_Part_Is_Written_Down_And_The_Rest_Is_Read`,
  `A_Corrupt_Address_Fails_The_Reading_And_Is_Not_Asked_For_Again_Until_The_Next`,
  `A_Corrupt_Page_Uses_Up_An_Attempt_And_Comes_Round_Again_Later`,
  `An_Engine_Dying_With_A_Request_In_Hand_Has_Used_Up_The_Attempt`,
  `A_Fault_On_A_Part_Is_Written_Down_And_The_Next_Part_Is_Read`,
  `A_Fault_On_A_Page_Is_Written_Down_And_The_Next_Page_Is_Read`,
  `A_Fault_On_The_Address_Fails_The_Reading_And_Comes_Round_Later`,
  `Nothing_A_Website_Wrote_Reaches_The_Log`,
  `A_Website_That_Drips_Its_Sitemap_Is_Abandoned_At_The_Timeout`,
  `A_Website_That_Drips_A_Page_Is_Abandoned_At_The_Timeout`,
  `A_Sitemap_Robots_Txt_Disallows_Is_Not_Asked_For`,
  `A_Part_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So`,
  `A_Page_Robots_Txt_Disallows_Is_Turned_Away_And_Not_Tried_Again`,
  `Nothing_Is_Asked_For_While_Robots_Txt_Is_In_Trouble`, `Robots_Txt_Is_Read_Once_In_A_Pass`,
  `A_Page_Redirecting_To_Where_Robots_Txt_Disallows_Is_Turned_Away`,
  `Where_A_Redirected_Page_Landed_Is_Kept_Beside_The_Path_Listed`,
  `A_Page_Redirecting_To_Where_Robots_Txt_Permits_Is_Read`,
  `A_Page_Redirecting_To_The_Websites_Other_Name_Is_Held_To_What_That_Name_Says`,
  `A_File_Redirecting_To_Where_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So`,
  `An_Address_Redirecting_To_Where_Robots_Txt_Disallows_Is_Not_Asked_For_And_Says_So`,
  `An_Address_Redirecting_To_The_Websites_Other_Name_Is_Held_To_What_That_Name_Says`,
  `A_Page_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Costs_No_Attempt`,
  `An_Address_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Says_What_The_Trouble_Is`,
  `A_File_Redirecting_To_A_Name_Whose_Robots_Txt_Is_In_Trouble_Says_What_The_Trouble_Is`,
  `A_Robots_Txt_That_Can_Not_Be_Followed_Is_What_The_Reading_Says_Stopped_It`,
  `A_File_On_A_Name_Whose_Robots_Txt_Can_Not_Be_Followed_Says_That_Stopped_It`,
  `A_Robots_Txt_In_Any_Other_Trouble_Says_What_The_Trouble_Is`,
  `Nothing_Is_Read_While_Robots_Txt_Says_It_Is_Being_Asked_Too_Often`,
  `Robots_Txt_Saying_It_Is_Being_Asked_Too_Often_Leaves_The_Website_Alone_For_The_Usual_Time`,
  `A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_The_Address_Is_Asked_For`,
  `A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_A_Page_Is_Asked_For`,
  `A_Wait_Asked_For_On_Robots_Txt_Is_Heard_Before_A_File_Is_Asked_For`,
  `A_Challenge_Answered_As_A_Success_Is_Not_The_Page_And_The_Page_Is_Read_Once_It_Stops`,
  `A_Website_Answering_Every_Page_With_A_Challenge_Costs_A_Few_Attempts_And_No_More`,
  `A_File_Answering_With_A_Challenge_Is_Turned_Away_And_Not_Taken_For_Something_Else`,
  `A_Sitemap_Whose_Tags_Hold_More_Than_Ascii_Goes_On_Being_Read`,
  `A_File_With_A_Tag_Longer_Than_Anything_In_A_Sitemap_Is_Not_A_Sitemap`,
  `A_Page_Compressed_With_Something_That_Is_Not_Read_Has_Not_Said_It_Is_Not_A_Post`,
  `What_A_Sitemap_Lists_Off_Its_Own_Host_Is_Skipped_And_Counted`,
  `A_Part_An_Index_Names_Off_Its_Own_Host_Is_Skipped_And_Counted`,
  `An_Index_Leading_Only_Off_The_Website_Fails_And_Unlists_Nothing`,
  `A_Sitemap_On_A_Host_The_Website_No_Longer_Permits_Is_Not_Asked_For`,
  `A_Sitemap_On_A_Host_The_Website_Permits_Is_Read`,
  `A_Sitemap_Listing_Only_Other_Hosts_Fails_And_Unlists_Nothing`,
  `An_Index_Inside_An_Index_Is_Not_Followed`,
  `Addresses_Told_Apart_Only_By_A_Query_Are_Skipped_And_Say_Why`,
  `Addresses_With_A_Query_Beside_Readable_Ones_Are_Skipped_In_Silence`,
  `An_Address_Listed_Twice_Is_One_Page`, `More_Parts_Than_Are_Read_Say_So_And_Unlist_Nothing`,
  `More_Pages_Than_Are_Kept_Say_So`,
  `A_Sitemap_Listing_Different_Pages_Every_Time_Cannot_Fill_The_Database`,
  `A_Sitemap_That_Never_Finishes_Cannot_Fill_The_Database`,
  `Pages_With_The_Longest_Addresses_Are_Held_To_What_They_Take_Up`,
  `The_Newest_Pages_Are_Read_First_And_No_More_Than_A_Pass_Allows`,
  `A_Reading_That_Runs_Out_Of_Budget_Carries_On_In_The_Next_Pass`,
  `A_Website_Asking_To_Be_Left_Alone_Is`,
  `A_Website_Asking_To_Be_Left_Alone_Is_Heard_Within_Reason`,
  `A_Website_In_Trouble_Costs_A_Few_Attempts_And_No_More`,
  `A_Website_In_Trouble_Is_Given_Up_On_For_This_Reading`,
  `Parts_That_Are_Not_In_Trouble_Are_All_Asked_For`,
  `One_Page_In_Trouble_Does_Not_Hold_Up_The_Rest`, `What_A_Page_Answers_Is_What_It_Comes_To`,
  `What_Says_It_Is_Not_A_Page_Is_Not_A_Post`,
  `What_The_Address_Answers_Is_What_The_Reading_Comes_To`,
  `A_Reading_That_Fails_Leaves_Everything_Already_Known`,
  `An_Engine_Stopping_Gives_Back_What_It_Had_In_Hand`, `The_Engine_Has_A_Reader`,
  `An_Installation_Does_Not_Start_On_A_Setting_That_Could_Not_Work`,
  `An_Installation_Reads_Under_These_Limits_As_It_Ships`,
  `An_Installation_Does_Not_Start_On_A_Budget_Its_Claim_Would_Not_Outlast`,
  `A_Row_Describing_An_Address_Nobody_Names_Any_More_Is_Cleared`,
  `A_Part_Larger_Than_One_Write_Is_Stored_In_Full`, `What_A_Website_Writes_Goes_In_As_Data`,
  `A_Fault_Reading_Robots_Txt_Costs_No_Attempts_And_Soon_Stops`,
  `A_Website_Asking_To_Be_Left_Alone_Partway_Through_Its_Parts_Is`,
  `A_Sitemap_Asking_For_Longer_Than_Usual_Is_Given_It`,
  `A_Sitemap_Whose_Name_Leads_Somewhere_Private_Says_So`, `A_Website_That_Does_Not_Answer_Says_So`,
  `What_Is_Not_A_Sitemap_Says_So`, `A_Sitemap_Larger_Than_Is_Read_Says_So`,
  `A_Sitemap_That_Redirects_Off_The_Website_Says_So`,
  `A_Sitemap_That_Redirects_To_The_Websites_Other_Name_Is_Read`,
  `A_Page_That_Redirects_Where_It_Should_Not_Is_Not_Asked_For_Again`,
  `A_Page_Left_Behind_On_Another_Host_Is_Not_Asked_For`,
  `A_Website_Saying_Nothing_Has_Changed_When_Nobody_Asked_Is_Not_Believed`,
  `An_Address_Unchanged_From_Something_Nobody_Has_A_Record_Of_Fails_The_Reading`.
- `SitemapVerdictTests`: `Every_Part_Read_Is_A_Complete_Reading_With_Nothing_To_Report`,
  `A_Sitemap_With_No_Parts_Has_Been_Read_In_Full`,
  `A_Part_That_Failed_Makes_The_Reading_Partial_And_Unable_To_Unlist`,
  `Nothing_Getting_Through_Is_A_Failure`, `The_First_Thing_That_Went_Wrong_Is_The_One_Reported`,
  `A_Part_Cut_Short_Is_Partial_And_Unable_To_Unlist_But_Did_Get_Through`,
  `A_List_Of_Parts_Cut_Short_Cannot_Unlist`,
  `Entries_Off_The_Website_Are_Reported_And_Do_Not_Stop_It_Being_Complete`,
  `Nothing_But_Entries_Off_The_Website_Is_A_Failure_And_Cannot_Unlist`,
  `Parts_Named_Off_The_Website_Are_Reported_And_Do_Not_Stop_It_Being_Complete`,
  `Addresses_With_Queries_Are_Only_A_Problem_When_Nothing_Else_Was_Found`,
  `A_Part_Was_Read_Only_If_All_Of_It_Is_Known`, `Everything_That_Can_Stop_A_Part_Goes_By_One_Name`,
  `Every_Way_Of_Failing_Has_A_Word`, `Only_Trouble_At_The_Website_Keeps_The_Reader_Away`.
- `SitemapReadingServiceTests`: `A_Pass_That_Fails_Does_Not_Stop_The_Engine`,
  `Shutting_Down_Part_Way_Through_A_Pass_Is_Not_A_Failure`, `The_First_Look_Waits`,
  `Every_Reader_Runs_And_Fails_On_Its_Own`, `Nothing_Is_Read_While_Switched_Off`,
  `An_Engine_With_No_Readers_Leaves_It_To_The_Others`.
- `SiteSettingsTests`: `An_Editor_Can_Name_The_Sitemap`, `A_New_Website_Has_No_Sitemap`,
  `A_Sitemap_Elsewhere_Is_Refused_By_Code`,
  `A_Sitemap_On_A_Name_With_No_Ascii_Form_Is_Refused_By_Code`,
  `An_Empty_Sitemap_Address_Stops_It_Being_Read`,
  `Somebody_Who_May_Only_Read_Cannot_Name_A_Sitemap`,
  `A_Change_That_Does_Not_Name_The_Sitemap_Leaves_It_As_It_Was`,
  `A_Change_Naming_A_Sitemap_Applies_Everything_Else_It_Names`,
  `A_Refused_Sitemap_Leaves_Every_Setting_As_It_Was`,
  `Changing_The_Sitemap_Forgets_How_The_Old_One_Was_Read_And_Keeps_The_Posts`,
  `Clearing_The_Sitemap_Forgets_Everything_Read_From_It`,
  `A_Change_That_Leaves_The_Sitemap_Alone_Leaves_Its_Reading_Alone`,
  `A_Change_Of_Sitemap_That_Cannot_Be_Saved_Forgets_Nothing`.
- `SitemapStatusTests`: `A_Website_Without_A_Sitemap_Says_None`, `A_New_Sitemap_Is_Waiting`,
  `Any_Role_May_Read_How_Reading_Is_Going`, `Another_Accounts_Website_Is_Not_Found`,
  `Nobody_Signed_In_Cannot_Read_How_Reading_Is_Going`, `Reading_Switched_Off_Says_So`,
  `A_Website_Without_A_Sitemap_Says_Off_With_Reading_Switched_Off`,
  `An_Installation_Does_Not_Start_On_A_Switch_That_Is_Neither_On_Nor_Off`,
  `What_A_Reading_Came_To_Is_Reported`, `The_Posts_Counted_Are_The_Ones_This_Website_Lists_Now`,
  `A_Reading_Of_Another_Address_Says_Nothing_About_This_One`,
  `The_Answer_Is_Written_Under_The_Names_The_Dashboard_Reads`.
- `ReportedNamesTests`: `Every_Sitemap_State_And_Problem_Has_A_Wire_Name`,
  `A_Sitemap_State_Goes_By_The_Word_The_Dashboard_Reads`,
  `A_Sitemap_Problem_Goes_By_The_Word_The_Dashboard_Reads`.
- `SchemaTests`: `What_Was_Read_From_A_Sitemap_Is_Kept_In_Tables_Of_Its_Own`,
  `A_Reading_Is_Kept_Under_The_Columns_The_Reader_Names`,
  `A_Reading_Is_Found_By_These_Indexes_And_No_Others`,
  `Removing_A_Website_Removes_What_Was_Read_From_Its_Sitemap`,
  `A_Post_Outlives_The_Sitemap_Part_It_Was_Listed_In`,
  `A_Page_Is_A_Post_By_What_It_Says_And_Where_It_Is_Listed`,
  `The_Longest_Address_Fits_Everywhere_It_Is_Kept`,
  `Every_Word_A_Reading_Stores_Fits_The_Column_It_Is_Stored_In`,
  `The_Words_A_Reading_Is_Stored_Under_Stay_As_They_Were_Written`,
  `A_Website_Written_Before_Sitemaps_Has_None`.
- `RemoveSiteTests`: `Everything_The_Control_Plane_Held_About_A_Website_Goes_With_It`,
  `Removing_A_Website_Waits_For_A_Write_From_A_Reading_Of_Its_Sitemap`.
- `ClosedAccountPurgeTests`:
  `A_Closed_Account_Whose_Time_Has_Run_Is_Deleted_With_Everything_The_Control_Plane_Held`.
- `CancellationTests`: `No_Failure_Is_Let_Through_For_Being_A_Cancellation`.
- `JudgingTests`: `The_Reader_Of_A_Sitemap_Is_Judged_A_Crawler_However_Many_Pages_It_Read`.

The dashboard:

- `site-settings.test.tsx`: "sends the sitemap address only when it changed",
  "takes no notice of space pasted round an address",
  "takes a box left holding only space as emptied",
  "sends nothing when Enter is pressed on the address already in force",
  "removes the sitemap when the box is emptied",
  "offers nothing to remove while no sitemap is named", "says the change went through",
  "stops saying so as soon as something else is being typed",
  "settles on the spelling the engine keeps, even of the address already in force",
  "saves apart from the name above it, and leaves each half-typed for the other",
  "keeps a half-typed address when the recording switch is used beside it",
  "asks for an address the way a phone keyboard can help with, written left to right",
  "stops %s here, saying what to do, and sends nothing",
  "takes the refusal back as soon as the address is being put right",
  "holds the address still while it is being saved",
  "sits under the switch and above the way to remove the website",
  "a refusal from the engine is shown with its catalogue sentence",
  "takes the engine’s refusal back as soon as the address is being put right",
  "says a save did not go through, and leaves the address alone, when it was not refused",
  "proves where the change came from", "says what came of reading the sitemap in every state",
  "never shows the words the engine answers in",
  "never says a reading finishes in the future, on a clock behind the engine’s",
  "asks nothing about it until the panel is opened",
  "announces itself politely, from one place that stays there while it changes",
  "asks again after a save that changed the address, and not after any other",
  "takes the verdict on the old address away while the new one is being asked about",
  "keeps what it knows when asking again fails, and recovers on its own",
  "says so when it cannot find out, and leaves the address free to change",
  "keeps asking while the first reading is under way and stops when it ends",
  "asks twice a minute while the pages are being read, and stops when they are done",
  "asks once more when somebody comes back to the tab", "asks afresh each time the panel is opened",
  "stops asking once the panel is closed", "a viewer sees no input and no button",
  "shows the address as text and never as a link",
  "says who can add one, rather than inviting somebody who cannot",
  "is still told how it is going, and it is still asked again", "lets the %s change it".
- `sitemap.test.ts`: "does not ask again until there has been a first answer to go on",
  "asks every five seconds while the sitemap itself is being read",
  "asks at that pace while it is read, whatever is already listed",
  "asks twice a minute while pages are left to read on a sitemap %s",
  "stops asking once no page is left on a sitemap %s",
  "stops asking on an installation that does not read sitemaps",
  "has nothing to ask where no sitemap is named", "leaves %s for the engine", "stops %s".
- `endpoints.test.ts`: "asks about the website alone",
  "reads a website nobody has named a sitemap for",
  "reads a reading that got partway, to the fraction of a second",
  "refuses a word it has no sentence for".
- `vocabulary.parity.test.ts`: "says in words what it means for a reading to stand at %s",
  "says in words what to do about %s",
  "says in words what to do about %s in a file the sitemap lists",
  "writes no sentence for anything the engine cannot report",
  "stops an address in the box exactly where the engine stops keeping one",
  "knows the refusal of an address by the engine’s word for it, and has words for it".

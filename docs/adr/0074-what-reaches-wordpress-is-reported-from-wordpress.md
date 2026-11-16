# 0074 — What reaches WordPress is reported from WordPress

- **Status**: accepted
- **Date**: 2026-11-15
- **Applies to**: both editions, and the whole of it is free: the WordPress plugin
  (`integrations/wordpress-plugin`, version 1.1.0) and the dashboard's WordPress steps. No engine
  change: the plugin reports to the server collector (`docs/server-reporting.md`) and reads names
  the way ADR 0073 made safe.

## Context

ADR 0072 put the browser's half of the measurement on a WordPress website. The traffic this product
exists to tell apart never runs it: a crawler fetches the markup and stops, an AI fetcher does the
same, a feed reader asks for the feed, a scanner probes `/.env`. Only code in the request path sees
them, and on WordPress that is a plugin.

What shaped the server's half:

- **WordPress has no "after the response" hook.** `'blocking' => false` still waits at least a
  second on cURL and silently drops a slow TLS handshake (`wp-includes/cron.php` says so). The only
  way to send something once the visitor has their page is what `wp-cron.php` does —
  `fastcgi_finish_request()` or `litespeed_finish_request()`, then a blocking request. Without
  either, as under Apache's `mod_php`, a request sent at `shutdown` holds the page open until it
  ends, for every crawler the site serves, search engines included, whose crawl rate falls when
  pages slow.
- **The two halves need a name to meet.** The visitor key is derived from the connection, and the
  page and the collector need not see the same one — IPv6 to one and IPv4 to the other, a carrier's
  shared addresses, Private Relay. Without a name each person counts twice, and the server's half is
  a visit that never ran a script. ADR 0073 made the engine join two keys only when a name can mean
  one person; names must reach only an engine that reads them that way.
- **The address is most of a verdict, and headers are the visitor's.** Behind Cloudflare or any
  proxy, the connection is the proxy's; the visitor's address is in a header anybody can write.
- **Some of what reaches WordPress is the site itself** — Site Health fetching the home page, a
  cache warming its copies, this plugin checking its tag — and some is people the browser's half
  already leaves out: everybody signed in, the dashboard, prefetches, a page's own missing images.

## Decision

**What is reported.** A request WordPress answered is reported when it is one of:

- a page — a `GET` or `HEAD` WordPress parsed, not the favicon, a preview, the Customizer, an embed
  or an AMP copy (unless the site refused it, below), for a visitor the browser's half would count
  (ADR 0072: signed-in people only when their role is ticked);
- a feed, robots.txt or a sitemap — WordPress's own or an SEO plugin's, which share the `sitemap`
  query variable — while that is chosen (on to begin with);
- a request to the sign-in page, while sign-in attempts are chosen (off to begin with): not from a
  browser navigating there (`Sec-Fetch-Dest: document`), not from anybody signed in, not signing out
  (`did_action( 'wp_logout' )`, since nobody is signed in by the time it is reported), and a `POST`
  only when WordPress announced nobody signing in (`did_action( 'wp_login' ) === 0`; nothing posted
  is read);
- a request to XML-RPC, with the same switch, from anybody not signed in.

A request is recognised by WordPress having parsed it (`did_action( 'parse_request' )`), not by its
having set itself up for the page: a feed answered `304 Not Modified` ends before then, and Yoast
SEO and Rank Math serve their sitemaps while the query is still running.

A request refused before WordPress parsed it — a security plugin's firewall answering while plugins
load, as Wordfence does, or at `init` — is a page, or a sign-in on `wp-login.php` (`$pagenow`),
unless its address is the REST API's own: the prefix, or `?rest_route=` on the site's root. Such a
request can end before WordPress has loaded its pluggable functions, which tell who is signed in and
open the key; the report loads them, each only where no plugin defined it first. It can also end
before WordPress has slashed `$_SERVER` (`wp_magic_quotes()`, which `sanitize_comment_cookies`
follows), and its headers are then read as they came.

Never the dashboard, its AJAX and `admin-post.php`, scheduled events, the REST API at its own
addresses, or WP-CLI.

**What a visitor's headers can leave out, and when.** Below 400, a request is left out when it is a
`HEAD`, a page asked for with any method but `GET` and `HEAD`, or a feed while feeds are not chosen;
when it says it is a prefetch or prerender (`Sec-Purpose`, or `prefetch`/`preview` in `Purpose`,
`X-Purpose` or `X-Moz`), names a destination other than a document in `Sec-Fetch-Dest`, or calls
itself this plugin, the installation's sitemap reader (`DewirideAnalytics/`), WP Rocket's cache
warming (`WP Rocket/`) or WordPress fetching this site (`WordPress/…; <home_url>`); and when it is a
browser navigation answered with a redirect, whose landing page is reported instead — one naming a
document in `Sec-Fetch-Dest`, or, from a browser that sends no fetch metadata, as every browser does
to a site served over http, one naming no destination and asking to be upgraded
(`Upgrade-Insecure-Requests: 1`). From 400 up none of that holds, since a scanner could hide every
probe behind a header, a method or a query: a page or a feed the site refused is reported whatever
its method, whatever the feeds switch says, and whatever its query asked it to be shown as — a
preview (`?preview=`), an embed (`?embed=`, which WordPress keeps on a 404), an AMP copy — and so is
a REST query (`?rest_route=`) added to an address that is not the REST API's own, which WordPress
answers from the REST API all the same. Two things leave a refusal out: who is signed in, which is
the site's knowledge rather than the visitor's say; and a request for a page's own file — an image,
a font, a stylesheet, a script, a source map, an icon, a web manifest — that names a destination
other than a document, as a broken page rather than a probe.

**What is the site's own, whatever it answered.** A visitor whose address is this computer, the web
server's own (`SERVER_ADDR`), or one the collector has seen the reports arrive from (ADR 0073's
`from`) — up to eight are kept, newest first, the first learnt at once and each further one at most
every 15 minutes, so a server sending through a pool of addresses does not rewrite the state on
every report; and a request carrying the plugin's own header, which the tag check sends with each
address it fetches and nobody without the site's secret keys can write: an HMAC-SHA256, keyed with
`wp_salt( 'nonce' )`, of the ten-minute period, the site's host and the path asked for. The current
period and the one before are accepted, for that page of that site only: a value seen on its way —
the sites of a network share their secret keys — leaves out nothing else.

**What a report holds.** `kind: pageview`; `url` — `home_url()`'s scheme, host and port and the path
asked for, without query or fragment, every character a path may not hold percent-escaped — square
brackets kept, as browsers send them, so that both halves name one path — and a stray `%` written as
`%25`, shortened past 2,048 characters from the middle, keeping the start and the last segment;
`referrer` rebuilt from its scheme, host, port and path, so without a user name, password, query or
fragment, only when it is an `http(s)` address; `ipAddress`; `userAgent` to 1,024 characters; the
three low-entropy client hints as sent, to 256; `statusCode` and `contentType` as the response
carried them; the first `Accept-Language` tag when it is one; `observedAt` from
`REQUEST_TIME_FLOAT`, on 64-bit PHP only, where milliseconds fit an integer; and the page's name
when it was given one. Empty fields are left out.

**When it is sent.** At `shutdown`, priority `PHP_INT_MAX`, after every output buffer has been
flushed: the response is finished with `fastcgi_finish_request()` or `litespeed_finish_request()`,
any PHP session closed, where PHP has sessions, so the visitor's next request is not kept waiting,
and the batch of one posted — blocking, `redirection` 0, `sslverify`, two seconds, connecting
included: without cURL, Requests' socket transport would otherwise allow ten seconds to connect
alone, so this request's `connect_timeout` is set to the same two through
`requests-requests.before_request`. While reports are being sent, the request is told to survive the
visitor leaving at the very start of `shutdown` (`PHP_INT_MIN`): WordPress and cache plugins write
out the page they held back at priority 1, and a visitor who hung up before then — a crawler that
gave up on a slow page — would otherwise end the request there, before its report. A report usually
takes a tenth of the two seconds; every second it may take is a second a PHP worker can be kept from
the next page when the installation stops answering, which is why it is given no longer. Where
neither finishing function exists, reporting is off unless a second switch accepts that every
reported page — readers' as well as robots' — is held open for its report. Reports are never queued:
a report sent late says a page was slower to arrive than it was, and a page's timing is part of how
a crawler is told apart.

The key goes only over https, or over http to a host that is this computer or a private network —
named so (`localhost`, `.local`, `.internal`, `.home.arpa`, `.test`), a private IPv4 literal, or a
name that resolves only to private addresses (a container's name beside the installation). A filter,
`dewiride_analytics_server_address`, sends the server's reports to another address than the
browsers' where WordPress reaches the installation differently. The name lookup guards against a
mistaken address, not against anybody: such a name comes only from somebody who can already run code
on the site — that filter, or a single site's administrator — and could read the key anyway, and it
reads IPv4 records once, before the request resolves the name again. It is made only once a report
is about to go, after the page has gone, so no page waits on a resolver. On a network every request
passes `reject_unsafe_urls`, as ADR 0072 already checks the address on save.

**When Dewiride Analytics cannot be reached.** One option, loaded with every page, holds the state;
it is written only when something changes. A failure — no answer, an error, any status that is not a
batch answer — is waited out for 30 seconds, doubling with each further failure to 15 minutes; when
a wait ends, the first request to find it over takes the turn (`probing_until`, ten seconds ahead)
and the rest wait for its answer; the first success ends the waiting. Every request reads the state
as loaded when it began, so all those under way when a wait ends find it over; the turn is therefore
taken in the database, the stored state read as it is now and replaced only while it still holds
what was read (`Stored_Option`), so that of those requests exactly one tries. That read and that
compare-and-set are the plugin's only queries outside the options API, and are made only by a
request that finds a wait over. A 429 is waited out for its `Retry-After`, a minute when it gives
none, an hour at most. An answer with `measuring: false`, and one whose `hosts` do not cover
`home_url()`'s host — the batch answered, every report in it refused — are asked again after 15
minutes, since what would change either is changed on the dashboard, not here. A 401, a 3xx, an
answer naming another website and a saved key that no longer opens stop the reports until the
address, the Website ID or the key changes — the state belongs to a fingerprint of those three, so
changing any of them starts it afresh, and saving or removing a key on the settings page deletes it
outright — or until Check connection finds everything in order. The key is read only once the page
has gone, so a key that no longer opens costs the one request that finds it. The time of the last
report and how long it took are kept every 15 minutes, so the busiest site writes the option a few
times an hour; Check connection's empty batch is not a report and moves neither.

**Naming a page.** At `wp` — once the query has run and before anything is printed, when whether the
page is an AMP copy can be asked, which the AMP plugin refuses to answer before — a `GET` from a
browser navigating to a page (`Sec-Fetch-Dest: document`, no prefetch) that will be reported gets
`Server-Timing: dw;desc="<32 hex>"`, appended beside any other timing. Only while the installation's
latest answer named the website: that field and ADR 0073's reading arrived in the same version, so
an installation that names the website is one that joins names safely, and an older one is never
given one to join unsafely — nor one rolled back to such a version, whose next answer, naming no
website, stops the naming. If WordPress then turns the response into a redirect (`wp_redirect`), the
name is taken off again and any other `Server-Timing` entry is kept, since the redirect is not
reported and its name could only come back unanswered. Feeds, robots.txt and sitemaps are never
named: no tracker runs in them to send a name back.

**Pausing while readers cannot run the tag.** While the tag check (ADR 0072) finds no tag, a tag a
tool has taken over, or one browsers refuse over http, the server's reports pause, or every reader
whose tag never ran would be reported as a program that runs no scripts. A page that could not be
fetched pauses nothing — nothing is known about it — and neither does a second copy of the beacon,
which runs once. The check's outcome is now loaded with every page, since every report first asks
it.

**The visitor's address.** Every address is normalised first: an IPv6 address carrying an IPv4 one
is the IPv4 address, and brackets and ports are taken off. Two ways a site is reached are known:

- **Automatically**: the connection's address — unless it is inside Cloudflare's published ranges
  and the request carries `CF-Connecting-IP`, when the header is believed only if the `Host` is the
  site's own name or its `www.` twin. Another Cloudflare customer can point a name of theirs at the
  same origin and write any header they like, but not ask for the site's own name. A private
  connection that carries any forwarding header is a proxy nobody named, and nothing is sent.
- **Through another proxy**: a header and the proxies' addresses — at most 50, none wider than /8
  (IPv4) or /16 (IPv6), so that a range cannot quietly take in half the internet. A connection from
  a public address that is none of them reached the site directly and is the visitor, whatever its
  headers say; a private one is a proxy the settings do not name, and nothing is sent. A list is
  read from its right-hand end past the proxies to the first address that is not one, and only its
  last 8 KB, as much as a web server accepts in one header: each proxy appends to the end, so
  cutting a long list from its start, as other headers are cut, would keep what the visitor wrote
  and drop what the proxies added. Nothing but control characters is taken out of it, and it is not
  emptied for holding something that is not UTF-8 — an entry that is not an address is simply not
  one. `Forwarded` (RFC 7239) is read by its `for=` parameters and split from its right-hand end,
  separators inside a quoted string being part of a value: a visitor's `Host` that a proxy copies
  into `host=` cannot add an element, and a quotation mark the visitor leaves open cannot reach the
  elements the proxies wrote after it. An entry that is not an address, or a list of proxies only,
  names nobody.

Cloudflare's ranges are fetched from `api.cloudflare.com/client/v4/ips` — no account, nothing about
the site sent — once reports are being sent and a request carries `CF-Connecting-IP`, then weekly by
WP-Cron; nothing reaches Cloudflare before somebody has switched reporting on. A list with any range
wider than /10 (IPv4) or /20 (IPv6), or more than 100 ranges, is refused whole; the last good copy
is kept, loaded with every page since every report on a Cloudflare site needs it; while none has
ever arrived, the fetch is retried within the hour and nothing that came through Cloudflare is sent
with an address. A request the site refused is the exception to that: whoever sends one can write
`CF-Connecting-IP` before the list has arrived, or send it through another Cloudflare customer's
zone, so a refused request is reported for those two reasons all the same, without an address —
which the installation keys by its user agent alone — rather than letting either hide a probe. In
every mode an address that is not publicly routable — private, loopback, link-local, reserved, or
carrier-shared (`100.64.0.0/10`, which PHP does not count) — is never sent, except on a site whose
environment type is `local` or `development`. The settings page shows the address WordPress sees for
whoever is reading it, or why it cannot be used; while that is a proxy nobody named, or one whose
header names nobody, its status says nothing is being reported yet rather than calling reporting on.

**The key.** On a single site, `DEWIRIDE_ANALYTICS_SERVER_KEY` in `wp-config.php` wins; a network's
sites share that file, so there only a saved key counts. A saved key is sealed —
`sodium_crypto_secretbox` with a random nonce, under HMAC-SHA256 of a fixed label keyed with
`wp_salt( 'auth' )`, stored as hexadecimal in an option never loaded with every page — so a copy of
the database alone does not give it away. That holds where `AUTH_KEY` and `AUTH_SALT` are in
`wp-config.php`, as WordPress's installer writes them; a site that never set them has WordPress keep
generated ones in the database, where the seal guards no more than the database does. Only the key's
last four characters live in the settings, which is what every page and the reports' fingerprint
read; the key itself is opened only to send a report, after the page has gone, and on the settings
page, never to serve a page. A key sealed under secret keys the site has since changed no longer
opens: the reports stop, and the settings page asks for a new key — Dewiride Analytics shows a key
only once, so there is no copy to paste again — and offers to remove the one that no longer opens. A
key saved some other way than through the settings page — imported, or written from WP-CLI — leaves
the settings without its ending, and the settings page asks for Save Changes to be pressed rather
than calling reporting on. Where the key is in `wp-config.php`, every sentence that would ask for a
key says to change it there, since the page has no field for it. It is never shown, never in REST,
and deleted with the plugin.

**Check connection** sends an empty batch, which stores nothing, and reads the answer against the
site field by field, in the order that says the most useful thing first — the key accepted, `site`
this Website ID, `measuring` (a closed account answers no `hosts` at all), `hosts` covering
`home_url()`'s host — then, when the server's reports go to the address the browsers load the tag
from, asks it for `dw.js`, which only an address the dashboard answers on serves; an address given
by the filter need not serve the tag and is not asked for it. A field an older installation leaves
out is said to be unchecked rather than guessed. It also reports WordPress refusing outbound
requests (`WP_HTTP_BLOCK_EXTERNAL`, with `api.cloudflare.com` named beside the installation's host
for a site behind Cloudflare), runs the tag check, and on success ends any wait or stop. Before
anything is sent it says so when the saved key no longer opens or the key in `wp-config.php` is not
a whole key. Of the answers that are not a batch, a page, a 404 or a 405 are told as an address that
does not lead to Dewiride Analytics, a 403 as something on the way turning the site away, and a
refused certificate apart from an address that could not be reached, each with what to do rather
than a status code. Only the outcome, a status and where a redirect leads are kept, under the
fingerprint of the address, Website ID and key ending it was made with: a result for settings that
have since changed is not shown. A redirect is shown as the scheme, host and port it leads to — a
location written as a path, or as a host without a scheme, completed from the address asked, and a
default port left out — and one leading somewhere on the address asked is told as an address to
check, since there is no other address to save; the same holds for the stop a redirect brings.

**On the dashboard**, the WordPress steps gain a fifth: an editor or owner creates a key named
`WordPress` in place and is shown it once with where to paste it — only once the website's keys have
been read, so a key that exists is not made again unseen, and only for the website it was made for;
a viewer is told who can, unless the website already has one. Once a key by that name exists, the
step says when it last connected: of several, the one used most recently, else the newest, so a key
made and never pasted does not hide the one in use. That is when the engine last authorised it —
Check connection's empty batch included — so it says the plugin reached Dewiride Analytics, not that
it is reporting; the plugin's settings page says how reports are going. The server keys list shows
every key's last four characters, as the plugin's settings page does, and offers creating and
removing keys to editors and owners only, as the engine allows.

**For the site's privacy policy**, the plugin offers WordPress's privacy guide a description of what
is collected — the server's reports included, sent whatever a visitor chooses in a consent banner —
and of what is kept for how long.

## What this deliberately does not do

- **It does not report the REST API at its own addresses**, refused or not, nor a sign-in that
  succeeded, even a program's.
- **It does not see what a page cache answers alone.** A visit served by WP Super Cache's rewrite
  rules, Batcache, Varnish or Cloudflare's page cache never reaches WordPress, so the server reports
  nothing for it; the browser's half still counts the people. The settings page says so when
  WordPress's page-cache drop-in is on.
- **A visitor can hide a page that was answered without an error** by calling it a prefetch, a
  request for something other than a document, or one of the site's own programs, by asking for it
  with `HEAD` or another method, or by asking for it as a preview, an embed or an AMP copy. What the
  site refused cannot be hidden that way, except a request for a page's own file — a script, a
  stylesheet, a source map, an image, a font, an icon, a web manifest — that names a destination
  other than a document, which is how a browser asks for a page's missing files.
- **It does not see a request refused before WordPress loads its plugins** — a firewall run from
  `auto_prepend_file`, or the web server's own rules — since no plugin runs for it.
- **Cloudflare is believed of the site's own names, which proves less than it seems.** Only
  Authenticated Origin Pulls or a Cloudflare Tunnel prove a request came through the site's own
  zone; the settings page and the readme say a proxy can be named instead.
- **Visitors sharing the site's own outgoing address are not reported by its server** — an office
  whose website is served from the same connection, for instance. The browser's half still counts
  them.
- **Browsers that send no `Sec-Fetch-*` headers** — Safari before 16.4, and every browser on a site
  served over http, where the tracker could not read a name either — are reported without a name, so
  their two halves are recognised as one only where their keys already agree; where they do not, the
  server's half is a visit that ran no script.
- **Reports are not retried.** What was sent while Dewiride Analytics could not be reached is lost,
  as a browser's report is.
- **It keeps no counts.** The status says how reports are going, not how many were sent; the
  dashboard is where they are counted.

## Consequences

- **Every reported request costs one request to Dewiride Analytics**, after the page where the
  server allows it: a new connection and TLS handshake each time, since PHP keeps nothing open
  between requests. The settings page shows how long the last one took. A PHP worker stays busy for
  as long as it takes, two seconds at most.
- **When the installation stops answering, its first two seconds are felt.** Measured on WordPress
  7.1.4 with PHP-FPM behind nginx and a pool of five workers: a report took 99 ms and crawlers'
  pages were answered in 70–180 ms with reporting on. With the installation stopped, the first
  failure was recorded within two seconds, after which pages were answered in 70–90 ms and the
  reports waited; but the reports already under way in those two seconds each held a worker, and one
  page waited 2.75 s for a free one. A site with more workers than reported requests in two seconds
  does not notice; one with fewer sees it once, as an outage begins. Bounding it further would take
  a count of reports under way kept outside PHP — a persistent object cache, which most sites do not
  have — or a queue, which this decision refuses.
- **Robots' page views count.** On the hosted service they count towards a plan's allowance, which
  its terms already say; sign-in attempts and XML-RPC, which arrive by the thousand in an attack,
  are off until chosen.
- **What the plugin keeps**: its settings and the reports' state, loaded with every page and written
  only on a change; the tag check's outcome, now loaded with every page too; Cloudflare's ranges,
  fetched only once reporting is on and loaded with every page once fetched; the sealed key and the
  last connection check, never loaded with every page. Never anything from a page or a visitor.
- **The weekly fetch of Cloudflare's ranges outlives a network deactivation** on every site but the
  one it ran on, as ADR 0072 records for the daily tag check: the events do nothing until the plugin
  is active again, and deleting it clears them on every site. Clearing them at deactivation would
  visit every site of a network, and would strip a site that also activated the plugin on its own,
  where it goes on running.
- **A key that leaks** lets whoever holds it report made-up traffic for that one website until it is
  removed on the dashboard; it reads nothing.
- **An installation older than ADR 0073** still receives reports, unnamed, and Check connection says
  it could only check them in part and that a reader may be counted twice.
- **Plugin 1.1.0** reaches every copy through the WordPress.org directory once it is listed, and
  through the dashboard's download until then.

## Tests that make this true

- Unit tests, one class of tests per class, in `integrations/wordpress-plugin/tests/Unit/` — among
  them every exclusion and its status boundary at 399 and 400, refusals reported whatever their
  method, the feeds switch, a REST query or a preview's, an embed's or an AMP copy's query, while a
  refusal for somebody signed in stays out; refusals before WordPress parsed the request, as a page,
  a sign-in or the REST API's own address on a site's root and in a subdirectory, read unslashed;
  the sign-in rule and signing out, a feed answered `304`, an http site's browser redirect, the
  referrer's user name and password, square brackets, `observedAt`; Cloudflare's `Host` rule with
  its `www.` twin and the range checks at their edges, the refused requests sent without an address
  for both reasons, proxy ranges wider than `/8` or `/16` and a 51st proxy refused, a direct
  connection in proxy mode, the proxy list read from its end past 8 KB and its right-to-left walk,
  `Forwarded`'s quoted strings and quotation marks left open; the waits and their turn — two
  requests finding a wait over together, one beaten to the turn, a success or a failure recorded
  meanwhile — against a stand-in database that changes a row only while it holds what was read, the
  stops and the fingerprint that ends them, the addresses learnt from answers, an answer naming no
  website ending the naming, a redirect read for the address it suggests, a Website ID written in
  capitals; the sealed key, a key in `wp-config.php` — whole and reported under, or not whole and
  sending nothing — a changed salt and the stop it brings; the connection bounded, a refused
  certificate, every Check connection outcome, the check not counted as a report and its result
  forgotten with the settings it was made for; which tag-check outcomes pause the reports; the
  plugin's header bound to its page and accepted for the period before; and the sentence for each
  setup problem, each state of the reports and each address reading, a key in `wp-config.php`, the
  page-cache note and the last check. Run in a process of their own, tests replace PHP's header
  functions in the plugin's namespace to show a page named only while the installation names the
  website and a redirect losing its name but no other timing, and define `fastcgi_finish_request()`
  or `litespeed_finish_request()` to show reports on without held pages being accepted and the page
  handed over before the key is opened. The tests slash `$_SERVER` as WordPress does, unless
  WordPress would not have yet, so a quotation mark or a backslash reaches the plugin as a visitor's
  would.
- The smoke harness (`tests/smoke/run.sh`), on WordPress 6.4 with PHP 7.4 and on the newest
  WordPress with the newest PHP, both under Apache: against a stand-in collector it asserts that
  nothing is sent until holding pages open is accepted, a browser's page left unnamed until the
  installation names the website, Check connection, a crawler's page reported unnamed, a browser's
  page named and reported under the same name, a prefetch and a redirected browser left alone, a
  redirect keeping another plugin's timing, a browser redirected on a site served over http left
  alone, a redirected crawler reported, a `HEAD`, the REST API, the favicon and a browser's missing
  image left out, a probe reported whatever it calls itself and when it asks for an embed of itself,
  a page a security plugin refused reported though asked for as a preview, a feed answered `304` and
  WordPress's sitemap reported, sign-in attempts only once chosen and never a successful or a
  browser's sign-in, XML-RPC's 405, probes a firewall refused while plugins were loading and as
  WordPress started reported, a sign-in it refused reported and the REST API it refused left out,
  the plugin's own fetch of its home page left out, and a request carrying the plugin's header left
  out when the header is genuine and reported when it is forged.
- `tracking-code.test.tsx` and `server-keys.test.tsx`: the key created in the WordPress steps, shown
  once and gone when the panel closes or the website changes, the button waiting for the website's
  keys and a failure to read them, a refusal forgotten, focus kept where it was, the key in use
  among several, the viewer's sentence only while there is no key, the last connection, the endings,
  and a viewer offered neither creating nor removing a key.
- End to end, once, against the whole product built from this change (with ADR 0073) and WordPress
  7.1.4 on PHP-FPM behind nginx: the key created in the dashboard's WordPress steps, saved on the
  settings page, Check connection connected; a reader in Chromium named on both of its pages and its
  browser echoing both names; GPTBot, a feed reader and a `/.env` probe reported unnamed; the
  signed-in administrator reported by neither half. With a named proxy putting the reader at
  198.51.100.77 for WordPress while the installation saw 172.18.0.1 — two visitor keys — the
  overview's own statement counted that reader as one visitor with three page views, where the same
  rows without names count two visitors and six. A stopped installation was waited out as described
  above, and a key nobody issued stopped the reports with the sentence that says what to do.

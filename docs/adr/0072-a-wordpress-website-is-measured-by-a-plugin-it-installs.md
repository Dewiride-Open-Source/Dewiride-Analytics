# 0072 — A WordPress website is measured by a plugin it installs

- **Status**: accepted
- **Date**: 2026-11-15
- **Applies to**: both editions, and the whole of it is in the free product. Adds
  `integrations/wordpress-plugin`, the first integration in `integrations/` and MIT like the
  tracker: a WordPress plugin that puts the tracker's tag and its no-script image on a website's
  pages. The dashboard's **Tracking code** panel gains a WordPress view that hands the plugin out,
  both dashboard images package it before they build, and both Builds check it through one shared
  action. In `ee/`, the commercial dashboard image packages it too, a release checks after it is
  deployed that the download answers, and the public website's install page names the plugin. The
  engine, the tracker, the schema and the retention envelope do not change: the plugin prints the
  tracker's own tag and image, so what reaches the collector from a WordPress website is what
  reaches it from the pasted snippet, and nothing new is collected.
- **Follows**: [0068](0068-a-website-s-posts-are-read-from-the-sitemap-it-names.md), whose
  consequence for a WordPress website on plain links the plugin's settings page now warns about;
  and [0031](0031-a-change-is-checked-before-anybody-reads-it.md) and
  [0051](0051-a-known-flaw-is-seen-within-the-week.md), whose Build, and its run every Monday, now
  check the plugin and the archive people download.

## Context

A website has been measured by two lines pasted into its pages, wherever it keeps what shows on
every page. On WordPress those lines meet four things a pasted line cannot answer for.

- **There is nowhere lasting to paste them.** A classic theme writes a page's head from a file the
  theme's next update replaces, unless a child theme holds the change; a block theme has no such
  file; and WordPress has no place of its own for a script in a page's head. Plugins exist whose
  only job is to hold such lines.
- **Optimisation plugins rewrite scripts.** They combine scripts into one file, copy them onto the
  site's own host, and hold them back until the visitor moves the mouse, scrolls or touches the
  screen. The tracker starts by reading its own tag: the website it names, and the address it was
  loaded from, beside which it sends its reports unless told otherwise. A combined file runs under
  a tag that names no website, so nothing is counted; a copy on the site's own host sends its
  reports there, where nothing answers; and a held-back tag never counts the reader who leaves
  without doing any of those things.
- **What a visitor receives is not what WordPress printed.** A page cache serves copies made before
  the lines were pasted, and a consent tool holds a script back until somebody accepts. Either
  stops counting some readers or all of them, and nothing on the owner's side says so.
- **The owner reads their own site signed in, and so does their team.** A pasted line counts them
  like anybody else. Only code running inside WordPress knows who is signed in.

Crawlers, AI fetchers, feed readers and scanners never run the tracker at all, and only code
running inside WordPress could report them. That is a decision of its own, and not this one.

The owner asked on 2026-11-14 how to add tracking to a WordPress website, and for a plugin if one
was needed. They chose to make it free, to hand it out from the dashboard first and to list it in
the WordPress.org plugin directory straight after, and to leave out people who are signed in unless
their role is chosen.

## Decision

**A plugin puts the tag on the page, and WordPress prints it.** `integrations/wordpress-plugin` is
a WordPress plugin, Dewiride Analytics, whose slug, folder and text domain are
`dewiride-analytics`. Its settings hold two values the dashboard hands over — the **Address** the
dashboard is read on and the **Website ID** — and on every page a counted visitor reads, it
enqueues the tracker as a classic script (`wp_enqueue_script`, handle `dewiride-analytics`), so
WordPress prints it as it prints every other script. The tracker's attributes are added through
`wp_script_attributes`, to that tag alone, recognised by the `id` WordPress gives it:
`dewiride-analytics-js`. Nothing is printed and nothing is fetched until both values are saved.

**`async`, in the head, where the snippet says `defer`.** A deferred script runs before
`DOMContentLoaded` and the event waits for it, so an Address that is slow or out of reach would hold
back that event — and everything themes and other plugins start on it — on somebody else's
website. An asynchronous script holds nothing back. The snippet keeps `defer` for the reason its
code gives (`frontend/apps/web/src/lib/tracker/snippet.ts`): it never competes with the page it
measures for parsing time. A plugin makes the choice for every website that installs it, without
anybody reading the tag, and there the cost that weighs more is the one `defer` carries. No version
is added to the address: the tracker is versioned by the installation that serves it, and the tag
check and the optimisers' exclusions below recognise it by its exact address.

**The tag says what the tracker would otherwise work out, and asks to be left alone.**

- `data-site`, the Website ID, and `data-collector`, the absolute address reports go to
  (`{Address}/collect`). Without `data-collector` the tracker reports beside wherever it was loaded
  from; with it, a copy an optimiser makes elsewhere still reports to the right place.
- `data-cfasync="false"`, first. Cloudflare's Rocket Loader leaves alone a script that carries it
  before `src`
  ([Cloudflare](https://developers.cloudflare.com/speed/optimization/content/rocket-loader/ignore-javascripts/)).
  WordPress 6.4 to 6.9 print attributes in the order they are given, so the plugin gives it first;
  from 7.0 WordPress writes the tag through its HTML API, which sets out added attributes in
  alphabetical order, and `data-cfasync` comes before `src` either way. It wins over any value
  another plugin gave it.
- `data-no-optimize="1"`, `data-no-defer="1"` and `data-no-minify="1"` for LiteSpeed Cache,
  `data-noptimize="1"` for Autoptimize and Breeze, `data-jetpack-boost="ignore"` for Jetpack Boost,
  and `nowprocket` for WP Rocket's delayed JavaScript. All but the last carry a value, so each reads
  the same whichever way WordPress writes the tag: WordPress 6.4 to 6.9, on a theme without HTML5
  script support, write an attribute that stands alone as `name="name"`.

**And the tag is put on the exclusion lists of the optimisers that take one through a filter**, in
the form each matches its entries against. Each was read in the tool's own source, in the version
current as this was written — WP Rocket 3.24, LiteSpeed Cache 7.9.3, Autoptimize 3.1.17,
SiteGround Speed Optimizer 7.8.6, Breeze 2.6.4 and Jetpack Boost 4.8.2 — and Perfmatters, whose
source is not public, in its own documentation.

- **The full address of `dw.js`**, where entries are matched against a script's address or its
  whole tag, as a substring or as a regular expression: WP Rocket's `rocket_delay_js_exclusions`,
  `rocket_exclude_defer_js` and `rocket_minify_excluded_external_js`; LiteSpeed Cache's
  `litespeed_optimize_js_excludes`, `litespeed_optm_js_defer_exc` and `litespeed_optm_gm_js_exc`;
  Perfmatters' `perfmatters_delay_js_exclusions` and `perfmatters_defer_js_exclusions`; and
  Breeze's `default_scripts_gnore_from_delay`, spelled as Breeze spells it. Breeze has no filter on
  its own delay setting, and this one keeps a matching tag out of its delay loader altogether.
- **The address's path**, where entries are matched against the files on the site's own host: WP
  Rocket's `rocket_exclude_js`, which matters where the installation shares the site's host.
- **The script's handle**: SiteGround Speed Optimizer's `sgo_js_minify_exclude`,
  `sgo_javascript_combine_exclude` and `sgo_js_async_exclude`.
- **The tag's `id`**: SiteGround's `sgo_javascript_combine_exclude_ids`, its one combine exclusion
  that reaches a script from another host.
- **Whichever shape arrives**: Autoptimize's `autoptimize_filter_js_exclude`, a comma-separated
  string or an array keyed by entry, which gains the address in the shape it came in.

Every callback adds to the list it is given and keeps what is already there, and hands back
untouched a list in a shape it does not recognise.

**`data-correlation` is never written into a page.** It names one delivery of one page, and a page
cache hands the markup it kept to everybody it serves, so a value in the markup would make strangers
one visitor. Nothing the plugin prints differs from one reader of a page to the next, which is also
why a cached page goes on counting its readers.

**The tracker is never copied into the plugin.** It is loaded from the Address, where the
installation serves the version that goes with its collector. A copy inside the plugin would be as
old as the plugin on every website that had not updated it.

**The no-script image names its page.** It is printed inside `<noscript>` at the top of the body,
from `wp_body_open`; a theme too old to call that gets it from `wp_footer` instead. It loads
`{Address}/collect/pixel.gif?site={Website ID}&u={page}`, where the page is the site's own scheme,
host and port, taken from `home_url()` and never from what the request claimed, followed by the path
that was asked for, without its query. The collector takes the page from `u` in preference to the
referrer (`NoScriptPixelEndpoint`), so a reader whose browser sends less than the whole page is
still counted on the page they read. `referrerpolicy="strict-origin"` sends only the site's origin
as the referrer, never the page or its query, where a search box puts what was searched for. The
referrer is still asked for, because the collector judges where the image was shown from the
`Origin` header, then the referrer's host, and only without both from `u`'s host
(`EventIngestor`); an image sends no `Origin`, so `no-referrer` would leave that check to `u`, which
a copy of the page served elsewhere carries unchanged. The snippet keeps
`no-referrer-when-downgrade` because it has no `u`: the referrer is the only place its page can come
from. `data-no-lazy="1"` with `class="skip-lazy"` keeps lazy-loading tools from swapping the image's
address for one only a script would restore — for the one reader who runs none.

**Who is counted.** A visitor who is not signed in is. Somebody signed in is counted only when one
of their roles on this site is ticked under **Who is counted**, and none is until somebody ticks
one, so the owner's visits and their team's are left out without anybody asking. Somebody signed
in with no role on this site — a network's super administrator looking at one of its sites — is
left out with them. A page not being read as itself is never counted, whoever reads it: a preview,
the Customizer, a post embedded on another website, or a copy the AMP plugin serves, where no
script of this kind can run. The tag and the image follow the same rule, and no screen of
WordPress's own dashboard carries either.

Three warnings on the settings page follow from what is counted. **An `http` Address on a site
served over `https`**, as a site that moved to `https` after its Address was saved has, loads a
script browsers refuse to run; the warning says to change the Address to `https`, or, to somebody
who may not change it, who can. **Plain permalinks** put every post at the home page's
address with only a query to tell them apart, and the collector keeps no query, so every visit would
read as the home page ([0068](0068-a-website-s-posts-are-read-from-the-sitemap-it-names.md)).
**Open registration** lets anybody sign in, and so leave the count, unless the role new members are
given is ticked.

**The settings are WordPress's own.** Settings → Dewiride Analytics, for whoever holds
`manage_options`, built with the Settings API and WordPress's own admin markup: **Connect**
(**Address**, **Website ID**), **Who is counted** (a box for every role), and a status saying
whether counting is on and what the last tag check found. "On" says only what the plugin does — the
tag is added to the pages of every visitor who is counted — because that is all it can see for
itself; whether the tag runs and its reports arrive is for the tag check and the dashboard to show.
One option, `dewiride_analytics_settings`, is created autoloaded when the plugin is activated,
because every page a visitor reads needs it. A value that cannot be used is refused with a message
and the saved one kept, so a mistyped field never undoes a setup that works; cleaning a value a
second time changes nothing, because the Settings API cleans an option twice the first time it is
saved. Emptying either field stops counting.

**Only somebody allowed to add code to the site may change the Address**, because it names the
script every counted page loads: choosing it is adding code to every page. That is WordPress's
`unfiltered_html`, which a single site's administrators hold, a network's only its network
administrators, and nobody where `DISALLOW_UNFILTERED_HTML` is set. A site's own administrator on a
network manages its options without being trusted with code, and where the network's sites share
one domain, a script they chose would run where the network administrator is signed in. Anybody else
sees the Address read-only — not disabled, since a disabled field is not sent and every save would
read as emptying it — with a note saying who can change it. A different Address from them, an
emptied one included, is refused with that note and the saved one kept; the saved one sent back, or
written another way — with a trailing slash, say — is no change. WP-CLI always may, since whoever
runs it can already run any code on the server, and refusing it would only stop a site being set up
from a script. The Website ID and the roles load no code and are not restricted. Until both are
saved, a notice on the Plugins screen asks whoever can finish the setup for exactly what is missing:
the Address only of somebody who may change it, the Website ID of anybody who manages the site's
options.

**The Address is narrower than an address may be.** `http` or `https`; a host name or an IPv4
address; an optional port; and an optional path of letters, digits, `-._~` and percent escapes,
with no `.` or `..` segment. No user name, password, query, fragment or IPv6 literal; the scheme
and host are lower-cased and a trailing slash removed. The optimisers above are handed the address
as an exclusion, and several use one text both as a regular expression and as a plain substring, so
it may hold only characters that, read as a pattern, still match the address itself: a `(`, `|` or
`*` in it could break an optimiser's exclusions for every script on the site. A website served over
`https` needs an `https` Address, since a browser refuses to run a script loaded over `http` on a
secure page. On a network the Address must also pass `wp_http_validate_url()`, WordPress's own test
of an address its servers may fetch, so that no site's setting points the network's servers at an
address WordPress would refuse to reach; a network administrator allows a private installation
through WordPress's `http_request_host_is_external` and `http_allowed_safe_ports` filters. Only
those who may change the Address ever reach that test, so its refusal speaks to them: use the
installation's public `https` address, or allow this one in the network's code. The Website ID is
lower-cased and has to be a UUID (`wp_is_uuid()`).

**The tag check looks at what a visitor receives.** Once a day, from a WP-Cron event, and whenever
the owner presses **Check the tag**, the plugin fetches its own home page as an anonymous visitor
(`wp_remote_get`, ten seconds, user agent `DewirideAnalytics/<version>`), follows at most three
redirects that stay on the site's own host, and reads the markup with WordPress's HTML API
(`WP_HTML_Tag_Processor`). It keeps the outcome, the tool it names and the time, and never anything
from the page.

The tag is intact when the page carries exactly one copy of the tracker — the plugin's own tag, by
its `id`, or any script that loads a `dw.js` and names a website — and that copy is the plugin's
own, with `src` exactly `{Address}/dw.js`, a `type` that is absent or `text/javascript`, `data-site`
the saved Website ID and an absolute `data-collector`, and the page is not served over `https` while
the tag loads over `http`. Otherwise the check says what it found, and names the tool where it can
tell:

- a `type` of `rocketlazyloadscript` or `text/rocketlazyloadscript`, or a `data-rocket-src`: WP
  Rocket is delaying the tag;
- `litespeed/javascript`: LiteSpeed Cache is delaying it;
- `text/plain`: a consent tool is holding it back until somebody accepts;
- another `src`: an optimiser combined it with other files or moved it;
- any other change: something altered it, and the tool is not named;
- more than one copy: it is loaded more than once — by a snippet pasted before the plugin, say;
- none of the plugin's own: it is missing — from a copy a page cache made before, say;
- the plugin's own tag as printed, loading over `http` on a page served over `https`, judged by the
  address that finally answered after the redirects within the site: browsers refuse it
  (`insecure`).

Each outcome is said in plain words on the settings page, with what to do wherever there is
anything to do. The check only reports: no outcome changes what the plugin prints. A home page the
site cannot fetch — some hosts refuse a site's requests to itself — is said plainly, as something
that does not affect counting, and nothing follows from it. Saving a different Address or Website
ID forgets the last outcome and schedules a check for the next time WP-Cron runs, so the page never
shows a verdict on settings that are gone. The first save does the same: WordPress makes it by
adding the option rather than updating it, since what activation stored is still the registered
default, so the plugin listens for `add_option_dewiride_analytics_settings` as well as
`update_option_dewiride_analytics_settings`. Activation adds its empty defaults the same way, and
those name nothing to check. The daily event is scheduled when the plugin is activated, and made
sure of whenever a site's dashboard is opened (`admin_init`), because a plugin activated across a
network runs its activation on one site only.

**What it keeps, and when it goes.** Two options — the settings and the last check's outcome —
and the daily event. Nothing is written while a visitor's page is served. Deactivating the plugin
removes the event and keeps the settings; deleting it removes all three, on every site of a
network.

**The dashboard hands the plugin out.** `scripts/package-wordpress-plugin.mjs`, which has no
dependency, writes `dewiride-analytics.zip`: one top folder, `dewiride-analytics/`, which WordPress
keeps as the plugin's own when it unpacks an upload, holding exactly the twelve files the script
names one at a time. It refuses to write anything when a name could reach outside that folder or is
listed twice, when a listed file is missing or is not an ordinary file, when a file sits where the
shipped files live without being listed, when the dashboard's download points anywhere but
`/downloads/dewiride-analytics.zip`, or when the four places that state the version — the plugin's
`Version:` header and its `DEWIRIDE_ANALYTICS_VERSION` constant, the readme's `Stable tag:`, and
the version the dashboard's button names — do not all agree; and it reads its own archive back
before saving it. Nothing in the archive depends on when or where it was made: one fixed time on
every entry, the entries in the list's order, forward slashes, names flagged as UTF-8, Unix
permissions. The same archive comes out, byte for byte, on Windows and in the Linux image that
builds the dashboard.

`pnpm dev`, `pnpm build` and both dashboard images package it first — the images before
`next build`, so an image whose button names one version and hands out another does not build. The
archive is build output and ignored by Git. The dashboard serves it at
`/downloads/dewiride-analytics.zip` with `Cache-Control: public, max-age=3600, must-revalidate`, as
it serves the tracker, and neither forwards that address to the engine nor redirects it. A hosted
release fails unless the download answers once it is deployed.

The **Tracking code** panel offers **Any website** or **WordPress**. The WordPress view is four
numbered steps — download the plugin, add it to WordPress, connect it, remove any code added
before — naming each thing to press as WordPress prints it (**Plugins**, **Add Plugin**,
**Upload Plugin**, **Install Now**, **Activate Plugin**; the readme adds that WordPress before 6.8
says **Add New Plugin**), with the Address and the Website ID each beside a button that copies it.
Until the plugin is listed in the directory, an update is a new download uploaded the same way,
with WordPress's own **Replace current with uploaded**. Listing it is the next piece of work.

**No `Update URI`, ever.** WordPress asks WordPress.org about every plugin that carries none, and
offers whatever the directory holds under the plugin's folder name as its update
([WordPress 5.8](https://make.wordpress.org/core/2021/06/29/introducing-update-uri-plugin-header-in-wordpress-5-8/)).
That is the channel wanted: once the plugin is listed, every copy — downloaded from a dashboard or
installed from the directory — is updated as any other plugin is, and an `Update URI` naming
anywhere else would cut the downloaded copies off from it for good. A listed plugin may not update
itself from anywhere else in any case: the directory's
[guidelines](https://developer.wordpress.org/plugins/wordpress-org/detailed-plugin-guidelines/)
forbid serving updates from servers other than WordPress.org's. Until the listing exists the name
is not yet this plugin's, and whatever the directory came to hold under `dewiride-analytics` would
be offered to every copy. That is why the listing comes straight after this, and why the hosted
service begins handing the download out once the owner has submitted the plugin to the directory.

**WordPress 6.4 and PHP 7.4 are the floor.** WordPress applies `wp_script_attributes` to the
scripts it enqueues from 6.4, and everything else the plugin uses — loading strategies, the HTML
API — is older, so no WordPress function is tested for or shimmed. PHP 7.4 is the oldest PHP
WordPress itself runs on from 7.0
([hosting handbook](https://make.wordpress.org/hosting/handbook/compatibility/version/7-0/)), and
the oldest the plugin's tools are resolved for. The header and the readme state both, so WordPress
refuses to activate the plugin on anything older. `Tested up to: 7.2` rests on the smoke harness,
whose newest run installs the plugin on WordPress 7.2's pre-release image.

**One action holds the checks, and both Builds run it.** `.github/actions/wordpress-plugin` defines
them once. The public Build's **WordPress plugin** job runs it on every change, and the commercial
edition's Build runs the same action against the commit of the free product it is given, or
`main` when it is given none — for a release, the exact commit it publishes — so a hosted release
cannot hand out a plugin that was not checked. In order, on PHP 8.5: `composer validate --strict`;
`composer audit --locked`, before anything is installed, because installing runs code;
`composer install`; PHPCS; PHPStan; PHPUnit; packaging, by the same script the images run;
unpacking outside the checkout; WordPress's Plugin Check, which the directory runs on a plugin
before accepting it, on exactly the unpacked folder, strict so that a warning fails as an error
does, with experimental checks left out and the newest WordPress; and the smoke harness at both
ends of what the plugin supports. The public Build also runs the unit tests on PHP 7.4
(**WordPress plugin on PHP 7.4**), since reading the code for 7.4 is not running it there. Both
Builds run every Monday as well, so a tool that gains an advisory turns them red within the week,
and Dependabot proposes the tools once a week, holding PHPUnit at 9, the last major that runs on
PHP 7.4, and PHP_CodeSniffer at 3, which the WordPress standard is written against.

The smoke harness, `tests/smoke/run.sh`, installs the unpacked archive into a throwaway WordPress
of the official images, with MariaDB and WP-CLI beside it. `floor` is the newest 6.4 release, looked
up and installed over the newest PHP 7.4 image, with a classic theme that has no HTML5 script
support, where what a page receives differs most from what the plugin asked for; `newest` is
WordPress 7.2's pre-release image on PHP 8.5 with the default block theme. It reads every page by
attribute, never by order.

**The WordPress coding standard, and not the editor's.** The plugin follows the WordPress Coding
Standards in full — tabs, `snake_case` functions and properties, `Class_Name` classes,
`class-*.php` files, Yoda conditions — the conventions of WordPress's own code and of the people
who read a plugin's. PHPCS with WPCS 3.4 enforces them in the Build, PHPCompatibilityWP reads the
code for PHP 7.4 upwards, and a suppression names its sniff and gives its reason on its own line.
SonarQube for IDE's default PHP rules contradict four of those conventions — S105 (tabs) and the
naming rules S100, S101 and S116 — and there is no analysis server to hold a profile of this
repository's own ([0031](0031-a-change-is-checked-before-anybody-reads-it.md)). For the plugin the
gate is WPCS, PHPStan at level `max` (with WordPress's stubs and `szepeviktor/phpstan-wordpress`,
reading the code as PHP 7.4) and Plugin Check, and those four findings are the WordPress convention
rather than a defect.

**The tools are installed, and never shipped.** The plugin has no runtime dependency. Its tools are
43 development packages: 9 under MIT, 29 under BSD-3-Clause, and 5 under LGPL-3.0-or-later —
PHPCompatibility, PHPCompatibility-ParagonIE, PHPCompatibility-WP, PHPCSExtra and PHPCSUtils.
WordPress, Plugin Check and MariaDB, under the GPL, run only as the smoke harness's containers and
in the Build's Plugin Check. The version table at the root of the tree keeps strong copyleft, and
`Hangfire.Core`'s LGPL, out of what this product is built from, and none of these tools becomes
part of it: `vendor/` is ignored by Git and kept out of every image's build context, and the
archive names its twelve files one at a time. It is the line `next.config.ts` already draws for
`sharp`, an image decoder installed with the framework and kept out of the image. `composer.lock`
is resolved for PHP 7.4 (`config.platform.php`), so the floor and the newest PHP install the same
tools.

**Every string goes through gettext, and no language file ships.** Strings carry the literal text
domain `dewiride-analytics`, which the coding standard checks. There is no `.pot`, no
`Domain Path` and no `load_plugin_textdomain()`: from WordPress 4.6 a plugin in the directory has
its translations loaded by its slug
([plugin handbook](https://developer.wordpress.org/plugins/internationalization/how-to-internationalize-your-plugin/)),
so once it is listed, WordPress.org's language packs reach every copy however it was installed, and
a new language stays a translation job. Until then the plugin is in English.

**The readme says what reaches whom.** `readme.txt`, in the directory's format, says under
`== External services ==` that nothing is sent until both values are saved, what the tracker sends
and when, what the image records, and that the tag check fetches only the site's own home page; it
links the hosted service's terms and privacy policy, and says that an installation the site's owner
runs receives everything itself and nothing reaches the hosted service.

## Considered and refused

- **The snippet, pasted by hand or held by another plugin.** It is still offered as **Any
  website**, and it works on WordPress, but with none of the above: nobody signed in left out, no
  exclusions, no check.
- **A script module** (`wp_enqueue_script_module`). A module has no `document.currentScript`, which
  is how the tracker finds its own tag.
- **Writing the tag out by hand from `wp_head`.** Enqueued, it is a script optimisers can find by
  its handle, WordPress applies its loading strategy, and the attribute filter reaches it. The
  WordPress standard refuses a script written out by hand, too
  (`WordPress.WP.EnqueuedResources.NonEnqueuedScript`).

## What this deliberately does not do

- **Report from WordPress's own server.** Crawlers, AI fetchers, feed readers and scanners never run
  the tracker, and only code running inside WordPress could report them, through the keyed collector
  that [server reporting](../server-reporting.md) describes. That half brings questions of its
  own — an identifier a page cache would hand to the wrong reader, an address a proxy in front may
  have rewritten, a page that finishes only after its report is sent — and is a later decision.
  Until it exists a WordPress website is measured from the browser, as one with the snippet is, and
  the collector's `wordpress-plugin` surface stays unused.
- **Relay reports through WordPress.** The plugin adds no route to WordPress's REST API, and no
  report passes through the site: the reader's browser sends every one straight to the Address. A
  relay would put the site's own server's address where the reader's belongs, and the address is
  most of what a verdict rests on; it would also run WordPress once for every report.
- **Wait for consent.** The plugin neither asks a consent tool nor holds the tag for one. The tag
  sets no cookie and stores nothing on the reader's device, and whether it may load before somebody
  answers a banner is the site owner's decision, which their consent tool carries out. The tag check
  says when a consent tool is holding the tag back, and what that costs: only the readers who accept
  are counted.

## Consequences

- **A WordPress visit is the snippet's visit.** It reaches the collector from the tracker or the
  image, and is recorded and judged exactly as one from a pasted snippet; nothing on the dashboard
  tells the two apart.
- **A WordPress website counts no robots yet.** What never runs the tracker is not seen, as before.
- **A snippet left in place counts nobody twice.** A second copy of the tracker on a page stands
  down. The tag check names the duplicate, and the dashboard's fourth step asks for the old code to
  go.
- **Readers who sign in are left out until their role is ticked.** A website whose readers have
  accounts — subscribers, members, customers — counts none of them by default; the warning about
  open registration names the role to tick.
- **On a network, the Address is a network administrator's to set**, or WP-CLI's. A site's own
  administrator sees it read-only, and still adds the Website ID and chooses who is counted.
- **Readers of a page's AMP copy, and of a post embedded elsewhere, are not counted.**
- **The home page stands for the site.** A tool that changes the tag on some pages only, or a
  template that never calls `wp_head`, is not seen by the check.
- **"Once a day" is once a day of traffic.** WP-Cron runs when somebody visits the site, or from the
  host's own scheduler where one is set up.
- **Every installation hands out the same plugin.** Nothing in the archive names an installation:
  the Address saved in its settings decides where it reports, so a self-hosted installation and the
  hosted service hand out one plugin.
- **A copy installed from a download stays on its version** until somebody uploads a newer one, or,
  once the plugin is listed, until WordPress offers the update.
- **On a network, deactivating clears the daily event of the site it ran on only.** The other sites'
  events stay scheduled and do nothing until the plugin is active again or deleted. Deleting it
  visits every site in turn, which takes a while on a very large network.
- **The plugin's PHP is not held to SonarQube's default PHP rules.** It is held to the WordPress
  standard, PHPStan and Plugin Check instead, as above.

## Tests that make this true

The plugin's unit tests, `integrations/wordpress-plugin/tests/Unit`: PHPUnit 9.6 with Brain Monkey
standing in for WordPress, in random order, failing on anything risky and on any warning, on PHP 8.5
in both Builds and on PHP 7.4 in the public one. A test that stands in for one of WordPress's
classes, or declares what every test after it would then see — WP-CLI's constant, the AMP plugin's
function — runs in a process of its own.

- `Settings_Test`: `test_a_usable_address_is_normalised` (a host, a trailing slash, capitals,
  spaces, a port, a path and its case, an IPv4 address, a percent escape),
  `test_an_unusable_address_is_refused` (no scheme, another scheme, a user name, a query, an empty
  query, a fragment, port nought, a port too high, an IPv6 literal, a dot-dot segment, a space in
  the host, a quote, a backslash, a host ending in a hyphen),
  `test_a_damaged_option_reads_as_nothing_saved`,
  `test_an_option_that_is_not_an_array_reads_as_the_defaults`,
  `test_the_script_and_collector_addresses_follow_the_saved_address`,
  `test_counting_needs_both_the_address_and_the_website_id`,
  `test_the_setting_is_registered_with_its_sanitising_and_defaults`,
  `test_the_option_is_created_autoloaded`, `test_usable_values_are_saved_clean`,
  `test_sanitising_twice_changes_nothing_the_second_time`,
  `test_an_unusable_address_keeps_the_saved_one`, `test_an_https_site_refuses_an_http_address`,
  `test_an_http_site_accepts_an_http_address`,
  `test_a_network_refuses_an_address_wordpress_would_not_fetch`,
  `test_a_network_accepts_an_address_wordpress_would_fetch`,
  `test_an_incomplete_website_id_keeps_the_saved_one`, `test_empty_fields_clear_the_settings`,
  `test_input_of_the_wrong_shape_is_treated_as_empty`,
  `test_somebody_allowed_to_add_code_may_change_the_address`,
  `test_somebody_not_allowed_to_add_code_keeps_the_saved_address`,
  `test_a_sites_administrator_on_a_network_is_sent_to_a_network_administrator`,
  `test_the_saved_address_sent_back_is_kept_without_a_message` (as it is shown, with a trailing
  slash, in capitals, with spaces around),
  `test_somebody_not_allowed_to_add_code_cannot_empty_the_address`,
  `test_a_refused_first_save_is_reported_once`, `test_wp_cli_may_change_the_address`,
  `test_the_lock_names_who_can_change_the_address` (a single site, a site of a network, a network
  administrator), `test_only_http_on_an_https_site_is_mixed_content` (http on an https site, in
  capitals, https on an https site, http on an http site, no address).
- `Audience_Test`: `test_a_visitor_who_is_not_signed_in_is_counted`,
  `test_a_page_not_read_as_itself_is_not_counted` (a preview, the Customizer, an embed),
  `test_an_amp_copy_is_not_counted`, `test_a_page_the_amp_plugin_serves_as_itself_is_counted`,
  `test_a_page_on_a_site_without_the_amp_plugin_is_counted`,
  `test_a_signed_in_person_is_left_out_by_default`,
  `test_a_signed_in_person_with_a_ticked_role_is_counted`,
  `test_a_signed_in_person_without_a_role_here_is_left_out`,
  `test_roles_that_are_not_text_are_ignored`.
- `Tracker_Tag_Test`: `test_it_hooks_enqueueing_and_attributes`,
  `test_the_beacon_is_enqueued_async_in_the_head_without_a_version`,
  `test_nothing_is_enqueued_until_counting_is_on`,
  `test_nothing_is_enqueued_for_somebody_signed_in`,
  `test_the_beacons_tag_carries_its_attributes`, `test_data_cfasync_comes_first_and_is_false`,
  `test_other_tags_are_left_alone`.
- `No_Script_Image_Test`: `test_it_hooks_the_body_and_the_footer`,
  `test_the_source_names_the_website_and_the_page`, `test_the_page_cannot_break_out_of_the_address`,
  `test_the_page_is_the_sites_origin_and_the_requested_path`, `test_the_page_keeps_the_sites_port`,
  `test_a_full_address_in_the_request_names_this_site`, `test_control_characters_are_removed`,
  `test_no_request_path_means_the_home_page`, `test_the_image_is_printed_with_an_escaped_address`,
  `test_the_footer_prints_nothing_once_the_body_has_it`,
  `test_the_footer_prints_the_image_for_older_themes`,
  `test_nothing_is_printed_for_somebody_not_counted`,
  `test_nothing_is_printed_until_counting_is_on`.
- `Optimiser_Exclusions_Test`: `test_each_filter_is_hooked` (all fifteen filters, each to the
  callback for its kind of list), `test_the_address_is_added_to_the_list`,
  `test_the_address_is_not_added_twice`, `test_the_path_is_added_to_the_list`,
  `test_the_handle_and_the_element_id_are_added`,
  `test_something_that_is_not_a_list_is_left_alone`,
  `test_nothing_is_added_until_the_address_is_saved`,
  `test_autoptimize_string_exclusions_gain_the_address`,
  `test_autoptimize_array_exclusions_keep_their_flags`,
  `test_autoptimize_exclusions_of_another_shape_are_left_alone`.
- `Tag_Check_Test`: `test_it_hooks_the_daily_check_and_settings_changes`,
  `test_it_hooks_the_button_and_scheduling_in_the_dashboard`,
  `test_the_daily_check_is_scheduled_when_it_is_not_yet`,
  `test_a_scheduled_check_is_not_scheduled_again`, `test_unscheduling_clears_the_event`,
  `test_nothing_is_checked_until_counting_is_on`, `test_the_home_page_is_fetched_as_the_plugin`,
  `test_a_failed_fetch_is_kept_as_unreachable`, `test_an_error_page_is_kept_as_unreachable`,
  `test_a_redirect_to_another_host_is_not_followed`,
  `test_redirects_within_the_site_are_followed_a_few_times`, `test_the_last_outcome_is_read_back`,
  `test_an_unreadable_outcome_reads_as_never_checked`,
  `test_saving_the_same_connection_keeps_the_outcome`, `test_the_first_save_checks_soon`,
  `test_adding_the_empty_settings_checks_nothing`,
  `test_a_new_connection_forgets_the_outcome_and_checks_again`,
  `test_the_button_refuses_somebody_without_the_capability`,
  `test_the_button_runs_the_check_and_returns_to_the_settings_page`,
  `test_each_outcome_is_told_in_plain_words`, `test_an_insecure_outcome_is_read_back`, and, with a
  stand-in for WordPress's HTML API that finds the plugin's tag,
  `test_a_tag_loading_over_http_on_a_secure_page_is_insecure`,
  `test_a_tag_loading_over_http_on_a_page_served_over_http_is_intact` and
  `test_the_page_is_judged_by_the_address_that_served_it`.
- `Settings_Page_Test`: `test_it_hooks_the_page_the_reminder_and_the_link`,
  `test_the_page_is_added_under_settings`, `test_no_warning_applies_to_an_ordinary_site`,
  `test_an_http_address_on_an_https_site_is_warned_about`,
  `test_an_http_address_on_an_http_site_needs_no_warning`, `test_plain_permalinks_are_warned_about`,
  `test_open_registration_is_warned_about_when_its_role_is_left_out`,
  `test_open_registration_is_fine_when_its_role_is_counted`, `test_the_settings_link_comes_first`,
  `test_links_of_another_shape_are_left_alone`,
  `test_the_reminder_shows_on_the_plugins_screen_until_setup_is_done`,
  `test_the_reminder_asks_for_what_is_missing` (both, the Address, the Website ID),
  `test_the_reminder_leaves_a_missing_address_to_somebody_allowed_to_change_it`,
  `test_the_reminder_asks_anybody_who_manages_the_site_for_a_missing_website_id`,
  `test_the_reminder_stays_away_where_it_does_not_belong`,
  `test_the_page_shows_the_status_and_the_last_check`,
  `test_the_page_shows_counting_off_before_setup`, `test_the_page_shows_the_warnings_that_apply`,
  `test_the_page_says_how_to_unblock_an_http_address`,
  `test_the_warning_sends_somebody_who_cannot_change_the_address_to_who_can`,
  `test_the_address_can_be_changed_by_somebody_allowed_to_add_code`,
  `test_the_address_is_read_only_for_somebody_not_allowed_to_add_code`,
  `test_on_a_network_the_note_names_a_network_administrator`,
  `test_the_role_checkboxes_tick_the_counted_roles`.
- `Plugin_Test`: `test_a_visitors_page_gets_the_tag_and_not_the_settings_page`,
  `test_the_dashboard_gets_the_settings_page_and_not_the_tag`,
  `test_activation_creates_the_settings_and_schedules_the_check`,
  `test_deactivation_removes_the_check_and_keeps_the_settings`.
- `Uninstall_Test`: `test_a_single_site_forgets_everything`,
  `test_every_site_on_a_network_forgets_everything`.

The smoke harness, `integrations/wordpress-plugin/tests/smoke/run.sh`, at `floor` and at `newest`,
on the archive as it was unpacked. Each run asserts six things, and wherever it reads markup, reads
it with WordPress's own HTML API, through `tests/smoke/inspect.php`:

- **The first save of the settings makes a check of the tag due at once**: the setting registered
  through the plugin's own method, as the dashboard registers it, then saved through
  `update_option()`, as the settings page saves it, over the defaults activation stored — which
  WordPress turns into adding the option. The Address is saved, and the next check of the tag,
  which activation set an hour away, is due at once.
- **A visitor receives the tag and the no-script image**: from the home page fetched anonymously,
  exactly one script loading `{Address}/dw.js`, with its `id`, `data-site`, absolute
  `data-collector`, `data-cfasync="false"`, every opt-out with its value, `async`, the `type` and
  the form of a lone attribute each WordPress writes, and no `integrity` or `crossorigin`; and
  exactly one image naming the website and the page's address as `u`.
- **The page parts rendered for nobody carry the tag and the image.**
- **The page parts rendered for the administrator carry neither.**
- **The tag check judges the page and its altered copies correctly**: intact as delivered; WP
  Rocket's and LiteSpeed Cache's delaying types, and a consent tool's, each named; a copy moved to
  the site's own host read as an optimiser; a snippet pasted into the theme again read as a
  duplicate; a page whose tag was never printed read as missing; and the page an `http` Address
  prints read as `insecure` served over `https` and as intact over `http`. The tag check's reading
  of markup needs WordPress's HTML API, so this is where it is tested.
- **The daily check fetches the home page and keeps `intact`**, run end to end inside WordPress.

The dashboard:

- `tracking-code.test.tsx`, "the steps for a WordPress website": offered beside the way that works
  on any website; numbers what there is to do, in the order it is done; downloads the plugin from
  the dashboard and says which version it is; names each thing to press the way WordPress does;
  gives the address and the website identifier to paste into WordPress; copies each on its own and
  says which was copied; puts the two lines back when any website is chosen again; is still chosen
  when the panel is opened again. The six tests of the snippet are unchanged.
- `copy-button.test.tsx`, the copy control the panel and server keys share: hands the value over and
  says so, tells a screen reader it worked, goes back to offering a copy, says nothing alarming when
  the browser refuses, and says what it copies where it is told.
- `proxy.test.ts` and `routes.test.ts`: `/downloads/dewiride-analytics.zip` is neither forwarded to
  the engine nor redirected, is no screen and is no file of a website put in front.

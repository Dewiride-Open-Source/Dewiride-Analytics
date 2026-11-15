# Dewiride Analytics for WordPress

The WordPress plugin that counts the people who read a site with Dewiride Analytics. It is
**MIT**, like the tracker it loads: it is installed into other people's sites, where a copyleft
licence is not a reasonable thing to ask anybody to accept.

The owner pastes two values — the **Address** they open Dewiride Analytics on and the **Website
ID** — into **Settings → Dewiride Analytics**, and the plugin then:

- prints the tracker's tag (`dw.js`) on every page a counted visitor reads, `async` in the head,
  with the attributes optimisation plugins read as "leave this alone", and adds the tag to the
  exclusion lists of the optimisers that take one as a filter;
- prints the `<noscript>` image that counts a reader whose browser runs no scripts, naming the
  page exactly;
- leaves out signed-in people unless one of their roles is ticked, and never counts previews, the
  Customizer, embeds or AMP pages;
- fetches the site's own home page once a day, and when asked, to check that visitors receive the
  tag as printed — naming the tool that changed it when it can — and keeps only the outcome.

With a **server key** saved as well, the site's own server reports what reaches WordPress to
`/collect/server` (`docs/server-reporting.md`, ADR 0074): crawlers, feed readers, scanners and
everything else that never runs the tag.

- Each request worth reporting is sent once it has been answered: pages, feeds, robots.txt and
  sitemaps, and — when chosen — sign-in attempts and XML-RPC. Never the dashboard, the REST API,
  WP-CLI, cron, people signed in (unless their role is counted), a successful sign-in, a browser
  signing in, or the site's own requests. A visitor's headers (prefetch, `Sec-Fetch-Dest`,
  `HEAD`, the site's own user agents) leave a request out only when it was answered below 400;
  from 400 up, neither its method, the feeds switch, a REST query on another address nor a query
  asking for a preview, an embed or an AMP copy does — only who is signed in.
- A browser navigating to a page gets a `Server-Timing: dw;desc="…"` name, matched to the
  browser's own report — only once the installation has named the website in an answer, which is
  how an installation that reads names safely (ADR 0073) shows itself.
- The report is sent at `shutdown` after `fastcgi_finish_request()` or
  `litespeed_finish_request()`; where neither exists, reporting is off unless the second switch
  accepts that every robot's page is held open for it.
- Failures are waited out (30 s doubling to 15 minutes, one request trying again after each
  wait); 429 is waited out for its `Retry-After`; a website not being measured, or one that does
  not cover the site's host, is asked again after 15 minutes; a refused key, a redirect, another
  website's key or a saved key that no longer opens stop the reports until the settings change.
- The visitor's address is the connection's, Cloudflare's `CF-Connecting-IP` for a connection
  from Cloudflare's published ranges and one of the site's own names, or a named proxy's header
  read from its right-hand end; an address that is not public is never sent outside a `local` or
  `development` environment.
- The key is kept sealed with `sodium_crypto_secretbox` under a key derived from
  `wp_salt( 'auth' )`, never autoloaded, or in `DEWIRIDE_ANALYTICS_SERVER_KEY` on a single site.

## What ships

Exactly these files, and nothing else under them:

```text
dewiride-analytics.php
uninstall.php
readme.txt
LICENSE
includes/class-plugin.php
includes/class-private-host.php
includes/class-settings.php
includes/class-settings-page.php
includes/class-audience.php
includes/class-tracker-tag.php
includes/class-optimiser-exclusions.php
includes/class-no-script-image.php
includes/class-tag-check.php
includes/class-request-headers.php
includes/class-address-range.php
includes/class-own-request.php
includes/class-server-key.php
includes/class-cloudflare-ranges.php
includes/class-visitor-address.php
includes/class-stored-option.php
includes/class-reporting-status.php
includes/class-collector-client.php
includes/class-request-report.php
includes/class-server-reporting.php
includes/class-connection-check.php
includes/class-privacy-policy.php
includes/class-server-settings.php
```

The packaging script names every file one at a time and fails when a file under those roots is
missing from its list, so a new class is added to the list in the same change that adds it, and
`require_once`d by name in `dewiride-analytics.php`. Everything else here — Composer, the
checkers' configuration, the tests — is for working on the plugin and never reaches a site.

The version is written in four places that must agree, which the packaging script checks: the
`Version:` header and the `DEWIRIDE_ANALYTICS_VERSION` constant in `dewiride-analytics.php`,
`Stable tag:` in `readme.txt`, and the dashboard's download link.

## Publishing a version

A version reaches people two ways: the dashboard hands out the archive its image was built with,
and the WordPress.org plugin directory offers it to every copy as an ordinary WordPress update,
however the copy was installed. A change to any shipped file therefore comes with a new version in
all four places, so that a version number never names two different sets of files.

The directory is published to by **WordPress plugin release**
(`.github/workflows/wordpress-plugin-release.yml`), started by hand on `main` from the Actions tab.
It refuses to start unless the `wordpress-org` environment is protected as below, runs the
plugin's checks against that commit and keeps the archive they passed, and then waits for a
reviewer of the environment to approve it. Approved, it packages the commit again, refuses unless
that is the checked archive file for file, and replaces the directory's `trunk` with it, copies it
to `tags/<version>`, replaces the listing's pictures with `.wordpress-org/`, and commits all of it
at once. A version the directory already holds with the same files is left as it is and the run
succeeds; one it holds with other files fails the run, because the version has to change.
**dry run** prepares the same commit and shows it without making it.

The `wordpress-org` environment is created by hand before the workflow first runs — a run that
names an environment that does not exist creates it unprotected — under **Settings →
Environments**, with:

- **Required reviewers**: whoever approves a publication, with **Prevent self-review** left off when
  the only reviewer is also the one who starts the run;
- **Deployment branches and tags**: **Selected branches and tags**, with one rule, the branch
  `main`;
- **Allow administrators to bypass configured protection rules** turned off;
- two environment secrets, held nowhere else: `SVN_USERNAME`, the WordPress.org username exactly as
  its profile spells it, and `SVN_PASSWORD`, the Subversion password generated under **Account &
  Security → SVN credentials** in that profile — not the account's own password, and useless for
  anything but committing. Generating a new one stops the old one working, and the secret has to be
  replaced with it.

The workflow checks the first two and refuses to publish without them.

`.wordpress-org/` holds what the listing shows and never ships: `icon.svg` with `icon-128x128.png`
and `icon-256x256.png` (the dashboard's own icon), `banner-772x250.png` and
`banner-1544x500.png`, and `screenshot-N.png`, each captioned by line N under `== Screenshots ==`
in `readme.txt`. The directory reads its listing from `readme.txt` in the published tag, so a
change to the readme alone — a newer `Tested up to:` among them — reaches the listing with the
next version.

## Conventions

- **PHP 7.4 and WordPress 6.4 are the floor.** Everything used exists natively there, so there is
  no `function_exists()` shim anywhere. PHPCompatibility checks the syntax from 7.4 upwards.
- **The WordPress Coding Standards, in full.** Tabs, `snake_case`, Yoda conditions and
  `class-*.php` file names are WordPress's conventions, and PHPCS checks them against the WordPress
  Coding Standards (`composer lint`). A suppression names its sniff and gives its reason on the
  same line.
- **Plugin Check is the WordPress.org directory's own review**, run on a plugin before the
  directory accepts it. It checks what the directory requires — among them the plugin's headers
  and `readme.txt`, escaped output, sanitised settings, translatable strings, a guard against a
  file being requested directly, and files that must not ship — and not how the code is laid out.
  The Build runs it on the plugin as it ships, strict, so a warning fails as an error does.
- **No runtime dependencies and no build step.** What is in the repository is what runs.
- **Every string goes through gettext with the literal text domain `dewiride-analytics`.** There
  is no `.pot` and no `load_plugin_textdomain()`: WordPress.org's language packs reach every
  installed copy by the plugin's slug.
- **Telemetry stays out of the database.** The plugin stores its settings, the server key sealed,
  the outcomes of its last checks, how the server's reports have been going — counts, times,
  statuses and fixed reasons, written only when they change — and Cloudflare's addresses. Never
  anything from a page or a visitor, and nothing on each request.

## Working on it

PHP and Composer need not be installed: everything runs through the official Docker images. From
this directory:

```bash
docker run --rm -v "$PWD:/app" -w /app composer:2 composer install
docker run --rm -v "$PWD:/app" -w /app composer:2 composer validate --strict
docker run --rm -v "$PWD:/app" -w /app composer:2 composer audit
docker run --rm -v "$PWD:/app" -w /app composer:2 composer lint      # PHPCS
docker run --rm -v "$PWD:/app" -w /app composer:2 composer analyse   # PHPStan, level max
docker run --rm -v "$PWD:/app" -w /app composer:2 composer test      # PHPUnit
docker run --rm -v "$PWD:/app" -w /app php:7.4-cli vendor/bin/phpunit
```

In Git Bash on Windows, put `MSYS_NO_PATHCONV=1` in front of each command and use `$(pwd -W)` for
`$PWD`.

`composer.lock` is resolved for PHP 7.4 (`config.platform.php`), so the same dependencies install
on the floor and on the newest PHP. The tools are development dependencies only, and some of them
are copyleft — PHPCSUtils, PHPCSExtra and PHPCompatibility under the LGPL, WordPress and Plugin
Check under the GPL. They are installed to check the plugin and never shipped with it.

### Unit tests

`tests/Unit/` runs without WordPress: Brain Monkey replaces WordPress's functions test by test. One
class of tests per class with logic, plus `uninstall.php`. What needs WordPress's HTML API —
reading a page's markup — is tested by the smoke harness instead.

### Smoke harness

`tests/smoke/run.sh` installs the plugin as it ships into a throwaway WordPress, saves its settings
the way the settings page does (the setting registered with its default, so the first save goes
through `add_option()`) and checks that the save makes a tag check due at once. It then checks, by
attribute and never by order, what an anonymous visitor and a signed-in administrator receive,
runs the tag check's parser over the page as delivered, over copies changed the way optimisers,
consent tools and pasted snippets change it and over the page an address saved with http produces,
and runs the daily check end to end.

It then checks the server's own reports against `collector.mjs`, a stand-in for the installation
that serves `dw.js`, records every batch it is sent and answers as the collector does. A must-use
plugin (`mu-plugins/dewiride-smoke.php`) points the server's reports at it, and answers 403 to a
page asked for with `smoke_refuse`, as a security plugin would; the WordPress is a `local` one so
the harness's private addresses are sent. Each request is made by `visit.mjs` from the stand-in's
own container — WordPress's and WP-CLI's addresses are the site's own, which are never reported —
and checks that the page was named, reported or ignored as expected, with the key and the page's
address in every batch: nothing until holding pages open is accepted (both images run PHP inside
Apache), Check connection, crawlers, browsers, prefetches, redirects, `HEAD`, the REST API, the
favicon, missing files and probes, probes and refusals asked for as an embed or a preview, a feed
answered `304` and WordPress's sitemap, sign-in attempts and XML-RPC, the plugin's own requests, and
its own header, ignored when genuine and reported when forged.

```bash
PLUGIN_DIR=/absolute/path/to/dewiride-analytics bash tests/smoke/run.sh floor
PLUGIN_DIR=/absolute/path/to/dewiride-analytics bash tests/smoke/run.sh newest
```

`PLUGIN_DIR` is a folder holding only the shipped files — the extracted `dewiride-analytics/` of
the zip — so the harness tests what users install rather than the working tree. `floor` is the
newest WordPress 6.4 release on PHP 7.4 with a classic theme that has no HTML5 script support;
`newest` is the newest WordPress, release candidates included, on the newest PHP with the default
block theme. It needs Docker and nothing else, exits non-zero when an assertion fails, and removes
its containers and volumes whatever happens.

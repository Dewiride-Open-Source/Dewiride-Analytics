# Dewiride Analytics

Web analytics that tells you which of your visitors are people.

Most analytics products count requests and call the total "visitors". A growing share of that
total is automation — search crawlers, AI training and retrieval agents, uptime monitors, scrapers
and scanners — and the products that hide it are quietly reporting numbers nobody can act on.
Dewiride Analytics is built the other way round: for every session it sets out to answer _who or
what generated this traffic, why was it classified that way, and should it count as genuine human
engagement_ — with a category, a strength of evidence, and reasons you can read.

Two rules shape everything here:

- **Never claim more certainty than the evidence carries.** A classification carries a strength
  band, never a percentage, because there is no labelled dataset to calibrate a percentage
  against and a number would look like a measurement while being an opinion. "Unknown" is a
  permitted answer and gets shown as one.
- **Collect the minimum that answers the question.** No form contents, no keystrokes, no session
  recordings, no cookie banners to click through. Raw addresses are dropped after 72 hours and the
  key used to recognise a returning reader is rotated daily.

## Where this is today

This is early. The repository is public from the start, so this section says plainly what runs and
what does not.

**Working now**

- The whole stack starts with one command and comes up healthy on a laptop.
- A collection endpoint accepts page views, engagement reports and clicks, stamps its own authoritative
  timestamp alongside the reported one, and writes them to the telemetry store.
- Sign-in, several accounts on one installation, and three roles with a real membership check.
- Looking after a website once it is there: change what it is called, change the time zone its days
  are counted in, or remove it. Removing a website deletes everything ever measured for it and
  nothing brings it back, so it asks you to type the website's address before it will go ahead.
  Anyone who can change a website's settings can rename it; removing one is the owner's alone.
- A dashboard showing page views, daily visitors, a daily traffic graph and the pages a period's
  traffic went to, for a website you own. A site running both the tracker and its own server's
  reports is counted once per page delivered rather than once per report, so running the product
  properly does not double what it tells you. Traffic arriving through a pool of rented addresses
  is counted as the one operator it is, rather than as a fresh person on every request. A page
  counts as read the moment anything at all is reported about it, so a reader whose arrival never
  reached us still counts for the page they were on — once, however long they stayed.
- Where your readers are, by country, by town, and by the network they arrived over, ranked by how
  many people were in each place rather than by how much browsing they did. Towns are named as an
  estimate, because that is what they are; the network view is there because a hundred rented
  servers in one datacentre read as an ordinary country until the network is named. The country,
  town and network-operator data is fetched from
  [DB-IP](https://db-ip.com) and [iptoasn.com](https://iptoasn.com) after the product starts,
  rather than shipped inside it, and an installation with no route to the internet counts traffic
  exactly the same and reports every country as not known.
- What your readers use: the split between computers, phones and tablets, and the browsers and
  operating systems behind it. Every one of those is worked out from what a browser volunteers
  about itself and from its user agent, so the visits that say nothing are counted as not known
  rather than guessed at — on most sites those are the visits that were never a person.
- How your pages are actually read: the typical time a page holds somebody, how far down they got,
  and how many did anything at all — for the whole site, and page by page. Only a browser can see
  any of this, so every figure states how many of a period's readings it could be taken from, and a
  site measured only from its own server reads as unmeasured rather than as an audience that did
  nothing. The tracker reports how a reading is going while it is still going, so a reader whose
  browser is closed without warning still counts for what they had read by then.
- How people move through your site: the pages visits begin and end on, how many pages a visit
  takes, and how many visits read a single page and nothing else. Worked out from the traffic
  itself rather than from what has been judged, so it keeps step with the headline totals — and
  only visits that have actually finished are counted, because one still under way has not decided
  yet how many pages it will read.
- The path any one visitor took. Open a judged visit and it lists the pages in order, with how long
  each held them, how far down they got, and — where your own server reported the request — what
  your site answered with. A sweep for a way in reads as what it is: a handful of addresses that do
  not exist, asked for in under a second, with nothing measuring how any of them was read.
- What people clicked: every link, button and field somebody uses, ranked by how often — and,
  separately, the places off your site that readers followed a link to. What is kept of a click is
  your own wording on the control and where it pointed; never anything a visitor typed, and nothing
  at all about where on the screen they pressed. It is on by default, it can be switched off for a
  website from the dashboard, and a `data-dw-ignore` attribute keeps any part of a page out of it.
- A tracker you paste into your own site, and an image fallback for readers whose browsers run no
  scripts. The dashboard hands you both lines with your address already filled in.
- A collection endpoint your own server can report to, so the traffic that never runs a script —
  crawlers, feed readers, scanners probing for paths that do not exist — is counted too, with the
  status your site returned. Keys are created and withdrawn in the dashboard; the wire format is
  in [`docs/server-reporting.md`](docs/server-reporting.md).

- Traffic that has been judged: activity is grouped into visits — once each, from the visit's own
  beginning rather than from wherever the engine happened to resume — each finished visit is examined,
  and the answer — somebody reading, a crawler that says it is an AI one, a search crawler, a sweep
  for a way in, or an honest "not enough to say" — is stored with the specific reasons behind it,
  including the ones that pointed the other way. A crawler's name is never treated as proof of who
  sent it; until the address it came from has been checked against what the operator publishes, the
  answer says it is only a claim. "Not enough to say" is kept for a visit that never named a page
  at all, rather than for one whose arrival went missing — everything a visit reported about a page
  is weighed, whether or not the report announcing the page arrived. The verdicts are stamped with
  the rules that produced them, so a number can still be explained after the rules improve.
- A breakdown of who a period's visitors were on the dashboard, and every individual visit on a
  screen of its own — **User journey** — with the whole case behind each verdict. Narrow it to the
  visits you came for: what generated them, how much evidence stands behind saying so, and how much
  of the site they read. Only the conclusions your own traffic actually reached are offered, with
  how many of each there were. The list is paged with numbers you can jump straight to and a choice
  of how many to show at once, because a verdict nobody can reach is a verdict nobody can question.
  Each observation is a written sentence rather than a code, and the strength of the evidence is a
  band shown beside the category — never a percentage. A judged visit only appears once it has
  finished, so that screen trails the headline totals and says so.

**Not built yet**

- **Checking a crawler's claim against its operator's published addresses.** Until that exists
  nothing is ever reported as a confirmed identity, which is why every recognised crawler is
  reported as suspected.
- Ready-made reporters for Cloudflare, WordPress, Netlify and Vercel. The endpoint they will use
  exists and is documented; writing one against it today is a few dozen lines.

## Running it

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine with
the Compose plugin). Nothing else — the .NET and Node toolchains are only needed if you want to
run the pieces outside containers.

1. **Get the code.**

   ```bash
   git clone https://github.com/Dewiride-Open-Source/Dewiride-Analytics.git
   cd Dewiride-Analytics
   ```

2. **Create your settings file.**

   ```bash
   cp .env.example .env
   ```

3. **Set the two passwords.** Open `.env` and fill in `POSTGRES_PASSWORD` and
   `CLICKHOUSE_PASSWORD` with values of your own. They are deliberately blank: a working default
   password is a default password somebody ships to production. The stack refuses to start until
   both are set.

4. **Start everything.**

   ```bash
   docker compose up --wait
   ```

   The first run builds two images and takes a few minutes. `--wait` returns once every service
   reports healthy, so a successful exit means the stack is genuinely up rather than merely
   started.

5. **Open the dashboard** at <http://localhost:3000>.

   Nobody has claimed this installation yet, so you get a one-time welcome screen. Fill it in and
   you are signed in as the owner. That screen is only offered once and can never be used again —
   the first person to arrive becomes the owner, and it takes a database lock so two people
   arriving together cannot both win.

6. **Put the tracker on your site.** Choose **Tracking code** on the dashboard and paste the two
   lines it gives you into your website's pages. Traffic appears as soon as somebody visits.

   The address in those lines is the one you are reading the dashboard on, so a site on the
   internet needs a dashboard the internet can reach.

To stop: `docker compose down`. To start over from nothing, including wiping the data:
`docker compose down --volumes`.

### When the tracker is installed and nothing appears

A website only accepts reports from its own address and addresses below it. That check is what
stops a stranger writing traffic into your numbers — the site identifier is printed in the source
of every page it measures, so anyone can read one — but it also means a site registered as
`example.com` discards everything sent from `localhost` while you develop against it, and the
collector answers exactly as it does for a report it accepted. Nothing is visibly wrong; the
dashboard simply stays at zero.

To see why, set `ENGINE_LOG_LEVEL=Debug` in `.env` and restart the engine:

```bash
docker compose up -d api
docker compose logs -f api
```

Every refused report then names the address it came from and the address the website is
registered as. Put the setting back to `Information` afterwards: the collector answers anybody,
so a line per refused report is a log whose size is decided by whoever is scanning the internet
that day.

While developing against a website you run locally, register a second website whose address is
`localhost` and use that one's tracking code.

### Memory

Roughly 2.5 GB with all four services running, most of which is the telemetry store. Its caches
are sized against `CLICKHOUSE_MEMORY` in `.env`, so lowering that figure is how you fit the stack
onto a smaller machine.

### Ports

| Address                                 | What it is                         |
| --------------------------------------- | ---------------------------------- |
| [localhost:3000](http://localhost:3000) | The dashboard                      |
| [localhost:8080](http://localhost:8080) | The engine: collection and data    |
| localhost:5432                          | PostgreSQL — accounts and settings |
| localhost:8123                          | ClickHouse — the traffic itself    |

Change any of them in `.env` if something else on your machine already has the port.

All four answer on this machine only. That matters on a rented server rather than a laptop:
Docker publishes a port by inserting its own forwarding rules ahead of the ones `ufw` and
`firewalld` manage, so a port published without an address answers the whole internet no matter
what the firewall was told to allow. If you put the product on a server, run a reverse proxy in
front of the dashboard to terminate TLS — signing in sets a cookie the browser only sends back
over the connection that set it — and leave the two stores where they are. `WEB_BIND` in `.env`
is there for the one case that needs it: a proxy running on a different machine.

### Putting your own website in front of it

Every screen lives under `/app`, and nothing occupies the root — so if you would rather have your
own front page on the same address you read the dashboard on, set `SITE_ORIGIN` in `.env` to
wherever it answers. Anything that names neither a screen nor one of the engine's addresses is
forwarded there, including your `robots.txt` and your sitemap.

It has to be reachable from the stack's own network: another container on it
(`http://mysite:3000`), or an address on the machine itself
(`http://host.docker.internal:8080`). Leave it empty, which is the ordinary case, and the root
simply leads to the dashboard.

### Copying it somewhere safe

`deploy/backup.sh` writes both stores to a directory you name, and `deploy/restore.sh` puts one of
those copies back. They use different methods, and it is not a matter of taste: the control plane
holds accounts and settings, where a logical dump restores cleanly into a later version of
PostgreSQL, and the telemetry store holds an append-mostly table where a logical dump would be
absurd.

```bash
./deploy/backup.sh /var/backups/dewiride
```

Run it from a timer, keep the copies on a machine that is not this one, and put one back before you
need to — a backup nobody has restored is a guess. `DEWIRIDE_COMPOSE_PROJECT` lets `restore.sh`
write into a second stack on empty volumes, which is how to prove a copy is worth something without
touching the installation you are trying to protect.

## What's inside

| Directory   | What lives there                                                        |
| ----------- | ----------------------------------------------------------------------- |
| `backend/`  | The engine: collection, the query surface, accounts, and classification |
| `frontend/` | The dashboard                                                           |
| `config/`   | Service tuning that is mounted into the containers                      |
| `tracker/`  | The browser beacon and its no-JavaScript fallback, MIT rather than AGPL |
| `docs/`     | How to report from your own server, and the decisions behind the design |

Folders for the hosting-platform integrations, the traffic generator and the cloud deployment
description arrive with the work that fills them. A directory with nothing in it is a promise, not
a feature.

Two stores, deliberately. PostgreSQL holds accounts, websites, settings and the job queue, where
records are updated and relationships matter. ClickHouse holds the telemetry, which is written
constantly, never edited, and queried by scanning columns across long ranges. Neither one is good
at the other's job.

## Licence

The engine, the dashboard, and everything that decides what your traffic is are
**[AGPL-3.0-only](LICENSE)** — free software, permanently. You may run it, read it, change it and
self-host it, including for your own commercial purposes. If you run a modified version as a
service for other people, the AGPL asks you to offer them your changes.

`tracker/` and `integrations/` are **MIT** instead, because they are pasted into other people's
websites and a copyleft beacon is not a reasonable thing to ask somebody to embed.

Self-hosting is not a crippled tier. The full detection engine, every screen, and unlimited
websites, traffic and retention are free. What the hosted service adds is the running of it —
and analysis that only exists because it sees many customers' traffic at once, which no single
installation could produce for itself. That part is commercial, lives in its own repository, and
is not required to build or run anything here.

Attributions for the data and libraries this depends on are in [`NOTICE`](NOTICE).

## Contributing

Contributions are welcome. Signing the [contributor licence agreement](CLA.md) is required before
a pull request can be merged — it lets the project offer the commercial edition alongside the free
one without asking every contributor for permission each time.

"Dewiride" and the Dewiride logo are trademarks and are not covered by the software licence; see
[`TRADEMARK.md`](TRADEMARK.md).

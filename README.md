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
- A collection endpoint accepts page views and engagement reports, stamps its own authoritative
  timestamp alongside the reported one, and writes them to the telemetry store.
- Sign-in, several accounts on one installation, and three roles with a real membership check.
- A dashboard showing page views, daily visitors and a daily traffic graph for a website you own.

**Not built yet**

- **The browser tracker.** Until it exists there is nothing to paste into a website, so the only
  way to send traffic is to call the collection endpoint yourself. A real site will show zeros.
- **The classification engine.** Nothing is categorised as human or automated yet — that is the
  point of the product and it is the next substantial piece of work.
- Capture from Cloudflare, WordPress, Netlify and Vercel.

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

To stop: `docker compose down`. To start over from nothing, including wiping the data:
`docker compose down --volumes`.

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

## What's inside

| Directory   | What lives there                                                        |
| ----------- | ----------------------------------------------------------------------- |
| `backend/`  | The engine: collection, the query surface, accounts, and classification |
| `frontend/` | The dashboard                                                           |
| `config/`   | Service tuning that is mounted into the containers                      |
| `tracker/`  | The browser beacon and its no-JavaScript fallback — licence only so far |
| `ee/`       | Commercially licensed extras — see below                                |

Folders for the hosting-platform integrations, the traffic generator, the cloud deployment
description and the decision records arrive with the work that fills them. A directory with
nothing in it is a promise, not a feature.

Two stores, deliberately. PostgreSQL holds accounts, websites, settings and the job queue, where
records are updated and relationships matter. ClickHouse holds the telemetry, which is written
constantly, never edited, and queried by scanning columns across long ranges. Neither one is good
at the other's job.

## Licence

The engine, the dashboard, and everything that decides what your traffic is are
**[AGPL-3.0-only](LICENSE)** — free software, permanently. You may run it, read it, change it and
self-host it, including for your own commercial purposes. If you run a modified version as a
service for other people, the AGPL asks you to offer them your changes.

- `tracker/` and `integrations/` are **MIT**, because they are pasted into other people's
  websites and a copyleft beacon is not a reasonable thing to ask somebody to embed.
- `ee/` is **not** free software. It holds what only exists because we run this as a hosted
  service — billing, single sign-on, directory synchronisation, alerting, and analysis across more
  than one customer's traffic. Its terms are in [`ee/COPYING.txt`](ee/COPYING.txt). Everything
  under `ee/` is excluded from the free build, and the build fails if free code reaches into it.

Self-hosting is not a crippled tier. The full detection engine, every screen, and unlimited
websites, traffic and retention are free.

Attributions for the data and libraries this depends on are in [`NOTICE`](NOTICE).

## Contributing

Contributions are welcome. Signing the [contributor licence agreement](CLA.md) is required before
a pull request can be merged — it lets the project offer the commercial edition alongside the free
one without asking every contributor for permission each time.

"Dewiride" and the Dewiride logo are trademarks and are not covered by the software licence; see
[`TRADEMARK.md`](TRADEMARK.md).

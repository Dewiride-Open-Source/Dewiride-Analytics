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
  recordings, no cookie banners to click through. Raw addresses are dropped after 72 hours, and
  the key that tells one visitor's activity from another's is worked out with a secret that
  changes every day, so once an address is gone there is no key that joins a reader's activity
  from one day to the next — and no screen or figure ever tries to.

## Where this is today

This is early. The repository is public from the start, so this section says plainly what runs,
and the [master plan](#master-plan) below it says what does not yet.

**Working now**

- The whole stack starts with one command and comes up healthy on a laptop.
- A collection endpoint accepts page views, engagement reports and clicks, stamps its own authoritative
  timestamp alongside the reported one, and writes them to the telemetry store.
- Sign-in, several accounts on one installation, and three roles with a real membership check.
- Looking after a website once it is there: change what it is called, change the time zone its days
  are counted in, or remove it — unless it is the only one, since an account needs at least one
  website. Removing a website deletes everything ever measured for it and nothing brings it back,
  so it asks you to type the website's address before it will go ahead. Anyone who can change a
  website's settings can rename it; removing one is the owner's alone.
- A dashboard showing page views, daily visitors, a daily traffic graph whose days can be pressed
  to look at one on its own, and the pages a period's traffic went to, for a website you own. A
  site running both the tracker and its own server's reports is counted once per page delivered
  rather than once per report, so running the product properly does not double what it tells you.
  Traffic arriving through a pool of rented addresses is counted as the one operator it is, rather
  than as a fresh person on every request. A page is counted once, when a visitor arrives on it,
  however many reports follow about how it was read; a reading whose arrival never reached us
  adds no page view and opens no visit, because nothing on it says which visit it belonged to. It
  still counts in how your pages are read, because there it is the only measurement of that page
  there is.
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
  sent it: where the address it arrived from is one the company itself vouches for, the visit is
  reported as that company's crawler and marked confirmed, and where it is not, the answer says
  plainly that this is only what the visitor called itself. A visit begins where somebody arrived, so
  a page announcing that it is being left hours later belongs to the visit it came from rather than
  being counted as a second person who read for a quarter of an hour and left. The verdicts are
  stamped with the rules that produced them, so a number can still be explained after the rules
  improve.
- A breakdown of who a period's visitors were on the dashboard, and every individual visit on a
  screen of its own — **User journey** — with the whole case behind each verdict. Narrow it to the
  visits you came for: what generated them, how much evidence stands behind saying so, and how much
  of the site they read. Only the conclusions your own traffic actually reached are offered, with
  how many of each there were. The list is paged with numbers you can jump straight to and a choice
  of how many to show at once, because a verdict nobody can reach is a verdict nobody can question.
  Each observation is a written sentence rather than a code, and the strength of the evidence is a
  band shown beside the category — never a percentage. A judged visit only appears once it has
  finished, so that screen trails the headline totals by at least the half hour of quiet that
  ends a visit.
- Every figure on the overview and the whole User journey list can be kept to the visits judged to
  be people, with one switch beside the period. It is the same arithmetic over fewer visits, so the
  cards, the graph and every list add up to each other, and the choice travels in the address with
  the period so a link opens on exactly what was being looked at.

- Two ways of establishing that a crawler really is whose crawler it says it is, which between
  them are the only basis on which a visit is ever reported as confirmed. Most companies publish a
  list of the addresses their crawlers connect from, and those are fetched from each company's own
  page twice a day and kept on a volume. The rest publish a domain their machines answer to, and
  that is settled a visit at a time by asking what the address is called and whether that name
  points back at it — a pair of questions only the company could arrange the answers to. Both reach
  crawlers that give no name at all: the largest search engine's fetches arrive behind an ordinary
  browser string. Only visits that already look like machinery are ever asked about, so a reader's
  address is never sent anywhere. An install with no way out to the internet can be given the lists
  by hand, or go without either check: traffic is measured the same, and every crawler simply reads
  as a claim.

## Master plan

Every capability the product is meant to have, phase by phase, in the order the work is picked
up. Each line is marked with one of four states. Last reviewed 2026-09-19.

- **Done** — shipped and visible on the dashboard, or collected on every capture surface.
- **Partly done** — a real piece ships; the line says what is still missing.
- **In progress** — being built now.
- **Not started** — nothing in the tree yet.

### Phase 1 — Public statements

- **Done** · README: retention is 12 months, not unlimited.
- **Done** · README: a page counts as read only when its arrival reached us.
- **Done** · README: multi-customer analysis is planned, not present.
- **Done** · README: the daily key does not recognise a returning reader.
- **Done** · README: a website cannot be removed when it is the only one; the User journey list
  trails the totals; the integrations' licence is stated for when they exist.
- **Done** · Public website: thirteen categories, not fourteen; "Fake traffic" no longer
  documented; category names match the dashboard; each verdict and strength band described as
  the engine reaches it; "An uptime check" grouped with named machinery.
- **Done** · Public website: the home-page illustration gives only reasons the engine produces.
- **Done** · Public website, privacy: two cookies, not one; the region is kept; fields count as
  clicks; the address may be looked up in the name system to confirm a crawler.
- **Done** · Public website, pricing and terms: the billing currency follows the first website's
  time zone, not the billing address.
- **Done** · Trademark note, tracker README and server-reporting guide: no licence or document
  cited that does not exist; reserved surface names marked as not yet shipped.
- **Done** · Private repository README: clustering, the intelligence feed, single sign-on and
  directory sync marked as not built.
- **Not started** · Terms and privacy promise 30 days' retention after an account is closed, but
  nothing closes an account — either build closure with that retention, or reword the promise.
  Owner's call.
- **Not started** · CLA note says signing is checked automatically on every pull request —
  confirm the checker is installed on the repository, or reword. Owner's call.

### Phase 2 — Behaviour signals

**2.1 What exists**

- **Done** · Very fast page traversal.
- **Done** · Very many pages in one visit.
- **Done** · Nothing read, scrolled or touched across several pages.
- **Done** · Declared automation flag; no script run; no language declared; probing for missing
  and sensitive paths.
- **Done** · Running a script is never taken as proof of a person.

**2.2 Weak spot to close**

- **Not started** · A headless browser with an ordinary Chrome name, its automation flag hidden, on
  an unlisted network, that runs the script and scrolls once is judged "A person · some signs"
  today.

**2.3 Timing**

- **Not started** · Highly regular intervals between pages.
- **Not started** · Identical timing patterns across visits.
- **Not started** · Time on each page (median dwell) as a signal.
- **Not started** · Tab visibility changes as a signal.

**2.4 Page order and structure**

- **Not started** · Tag, author, archive, category and pagination sweeps.
- **Not started** · Sequential, alphabetical and numeric traversal.
- **Not started** · Sitemap-like order; breadth-first and depth-first crawling.
- **Not started** · Following a contextual internal link as a human signal.
- **Not started** · Arriving from a search engine as a human signal.
- **Not started** · Progressive scrolling, not just the deepest point reached.

**2.5 Journey types**

- **Not started** · Normal reader · search-to-article · homepage exploration · content discovery ·
  documentation exploration · crawler-style traversal · sequential crawling · repeated URL testing
  · brute-force paths · recursive link traversal.

**2.6 Exploration labels**

- **Not started** · Site enumeration · tag enumeration · author enumeration · archive enumeration
  · documentation-tree crawl · sequential URL exploration.
- **Not started** · A "Pattern detected" line on the visit.

**2.7 Intent grading**

- **Done** · Nothing is ever called an "attack"; neutral labels only.
- **Partly done** · Scraper, scanner, suspicious automation and unknown exist; "normal crawler"
  only through a recognised name.
- **Not started** · Aggressive crawler · potentially abusive · probable attack behaviour, the last
  with strong evidence only.
- **Not started** · Context sentences: "substantial traffic, no sign of exploit attempts".
- **Not started** · Guidance: ignore / investigate / block.

**2.8 Ghost and synthetic traffic**

- **Not started** · Reports with no matching page arrival flagged rather than dropped.
- **Not started** · Impossible combinations of browser characteristics.
- **Not started** · Unrealistic event sequences; repeated fabricated-looking reports.
- **Not started** · A "possible synthetic activity" verdict with reasons — this is what makes
  "Fake traffic" reachable.

### Phase 3 — Families and returners

**3.1 Grouping visits into families**

- **Not started** · Group visits by network, browser profile, navigation pattern, timing, page
  selection, interaction profile, recurrence and duration.
- **Not started** · A family card: visits over N days, share by country, related networks, share
  with identical navigation, share of the site reached, return cadence, family verdict and band.

**3.2 Returners**

- **Not started** · The same crawler returning: visits over 30 days, typical return interval.
- **Not started** · How quickly new pages are discovered — needs knowing when a page first
  appeared.
- **Not started** · Returns after new content or sitemap changes.

**3.3 Site coverage**

- **Not started** · An inventory of a website's known pages, from traffic, sitemap or feed.
- **Not started** · Share of known pages and of published articles a crawler has reached.
- **Not started** · Whether it reached every tag and author page; whether it touched admin or
  unlinked paths.

**3.4 How pages were discovered**

- **Not started** · Internal links · sitemap · feed · sequential · archive · tag · pagination ·
  guessed · unknown — worded as "consistent with", never as fact.

**3.5 Similarity and novelty**

- **Not started** · "Have I seen this before?" — similar past visits, count over 30 days, first
  and last seen, similarity as a band.
- **Not started** · New pattern detected: first seen, visits, origin, pages per visit, no known
  identity.
- **Not started** · "Similar visits active right now" on the live screen.

### Phase 4 — Collect traffic

**4.1 Website integrations**

- **Not started** · WordPress plugin.
- **Not started** · Cloudflare Worker.
- **Not started** · Netlify edge function.
- **Not started** · Vercel edge middleware.
- **Not started** · Next.js middleware.
- **Not started** · ASP.NET Core middleware.
- Until these exist real websites report from the browser only, so the server-side signals —
  scanners, scrapers, no script run — never fire outside the test suite.

**4.2 Browser tracker**

- **Done** · Page views, including sites that change page without reloading.
- **Done** · The page and site a visitor came from.
- **Done** · Time a page was actually on screen.
- **Done** · How far down a page was scrolled.
- **Done** · Whether the visitor clicked, tapped or typed — presence only, never content.
- **Done** · Screen size, language, time-zone offset, and whether the browser declares itself
  automated.
- **Done** · Clicks on links, buttons and fields — your own wording on the control, never what was
  typed; can be switched off per website.
- **Done** · Timestamps from the browser and the server, with drift reconciled.
- **Not started** · Campaign tags in links (utm and similar).
- **Not started** · Active time told apart from idle time — today only "on screen" is measured.
- **Not started** · Page-load timing.
- **Not started** · Repeat visits within a day shown as such. Across days is impossible by design.

**4.3 No-script image fallback**

- **Done** · One page view per image load, for browsers that run no scripts.

**4.4 Server-side reporting**

- **Done** · A keyed endpoint for a website's own server; keys created and withdrawn in the
  dashboard; wire format in [`docs/server-reporting.md`](docs/server-reporting.md).
- **Done** · Status code, content type, response size, address and user agent per request.
- **Partly done** · Response size is stored but never used or shown.

**4.5 Visits**

- **Done** · Activity grouped into visits: start, pages in order, landing page, exit page, time on
  each page, referrer, source kind, place, network, device, verdict, strength, reasons.
- **Done** · A browser report and a server report of the same page counted once.
- **Partly done** · End time and duration are computed but shown nowhere.
- **Not started** · Recognising visits that look alike or machine-made — Phase 3.

**4.6 Privacy**

- **Done** · Nothing typed is collected: no form contents, keystrokes or recordings.
- **Done** · Raw address dropped after 72 hours; visitor key rotated daily.
- **Done** · Towns named as estimates; only the three low-entropy browser hints read.
- **Not started** · Per-website option to keep query strings — exists in the engine, nothing can
  switch it on.

### Phase 5 — Numbers, views and comparisons

**5.1 Views of the numbers**

- **Done** · A "People only" switch that recalculates every card, chart and list on the overview
  and the User journey list.
- **Not started** · Further positions — Automated · Known crawlers · AI-related · Suspicious ·
  Unknown — with every number recalculating.
- **Not started** · Any number narrowed by country, page or source.

**5.2 Headline figures**

- **Done** · Page views, daily visitors, pages per visitor — and the same for people only.
- **Done** · Who's visiting: visits and pages per category with strength; share per tone.
- **Not started** · Single figures: automated visits, unknown visits, automated page views,
  crawler fetches, content reads.
- **Not started** · Human Traffic Score — the share of judged visits that were people, with its
  meaning stated beside it.
- **Not started** · Estimated authentic audience.

**5.3 Per page**

- **Done** · Views and visitors per page; people only; typical time, scroll depth and interaction
  per page.
- **Not started** · Per page: human reads · automated · AI crawler · search crawler · suspicious ·
  unknown · repeat crawler fetches.
- **Not started** · Per-page Human Traffic Score; average human reading time; estimated
  completion.
- **Not started** · Traffic origins per page; families per page.
- **Not started** · Page kinds: article, tag, author, category, archive, documentation, landing.

**5.4 Automation view**

- **Not started** · Automated visits and page views; top categories; top families; top networks;
  top countries; most-crawled pages; crawl frequency; site coverage; bandwidth.
- **Not started** · AI activity, search activity and suspicious automation as sections of their
  own.

**5.5 Human and automation side by side**

- **Partly done** · The reader flips the switch and compares from memory.
- **Not started** · Side by side for a page, country, source or period: pages per visit, active
  time, share with a referrer, share from datacentres.

**5.6 Trends**

- **Done** · Totals and people only over time; change against the period before; the earlier
  period as a dashed line; days that can be pressed.
- **Partly done** · The "Who came" chart stacks four tones over time; AI and search are folded
  into Machinery.
- **Not started** · AI crawlers and search crawlers as lines of their own.
- **Not started** · Changes for automated, suspicious and unknown traffic; Human Traffic Score
  over time.
- **Not started** · Calendar week against the previous week; calendar month against the previous
  month.
- **Not started** · Country and network distribution over time.
- **Not started** · "Traffic up, audience quality down" shown as one comparison.

**5.7 Resource use**

- **Done** · Pages per category.
- **Not started** · Bandwidth — stored today, never shown.
- **Not started** · Share of requests from automation; highest-volume crawler; repeated fetches.
- **Not started** · "Automation made 38% of requests and 7% of human reading" as one line.

### Phase 6 — AI analytics

- **Done** · AI crawler visits and pages on the breakdown; each AI visit's trail on the User
  journey list.
- **Not started** · Most AI-crawled articles.
- **Not started** · AI crawler requests over time.
- **Not started** · By provider, where confirmed.
- **Not started** · Content newly discovered by AI crawlers.
- **Not started** · AI revisit frequency.
- **Not started** · Share of the content catalogue reached by AI systems.
- **Not started** · Human reads, search fetches and AI fetches per article, side by side.

### Phase 7 — Judge each visit

**7.1 Categories**

- **Done** · A person · A search engine's crawler · An AI crawler · Says it's an AI crawler · A
  known service · An automated browser · A crawler · Copying your pages · An uptime check ·
  Probing for a way in · Something automated · Couldn't tell · Too little to go on.
- **Not started** · "Fake traffic" reachable — the label exists, the engine never produces it
  (see 2.8).
- **Not started** · Named uptime monitors (UptimeRobot, Pingdom, StatusCake, Better Stack) read as
  "An uptime check" rather than "A crawler".
- **Not started** · Named social previews and site tools read as "A known service" rather than
  "A crawler".
- **Not started** · A browser that announces itself headless reads as "An automated browser"
  rather than "A crawler".

**7.2 Strength and reasons**

- **Done** · Strength of evidence as a band — nothing to go on, slight signs, some signs, strong
  signs, confirmed — never a percentage.
- **Done** · Reasons for every verdict in plain sentences, with the ones pointing the other way
  shown too.
- **Done** · "Couldn't tell" and "Too little to go on" are honest answers and shown as such.
- **Done** · A person is never "verified"; "confirmed" only for a crawler proven by its operator's
  published addresses.
- **Not started** · A closing sentence saying why the evidence pointing the other way did not win
  ("scrolling alone is not enough, because automation can scroll").
- **Not started** · Saying on screen when a band was held back by contradicting evidence.

**7.3 Known crawlers**

- **Done** · 72 named crawlers from 26 operators: major search engines, SEO tools, Common Crawl,
  four uptime monitors, three social previews, the known AI crawlers.
- **Done** · Confirmation from published address lists fetched twice a day, and from the
  reverse-name check.
- **Done** · Confirmed name on the visit row; an unconfirmed name marked as a claim.
- **Not started** · Link checkers.
- **Not started** · Security tools by name.
- **Not started** · Internet Archive and other archive crawlers.
- **Not started** · X, LinkedIn, WhatsApp, Telegram, Discord, Mastodon and Bluesky previews.
- **Not started** · Naver, Qwant, Brave, Ecosia, Mojeek, PetalBot.
- **Not started** · Feed readers (Feedly, Inoreader, NewsBlur).
- **Not started** · A disposition beside each category: Expected / Investigate.

**7.4 AI crawlers**

- **Done** · A known AI crawler kept apart from "Says it's an AI crawler" on every screen.
- **Partly done** · Purpose — training, answering questions, AI search — appears only inside a
  sentence on the opened visit.
- **Not started** · Purpose as something you can filter and count.
- **Not started** · AI agent browsers recognised.
- **Not started** · AI crawlers by provider as a count.

**7.5 Network**

- **Done** · Country, town and network owner on every visit and on the places card, with the
  DB-IP credit.
- **Done** · Datacentre origin as a reason on the visit; hosting operators named in the Networks
  list.
- **Not started** · Region shown — stored, never shown.
- **Not started** · Network number shown.
- **Not started** · A "datacentre / hosting" label on rows and lists.
- **Not started** · Residential indication.
- **Not started** · Proxy and VPN indication, only where reliable.
- **Not started** · The places card framed as where visits arrived from, not where readers are.

**7.6 Keeping kinds apart**

- **Done** · Every verdict names its exact category; nothing collapses into "bots".
- **Partly done** · The ring and the time chart fold search, AI and services into one "Machinery"
  colour.
- **Not started** · Six groups on charts: Search / AI / Operational / Automation / Security /
  Unknown.
- **Not started** · Google, Bing and other search engines as separate counts.
- **Not started** · Synthetic testing told apart from uptime monitoring.

### Phase 8 — Investigation screens

**8.1 One visit**

- **Done** · Timeline with time on each page, scroll depth, status and controls pressed; who it
  was; reasons for and against; category and band.
- **Not started** · Duration shown; first seen shown on the live row.
- **Not started** · The User journey list says that the newest visits are still being judged.
- **Not started** · Labelled facts: script ran, scrolled, pointer, keyboard, tab visible.
- **Not started** · Was behaviour deterministic; seen before; which family — Phases 2 and 3.

**8.2 One country**

- **Partly done** · Places card by country, town and network; the User journey list narrowed by
  country.
- **Not started** · A country page: visits, human / automated / unknown share, networks, pages,
  families, patterns, datacentre share, trend, periodicity, what happened when the spike began.

**8.3 Direct traffic**

- **Partly done** · "Came straight here" shown beside each visit's verdict, place and network.
- **Not started** · How much direct traffic was automated, as a figure.
- **Not started** · Copy that says direct is an attribution category, not a person typing an
  address.
- **Not started** · Replace the current hint ("typed in, bookmarked, or from an app"), which
  suggests the opposite.

**8.4 Live**

- **Done** · Visitors now, pages being read, a trail per visitor. Only confirmed or self-declared
  machinery is named while a visit is under way — by design, so a person is never guessed at
  mid-read.
- **Not started** · Similar visits active — 3.5.

**8.5 Question-led entry points**

- **Not started** · Screens shaped around: why did this country spike · why did direct traffic
  spike · is this visitor human · is this crawler known · has it been here before · what did it
  consume · how fast · how much of the site · is it costing resources · does it look malicious ·
  are AI systems reading this · most human attention · most crawler attention · what to exclude.

### Phase 9 — Explanations and digests

- **Not started** · Spike detection by country, network, automated views, direct traffic, crawler
  coverage, visit frequency and Human Traffic Score, with neutral labels: unusual / suspicious /
  automated / investigate / potentially harmful.
- **Not started** · An explanation of a change: "traffic up 41%, human up 3%, the rest one family
  on Singapore hosting networks sweeping tag and article pages".
- **Not started** · An investigation summary for a period, country or family.
- **Not started** · The week's story in sentences.
- **Not started** · A daily digest — yesterday's totals by kind, what changed, new crawlers, AI
  crawlers on the latest article — sent only when there is something worth saying.
- **Not started** · Questions in plain language, answered only from collected data, and saying
  "not enough evidence" when that is the answer.

### Phase 10 — Feedback and own automation

- **Not started** · Mark a visit: this is a bot · a legitimate service · human · ignore · not
  sure.
- **Not started** · Labels stored and used to measure accuracy.
- **Not started** · Register your own uptime monitors, CI, QA automation and partner crawlers so
  they stop reading as suspicious.
- **Not started** · Editors can correct a verdict.

### Phase 11 — Housekeeping

- **Not started** · Documents cited by name that do not exist: design notes 1 to 10, contributor
  guide, self-host runbook.
- **Not started** · Private repository notes out of step with the code: the test list, the
  settings and message counts, the build commands, the go-live steps, the payment checks.
- **Not started** · The synthetic traffic tool, with its own README stating it is regression
  protection only.
- **Not started** · A recorded-traffic test collection with hand-written labels.
- **Not started** · Stored but unused data — use it or stop collecting it: time-zone offset,
  screen height, clock drift, response size, content type, mobile hint, region.
- **Not started** · Commercial edition: clustering across customers, the curated intelligence
  feed, alerting, single sign-on, directory sync.

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

`tracker/` is **MIT** instead, and the integrations will be when they arrive, because they are
pasted into other people's websites and a copyleft beacon is not a reasonable thing to ask
somebody to embed.

Self-hosting is not a crippled tier. The full detection engine, every screen that shows or
judges your traffic, and unlimited websites and traffic are free, and every edition keeps twelve
months of traffic and verdicts. What the hosted service adds today is the running of it: an
account anyone can create, plans and billing. Analysis that only exists because it sees many
customers' traffic at once — which no single installation could produce for itself — is planned
for that edition and not yet built. The commercial part lives in its own repository and is not
required to build or run anything here.

Attributions for the data and libraries this depends on are in [`NOTICE`](NOTICE).

## Contributing

Contributions are welcome. Signing the [contributor licence agreement](CLA.md) is required before
a pull request can be merged — it lets the project offer the commercial edition alongside the free
one without asking every contributor for permission each time.

"Dewiride" and the Dewiride logo are trademarks and are not covered by the software licence; see
[`TRADEMARK.md`](TRADEMARK.md).

# Reporting from your own server

The browser tracker cannot see most of what this product exists to identify. A crawler asks for
the page, reads the markup and stops; it never runs the script, so as far as `dw.js` is concerned
it was never there. The same is true of feed readers, uptime monitors, link previewers and every
security scanner probing for `/.env`.

Anything sitting in the request path does see them — an edge worker, a CMS plugin, the site's own
middleware. This is the endpoint those report to.

## What a server-side reporter can observe that a browser cannot

| Observation                         | Browser tracker | Server-side reporter |
| ----------------------------------- | --------------- | -------------------- |
| Requests that never execute script  | no              | **yes**              |
| HTTP status code                    | no              | **yes**              |
| Response content type and size      | no              | **yes**              |
| Requests to paths that do not exist | no              | **yes**              |
| Engaged time, scroll depth          | **yes**         | no                   |
| Pointer and keyboard presence       | **yes**         | no                   |
| What a visitor operated             | **yes**         | no                   |
| Viewport, declared automation       | **yes**         | no                   |

Neither replaces the other, and the surface is recorded on every event so a classification can be
read against what the surface was able to see. Running both on one site is the intended
arrangement.

## Getting a key

The browser tracker needs no credential: everything it reports is observed from the connection it
arrives on, and a site identifier is printed in the page source of every page it measures.

A server-side reporter is the opposite case. It stands between the visitor and the collector, so
the address, the user agent and the status it sends are asserted rather than observed. Accepting
those from anybody would let a stranger write whatever traffic they liked into somebody else's
account.

In the dashboard, open the website and choose **Server keys**. The secret is shown once, at the
moment it is created; only a hash of it is stored, so it cannot be recovered afterwards. Keep it
where the reporter runs and nowhere else — it is not a value that belongs in a page, in a client
bundle, or in a repository.

A key is bound to one site. It cannot report for any other, and the site is not named in the
request body at all.

## The endpoint

```http
POST /collect/server
Authorization: Bearer dwk_...
Content-Type: application/json
```

```json
{
  "surface": "cloudflare-worker",
  "events": [
    {
      "kind": "pageview",
      "url": "https://example.com/posts/hello",
      "referrer": "https://news.example/",
      "ipAddress": "203.0.113.7",
      "userAgent": "Mozilla/5.0 (compatible; ExampleBot/1.0; +https://example.test/bot)",
      "statusCode": 200,
      "contentType": "text/html",
      "responseBytes": 12345,
      "language": "en-GB",
      "observedAt": 1755500000000,
      "correlationId": "01a01411-27e0-7853-ab4c-a8b407aec496"
    }
  ]
}
```

### The batch

| Field     | Required | Meaning                                                                                                                                                                                                             |
| --------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `surface` | no       | Which reporter this is. Unrecognised values are recorded as a server-side reporter of unstated identity rather than refused, so a reporter written against a later release keeps working against an earlier engine. |
| `events`  | yes      | Up to 100 observations, oldest first. A batch of one is valid and is what a stateless edge function will send.                                                                                                      |

Recognised `surface` values: `cloudflare-worker`, `wordpress-plugin`, `netlify-edge`,
`vercel-edge`, `aspnetcore-middleware`, `nextjs-middleware`, `log-import`. The names are reserved
for the reporters this product will ship; none of them ships yet, and a reporter you write
yourself names the surface it runs on.

### One observation

| Field           | Required | Meaning                                                                                                                                                                                                       |
| --------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `kind`          | yes      | `pageview`, `engagement`, `exit` or `action`. A server surface reports `pageview`: it never sees a page being read or a control being operated.                                                               |
| `url`           | yes      | Absolute `http` or `https` URL of the page requested. Its hostname must be one the site covers.                                                                                                               |
| `referrer`      | no       | The referring URL the visitor's browser sent. Kept only up to its path: anything after a `?` or a `#` is cut where it arrives.                                                                                |
| `ipAddress`     | no       | The **visitor's** address, not the reporter's. Absent means the reporter could not determine one; present but unparseable is refused.                                                                         |
| `userAgent`     | no       | The **visitor's** user agent, not the reporter's.                                                                                                                                                             |
| `mobile`        | no       | The visitor's `Sec-CH-UA-Mobile` header, forwarded exactly as sent — `?1` or `?0`. Anything else means the visitor's browser said nothing, which is the ordinary case outside Chromium.                       |
| `platform`      | no       | The visitor's `Sec-CH-UA-Platform` header, as sent.                                                                                                                                                           |
| `brands`        | no       | The visitor's `Sec-CH-UA` header, as sent.                                                                                                                                                                    |
| `statusCode`    | no       | Status the site returned.                                                                                                                                                                                     |
| `contentType`   | no       | Content type of the response.                                                                                                                                                                                 |
| `responseBytes` | no       | Bytes sent in the response.                                                                                                                                                                                   |
| `language`      | no       | Primary language the visitor's browser asked for.                                                                                                                                                             |
| `observedAt`    | no       | Unix milliseconds at which the reporter saw the request. Never used as the event's time; the collector stamps that on receipt. The difference between the two is recorded.                                    |
| `correlationId` | no       | Names this one delivery, so the browser's account of the same page can be matched to it. See [Naming the delivery](#naming-the-delivery) — a reporter that runs alongside the browser tracker should send it. |

Fields the browser observes and a server cannot — viewport, engaged time, scroll depth, pointer
and keyboard presence, declared automation, and what a visitor operated — are absent from this
shape on purpose. There is no
way to assert them here, and the store records them as _not observed_ rather than as zero.

The three headers above are forwarded rather than interpreted, and only those three. They are the
low-entropy client hints, which a browser sends unasked and to any origin, and they are what lets
a visit reported from your server still be attributed to a phone rather than to a computer. Do not
request the high-entropy ones on this product's behalf: they name the exact device and build, which
is the part that would help identify a person, and nothing here reads them.

### Naming the delivery

A reporter here and the browser tracker both see every page a person reads, and each works out for
itself who the visitor was — from addresses that need not agree, because the page and the collector
are different hosts and a visitor whose network offers both kinds of address can reach one over
each. Left alone, that is two page views and two people for every one of each.

Send a `correlationId` and put the same value on the response, and the two accounts are recognised
as one. Mint it per request; it names one delivery rather than a person, and has no meaning once the
pair has been matched.

Put it on the response as a `Server-Timing` metric called `dw`:

```http
Server-Timing: dw;desc="7b21ae4c90f1d33e"
```

The tracker reads it from the timings the browser already collected for the page, so nothing has to
be written into the markup — which matters, because a reporter sitting in the request path often
cannot alter a body at all. A browser hands these timings to script on the page they came from and,
unless the site says otherwise, to nobody else. The tracker also reads a value written into its tag
as `data-correlation`, and the tag wins if both are present.

Name only a delivery you will report, to a browser that is navigating to the page — a request
carrying `Sec-Fetch-Dest: document` that is not a prefetch. A crawler sends no `Sec-Fetch-*` headers,
so a page a crawler fetched goes out unnamed.

A page cache in front of a site keeps the response whole, headers and markup alike, and hands it to
everybody who asks while the copy lasts, so one delivery's name can come back from many browsers, and
a crawler's request from a stranger's. The collector reads names accordingly:

- A name is a browser's answer when that browser is the only one to send it back within two minutes
  either side of your report. A name two browsers send back within that time was handed out by a
  cache, and counts as one of your names that nobody answered; one sent back later is a copy and is
  not counted.
- Each of your key's reports is recognised as a browser's visitor when, within 28 minutes either side
  of it, that browser is the only one to have answered your key and has answered at least half of the
  pages your key named. Reports without a name — a redirect, a feed, a page that was not there —
  follow the same rule, so they stay inside the reader's visit.

Both ways of naming are optional. Without either, page views are still counted once per delivery —
the two halves are counted separately and the larger kept — but the two halves cannot be recognised
as one visitor, so a site running both will report each person twice.

HTML served under an identifier that no script ever reported is a page that was fetched and never
rendered, which is itself evidence.

### The answer

```json
{
  "accepted": 3,
  "rejected": 1,
  "site": "0197c0de-0000-7000-8000-000000000001",
  "hosts": ["example.com"],
  "measuring": true,
  "from": "198.51.100.20"
}
```

Unlike the browser collector, which tells a page whose page view it stored for a website that
records presses only that, and answers every other report it reads with nothing, this one says what
became of the batch. The caller has already proved it holds a key for the site, so there is nothing
left for the answer to disclose, and whoever is writing the reporter needs to know whether it works.

`rejected` counts observations that were malformed, that named a page on a hostname the site does
not cover, that arrived while the account the website belongs to was closed, or that belonged to a
visit from a website the installation does not keep visits from (`Dewiride:Collector:IgnoredSenders`:
the visit whose referrer named it, and what its visitor sends until the visit is over). The rest of
the batch is still stored.

| Field       | Meaning                                                                                                                                                                     |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `site`      | The website the key belongs to, which is the website the batch was recorded against.                                                                                        |
| `hosts`     | The hostnames the website covers, each standing for itself and every hostname below it. A page on any other hostname is rejected. Empty while the account is closed.        |
| `measuring` | Whether anything sent is being kept: false while the account the website belongs to is closed, and on the hosted service while the account's plan has stopped admitting it. |
| `from`      | The address the batch arrived from, as the collector saw it — usually the address your own server's requests to your own website arrive from too.                           |

A batch whose `events` array is empty stores nothing and is answered in full, which makes it the way
to check a connection before there is anything to send: the key works, it belongs to the website you
expect, your pages are on a hostname that website covers, and the website is being measured. The
four fields after `rejected` were added after the endpoint's first release, so read each where it is
present and do without it where it is not.

| Status | Meaning                                                                                                          |
| ------ | ---------------------------------------------------------------------------------------------------------------- |
| `200`  | The batch was processed. Read `accepted` and `rejected`.                                                         |
| `400`  | The body was not a batch, or held more than 100 observations.                                                    |
| `401`  | No key, an unknown key, or a withdrawn key. All three answer identically.                                        |
| `413`  | The body was larger than the configured limit.                                                                   |
| `429`  | Too many batches in one minute, with this key or from this address. `Retry-After` says how many seconds to wait. |

A withdrawn key can keep working for up to a minute: resolved keys are held briefly in memory so
that a reporter presenting the same secret on every batch does not put the control-plane database
in front of the busiest write path in the product.

## Requests from the engine's own sitemap reader

Where a website's settings name its sitemap, the engine reads the sitemap and the pages it lists,
and says so in its user agent: `DewirideAnalytics/<version> (sitemap fetcher; +…)`. A reporter sees
those requests as it sees any other, and what it reports is judged, as a crawler, and counted, on
the hosted service against the account's monthly allowance. To keep them out, leave out requests
whose user agent begins `DewirideAnalytics/`. The engine does not leave them out itself, because
anybody can send that name.

## Limits

Configurable under `Dewiride:Collector`:

| Setting                            | Default  | Meaning                                                                                                                                         |
| ---------------------------------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxServerBatchBytes`              | `262144` | Largest accepted body.                                                                                                                          |
| `MaxEventsPerBatch`                | `100`    | Most observations in one batch.                                                                                                                 |
| `ServerBatchesPerMinutePerKey`     | `600`    | Batches accepted per minute with one live key.                                                                                                  |
| `ServerBatchesPerMinutePerAddress` | `600`    | Batches accepted per minute from one address without a live key: none, one not shaped like a key, one that matches nothing, or a withdrawn one. |

A batch with a live key — issued and not withdrawn — is counted against the key, so websites
reporting from one shared host each have the whole allowance, from their first batch. Every other
batch — no key, one not shaped like a key, one that matches nothing, a withdrawn one — is counted
against the address it came from, before anything is looked up. An address past that allowance is
turned away for the rest of the minute, while the live keys reporting from it carry on under their
own. Which keys are live is read again every minute, so a key created a moment ago is counted
against its address until then. A website that reports each request as it is answered loses what
it reports past its allowance for the rest of the minute, and is told how long to wait:
`Retry-After` gives a wait after which the allowance is certain to have reopened, which can be up
to a minute longer than it had to be.

## What a reporter leaves out

- **Requests your own server makes to your own website** — a health check, a cache warming itself, a
  page your reporter fetched for its own reasons. `from` in the answer is the address your batches
  arrive from, which is usually where those come from too. The engine's own sitemap reader is
  described above.
- **Requests a visitor's headers say to leave out, only when your website answered below 400.** A
  visitor writes every header it sends, so a request that says it is a prefetch (`Sec-Purpose`,
  `Purpose`), that it is not for a document (`Sec-Fetch-Dest`), or that is a `HEAD` can be left out
  when it was answered — and is reported whatever it carries when it was refused. Otherwise a scanner
  probing for a way in could hide every probe behind one header.

## Trying it

Against a local stack, with a key created in the dashboard:

```bash
curl -sS -X POST http://localhost:8080/collect/server \
  -H "Authorization: Bearer $DEWIRIDE_SERVER_KEY" \
  -H 'content-type: application/json' \
  -d '{
        "surface": "cloudflare-worker",
        "events": [{
          "kind": "pageview",
          "url": "https://example.com/posts/hello",
          "ipAddress": "203.0.113.7",
          "userAgent": "Mozilla/5.0 (compatible; ExampleBot/1.0)",
          "statusCode": 200,
          "contentType": "text/html"
        }]
      }'
```

## Privacy

The privacy envelope is the same as the browser tracker's, and a reporter must not widen it. The
address is kept for 72 hours and then dropped, leaving the network attributes derived from it; the
visitor key is derived under a secret that changes daily, so nothing joins one day's activity to
the next. Do not send anything the tracker would not: no form contents, no cookie values, no
request bodies, no headers beyond the ones named above.

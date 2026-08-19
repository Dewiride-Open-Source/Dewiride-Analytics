# The Dewiride Analytics tracker

The script that goes on the websites being measured, and the image that stands in for it when a
browser will not run scripts. Both are **MIT** — they are pasted into other people's pages, and a
copyleft beacon is not a reasonable thing to ask anybody to embed.

Around **1.9 KB** compressed. A test fails the build above 2 KB.

## Installing it

The dashboard shows these two lines with your own address and website already filled in — open a
website and choose **Tracking code**. They look like this:

```html
<script defer src="https://analytics.example.com/dw.js" data-site="YOUR-SITE-ID"></script>
<noscript
  ><img
    src="https://analytics.example.com/collect/pixel.gif?site=YOUR-SITE-ID"
    referrerpolicy="no-referrer-when-downgrade"
    alt=""
    width="1"
    height="1"
    style="position: absolute"
/></noscript>
```

The script works out where to send reports from where it was itself loaded, so there is only ever
one address to get right. An installation served under a path keeps that path.

| Attribute          | Required | What it does                                                                              |
| ------------------ | -------- | ----------------------------------------------------------------------------------------- |
| `data-site`        | yes      | The website's identifier. Nothing is reported without it.                                 |
| `data-collector`   | no       | Sends reports somewhere other than beside the script.                                     |
| `data-correlation` | no       | An identifier stamped into the page by the server, echoed back so the two can be matched. |

Anything inside an element carrying **`data-dw-ignore`** is left alone entirely — no press inside
it is ever reported. That is how you keep a signed-in area, or anything whose controls are named
after the person using them, out of the measurement without turning anything off elsewhere.

## What it reports

A **page view** when the reading starts — the address, the referring page, the size of the window,
the declared language, the offset from UTC, and whether the browser says it is being driven by
automation.

**Progress** while the reading goes on, less often the longer somebody stays. A page whose browser
is killed outright — the tab dismissed on a phone, the machine put to sleep — announces nothing when
it ends, and without these the readers who stayed longest would be the ones who counted for nothing.

An **exit** when the reading ends — how long the page was actually in front of somebody, how far
down it they got, and whether there was any pointer or keyboard activity at all.

An **action** each time somebody uses a link, button or field: what sort of control it was, what it
said, and where it pointed. What it said is your own wording — its accessible name, or the text it
reads as on screen, cut to 64 characters. Where it pointed is the path for a page on the same site
and the host alone for anywhere else; an address to write to or ring is recorded as having been used
and never kept. A press is attributed to the nearest thing a person can operate, so one that lands
on the page rather than on anything in it is not reported at all. This can be switched off for a
website from the dashboard.

Anything not yet measured is **left out** rather than sent as nought. "Nobody touched this page"
and "we were not watching yet" are different facts and the store keeps them apart.

### What it never reports

No cookie and no stored identifier of any kind. No canvas, font list, or device fingerprint. No
form contents and no keystrokes — only whether a key was pressed at all, and never the contents of a
field somebody clicked into. Nothing whatever about a press beyond what was pressed: no coordinates,
no button, no modifier keys, no element identifier and no selector path. Nothing about the reader
that outlives the page.

### When it stays quiet

- While a page is being rendered in advance of anybody asking for it. Such a page has never been
  in front of a person, and counting it would inflate every number on the dashboard.
- When the page comes back from the browser's own store of kept pages. That is a reading resuming,
  not a new one.
- When a second copy of the script is already running on the page.

## The image fallback

It records that a page was requested by something that renders images but does not run scripts —
in practice, a person with scripting turned off or blocked.

**It is not a way of catching crawlers.** A crawler that does not run scripts does not fetch images
either: it asks for the page, reads the markup, and stops. Nothing here sees traffic the script
missed for that reason, and it must never be described as though it does.

The `referrerpolicy` attribute is doing real work. Without it a browser tells the collector only
which site the image was requested from and not which page, and every reader with scripting off
would be recorded as having read the front page.

## Browsers

Compiled for **Chrome 55, Edge 15, Firefox 53 and Safari 11.1** and newer — the oldest releases
that both have the transport it uses and can be compiled for. Syntax a browser cannot read fails
before any check inside the file can run, so a floor set too high does not degrade gracefully; it
silently stops counting a whole class of visitor.

## Working on it

```bash
pnpm --filter @dewiride/tracker build   # compile, and report the size
pnpm --filter @dewiride/tracker test    # behaviour, and the size budget
```

The build writes `dist/dw.js` and the copy the dashboard serves. Both are output; the source of
record is `src/`.

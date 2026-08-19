/**
 * The measuring half of the beacon.
 *
 * Kept apart from the tag that starts it so that everything here can be driven directly by a test
 * without a script element existing at all.
 *
 * Two rules shape all of it. Nothing is ever reported that was not observed — a measurement that
 * has not been taken is left out of the payload rather than sent as nought, because "nobody
 * touched this page" and "we were not watching yet" are different facts and the store keeps them
 * apart. And nothing is read that describes the reader rather than the reading: no cookie, no
 * stored identifier, no canvas, no font list, and never the content of anything typed.
 */

/** What one report carries. Anything not measured is absent rather than nought. */
interface Report {
  readonly siteId: string;
  readonly kind: 'pageview' | 'exit';
  readonly url: string;
  readonly clientTimestamp: number;
  readonly referrer?: string;
  readonly viewportWidth?: number;
  readonly viewportHeight?: number;
  readonly language?: string;
  readonly timezoneOffsetMinutes?: number;
  readonly engagedMs?: number;
  readonly scrollDepthPercent?: number;
  readonly pointerInteraction?: boolean;
  readonly keyboardInteraction?: boolean;
  readonly webDriver?: boolean;
  readonly correlationId?: string;
}

/** What the tag on the page supplies. */
export interface Settings {
  /** The website's identifier, as it appears in the tag. */
  readonly siteId: string;
  /** Absolute address reports are sent to. */
  readonly endpoint: string;
  /** Identifier the site's own server put on this page, echoed back so the two can be matched. */
  readonly correlationId?: string;
}

/** The timing a reporter on the site's own server writes its identifier into. */
const STAMP = 'dw';

/**
 * The identifier the site's own server put on this page's response, if it put one there.
 *
 * Taken from the timings the browser already collected for this document rather than from anything
 * written into the page. Most of the sites this product is for are built once and served from a
 * cache, so there is no per-request markup to carry an identifier — but every response has headers,
 * whoever served it and however long ago it was built.
 *
 * Readable because this is the page's own response. A browser hands these back for the address the
 * page came from and, without the site saying otherwise, for nowhere else.
 */
export function stampedIdentifier(): string | undefined {
  const entries = performance.getEntriesByType('navigation') as PerformanceNavigationTiming[];

  for (const timing of entries[0]?.serverTiming || []) {
    if (timing.name === STAMP) {
      return timing.description || undefined;
    }
  }

  return undefined;
}

/**
 * Marks the window as already being measured.
 *
 * A page that loads the beacon twice — a theme that embeds it and an owner who pastes it as well —
 * would otherwise count every reader twice, which is worse than not measuring at all.
 */
const ALREADY_MEASURING = '__dwMeasuring';

/** Marks the history object as already reporting the addresses it is given. */
const HISTORY_REPORTS = '__dwHistory';

/** Raised on the window when a page moves itself to a new address. */
const MOVED = 'dw:navigate';

/** Largest scroll depth that means anything. */
const FULLY_SCROLLED = 100;

interface Measured extends Window {
  [ALREADY_MEASURING]?: boolean;
}

interface Reported extends History {
  [HISTORY_REPORTS]?: boolean;
}

/** Chromium renders a page before anybody asks for it; the property says so while it does. */
interface MaybePrerendering extends Document {
  readonly prerendering?: boolean;
}

/**
 * Begins measuring this page, and every page reached from it without a reload.
 *
 * @param settings What the tag on the page supplied.
 */
export function start(settings: Settings): void {
  const holder = window as Measured;

  if (holder[ALREADY_MEASURING]) {
    return;
  }

  holder[ALREADY_MEASURING] = true;

  let address = '';
  let engagedMs = 0;
  let visibleSince = 0;
  let deepest = 0;
  let pointer = false;
  let keyboard = false;
  let ended = false;
  let firstView = true;

  function send(report: Report): void {
    const body = JSON.stringify(report);

    // A string body is sent as plain text, which is one of the few kinds a browser will post to
    // another address without asking permission first. A typed blob looks like structured data
    // instead, which turns the report into a request that has to be cleared in advance — and if
    // that is refused the report is never sent at all, and the loss is silent.
    if (navigator.sendBeacon?.(settings.endpoint, body)) {
      return;
    }

    // Either this browser has no beacon or its queue is full. The reply is of no interest, so it
    // is asked for in the mode that needs no permission to read one.
    void fetch(settings.endpoint, {
      method: 'POST',
      body,
      mode: 'no-cors',
      credentials: 'omit',
      keepalive: true,
    }).catch(() => {
      // Nothing to be done, and nobody to tell: the page belongs to somebody else and its console
      // is not ours to write in.
    });
  }

  function base(kind: Report['kind']): Report {
    return { siteId: settings.siteId, kind, url: address, clientTimestamp: Date.now() };
  }

  /** How far down the page the reader has been, as a percentage of its height. */
  function scrolled(): number {
    const page = document.documentElement;
    const height = Math.max(page.scrollHeight, document.body?.scrollHeight ?? 0);

    if (height <= 0) {
      return 0;
    }

    const reached = ((window.scrollY + window.innerHeight) / height) * FULLY_SCROLLED;

    return Math.min(FULLY_SCROLLED, Math.max(0, Math.round(reached)));
  }

  function watch(): void {
    deepest = Math.max(deepest, scrolled());
  }

  /** Milliseconds this page has been in front of somebody, rather than merely open. */
  function attention(): number {
    return Math.round(engagedMs + (visibleSince > 0 ? performance.now() - visibleSince : 0));
  }

  function beginView(next: string, cameFrom?: string): void {
    address = next;
    engagedMs = 0;
    visibleSince = document.visibilityState === 'visible' ? performance.now() : 0;
    deepest = 0;
    pointer = false;
    keyboard = false;
    ended = false;

    send({
      ...base('pageview'),
      referrer: cameFrom || undefined,
      // Rounded because a zoomed or scaled display reports a fraction of a pixel, and the store
      // holds whole ones. Sent as written, the whole report is refused as unreadable and that
      // reader is never counted.
      viewportWidth: Math.round(window.innerWidth),
      viewportHeight: Math.round(window.innerHeight),
      language: navigator.language || undefined,
      timezoneOffsetMinutes: -new Date().getTimezoneOffset(),
      webDriver: navigator.webdriver,
      // Only the first reading carries it. The identifier belongs to the one page the site's
      // server handed over; everything reached from it afterwards without a fresh request was
      // never delivered by anybody, so sending it again would fold several readings into one.
      correlationId: firstView ? settings.correlationId : undefined,
    });

    firstView = false;
  }

  /**
   * Reports what this page turned out to be worth, once.
   *
   * Closing a tab raises both of the events this is attached to, and a phone that kills the
   * browser outright raises neither. So it is written to be safe to call at any moment and to do
   * nothing the second time — the alternative is either counting one departure twice or missing
   * the ones that matter most.
   */
  function endView(): void {
    if (ended) {
      return;
    }

    ended = true;
    watch();

    if (visibleSince > 0) {
      engagedMs += performance.now() - visibleSince;
      visibleSince = 0;
    }

    send({
      ...base('exit'),
      engagedMs: attention(),
      scrollDepthPercent: deepest,
      pointerInteraction: pointer,
      keyboardInteraction: keyboard,
    });
  }

  /** The reader came back to a page that was never thrown away. */
  function resumeView(): void {
    if (visibleSince === 0) {
      visibleSince = performance.now();
    }

    ended = false;
  }

  /** The address changed without the page being fetched again. */
  function navigated(): void {
    if (window.location.href === address) {
      return;
    }

    const previous = address;

    endView();
    beginView(window.location.href, previous);
  }

  function observe(): void {
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'hidden') {
        endView();
      } else {
        resumeView();
      }
    });

    window.addEventListener('pagehide', endView);

    // Restored from the browser's own store of pages it kept rather than fetched again. The
    // reading resumes; it is not a new one, and counting it as one would invent a page view.
    window.addEventListener('pageshow', (event) => {
      if (event.persisted) {
        resumeView();
      }
    });

    window.addEventListener('scroll', watch, { passive: true });

    window.addEventListener(
      'pointerdown',
      () => {
        pointer = true;
      },
      { passive: true, once: true },
    );

    window.addEventListener(
      'keydown',
      () => {
        keyboard = true;
      },
      { passive: true, once: true },
    );

    window.addEventListener('popstate', navigated);
    window.addEventListener('hashchange', navigated);
    window.addEventListener(MOVED, navigated);
    announceHistory();

    beginView(window.location.href, document.referrer);
  }

  // A page rendered in advance has never been in front of anybody, and reports itself as hidden
  // for the whole of its prerendered life — so measuring it there would file a departure for a
  // reading that never happened. Nothing starts until it is genuinely on screen.
  if ((document as MaybePrerendering).prerendering) {
    document.addEventListener('prerenderingchange', observe, { once: true });
  } else {
    observe();
  }
}

/**
 * Makes a change of address raise something that can be listened for.
 *
 * A browser announces going back, but says nothing when a page moves itself — which is how nearly
 * every modern site navigates. So those two calls are wrapped. The original is kept and called
 * first, and the wrapper is never taken off again: removing it would take whoever wrapped it
 * afterwards away with it.
 *
 * It raises an event rather than calling anything directly, so the wrapper holds on to nothing.
 * A wrapper that captured the measuring code would keep it alive for as long as the page lasted,
 * which on a page that loaded the beacon twice means the copy that stood down still reporting.
 */
function announceHistory(): void {
  const history = window.history as Reported;

  if (history[HISTORY_REPORTS]) {
    return;
  }

  history[HISTORY_REPORTS] = true;

  for (const name of ['pushState', 'replaceState'] as const) {
    const original = history[name];

    history[name] = function patched(this: History, ...args: Parameters<History['pushState']>) {
      const result = original.apply(this, args);

      window.dispatchEvent(new Event(MOVED));

      return result;
    };
  }
}

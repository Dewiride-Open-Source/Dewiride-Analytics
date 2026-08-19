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
  readonly kind: 'pageview' | 'engagement' | 'exit' | 'action';
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
  readonly element?: string;
  readonly label?: string;
  readonly target?: string;
  readonly targetKind?: 'internal' | 'external' | 'contact';
}

/**
 * What a click landed on — as much of it as a report is built from, and no more.
 *
 * A click can be raised on the window or the document as well as on an element, and neither of
 * those has any of this. Declared rather than assumed so the guard below is a check the compiler
 * agrees is worth making.
 */
interface Clicked {
  readonly closest?: (selectors: string) => Element | null;
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

/**
 * Marks a part of a page whose clicks are never reported, along with everything inside it.
 *
 * The way a site keeps a region out of the measurement without turning the whole feature off — a
 * signed-in area, an administration panel, anything whose controls are named after the person
 * using them.
 */
const UNREPORTED = '[data-dw-ignore]';

/**
 * What a click is attributed to: the nearest thing above it that a person can operate.
 *
 * A click lands on whatever is under the pointer, which is usually a span inside a label inside a
 * button. What somebody meant to press is the control, so the report is built from that and not
 * from the fragment of text they happened to hit. A press that reaches none of these landed on the
 * page rather than on anything in it, and is left alone: it names nothing anybody could act on,
 * and a page whose fields are labelled raises a second press of its own on the field itself, which
 * would otherwise count one tick of a box as two.
 */
const CONTROLS =
  'a,button,summary,select,textarea,input,[role=button],[role=link],[role=tab],[role=menuitem]';

/**
 * Longest label kept, in characters.
 *
 * A control is named in a few words. Anything past this is not a name, and the text on a page
 * belongs to whoever wrote the page — so the less of it that travels, the better.
 */
const LONGEST_LABEL = 64;

/**
 * Stands for a page that is not in front of anybody at the moment.
 *
 * Outside the range a clock reading can take, rather than nought: a page measured from the instant
 * the document began reads as nought legitimately, and a sentinel that is also a legal reading
 * makes a page that was never looked away from indistinguishable from one nobody has looked at.
 */
const AWAY = -1;

/**
 * How long a page waits before saying how it is going, in milliseconds.
 *
 * Short enough that a reader who leaves in the first minute is still measured, and long enough
 * that a glance costs one report rather than a stream of them.
 */
const FIRST_PROGRESS = 15000;

/**
 * How far apart progress reports settle once a page is being read properly, in milliseconds.
 *
 * The gap doubles after each one until it reaches this. A long read is worth a handful of reports
 * rather than one every quarter-minute for an hour, and by the time the gap is this wide the
 * question has stopped being whether anybody is there.
 */
const SLOWEST_PROGRESS = 60000;

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
  let visibleSince = AWAY;
  let deepest = 0;
  let pointer = false;
  let keyboard = false;
  let ended = false;
  let firstView = true;
  let gap = FIRST_PROGRESS;
  let progressTimer = 0;

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
    return Math.round(engagedMs + (visibleSince === AWAY ? 0 : performance.now() - visibleSince));
  }

  /**
   * What this page has been worth so far.
   *
   * Every figure is a running total rather than what has happened since the last report, so a
   * report that never arrives costs nothing and one that arrives twice counts once. That matters
   * more than it sounds: reports are sent by a transport with no acknowledgement, from a page that
   * is frequently in the act of being closed.
   */
  function progress(kind: Report['kind']): Report {
    watch();

    return {
      ...base(kind),
      engagedMs: attention(),
      scrollDepthPercent: deepest,
      pointerInteraction: pointer,
      keyboardInteraction: keyboard,
    };
  }

  /**
   * Says how the reading is going, and arranges to say so again a little later.
   *
   * A page that is closed by the reader announces itself, but a page whose browser is killed
   * outright — the tab dismissed on a phone, the machine put to sleep, the process shut down to
   * free memory — announces nothing at all. Without this, exactly the readers who stayed longest
   * would be the ones who counted for nothing.
   */
  function reportProgress(): void {
    if (visibleSince !== AWAY) {
      send(progress('engagement'));
      gap = Math.min(gap * 2, SLOWEST_PROGRESS);
    }

    keepTime();
  }

  /** Puts the next progress report on the clock, replacing any already waiting. */
  function keepTime(): void {
    window.clearTimeout(progressTimer);
    progressTimer = window.setTimeout(reportProgress, gap);
  }

  function beginView(next: string, cameFrom?: string): void {
    address = next;
    engagedMs = 0;
    visibleSince = document.visibilityState === 'visible' ? performance.now() : AWAY;
    deepest = 0;
    pointer = false;
    keyboard = false;
    ended = false;
    gap = FIRST_PROGRESS;
    keepTime();

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
    window.clearTimeout(progressTimer);

    if (visibleSince !== AWAY) {
      engagedMs += performance.now() - visibleSince;
      visibleSince = AWAY;
    }

    send(progress('exit'));
  }

  /** The reader came back to a page that was never thrown away. */
  function resumeView(): void {
    if (visibleSince === AWAY) {
      visibleSince = performance.now();
    }

    ended = false;
    keepTime();
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

  /**
   * Reports one click: what was pressed, what it said, and where it pointed.
   *
   * Structural on purpose. Nothing is recorded about where on the screen the press landed, and
   * nothing is ever read out of a field, so what travels is the site's own description of its own
   * control. Sent as it happens rather than saved up, because the commonest click is the one that
   * takes the page away.
   */
  function reportClick(event: Event): void {
    const at = event.target as Clicked | null;

    if (!at || !at.closest || at.closest(UNREPORTED)) {
      return;
    }

    const control = at.closest(CONTROLS);

    if (!control) {
      return;
    }

    send({
      ...base('action'),
      // What the page says the control is, preferring the part it declared for a screen reader:
      // a site that builds its buttons out of other elements has already written down what they
      // are, and the element alone would report those as whatever they happen to be made of.
      element: control.getAttribute('role') || control.tagName.toLowerCase(),
      label: labelOf(control),
      ...pointedAt(control),
    });
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

    // Watched as the press travels down to whatever was pressed, so that a page which handles
    // its own clicks and stops them going any further is still measured.
    document.addEventListener('click', reportClick, true);

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
 * What a control says it is.
 *
 * Its own accessible name first, then whatever it reads as on the screen. Never the value of a
 * field: what somebody typed is theirs, and a control's name is the site's own writing.
 */
function labelOf(control: Element): string | undefined {
  const written =
    control.getAttribute('aria-label') ||
    control.getAttribute('title') ||
    control.textContent ||
    '';

  return written.replace(/\s+/g, ' ').trim().slice(0, LONGEST_LABEL) || undefined;
}

/**
 * Where a control pointed, for the controls that point anywhere.
 *
 * A page on the same site is worth keeping in full, because which page somebody went to is the
 * question. Somewhere else is kept as the host alone — the rest of an address off the site can
 * carry anything, including who followed it. An address to write to or ring is recorded as having
 * been used and nothing more: the address itself names a person.
 */
function pointedAt(control: Element): Partial<Report> {
  const written = control.getAttribute('href');

  if (!written) {
    return {};
  }

  try {
    const address = new URL(written, window.location.href);

    if (address.protocol === 'mailto:' || address.protocol === 'tel:') {
      return { targetKind: 'contact' };
    }

    if (address.protocol === 'http:' || address.protocol === 'https:') {
      const here = address.host === window.location.host;

      return {
        target: here ? address.pathname : address.host,
        targetKind: here ? 'internal' : 'external',
      };
    }
  } catch {
    // An address the browser itself cannot read points nowhere worth recording.
  }

  return {};
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

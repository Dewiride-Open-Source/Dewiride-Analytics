import { expect, vi } from 'vitest';
import { start } from './beacon';

/**
 * A page to run the beacon on.
 *
 * Everything the beacon touches is global, and one document is shared by every test in a file — so
 * this keeps a record of what was hooked and unhooks it afterwards. Without that, a beacon from an
 * earlier test is still listening and files reports against the next one.
 */

/** What one report said, once it has been read back out of the transport. */
export type Sent = Record<string, unknown>;

interface Page {
  /** Every report sent since the page was opened, oldest first. */
  readonly sent: Sent[];
  /** Whatever was handed to the transport, so its type can be checked as well as its content. */
  readonly bodies: unknown[];
  /** How many of the collector's replies the beacon has read. */
  readonly repliesRead: () => number;
  /** Starts the beacon, as the tag on a page would. */
  readonly open: (settings?: { readonly correlationId?: string }) => void;
  /** Makes the browser's beacon refuse, which is what a full queue looks like. */
  readonly refuseTransport: () => void;
  /** Undoes everything the beacon hooked. */
  readonly close: () => void;
}

/**
 * One thing the beacon asked to listen for.
 *
 * The options are kept with it because a listener watching a press on its way down is only taken
 * off by asking for it back the same way. Without them the beacon from one test is still watching
 * during the next, and files its reports against that one.
 */
interface Hook {
  readonly target: EventTarget;
  readonly type: string;
  readonly listener: EventListenerOrEventListenerObject | null;
  readonly options: boolean | AddEventListenerOptions | undefined;
}

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';
const COLLECTOR = 'https://analytics.example.com/collect';

/**
 * What the collector says to the first page view: that the website records presses, nothing at
 * all, some other reply with this status and body, or — the collector being out of reach — no
 * reply.
 */
export type Answer =
  | 'presses'
  | 'nothing'
  | 'unreachable'
  | { readonly status: number; readonly body: string };

/** The browser a page is opened in. */
export interface Browser {
  /**
   * Whether a request the page sends can outlive the page. Every browser the beacon is built for
   * has a beacon; only the newer ones can keep any other request alive.
   */
  readonly keepsRequestsAlive?: boolean;
}

/**
 * Opens a page the beacon can be run on.
 *
 * Only one of these may be open at a time: it replaces what the document does when something is
 * listened for, and a second one wrapping the first has no way back out again.
 *
 * @param answer What the collector says to a page view. Unless a test asks otherwise it says
 * nothing, which is what every website is told until somebody switches recording on.
 * @param browser The browser the page is opened in: unless a test says otherwise, a current one.
 */
export function openPage(
  answer: Answer = 'nothing',
  { keepsRequestsAlive = true }: Browser = {},
): Page {
  const sent: Sent[] = [];
  const bodies: unknown[] = [];
  const hooked: Hook[] = [];
  let accepted = true;
  let read = 0;

  function record(body: unknown) {
    bodies.push(body);

    if (typeof body === 'string') {
      sent.push(JSON.parse(body) as Sent);
    }
  }

  const sendBeacon = vi.fn((_url: string, body?: BodyInit | null) => {
    if (accepted) {
      record(body);
    }

    return accepted;
  });

  vi.stubGlobal(
    'fetch',
    vi.fn((_url: string, init?: RequestInit) => {
      record(init?.body);

      if (answer === 'unreachable') {
        return Promise.reject(new TypeError('Failed to fetch'));
      }

      // The collector says something only to a page view, and only a request sent in the mode
      // whose reply can be read gets to read it.
      const reply =
        init?.mode === 'cors' && kindOf(init.body) === 'pageview'
          ? replyFor(answer)
          : new Response(null, { status: 204 });
      const readBody = reply.json.bind(reply);

      reply.json = () => {
        read += 1;

        return readBody();
      };

      return Promise.resolve(reply);
    }),
  );

  Object.defineProperty(navigator, 'sendBeacon', { value: sendBeacon, configurable: true });

  if (!keepsRequestsAlive) {
    // A request, as a browser from before one could outlive its page describes it.
    vi.stubGlobal('Request', { prototype: {} });
  }

  for (const target of [window, document] as EventTarget[]) {
    const original = target.addEventListener.bind(target);

    vi.spyOn(target, 'addEventListener').mockImplementation((type, listener, options) => {
      hooked.push({ target, type, listener, options });
      original(type, listener, options);
    });
  }

  return {
    sent,
    bodies,
    repliesRead: () => read,
    open: (settings = {}) => start({ siteId: SITE, endpoint: COLLECTOR, ...settings }),
    refuseTransport: () => {
      accepted = false;
    },
    close: () => {
      for (const { target, type, listener, options } of hooked) {
        target.removeEventListener(type, listener, options);
      }

      hooked.length = 0;
      document.body.innerHTML = '';
      delete (window as { __dwMeasuring?: boolean }).__dwMeasuring;
      vi.unstubAllGlobals();
      vi.restoreAllMocks();
    },
  };
}

/** What kind of report a body is, where it is one at all. */
function kindOf(body: BodyInit | null | undefined): unknown {
  return typeof body === 'string' ? (JSON.parse(body) as Sent)['kind'] : undefined;
}

/** The reply the collector gives a page view, for an answer that is a reply at all. */
function replyFor(answer: Exclude<Answer, 'unreachable'>): Response {
  if (answer === 'presses') {
    return new Response('{"presses":true}', { status: 200 });
  }

  if (answer === 'nothing') {
    return new Response(null, { status: 204 });
  }

  return new Response(answer.body, { status: answer.status });
}

/**
 * Gives this page's response the timings a site's own server would have put on it.
 *
 * How a reporter on the site's server tells the tracker which delivery it is looking at, without
 * anything having to be written into the page itself.
 */
export function stampResponse(timings: readonly { name: string; description: string }[]): void {
  vi.spyOn(performance, 'getEntriesByType').mockImplementation((type) =>
    type === 'navigation'
      ? ([{ serverTiming: timings }] as unknown as PerformanceEntryList)
      : ([] as PerformanceEntryList),
  );
}

/**
 * Puts markup on the page and presses the part of it marked with `data-press`.
 *
 * The press is raised on the element itself, which is how a browser raises one: it travels down
 * from the top of the document to whatever was hit and then back up again. Anything watching on
 * the way down sees it whether or not the page lets it travel back.
 */
export function press(markup: string): void {
  document.body.innerHTML = markup;

  const pressed = document.body.querySelector('[data-press]');

  if (!pressed) {
    throw new Error('The markup marks nothing to press.');
  }

  pressed.dispatchEvent(new MouseEvent('click', { bubbles: true }));
}

/**
 * Lets the collector's reply to the first page view arrive and be read, as a moment passing would.
 *
 * Nothing listens for a press until that reply says yes, so a press made before it has been read
 * is, rightly, never reported.
 */
export function replied(): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, 0);
  });
}

/** Puts the page in front of somebody, or takes it away, and tells the beacon. */
export function visibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
  document.dispatchEvent(new Event('visibilitychange'));
}

/** The page moving itself to another address, the way a modern site navigates. */
export function goTo(path: string): void {
  window.history.pushState({}, '', path);
}

/** The one report of its kind, or a failure naming what was actually sent. */
export function only(sent: readonly Sent[], kind: string): Sent {
  const matching = sent.filter((report) => report['kind'] === kind);

  expect(matching, `reports sent: ${JSON.stringify(sent.map((one) => one['kind']))}`).toHaveLength(
    1,
  );

  return matching[0] as Sent;
}

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
  /** Starts the beacon, as the tag on a page would. */
  readonly open: (settings?: { readonly correlationId?: string }) => void;
  /** Makes the browser's own transport refuse, which is what a full queue looks like. */
  readonly refuseTransport: () => void;
  /** Undoes everything the beacon hooked. */
  readonly close: () => void;
}

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';
const COLLECTOR = 'https://analytics.example.com/collect';

/**
 * Opens a page the beacon can be run on.
 *
 * Only one of these may be open at a time: it replaces what the document does when something is
 * listened for, and a second one wrapping the first has no way back out again.
 */
export function openPage(): Page {
  const sent: Sent[] = [];
  const bodies: unknown[] = [];
  const hooked: [EventTarget, string, EventListenerOrEventListenerObject | null][] = [];
  let accepted = true;

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

      return Promise.resolve(new Response(null, { status: 204 }));
    }),
  );

  Object.defineProperty(navigator, 'sendBeacon', { value: sendBeacon, configurable: true });

  for (const target of [window, document] as EventTarget[]) {
    const original = target.addEventListener.bind(target);

    vi.spyOn(target, 'addEventListener').mockImplementation((type, listener, options) => {
      hooked.push([target, type, listener]);
      original(type, listener, options);
    });
  }

  return {
    sent,
    bodies,
    open: (settings = {}) => start({ siteId: SITE, endpoint: COLLECTOR, ...settings }),
    refuseTransport: () => {
      accepted = false;
    },
    close: () => {
      for (const [target, type, listener] of hooked) {
        target.removeEventListener(type, listener);
      }

      hooked.length = 0;
      delete (window as { __dwMeasuring?: boolean }).__dwMeasuring;
      vi.unstubAllGlobals();
      vi.restoreAllMocks();
    },
  };
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

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from '@testing-library/react';
import { type AbstractIntlMessages, NextIntlClientProvider } from 'next-intl';
import type { ReactElement } from 'react';
import { hydrateRoot, type Root } from 'react-dom/client';
import { renderToString } from 'react-dom/server';
import { type Mock, type MockInstance, onTestFinished, vi } from 'vitest';
import { sessionKey } from '@/lib/queries/session';
import product from '../../messages/en.json';

/**
 * One machine's copy of the world's time zone rules, and the zone the machine itself is set to.
 *
 * Stated rather than taken from the machine the test runs on, because what is being tested is two
 * machines disagreeing — a server whose copy of the rules is a release behind the browser that
 * takes its page over, and set to a different zone — and one machine cannot disagree with itself.
 */
export interface Platform {
  /** Every zone it knows, as it answers when asked for them. */
  readonly zones: readonly string[];
  /** The zone it is itself set to. A container's is UTC. */
  readonly here: string;
  /** Where every zone stands against London today, written the way it writes it: `GMT+01:00`. */
  readonly offset: string;
}

/**
 * A server drawing pages in a container, from a copy of the rules a release behind the browser's.
 *
 * Every zone stands an hour away from where the browser puts it. A runtime a release behind can put
 * Casablanca and El Aaiun an hour ahead of a current browser; this server moves every zone, which is
 * the same disagreement made wider.
 */
export const STALE_SERVER: Platform = {
  zones: ['Africa/Casablanca', 'Africa/El_Aaiun', 'Europe/London'],
  here: 'UTC',
  offset: 'GMT+01:00',
};

/** The browser that takes its pages over: the current rules, on a device in Casablanca. */
export const CURRENT_BROWSER: Platform = {
  zones: ['Africa/Casablanca', 'Africa/El_Aaiun', 'Europe/London'],
  here: 'Africa/Casablanca',
  offset: 'GMT',
};

interface Across {
  /** The machine that drew the page before sending it. */
  readonly server: Platform;
  /** The machine whose browser took the page over. */
  readonly browser: Platform;
  /**
   * An edition's catalogue, for a screen of its own. Laid over the product's one level deep, which
   * is how the running application lays it.
   */
  readonly edition?: AbstractIntlMessages;
  /**
   * What both sides already hold before the screen is drawn, the way a page whose answers were
   * read before it was sent would hold them.
   */
  readonly known?: (cache: QueryClient) => void;
}

/** What taking a server's page over came to. */
export interface Hydration {
  /** The page exactly as the server sent it. */
  readonly html: string;
  /**
   * Every disagreement React recovered from, by throwing the server's page away and drawing its
   * own in its place.
   */
  readonly recovered: Mock<(error: unknown) => void>;
  /**
   * Everything said on the console while the browser took the page over, which is where React
   * reports a disagreement it let stand.
   */
  readonly complaints: MockInstance<typeof console.error>;
}

/**
 * Draws a screen the way the server does, then has a browser take the page over.
 *
 * In production the two happen on different machines, and that is the point: whatever a screen
 * reads from the platform while it is being drawn is read once on each, and where the answers
 * differ React finds a page that does not match the screen it is drawing. It says so in one of two
 * places, and both are handed back so a test can insist that neither was used.
 *
 * Each side gets answers of its own, as two machines would. Everything the browser does in taking
 * the page over has happened by the time this returns, including drawing whatever it could only
 * draw once the page was its own. The page is taken down again when the test finishes, whatever the
 * test concluded.
 */
export function hydrated(
  ui: ReactElement,
  { server, browser, edition = {}, known }: Across,
): Hydration {
  const messages = { ...product, ...edition };

  runningOn(server);

  const html = renderToString(provided(ui, messages, known));
  const container = document.createElement('div');

  container.innerHTML = html;
  document.body.append(container);

  runningOn(browser);

  const recovered = vi.fn<(error: unknown) => void>();
  const complaints = vi.spyOn(console, 'error');
  let root: Root | undefined;

  onTestFinished(() => {
    act(() => root?.unmount());
    container.remove();
  });

  act(() => {
    root = hydrateRoot(container, provided(ui, messages, known), {
      onRecoverableError: recovered,
    });
  });

  return { html, recovered, complaints };
}

/** Makes this the machine whatever is drawn next is drawn on. */
function runningOn({ zones, here, offset }: Platform): void {
  vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue([...zones]);
  vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({
    locale: 'en-GB',
    calendar: 'gregory',
    numberingSystem: 'latn',
    timeZone: here,
  });
  vi.spyOn(Intl.DateTimeFormat.prototype, 'formatToParts').mockReturnValue([
    { type: 'timeZoneName', value: offset },
  ]);
}

/** The screen with what every screen assumes around it: somewhere to keep answers, and words. */
function provided(
  ui: ReactElement,
  messages: AbstractIntlMessages,
  known: ((cache: QueryClient) => void) | undefined,
): ReactElement {
  const cache = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  cache.setQueryData(sessionKey, {
    setupCompleted: true,
    user: null,
    token: 'proof-value',
    closure: null,
  });
  known?.(cache);

  return (
    <NextIntlClientProvider locale="en" messages={messages}>
      <QueryClientProvider client={cache}>{ui}</QueryClientProvider>
    </NextIntlClientProvider>
  );
}

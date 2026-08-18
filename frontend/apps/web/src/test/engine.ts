import { vi } from 'vitest';

/**
 * Stands in for the engine while a test runs.
 *
 * Tests describe what the engine answers and then read back what was actually sent to it. Doing
 * that through one helper keeps the casts that a mocked global needs in a single place instead of
 * in every test that looks at a request.
 */

/** What a screen sent to the engine. */
export interface Sent {
  readonly path: string;
  readonly init: RequestInit;
}

type Answering = (path: string, init: RequestInit) => Promise<Response>;

export interface Engine {
  /** How many requests reached it. */
  readonly count: number;
  /** The first request, or a failed test if nothing was sent. */
  first(): Sent;
  /** One header from the first request. */
  header(name: string): string | undefined;
  /** The first request's body, read back as the object it was sent as. */
  body(): unknown;
}

/** Answers every request with the same thing. */
export function engineAnswering(status: number, body: unknown): Engine {
  return engineDoing(async () => respondWith(status, body));
}

/** Answers as if nothing is listening. */
export function engineStopped(): Engine {
  return engineDoing(() => {
    throw new TypeError('Failed to fetch');
  });
}

/** Answers however the test says. */
export function engineDoing(answering: Answering): Engine {
  const fetching = vi.fn(answering);

  vi.stubGlobal('fetch', fetching);

  const sent = (): Sent[] =>
    fetching.mock.calls.map(([path, init]) => ({ path, init }) satisfies Sent);

  return {
    get count() {
      return fetching.mock.calls.length;
    },
    first() {
      const first = sent()[0];

      if (!first) {
        throw new Error('Nothing was sent to the engine.');
      }

      return first;
    },
    header(name) {
      return (this.first().init.headers as Record<string, string> | undefined)?.[name];
    },
    body() {
      const raw = this.first().init.body;

      return typeof raw === 'string' ? JSON.parse(raw) : raw;
    },
  };
}

/** Builds the smallest thing the client will accept as an answer. */
export function respondWith(status: number, body: unknown): Response {
  return {
    ok: status < 400,
    status,
    json: async () => body,
  } as unknown as Response;
}

/** Builds an answer whose body cannot be read at all. */
export function respondWithRubbish(status: number): Response {
  return {
    ok: status < 400,
    status,
    json: async () => {
      throw new SyntaxError('not json');
    },
  } as unknown as Response;
}

import type { ZodType } from 'zod';
import { ApiError, readProblem } from './problem';

/**
 * The one way this dashboard talks to the engine.
 *
 * Every request goes to this server's own address and is forwarded on from there, so the browser
 * only ever sees one origin. That is what makes the sign-in cookie work without any cross-origin
 * arrangement to configure, and it is why nothing here needs to know where the engine actually
 * lives.
 */

/** Header the engine expects proof-of-origin in on anything that changes something. */
const PROOF_HEADER = 'X-Csrf-Token';

const ACCEPTS_JSON = { accept: 'application/json' } as const;

/** Reads something from the engine and checks the answer is the shape it is supposed to be. */
export async function readResource<T>(path: string, shape: ZodType<T>): Promise<T> {
  const response = await send(path, { method: 'GET', headers: ACCEPTS_JSON });

  return interpret(response, shape);
}

/** The ways this dashboard asks the engine to change something. */
type ChangingMethod = 'POST' | 'PUT' | 'DELETE';

/**
 * Submits something to the engine.
 *
 * The proof-of-origin value is required rather than optional: every endpoint that changes
 * anything demands one, and making it a parameter means a caller that has not got one cannot
 * compile rather than discovering it as a refusal at run time.
 */
export async function submitResource<T>(
  path: string,
  method: ChangingMethod,
  proof: string,
  shape: ZodType<T>,
  body?: unknown,
): Promise<T> {
  const response = await send(path, {
    method,
    headers:
      body === undefined
        ? { ...ACCEPTS_JSON, [PROOF_HEADER]: proof }
        : { ...ACCEPTS_JSON, 'content-type': 'application/json', [PROOF_HEADER]: proof },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  return interpret(response, shape);
}

/**
 * Submits something the engine answers with nothing at all.
 *
 * Separate from the call that reads an answer back, because a successful removal has an empty
 * body and asking to read one as JSON fails on the answer that means it worked.
 */
export async function discardResource(
  path: string,
  method: ChangingMethod,
  proof: string,
): Promise<void> {
  const response = await send(path, {
    method,
    headers: { ...ACCEPTS_JSON, [PROOF_HEADER]: proof },
  });

  if (!response.ok) {
    const problem = await readProblem(response);

    throw new ApiError(
      response.status,
      problem,
      problem?.title ?? `The engine answered with ${response.status}.`,
    );
  }
}

/**
 * Makes the request, turning a connection that never completed into the same kind of failure as
 * one the engine refused.
 *
 * Without this, a stopped engine surfaces as a raw network error somewhere inside a component and
 * every screen has to know how to recognise one.
 */
async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(path, { ...init, credentials: 'same-origin', cache: 'no-store' });
  } catch (cause) {
    throw new ApiError(0, null, `The engine could not be reached at ${path}.`, cause);
  }
}

async function interpret<T>(response: Response, shape: ZodType<T>): Promise<T> {
  if (!response.ok) {
    const problem = await readProblem(response);

    throw new ApiError(
      response.status,
      problem,
      problem?.title ?? `The engine answered with ${response.status}.`,
    );
  }

  const body: unknown = await response.json();
  const checked = shape.safeParse(body);

  if (!checked.success) {
    throw new ApiError(response.status, null, 'The engine answered in an unexpected shape.');
  }

  return checked.data;
}

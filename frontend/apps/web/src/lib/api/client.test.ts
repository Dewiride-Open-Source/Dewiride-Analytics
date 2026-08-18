import { afterEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { discardResource, readResource, submitResource } from '@/lib/api/client';
import { ApiError } from '@/lib/api/problem';
import {
  engineAnswering,
  engineDoing,
  engineStopped,
  respondWith,
  respondWithRubbish,
} from '@/test/engine';

const shape = z.object({ name: z.string() });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('reading from the engine', () => {
  it('returns the answer once it matches the shape expected', async () => {
    engineAnswering(200, { name: 'example.com' });

    await expect(readResource('/api/thing', shape)).resolves.toEqual({ name: 'example.com' });
  });

  it('refuses an answer of the wrong shape rather than passing it on', async () => {
    engineAnswering(200, { name: 42 });

    await expect(readResource('/api/thing', shape)).rejects.toBeInstanceOf(ApiError);
  });

  it('reports an engine that never answered as unreachable', async () => {
    engineStopped();

    await expect(readResource('/api/thing', shape)).rejects.toMatchObject({
      status: 0,
      unreachable: true,
    });
  });

  it('carries a refusal through with everything the engine said about it', async () => {
    engineAnswering(400, {
      title: 'Those details cannot be used.',
      problems: [{ code: 'PasswordTooShort', description: 'Too short.' }],
    });

    const failure = await readResource('/api/thing', shape).catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(400);
    expect((failure as ApiError).reasons).toEqual([
      { code: 'PasswordTooShort', description: 'Too short.' },
    ]);
  });

  it('still reports a refusal whose body cannot be read', async () => {
    engineDoing(async () => respondWithRubbish(500));

    await expect(readResource('/api/thing', shape)).rejects.toMatchObject({ status: 500 });
  });
});

describe('submitting to the engine', () => {
  it('sends the proof of origin and the body together', async () => {
    const engine = engineAnswering(200, { name: 'ok' });

    await submitResource('/api/session', 'POST', 'proof-value', shape, { a: 1 });

    expect(engine.first().path).toBe('/api/session');
    expect(engine.first().init.method).toBe('POST');
    expect(engine.first().init.credentials).toBe('same-origin');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
    expect(engine.header('content-type')).toBe('application/json');
    expect(engine.body()).toEqual({ a: 1 });
  });

  it('sends no body and declares no content type when there is nothing to send', async () => {
    const engine = engineAnswering(200, { name: 'ok' });

    await submitResource('/api/session', 'DELETE', 'proof-value', shape);

    expect(engine.first().init.body).toBeUndefined();
    expect(engine.header('content-type')).toBeUndefined();
  });
});

describe('submitting something the engine answers with nothing', () => {
  /**
   * The answer that means it worked has an empty body. Reading one as though it held something
   * would turn every success into a failure.
   */
  it('accepts an empty answer as success', async () => {
    const engine = engineDoing(async () => respondWithRubbish(204));

    await expect(discardResource('/api/thing/1', 'DELETE', 'proof-value')).resolves.toBeUndefined();

    expect(engine.first().init.method).toBe('DELETE');
    expect(engine.header('X-Csrf-Token')).toBe('proof-value');
  });

  it('reports a refusal in the same way as everything else', async () => {
    engineDoing(async () => respondWith(403, { title: 'Not allowed' }));

    const failure = await discardResource('/api/thing/1', 'DELETE', 'proof-value').catch(
      (error: unknown) => error,
    );

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(403);
    expect((failure as ApiError).message).toBe('Not allowed');
  });
});

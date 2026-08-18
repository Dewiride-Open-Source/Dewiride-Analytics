import { afterEach, describe, expect, it, vi } from 'vitest';
import { engineOrigin } from '@/lib/engine-origin';

afterEach(() => {
  vi.unstubAllEnvs();
});

describe('finding the engine', () => {
  it('uses the address it was given', () => {
    vi.stubEnv('DEWIRIDE_API_ORIGIN', 'http://api:8080');

    expect(engineOrigin()).toBe('http://api:8080');
  });

  it('ignores a trailing slash, so the address joins cleanly onto a path', () => {
    vi.stubEnv('DEWIRIDE_API_ORIGIN', 'https://engine.example.com//');

    expect(engineOrigin()).toBe('https://engine.example.com');
  });

  /**
   * A default that happens to work on the machine it was written on is exactly the setting that
   * fails once, silently, somewhere else. In production there is no default at all.
   */
  it('refuses to guess in production', () => {
    vi.stubEnv('DEWIRIDE_API_ORIGIN', '');
    vi.stubEnv('NODE_ENV', 'production');

    expect(() => engineOrigin()).toThrow(/DEWIRIDE_API_ORIGIN/);
  });

  it('allows the obvious local address while developing, so a fresh clone runs', () => {
    vi.stubEnv('DEWIRIDE_API_ORIGIN', '');
    vi.stubEnv('NODE_ENV', 'development');

    expect(engineOrigin()).toBe('http://localhost:8080');
  });
});

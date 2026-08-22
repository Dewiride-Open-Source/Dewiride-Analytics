import { afterEach, describe, expect, it, vi } from 'vitest';
import { siteOrigin } from '@/lib/site-origin';

afterEach(() => {
  vi.unstubAllEnvs();
});

describe('finding the website in front of the product', () => {
  it('uses the address it was given', () => {
    vi.stubEnv('DEWIRIDE_SITE_ORIGIN', 'http://site:3000');

    expect(siteOrigin()).toBe('http://site:3000');
  });

  it('ignores a trailing slash, so the address joins cleanly onto a path', () => {
    vi.stubEnv('DEWIRIDE_SITE_ORIGIN', 'https://www.example.com//');

    expect(siteOrigin()).toBe('https://www.example.com');
  });

  /**
   * The difference between this setting and the engine's. A dashboard with no engine cannot answer
   * a single screen and says so loudly; a dashboard with nothing in front of it is an installation
   * somebody runs themselves, which is the ordinary case and not a fault.
   */
  it.each([
    ['', 'unset'],
    ['   ', 'blank'],
  ])('answers nothing rather than failing when it is %s (%s)', (value) => {
    vi.stubEnv('DEWIRIDE_SITE_ORIGIN', value);
    vi.stubEnv('NODE_ENV', 'production');

    expect(siteOrigin()).toBeNull();
  });
});

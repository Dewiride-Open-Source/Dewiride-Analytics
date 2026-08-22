import { describe, expect, it } from 'vitest';
import { config } from '@/proxy';

/**
 * The rules saying which addresses reach the proxy at all, read as what they actually are.
 *
 * Everything the proxy does — forwarding to the engine, forwarding to whatever is in front of the
 * product, giving a screen its language — happens only for an address one of these matches. An
 * address that matches none of them is served by the framework directly, and every one of those
 * decisions is silently skipped.
 *
 * The last of them is a regular expression inside a string, so every backslash in it has to survive
 * being a string escape first. One that does not changes the meaning without changing the shape:
 * `.*\..*` is "has a full stop in it", and `.*..*` — the same pattern having lost a backslash — is
 * "is at least two characters long", which excludes very nearly every address there is. It fails as
 * a page that cannot be found, which is not a failure anybody would look for here.
 */
const MATCHERS = config.matcher.map((pattern) =>
  pattern.includes(':path*')
    ? new RegExp(`^${pattern.replace('/:path*', '(?:/.*)?')}$`)
    : new RegExp(`^${pattern}$`),
);

function reaches(address: string): boolean {
  return MATCHERS.some((matcher) => matcher.test(address));
}

describe('which addresses reach the proxy', () => {
  it.each([
    '/',
    '/app',
    '/app/journeys',
    '/app/sign-in',
    '/app/settings/you',
    '/api/session',
    '/api/sites/1/overview',
    '/collect',
    '/collect/pixel.gif',
    '/site-assets/_next/static/chunks/main.js',
    '/robots.txt',
    '/sitemap.xml',
  ])('reaches it for %s', (address) => {
    expect(reaches(address)).toBe(true);
  });

  /**
   * The framework's own files and the beacon are served as they are. The beacon in particular must
   * never be given a language prefix: its address is written into other people's websites.
   */
  it.each(['/_next/static/chunks/main.js', '/dw.js', '/favicon.ico'])(
    'leaves %s to be served directly',
    (address) => {
      expect(reaches(address)).toBe(false);
    },
  );
});

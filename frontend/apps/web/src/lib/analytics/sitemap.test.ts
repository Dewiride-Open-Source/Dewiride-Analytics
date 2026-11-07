import { describe, expect, it } from 'vitest';
import { askAgainIn, couldBeASitemap } from '@/lib/analytics/sitemap';
import type { SitemapStatus } from '@/lib/api/schemas';

const NOTHING_READ: SitemapStatus = {
  address: 'https://example.com/sitemap.xml',
  state: 'waiting',
  problem: null,
  readAt: null,
  succeededAt: null,
  posts: 0,
  dated: 0,
  reading: 0,
  turnedAway: 0,
};

describe('how long to leave it before asking how a reading is going', () => {
  it('does not ask again until there has been a first answer to go on', () => {
    expect(askAgainIn(undefined)).toBe(false);
  });

  it('asks every five seconds while the sitemap itself is being read', () => {
    expect(askAgainIn(NOTHING_READ)).toBe(5_000);
  });

  /** The address has just changed and posts stay listed meanwhile, so the state is what decides. */
  it('asks at that pace while it is read, whatever is already listed', () => {
    expect(askAgainIn({ ...NOTHING_READ, posts: 12, reading: 40 })).toBe(5_000);
  });

  it.each(['read', 'partly-read', 'failed'] as const)(
    'asks twice a minute while pages are left to read on a sitemap %s',
    (state) => {
      expect(askAgainIn({ ...NOTHING_READ, state, reading: 1 })).toBe(30_000);
    },
  );

  it.each(['read', 'partly-read', 'failed'] as const)(
    'stops asking once no page is left on a sitemap %s',
    (state) => {
      expect(askAgainIn({ ...NOTHING_READ, state })).toBe(false);
    },
  );

  /** Nothing is being read, so nothing can come of asking however many pages are left unread. */
  it('stops asking on an installation that does not read sitemaps', () => {
    expect(askAgainIn({ ...NOTHING_READ, state: 'off', reading: 40 })).toBe(false);
  });

  it('has nothing to ask where no sitemap is named', () => {
    expect(askAgainIn({ ...NOTHING_READ, address: null, state: 'none' })).toBe(false);
  });
});

describe('whether what somebody typed could be the address of a sitemap', () => {
  it.each([
    'https://example.com/sitemap.xml',
    'http://example.com/sitemap.xml',
    'https://blog.example.com/wp-sitemap.xml',
    'https://example.com/sitemap.php?type=posts',
    'HTTPS://EXAMPLE.COM/SITEMAP.XML',
    'https://bücher.example/sitemap.xml',
    // Whether these are this website's own, or can be read at all, is the engine's to say.
    'https://elsewhere.example.org/sitemap.xml',
    'https://example.com:8443/sitemap.xml',
    'https://somebody@example.com/sitemap.xml',
  ])('leaves %s for the engine', (typed) => {
    expect(couldBeASitemap(typed)).toBe(true);
  });

  it.each([
    'example.com/sitemap.xml',
    'www.example.com',
    'sitemap.xml',
    '/sitemap.xml',
    'not an address',
    'ftp://example.com/sitemap.xml',
    'file:///etc/passwd',
    'javascript:alert(1)',
    'https://',
    'https://localhost/sitemap.xml',
  ])('stops %s', (typed) => {
    expect(couldBeASitemap(typed)).toBe(false);
  });
});

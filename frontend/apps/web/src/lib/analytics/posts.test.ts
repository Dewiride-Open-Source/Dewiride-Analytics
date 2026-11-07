import { describe, expect, it } from 'vitest';
import { postLink, postsLook, postsNote, viewsCountedSince } from '@/lib/analytics/posts';
import type { SitemapStatus } from '@/lib/api/schemas';

const READ: SitemapStatus = {
  address: 'https://example.com/sitemap.xml',
  state: 'read',
  problem: null,
  readAt: '2026-11-07T08:00:00+00:00',
  succeededAt: '2026-11-07T08:00:00+00:00',
  posts: 0,
  dated: 0,
  reading: 0,
  turnedAway: 0,
};

describe('where a post can be opened from', () => {
  it.each([
    ['https://example.com/a/', 'https://example.com/a/'],
    ['http://example.com/a/', 'http://example.com/a/'],
    ['HTTPS://EXAMPLE.COM/A/', 'https://example.com/A/'],
  ])('links to %s', (address, link) => {
    expect(postLink(address)).toBe(link);
  });

  it.each([
    'javascript:alert(document.cookie)',
    ' JavaScript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'vbscript:msgbox(1)',
    'file:///etc/passwd',
    'ftp://example.com/a/',
    '/a/',
    '//example.com/a/',
    'not an address',
    '',
  ])('never links to %j', (address) => {
    expect(postLink(address)).toBeNull();
  });
});

describe('what stands where the posts are shown', () => {
  it.each([
    ['listed', { state: 'read', posts: 3 }],
    ['listed', { state: 'failed', posts: 3 }],
    ['listed', { state: 'off', posts: 3 }],
    ['listed', { state: 'waiting', posts: 3 }],
    ['none', { state: 'none' }],
    ['off', { state: 'off' }],
    ['waiting', { state: 'waiting' }],
    ['pages', { state: 'read', reading: 12 }],
    ['failed', { state: 'failed' }],
    ['failed', { state: 'failed', reading: 12 }],
    ['partly', { state: 'partly-read' }],
    ['partly', { state: 'partly-read', reading: 12 }],
    ['empty', { state: 'read' }],
  ] as const)('is %s for %j', (look, reading) => {
    expect(postsLook({ ...READ, ...reading })).toBe(look);
  });
});

describe('what the card counting the posts says under its number', () => {
  it.each([
    ['listed', { state: 'read', posts: 3 }],
    ['listed', { state: 'partly-read', posts: 3 }],
    ['failed', { state: 'failed', posts: 3 }],
    ['off', { state: 'off', posts: 3 }],
    ['waiting', { state: 'waiting', posts: 3 }],
    ['none', { state: 'none' }],
    ['off', { state: 'off' }],
    ['waiting', { state: 'waiting' }],
    ['pages', { state: 'read', reading: 12 }],
    ['failed', { state: 'failed' }],
    ['failed', { state: 'failed', reading: 12 }],
    ['partly', { state: 'partly-read' }],
    ['partly', { state: 'partly-read', reading: 12 }],
    ['empty', { state: 'read' }],
  ] as const)('says %s for %j', (note, reading) => {
    expect(postsNote({ ...READ, ...reading })).toBe(note);
  });
});

describe('since when views are said to be counted', () => {
  it('names a month later than the record begins', () => {
    expect(viewsCountedSince('2026-08', '2025-11')).toBe('2026-08');
  });

  it.each([
    ['the month the record begins', '2025-11', '2025-11'],
    ['no month at all', null, '2025-11'],
    ['a record not known yet', '2026-08', undefined],
  ] as const)('names nothing for %s', (_, viewsFrom, firstMonth) => {
    expect(viewsCountedSince(viewsFrom, firstMonth)).toBeNull();
  });
});

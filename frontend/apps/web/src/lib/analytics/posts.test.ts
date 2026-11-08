import { describe, expect, it } from 'vitest';
import {
  POSTS_SEARCH_MOST,
  pageFrom,
  postLink,
  postsLook,
  postsNote,
  searchFrom,
  viewsCountedSince,
} from '@/lib/analytics/posts';
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

/** A search is read here exactly as the engine reads one, so nothing the box sends is refused. */
describe('how a search of the posts is read', () => {
  const LONGEST = 'x'.repeat(POSTS_SEARCH_MOST);

  it('reads a search as words, as the engine does, no longer than it reads one', () => {
    expect(searchFrom('  Hello,\t\n  world  ')).toBe('Hello, world');
    expect(searchFrom('a\u0000b\u00a0c\u2003d\u2028e\u0085f\u007fg')).toBe('a b c d e f g');
    expect(searchFrom(LONGEST)).toBe(LONGEST);
    expect(searchFrom(`${LONGEST}y`)).toBe(LONGEST);
  });

  it.each(['', ' ', '\t\n', '\u0000\u3000'])('reads %j as no search at all', (typed) => {
    expect(searchFrom(typed)).toBe('');
  });

  /** Wildcards and quotes are letters like any other: the engine compares the search as a value. */
  it.each([
    '100% sure',
    'snake_case',
    'C:\\temp',
    'It’s "quoted"',
    "It's 'quoted'",
    "'; DROP TABLE posts; --",
  ])('reads %j as it was typed', (typed) => {
    expect(searchFrom(typed)).toBe(typed);
  });

  it('is measured once its spaces are folded', () => {
    expect(searchFrom(`   ${LONGEST}   `)).toBe(LONGEST);
    expect(searchFrom(`a${' '.repeat(200)}b`)).toBe('a b');
  });

  it('never cuts a character written in two halves, nor ends on the space it was cut after', () => {
    expect(searchFrom(`${'x'.repeat(POSTS_SEARCH_MOST - 1)}😀`)).toBe(
      'x'.repeat(POSTS_SEARCH_MOST - 1),
    );
    expect(searchFrom(`${'x'.repeat(POSTS_SEARCH_MOST - 2)}😀z`)).toBe(
      `${'x'.repeat(POSTS_SEARCH_MOST - 2)}😀`,
    );
    expect(searchFrom(`${'x'.repeat(POSTS_SEARCH_MOST - 1)} yz`)).toBe(
      'x'.repeat(POSTS_SEARCH_MOST - 1),
    );
  });

  /** An address holding a search reads back as the search it holds, so it is never written again. */
  it.each([
    '  Hello,\t\n  world  ',
    `${'x'.repeat(POSTS_SEARCH_MOST - 1)} yz`,
    `${'x'.repeat(POSTS_SEARCH_MOST - 1)}😀`,
  ])('reads what it read from %j the same way again', (typed) => {
    expect(searchFrom(searchFrom(typed))).toBe(searchFrom(typed));
  });
});

describe('how a page of the posts is read', () => {
  it('reads a page only as a whole number from one up', () => {
    expect(pageFrom('1')).toBe(1);
    expect(pageFrom('2')).toBe(2);
    expect(pageFrom('40000')).toBe(40000);
  });

  it.each([
    '',
    '0',
    '-1',
    '+2',
    '02',
    '2.0',
    '2.5',
    '1e3',
    ' 2',
    '2 ',
    'two',
    '0x10',
    '99999999999999999999',
  ])('reads %j as no page at all', (written) => {
    expect(pageFrom(written)).toBeNull();
  });

  /** Where a page that far along begins is still a whole number the engine reads. */
  it('reads no page further along than a million', () => {
    expect(pageFrom('1000000')).toBe(1_000_000);
    expect(pageFrom('1000001')).toBeNull();
  });
});

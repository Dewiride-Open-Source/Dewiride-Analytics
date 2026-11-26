import { describe, expect, it } from 'vitest';
import { pagesState } from '@/lib/analytics/pages-listing';
import type { PagesListing } from '@/lib/analytics/posts';
import type { AllPages } from '@/lib/api/schemas';

const FIRST: PagesListing = { search: '', limit: 25, offset: 0 };

const SEARCHED: PagesListing = { ...FIRST, search: 'azure bot' };

const FURTHER: PagesListing = { ...FIRST, offset: 50 };

/** A page of the list holding two pages of a list of thirty. */
const LISTED: AllPages = {
  matching: 30,
  viewsFrom: '2026-08',
  leftOut: false,
  mostPageViews: 120,
  shown: [
    { path: '/', pageViews: 120 },
    { path: '/about/', pageViews: 40 },
  ],
};

/** A page further along than the list of thirty reaches. */
const PAST_THE_END: AllPages = { ...LISTED, shown: [] };

/** What the engine answers where no page matches, whatever was asked. */
const NONE: AllPages = {
  matching: 0,
  viewsFrom: null,
  leftOut: false,
  mostPageViews: 0,
  shown: [],
};

/**
 * What the engine answers where no page matches and earlier months left pages out: each was opened
 * by one visitor only, and none has been viewed since.
 */
const NONE_KEPT: AllPages = { ...NONE, leftOut: true };

describe('what stands where the list of every page is shown', () => {
  it('waits for the first page of the list, whoever it counts', () => {
    expect(pagesState(undefined, FIRST, 'everybody', false)).toStrictEqual({ look: 'waiting' });
    expect(pagesState(undefined, SEARCHED, 'people', false)).toStrictEqual({ look: 'waiting' });
  });

  it('says what went wrong where no page could be had', () => {
    const refused = new Error('refused');

    expect(pagesState(refused, FIRST, 'everybody', false)).toStrictEqual({
      look: 'failed',
      error: refused,
    });
  });

  it.each([
    ['the page asked for', false],
    ['a page standing in for the one asked for', true],
  ])('lists %s', (_, standingIn) => {
    expect(pagesState(LISTED, FIRST, 'everybody', standingIn)).toStrictEqual({
      look: 'listed',
      answer: LISTED,
    });
  });

  it.each([
    ['the page asked for', false],
    ['a page standing in for the one asked for', true],
  ])('says %s is past the end of a list that still holds pages', (_, standingIn) => {
    expect(pagesState(PAST_THE_END, FURTHER, 'people', standingIn)).toStrictEqual({
      look: 'pastTheEnd',
      answer: PAST_THE_END,
    });
  });

  it.each([
    ['everybody', 'empty'],
    ['people', 'noPeople'],
  ] as const)('says nothing has been viewed by %s, where nothing was searched for', (who, look) => {
    expect(pagesState(NONE, FIRST, who, false)).toStrictEqual({ look, answer: NONE });
  });

  /** Pages were viewed, in months that keep only the pages more than one visitor opened. */
  it.each(['everybody', 'people'] as const)(
    'says there are no pages to list yet for %s, never that nothing was viewed, where earlier months kept none',
    (who) => {
      expect(pagesState(NONE_KEPT, FIRST, who, false)).toStrictEqual({
        look: 'noneKept',
        answer: NONE_KEPT,
      });
      expect(pagesState(NONE_KEPT, SEARCHED, who, false)).toStrictEqual({
        look: 'noMatch',
        answer: NONE_KEPT,
      });
    },
  );

  it.each([
    ['the page asked for', false],
    ['a page standing in for the one asked for', true],
  ])('says no page holds what was searched for, on %s', (_, standingIn) => {
    expect(pagesState(NONE, SEARCHED, 'everybody', standingIn)).toStrictEqual({
      look: 'noMatch',
      answer: NONE,
    });
  });

  /** The page on its way may well list some, and the tracking code is no answer to that. */
  it.each(['everybody', 'people'] as const)(
    'never says nothing has been viewed by %s while the next page is on its way',
    (who) => {
      expect(pagesState(NONE, FIRST, who, true)).toStrictEqual({ look: 'waiting' });
    },
  );

  /** The page on its way may well list some, so saying there are none would be untrue. */
  it.each(['everybody', 'people'] as const)(
    'never says there are no pages to list yet for %s while the next page is on its way',
    (who) => {
      expect(pagesState(NONE_KEPT, FIRST, who, true)).toStrictEqual({ look: 'waiting' });
    },
  );
});

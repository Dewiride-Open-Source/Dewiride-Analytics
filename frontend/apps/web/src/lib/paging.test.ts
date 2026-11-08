import { describe, expect, it } from 'vitest';
import { offsetOf, pageCount, pageOf, pageRow, pagesFor, phonePagesFor } from '@/lib/paging';

describe('dividing a list into pages', () => {
  it('counts a part-full last page as a page', () => {
    expect(pageCount(30, 25)).toBe(2);
    expect(pageCount(50, 25)).toBe(2);
    expect(pageCount(51, 25)).toBe(3);
  });

  it('has no pages when there is nothing to show', () => {
    expect(pageCount(0, 25)).toBe(0);
  });

  it('says which page a position falls on, counting from one', () => {
    expect(pageOf(0, 25)).toBe(1);
    expect(pageOf(24, 25)).toBe(1);
    expect(pageOf(25, 25)).toBe(2);
  });

  it('turns a page back into where it begins', () => {
    expect(offsetOf(1, 25)).toBe(0);
    expect(offsetOf(3, 25)).toBe(50);
  });

  it('reads back whichever page an offset was taken from', () => {
    for (const page of [1, 2, 7, 40]) {
      expect(pageOf(offsetOf(page, 10), 10)).toBe(page);
    }
  });
});

describe('which page numbers are offered', () => {
  it('offers nothing at all for an empty list', () => {
    expect(pagesFor(1, 0)).toEqual([]);
  });

  it('offers every page while they all fit', () => {
    expect(pagesFor(3, 5)).toEqual([1, 2, 3, 4, 5]);
  });

  it('keeps both ends and the pages either side of where somebody is', () => {
    expect(pagesFor(5, 10)).toEqual([1, 'gap', 4, 5, 6, 'gap', 10]);
  });

  it('needs no break at the end somebody is standing on', () => {
    expect(pagesFor(2, 10)).toEqual([1, 2, 3, 'gap', 10]);
    expect(pagesFor(9, 10)).toEqual([1, 'gap', 8, 9, 10]);
  });

  /**
   * A break that stands for one page is wider than the page it replaced, and it puts a page two
   * steps away for no reason.
   */
  it('shows a single left-out page rather than a break', () => {
    expect(pagesFor(1, 4)).toEqual([1, 2, 3, 4]);
    expect(pagesFor(4, 6)).toEqual([1, 2, 3, 4, 5, 6]);
  });

  it('keeps only both ends and where somebody is, where it offers no pages beside it', () => {
    expect(pagesFor(5, 10, 0)).toEqual([1, 'gap', 5, 'gap', 10]);
    expect(pagesFor(3, 10, 0)).toEqual([1, 2, 3, 'gap', 10]);
  });
});

describe('which page numbers a phone is offered', () => {
  /** With every page large enough to press, a phone has room for seven places in a row, not nine. */
  it('offers a phone the page somebody is on and both ends, and the pages beside it only where there is room', () => {
    expect(pageRow(5, 10)).toEqual([
      { step: 1, on: 'every' },
      { step: 'gap', on: 'every' },
      { step: 4, on: 'wide' },
      { step: 5, on: 'every' },
      { step: 6, on: 'wide' },
      { step: 'gap', on: 'every' },
      { step: 10, on: 'every' },
    ]);
  });

  it('gives a phone a break of its own where the pages it leaves out are offered everywhere else', () => {
    expect(pageRow(4, 10)).toEqual([
      { step: 1, on: 'every' },
      { step: 2, on: 'wide' },
      { step: 3, on: 'wide' },
      { step: 'gap', on: 'narrow' },
      { step: 4, on: 'every' },
      { step: 5, on: 'wide' },
      { step: 'gap', on: 'every' },
      { step: 10, on: 'every' },
    ]);
  });

  it('offers the same row everywhere while every page fits', () => {
    expect(pageRow(3, 5).every(({ on }) => on === 'every')).toBe(true);
  });

  /** A list of a few pages is offered whole on a phone, from whichever end somebody is at. */
  it.each([
    [1, 4, [1, 2, 3, 4]],
    [4, 4, [1, 2, 3, 4]],
    [1, 5, [1, 2, 'gap', 5]],
    [2, 5, [1, 2, 3, 4, 5]],
    [5, 5, [1, 'gap', 4, 5]],
  ])('offers a phone page %i of %i as the row a wider screen offers', (current, count, row) => {
    expect(phonePagesFor(current, count)).toEqual(row);
    expect(pageRow(current, count).every(({ on }) => on === 'every')).toBe(true);
  });

  it('offers a phone only the page somebody is on and both ends once the row would not fit', () => {
    expect(phonePagesFor(5, 10)).toEqual([1, 'gap', 5, 'gap', 10]);
    expect(phonePagesFor(4, 7)).toEqual([1, 'gap', 4, 'gap', 7]);
  });

  /** Whatever the width, the row is exactly the one that width offers, with every page in it once. */
  it.each([2, 3, 6, 7, 10, 120, 2000])(
    'offers each width of screen exactly its own row of %i pages, wherever somebody is',
    (count) => {
      const places = [1, 2, 3, 4, Math.ceil(count / 2), count - 3, count - 2, count - 1, count];

      for (const current of places.filter((page) => page >= 1 && page <= count)) {
        const row = pageRow(current, count);
        const offeredOn = (width: 'wide' | 'narrow') =>
          row.filter(({ on }) => on === 'every' || on === width).map(({ step }) => step);

        expect(offeredOn('wide')).toEqual(pagesFor(current, count));
        expect(offeredOn('narrow')).toEqual(phonePagesFor(current, count));
      }
    },
  );
});

import { describe, expect, it } from 'vitest';
import { offsetOf, pageCount, pageOf, pagesFor } from '@/lib/paging';

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
});

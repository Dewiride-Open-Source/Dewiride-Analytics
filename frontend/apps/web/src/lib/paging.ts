/**
 * How a long list is divided into pages, and which of them are worth offering at once.
 *
 * Kept apart from the control that draws them so the rules can be checked without rendering
 * anything, and so that every list on the dashboard divides a period up the same way.
 */

/** One place in the row of page numbers: a page to jump to, or a break where pages were left out. */
export type PageStep = number | 'gap';

/**
 * One place in the row of page numbers, and the screens it is offered on: every screen; only a
 * screen with room for the pages either side of the current one; or only a screen without it, as
 * the break standing for the pages it leaves out.
 */
export interface PageSlot {
  readonly step: PageStep;
  readonly on: 'every' | 'wide' | 'narrow';
}

/** How many pages either side of the current one are offered where there is room for them. */
const NEIGHBOURS = 1;

/**
 * How many places a phone has room for between the steps to the page before and the page after,
 * with every place in the row large enough to press with a thumb.
 */
const PHONE_PLACES = 5;

/** How many pages a list has, or none when it is empty. */
export function pageCount(total: number, perPage: number): number {
  return perPage > 0 ? Math.ceil(Math.max(total, 0) / perPage) : 0;
}

/** Which page a position in the list falls on, counting from one. */
export function pageOf(offset: number, perPage: number): number {
  return perPage > 0 ? Math.floor(Math.max(offset, 0) / perPage) + 1 : 1;
}

/** Where a page begins in the list. */
export function offsetOf(page: number, perPage: number): number {
  return Math.max(page - 1, 0) * perPage;
}

/**
 * The page numbers to offer, with breaks where the rest were left out.
 *
 * The first page, the last, and the ones either side of where somebody is — enough to move a step
 * at a time, to jump to either end, and to see how long the list is, without a row of numbers that
 * wraps onto three lines on a phone.
 *
 * A break never stands for a single page. One number is narrower than the break that would replace
 * it, and a page nobody can reach directly because it happened to fall next to the edge of the
 * window is a page somebody has to take two steps to see.
 *
 * @param current The page on screen, counting from one.
 * @param count How many pages there are.
 * @param neighbours How many pages either side of the current one are offered.
 * @returns The row, in order.
 */
export function pagesFor(
  current: number,
  count: number,
  neighbours = NEIGHBOURS,
): readonly PageStep[] {
  const offered = [...new Set([1, current - neighbours, current, current + neighbours, count])]
    .filter((page) => page >= 1 && page <= count)
    .sort((first, second) => first - second);

  const row: PageStep[] = [];

  for (const [index, page] of offered.entries()) {
    const previous = offered[index - 1];

    if (previous !== undefined && page - previous === 2) {
      row.push(previous + 1);
    } else if (previous !== undefined && page - previous > 2) {
      row.push('gap');
    }

    row.push(page);
  }

  return row;
}

/**
 * The page numbers to offer on a phone.
 *
 * The row {@link pagesFor} offers wherever it fits in the places a phone has room for, so a short
 * list is offered whole. Beyond that only the current page and both ends: the pages either side of
 * the current one, and the breaks around them, would make a row a phone cannot hold.
 *
 * @param current The page on screen, counting from one.
 * @param count How many pages there are.
 * @returns The row, in order.
 */
export function phonePagesFor(current: number, count: number): readonly PageStep[] {
  const wide = pagesFor(current, count);

  return wide.length <= PHONE_PLACES ? wide : pagesFor(current, count, 0);
}

/**
 * The page numbers to offer on a screen of either width, as one row.
 *
 * Where there is room, the row {@link pagesFor} offers, and on a phone the row
 * {@link phonePagesFor} offers. Written as one row rather than two, so every page is in it once and
 * the screen only leaves out what a width does not offer. A break where there is room stands for the
 * pages a phone leaves out as well, and a phone is given a break of its own only where there is none.
 *
 * @param current The page on screen, counting from one.
 * @param count How many pages there are.
 * @returns The row, in order.
 */
export function pageRow(current: number, count: number): readonly PageSlot[] {
  const narrow = new Set(phonePagesFor(current, count));
  const row: PageSlot[] = [];
  // The last page a phone offers, and whether a break both widths offer has come since it.
  let lastOffered: number | undefined;
  let brokenSince = false;

  for (const step of pagesFor(current, count)) {
    if (step === 'gap') {
      row.push({ step, on: 'every' });
      brokenSince = true;
    } else if (narrow.has(step)) {
      if (lastOffered !== undefined && step - lastOffered > 1 && !brokenSince) {
        row.push({ step: 'gap', on: 'narrow' });
      }

      row.push({ step, on: 'every' });
      lastOffered = step;
      brokenSince = false;
    } else {
      row.push({ step, on: 'wide' });
    }
  }

  return row;
}

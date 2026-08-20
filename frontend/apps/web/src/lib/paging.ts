/**
 * How a long list is divided into pages, and which of them are worth offering at once.
 *
 * Kept apart from the control that draws them so the rules can be checked without rendering
 * anything, and so that every list on the dashboard divides a period up the same way.
 */

/** One place in the row of page numbers: a page to jump to, or a break where pages were left out. */
export type PageStep = number | 'gap';

/** How many pages either side of the current one are always offered. */
const NEIGHBOURS = 1;

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
 * @returns The row, in order.
 */
export function pagesFor(current: number, count: number): readonly PageStep[] {
  const offered = [...new Set([1, current - NEIGHBOURS, current, current + NEIGHBOURS, count])]
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

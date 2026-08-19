/**
 * A length of time split into the figures a sentence is written from.
 *
 * Kept apart from the areas that show one, on the same terms as the share helper: the arithmetic
 * belongs to nobody in particular, and a helper living inside the first area to need it makes the
 * second import that area for something that has nothing to do with it.
 *
 * Split rather than written, because the writing is a translation job. Which of "12s" and
 * "1m 12s" a reader sees, and how either is worded, comes from the message catalogue.
 */

/** Seconds in a minute. */
const MINUTE = 60;

/** A length of time as the two figures it is written from. */
export interface SplitDuration {
  readonly minutes: number;
  readonly seconds: number;
}

/**
 * Splits a length of time into whole minutes and the seconds left over.
 *
 * @param ms The length of time, in milliseconds.
 * @returns Whole minutes, and the remaining whole seconds.
 * @remarks
 * Rounded to the nearest second and never negative. Attention is measured by a clock in somebody
 * else's browser, and a page restored from the browser's own store can hand back a reading taken
 * before the one it is compared against.
 */
export function splitDuration(ms: number): SplitDuration {
  const total = Math.max(0, Math.round(ms / 1000));

  return { minutes: Math.floor(total / MINUTE), seconds: total % MINUTE };
}

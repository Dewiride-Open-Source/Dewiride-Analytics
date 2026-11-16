/**
 * The earlier days somebody may choose to set a period beside, on a website's overview.
 *
 * A choice rather than something the period settles for itself, because the same day is read
 * against more than one other day: against the same day last week to see whether the week is
 * holding up, against yesterday to see what changed overnight, against the same day four weeks ago
 * to see where a month has taken it. Only the reader knows which they came for.
 *
 * Every period opens on its own wherever that is still kept: a run of days beside the same weekdays
 * the fewest whole weeks earlier that clear it, and a month beside the month before. The others are
 * offered only on a run of days, and only where they clear it and land on days the period's own does
 * not — the day before on a single day, four weeks earlier on a run of three weeks or less. Never the
 * same days last year: a day's figures are kept for twelve months, so those days are always partly
 * gone.
 */

/** The earlier days offered, the period's own first, which is the order they are listed in. */
export const COMPARED_WITH = ['before', 'day-before', '4-weeks-before'] as const;

/** One of them. */
export type ComparedWith = (typeof COMPARED_WITH)[number];

/**
 * The earlier days an address can name: every one but the period's own, which an address saying
 * nothing already means.
 */
export const CHOSEN_EARLIER_DAYS = [
  'day-before',
  '4-weeks-before',
] as const satisfies readonly ComparedWith[];

/** One of them. */
export type ChosenEarlierDays = (typeof CHOSEN_EARLIER_DAYS)[number];

/** What the choice is called in an address. */
export const COMPARED_WITH_KEY = 'vs';

/**
 * The earlier days a period is set beside: the ones somebody chose where the period offers them,
 * and the first the period offers where it does not — or nothing where it offers none.
 *
 * A choice the period cannot honour is set aside rather than forgotten. Somebody who reads their
 * days against yesterday and looks at a month for a moment has yesterday back the moment they look
 * at a single day again.
 *
 * @param offered What the period may be set beside, its own first.
 * @param chosen What somebody chose, or nothing where they left it to the period.
 */
export function comparisonOf<T extends { readonly against: ComparedWith }>(
  offered: readonly T[],
  chosen: ComparedWith | null,
): T | null {
  return offered.find((one) => one.against === chosen) ?? offered[0] ?? null;
}

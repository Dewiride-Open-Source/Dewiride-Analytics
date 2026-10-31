/**
 * The ways somebody may choose to cut the picture on the overview.
 *
 * A choice rather than something the period settles for itself, because the same quarter is read
 * for two different things — the shape of it over its weeks, and what happened on one particular
 * day — and only the reader knows which they came for.
 *
 * An hour is never chosen. A period of two days or less is always drawn an hour at a time, since a
 * day of it is one or two points; anything longer has more hours in it than a phone can draw apart.
 * A year is offered over all time alone, once the website's record runs past a first of January:
 * no other period holds two years to set beside each other.
 */

/** The ways offered, finest first, which is the order they are listed in. */
export const CHART_BYS = ['day', 'week', 'month', 'year'] as const;

/** One of them. */
export type ChartBy = (typeof CHART_BYS)[number];

/** What the choice is called in an address. */
export const BY_KEY = 'by';

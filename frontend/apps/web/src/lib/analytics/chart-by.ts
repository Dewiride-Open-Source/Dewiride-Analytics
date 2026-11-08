/**
 * The ways somebody may choose to cut a picture of a period: a website's on its overview, and a
 * post's on its own page, which share the one choice.
 *
 * A choice rather than something the period settles for itself, because the same quarter is read
 * for two different things — the shape of it over its weeks, and what happened on one particular
 * day — and only the reader knows which they came for.
 *
 * An hour is never chosen. A period of two days or less is always drawn an hour at a time, since a
 * day of it is one or two points; anything longer has more hours in it than a phone can draw apart.
 * A year is offered over all time alone, once the record the picture is drawn from runs past a first
 * of January: no other period holds two years to set beside each other.
 */

/** The ways offered, finest first, which is the order they are listed in. */
export const CHART_BYS = ['day', 'week', 'month', 'year'] as const;

/** One of them. */
export type ChartBy = (typeof CHART_BYS)[number];

/**
 * The ways the period on screen may be cut, and where a choice of one goes.
 *
 * Every way another period would show is listed beside the ways this one offers. Choosing one it
 * does not offer is still a choice, and where it goes is the screen's to decide: the screen owns the
 * period, and a way of cutting this period cannot show is a different period.
 */
export interface ChartCutting {
  /** The ways this period can be cut, its own way first. */
  readonly offered: readonly ChartBy[];
  /**
   * The ways the list names, in order: those this period offers and those it would move to another
   * period for. A way no period can show for the record on screen is left off it.
   */
  readonly listed: readonly ChartBy[];
  readonly onChoose: (cut: ChartBy) => void;
}

/** What the choice is called in an address. */
export const BY_KEY = 'by';

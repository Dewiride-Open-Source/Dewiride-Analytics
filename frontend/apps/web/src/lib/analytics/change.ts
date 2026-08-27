/**
 * How a figure moved against the same figure over the period before it.
 *
 * A number on its own says how much; a number beside the one before it says whether anything is
 * happening. That second reading is what somebody opens a dashboard for, and working it out here
 * rather than on each card is what keeps every card saying it the same way.
 */

/** Which way a figure went. */
export type Direction = 'up' | 'down' | 'level';

/** How a figure moved. */
export interface Change {
  readonly direction: Direction;
  /**
   * How far it moved, as a fraction of the earlier figure — `0.12` is twelve per cent up.
   *
   * Nothing at all where the earlier period held none of it. A share of nothing is not a number,
   * and forty visits after none is neither four thousand per cent nor infinitely many: it is a
   * rise with no size to it, and the card says so in words instead.
   */
  readonly fraction: number | null;
}

/**
 * How finely a change is reported: to the nearest whole percentage point.
 *
 * Rounded here rather than on the way to the screen, so that the direction and the figure cannot
 * disagree. Half a per cent shown as nought per cent beside an arrow pointing up reads as a
 * mistake in the arithmetic.
 */
const REPORTED_TO = 100;

/**
 * How a figure moved against the period before, or nothing where there is no move to report.
 *
 * @param now What the period being looked at holds.
 * @param before What the period before it held.
 */
export function changeBetween(now: number, before: number): Change | null {
  if (before > 0) {
    const moved = Math.round(((now - before) / before) * REPORTED_TO) / REPORTED_TO;
    // A fall too small to show rounds to a negative nought, which anything comparing it against
    // nought finds different and anything writing it out prints with a sign in front of it.
    const fraction = moved === 0 ? 0 : moved;

    return { direction: directionOf(fraction), fraction };
  }

  return now > 0 ? { direction: 'up', fraction: null } : null;
}

/** Which way a move went, read off the figure as it will be shown. */
function directionOf(fraction: number): Direction {
  if (fraction > 0) {
    return 'up';
  }

  return fraction < 0 ? 'down' : 'level';
}

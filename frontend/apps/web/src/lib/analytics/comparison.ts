/**
 * Whether the picture carries the earlier days the period is set beside as well as the period
 * being read.
 *
 * The headline numbers always say which way they moved: a figure with nothing beside it is a
 * figure nobody can act on. The drawing is asked for, because a second set of lines behind the
 * first is a real cost to reading the first, and most of the time somebody is looking at the
 * shape of this period rather than at two periods at once.
 *
 * Which earlier days those are is a separate choice, made beside the period. The line behind the
 * picture is drawn from the stretch the cards are measured against by the hour and by the day, and
 * by the week on a run of days; by the month it is moved back by whole months, and by the week on a
 * month by whole weeks, so each earlier bucket lines up with its partner — and the caption says
 * which days it covers.
 */

/** What comparing is called in an address. */
export const COMPARISON_KEY = 'against';

/**
 * What a period may be set against in the drawing.
 *
 * A closed set of one, meaning the earlier days the period is set beside, whichever they are. The
 * address is written by whoever sent the link, so a word that is not this one leaves the drawing on
 * the period it would have drawn anyway.
 */
export const COMPARISONS = ['before'] as const;

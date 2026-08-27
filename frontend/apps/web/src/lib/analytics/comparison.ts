/**
 * Whether the picture carries the period before it as well as the one being read.
 *
 * The headline numbers always say which way they moved: a figure with nothing beside it is a
 * figure nobody can act on. The drawing is asked for, because a second set of lines behind the
 * first is a real cost to reading the first, and most of the time somebody is looking at the
 * shape of this period rather than at two periods at once.
 */

/** What comparing is called in an address. */
export const COMPARISON_KEY = 'against';

/**
 * What a period may be set against.
 *
 * A closed set of one. The address is written by whoever sent the link, so a word that is not
 * this one leaves the drawing on the period it would have drawn anyway.
 */
export const COMPARISONS = ['before'] as const;

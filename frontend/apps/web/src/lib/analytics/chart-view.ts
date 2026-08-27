/**
 * Which question the picture on the overview is answering.
 *
 * Two views of one period rather than two cards, because they are the same question asked at two
 * levels — who came, and how much they read — and nobody wants both drawings on screen at once.
 *
 * They do not count the same thing and neither pretends to. `activity` counts everything a
 * website recorded as it happened. `who` counts visits that have finished and been judged, which
 * is the same population the breakdown further down the screen reports. Each view says which it
 * is, and neither figure is ever shown under the other's name.
 */

/** The views, in the order they are offered. */
export const CHART_VIEWS = ['who', 'activity'] as const;

/** One of them. */
export type ChartView = (typeof CHART_VIEWS)[number];

/**
 * The view the overview opens on.
 *
 * Who the traffic was, because it is the question this product exists to answer and the one
 * nobody else can answer for them. How much traffic there was is a press away.
 */
export const DEFAULT_VIEW: ChartView = 'who';

/** What the view is called in an address. */
export const VIEW_KEY = 'show';

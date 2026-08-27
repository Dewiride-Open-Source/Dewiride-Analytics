import type { ChartView } from '@/lib/analytics/chart-view';

/**
 * How the same numbers are drawn.
 *
 * A choice about appearance and nothing else: the period, the buckets and the counts are
 * identical whichever is picked. Some people read a shape better as a line, some want a column
 * they can compare against the one beside it, and neither is more correct than the other.
 */

/** The styles on offer, in the order they are shown. */
export const DRAWINGS = ['line', 'columns', 'area'] as const;

/** One of them. */
export type Drawing = (typeof DRAWINGS)[number];

/**
 * The style a chart is drawn in until somebody picks another.
 *
 * Offered by every view, which is what lets a remembered style that one view cannot honour fall
 * back to something rather than to nothing.
 */
export const DEFAULT_DRAWING: Drawing = 'area';

/**
 * The styles each view can honestly be drawn in.
 *
 * The stacked view offers no line. Each band on a stack is read by its thickness, and a line
 * drawn along the top of one is read as that band's own figure when it is really the total of
 * everything underneath it.
 */
const OFFERED: Readonly<Record<ChartView, readonly Drawing[]>> = {
  who: ['columns', 'area'],
  activity: ['line', 'columns', 'area'],
};

/** What a view may be drawn as. */
export function drawingsFor(view: ChartView): readonly Drawing[] {
  return OFFERED[view];
}

/** The style a view is actually drawn in, given what somebody last asked for. */
export function drawingFor(view: ChartView, remembered: Drawing): Drawing {
  return drawingsFor(view).includes(remembered) ? remembered : DEFAULT_DRAWING;
}

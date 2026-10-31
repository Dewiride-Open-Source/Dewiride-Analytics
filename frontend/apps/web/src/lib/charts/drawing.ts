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

/**
 * The fewest buckets a picture needs before a line or an area has a shape to draw.
 *
 * One bucket is a single point, which a line and an area draw as nothing at all, and two are one
 * slope between two figures, read as a trend that is not there. Set side by side as columns, each
 * is read as the figure it is — which is how the first month of a website's all time is drawn, and
 * its first two years.
 */
export const SHAPED_FROM = 3;

/** The one style a picture with too few buckets for a shape is drawn in. */
const FEW: readonly Drawing[] = ['columns'];

/**
 * What a view may be drawn as.
 *
 * @param view The question the picture answers.
 * @param buckets How many buckets the picture has, where that is known.
 */
export function drawingsFor(view: ChartView, buckets?: number): readonly Drawing[] {
  return tooFew(buckets) ? FEW : OFFERED[view];
}

/**
 * The style a view is actually drawn in, given what somebody last asked for.
 *
 * @param view The question the picture answers.
 * @param remembered The style somebody last asked for.
 * @param buckets How many buckets the picture has, where that is known.
 */
export function drawingFor(view: ChartView, remembered: Drawing, buckets?: number): Drawing {
  if (tooFew(buckets)) {
    return 'columns';
  }

  return drawingsFor(view).includes(remembered) ? remembered : DEFAULT_DRAWING;
}

/** Whether a picture has too few buckets for a line or an area to have a shape. */
function tooFew(buckets: number | undefined): boolean {
  return buckets !== undefined && buckets < SHAPED_FROM;
}

import { LAYERS, UNSETTLED } from '@/lib/charts/frame';
import type { ChartPalette } from '@/lib/charts/palette';

/**
 * How the buckets that have not finished being judged are quietened on a drawing.
 *
 * By different means in each style, because a column and an area offer different things to
 * quieten. A column has a fill of its own and is faded one column at a time, which lands exactly
 * on the buckets it belongs to. An area has one fill for the whole run and none per bucket, so a
 * wash is laid over the tail instead — and that wash cannot be used on columns, because it is
 * placed against the points the labels sit at and would cut the first and last column of the run
 * down the middle.
 */

/** One figure as a column draws it: plain where it is settled, faded where it is still filling. */
export type Quietened =
  | number
  | { readonly value: number; readonly itemStyle: { readonly opacity: number } };

/** Each figure faded where its bucket is still being judged, for a drawing with a fill a bucket. */
export function quietened(values: readonly number[], stillJudging: number | null): Quietened[] {
  return values.map((value, bucket) =>
    stillJudging !== null && bucket >= stillJudging
      ? { value, itemStyle: { opacity: UNSETTLED } }
      : value,
  );
}

/**
 * The wash over the buckets that have not finished being judged, or nothing when every one has.
 *
 * Runs from the last settled point to the end of the run. A drawing placed at points draws
 * everything to the right of the last settled figure from an unsettled one, so that is where the
 * wash begins. Begun at the first unsettled point, it would have no width at all on the ordinary
 * live period, whose only unsettled bucket is the newest.
 *
 * Carried by a series of its own, at a depth of its own, so that it sits over the bands it is
 * quietening rather than behind them. It is the card's own background at part strength, so the
 * tail reads as faded rather than as a different colour that might mean something.
 */
export function washOver(
  labels: readonly string[],
  stillJudging: number | null,
  palette: ChartPalette,
): Record<string, unknown> | undefined {
  if (stillJudging === null) {
    return undefined;
  }

  const from = labels[Math.max(stillJudging - 1, 0)];
  const to = labels.at(-1);

  if (from === undefined || to === undefined) {
    return undefined;
  }

  return {
    type: 'line',
    data: [],
    silent: true,
    markArea: {
      silent: true,
      itemStyle: { color: palette.surface, opacity: 0.62 },
      z: LAYERS.unsettled,
      data: [[{ xAxis: from }, { xAxis: to }]],
    },
  };
}

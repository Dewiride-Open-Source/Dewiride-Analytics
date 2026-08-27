import { color } from 'echarts/core';
import { LAYERS } from '@/lib/charts/frame';

/**
 * A measure from the period before, drawn behind the one being read.
 *
 * A dashed line, whatever the current period is drawn as. Two sets of columns side by side halve
 * the width of both and turn a shape into a comb; a second area buries the first. A dashed line
 * reads as a reference rather than as a measurement, which is what an earlier period is.
 *
 * It is drawn against the buckets of the period being read rather than its own, because the two
 * are the same stretch of time offset by one period and the whole point is to read them off the
 * same place on the axis. Which days the dashed line actually covers is stated beneath the
 * drawing, and its figures are published in the table with the rest.
 */

/** How much of its colour a measure keeps when it is the period before rather than this one. */
const FADED = 0.6;

/** One measure over the earlier period. */
export interface Ghost {
  /** What it is called, already marked as the earlier period's. */
  readonly name: string;
  /** What it counted, bucket by bucket. */
  readonly values: readonly number[];
  /** The colour the same measure is drawn in for the period being read. */
  readonly colour: string;
  /** How many buckets the period being read has. */
  readonly buckets: number;
  /**
   * How much the measure it is set against is curved.
   *
   * The same curve, so that the two shapes are the same drawing of the same kind of thing. A
   * straight line beside a curved one reads as a difference in the traffic rather than as a
   * difference in how it was drawn.
   */
  readonly smooth: number | false;
}

/** The earlier period's line. */
export function ghostSeries(ghost: Ghost): Record<string, unknown> {
  const faded = color.modifyAlpha(ghost.colour, FADED);

  return {
    name: ghost.name,
    type: 'line',
    // Padded out to the buckets being drawn against. A month set beside a shorter one has no
    // thirty-first day, and a line that stops is the truthful drawing of a day that did not exist.
    data: Array.from({ length: ghost.buckets }, (_, at) => ghost.values[at] ?? null),
    smooth: ghost.smooth,
    symbol: 'none',
    z: LAYERS.earlier,
    lineStyle: { width: 1.5, type: 'dashed', color: faded },
    itemStyle: { color: faded },
  };
}

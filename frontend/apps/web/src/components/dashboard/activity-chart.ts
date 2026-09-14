import { color, type EChartsCoreOption } from 'echarts/core';
import type { Drawing } from '@/lib/charts/drawing';
import { chartFrame } from '@/lib/charts/frame';
import { ghostSeries } from '@/lib/charts/ghost';
import type { ChartPalette } from '@/lib/charts/palette';
import { quietened, washOver } from '@/lib/charts/wash';

/** One thing counted across a period, named in the reader's own language. */
export interface Measure {
  readonly name: string;
  readonly values: readonly number[];
}

/** Two measures of how much of a website was read over a period, ready to be drawn. */
export interface ActivityChart {
  /** What each bucket is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** The two measures, in the order they are keyed and tabled. */
  readonly measures: readonly [Measure, Measure];
  readonly drawing: Drawing;
  /**
   * The first bucket still being judged, or nothing when every figure is settled — which
   * everything recorded as it happened always is.
   */
  readonly stillJudging?: number | null;
  /** The same two measures over the period before, where that is drawn behind this one. */
  readonly earlier?: readonly [Measure, Measure];
}

/** Two measures across a period, each in the chart's own colour for it. */
export function activityOption(chart: ActivityChart, palette: ChartPalette): EChartsCoreOption {
  const columns = chart.drawing === 'columns';
  const stillJudging = chart.stillJudging ?? null;
  const wash = columns ? undefined : washOver(chart.labels, stillJudging, palette);

  return {
    ...chartFrame(palette, chart.labels, columns),
    series: [
      measure(chart.measures[0], palette.series[0], chart.drawing, stillJudging),
      measure(chart.measures[1], palette.series[1], chart.drawing, stillJudging),
      ...(wash ? [wash] : []),
      ...ghosts(chart, palette),
    ],
  };
}

/**
 * The same two measures over the period before, where one is being drawn behind this one.
 *
 * Each is drawn in the colour of the measure it is set against, so that a rise or a fall is read
 * off a pair rather than off four lines somebody has to match up first.
 */
function ghosts(chart: ActivityChart, palette: ChartPalette): Record<string, unknown>[] {
  const earlier = chart.earlier;

  if (!earlier) {
    return [];
  }

  const buckets = chart.labels.length;
  const smooth = curve(chart.drawing);
  const [first, second] = earlier;

  return [
    { ...first, colour: palette.series[0], buckets, smooth },
    { ...second, colour: palette.series[1], buckets, smooth },
  ].map((earlierMeasure) => ghostSeries(earlierMeasure));
}

/**
 * How much a line is curved.
 *
 * Enough to read as a trend rather than as a chain of readings, and not so much that it invents a
 * peak between two buckets.
 */
const CURVE = 0.3;

/** The curve a measure is drawn with, which columns leave to the line beside them. */
function curve(drawing: Drawing): number | false {
  return drawing === 'columns' ? false : CURVE;
}

/**
 * One measure drawn across the period, in whichever style was asked for.
 *
 * Drawn as columns, each bucket still being judged is faded on its own; drawn as a line or an
 * area, the wash the option lays over the tail does the quietening instead.
 */
function measure(
  one: Measure,
  colour: string,
  drawing: Drawing,
  stillJudging: number | null,
): Record<string, unknown> {
  if (drawing === 'columns') {
    return {
      name: one.name,
      type: 'bar',
      data: quietened(one.values, stillJudging),
      itemStyle: { color: colour, borderRadius: [2, 2, 0, 0] },
      barMaxWidth: 22,
    };
  }

  return {
    name: one.name,
    type: 'line',
    data: [...one.values],
    smooth: CURVE,
    symbol: 'circle',
    symbolSize: 6,
    // A dot on every bucket of a quarter is a smear. They earn their place only where there are
    // few enough of them to point at one and read its figure.
    showSymbol: one.values.length <= 14,
    lineStyle: { width: 2, color: colour },
    itemStyle: { color: colour },
    areaStyle: drawing === 'area' ? { color: fade(colour) } : undefined,
  };
}

/**
 * The wash beneath a measure, fading out towards the axis.
 *
 * A flat fill at one opacity has to be light enough not to muddy the dark theme, which leaves it
 * invisible in the light one. A gradient is legible in both because it is strongest where the
 * line is and gone by the time it reaches the bottom of the chart — which is also what lets two
 * of them sit one inside the other without the nearer one burying the further.
 */
function fade(colour: string) {
  return {
    type: 'linear' as const,
    x: 0,
    y: 0,
    x2: 0,
    y2: 1,
    colorStops: [
      { offset: 0, color: color.modifyAlpha(colour, 0.28) },
      { offset: 1, color: color.modifyAlpha(colour, 0) },
    ],
  };
}

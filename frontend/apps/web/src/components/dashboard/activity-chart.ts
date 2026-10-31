import { color, type EChartsCoreOption } from 'echarts/core';
import type { Granularity } from '@/lib/analytics/period';
import type { Drawing } from '@/lib/charts/drawing';
import { chartFrame, type FigureWriter } from '@/lib/charts/frame';
import { ghostSeries } from '@/lib/charts/ghost';
import type { ChartPalette } from '@/lib/charts/palette';
import { type Quiet, quietened, UNQUIETENED, washOver } from '@/lib/charts/wash';

/** One thing counted across a period, named in the reader's own language. */
export interface Measure {
  readonly name: string;
  /**
   * What it counted in each bucket, or nothing for a month of all time left out — never counted,
   * or kept before its visits were all judged, which leaves it out of every figure resting on a
   * verdict: who came, for everybody as well, and how much the people read — which a line leaves a
   * gap at, and a run of columns leaves empty.
   */
  readonly values: readonly (number | null)[];
}

/** Two measures of how much of a website was read over a period, ready to be drawn. */
export interface ActivityChart {
  /** What each bucket is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** What each bucket is called in full, over the card a hover opens. */
  readonly titles: readonly string[];
  /** How the period is cut, which for years names every bucket along the axis. */
  readonly granularity: Granularity;
  /** The two measures, in the order they are keyed and tabled. */
  readonly measures: readonly [Measure, Measure];
  /** How the card a hover opens writes a figure of either measure. */
  readonly figure: FigureWriter;
  readonly drawing: Drawing;
  /**
   * The buckets drawn quietly: a week or a month the period cuts short, at either end, and those
   * at the end still being judged — which everything recorded as it happened never is. Nothing is
   * quietened where this is left out.
   */
  readonly quiet?: Quiet;
  /** The same two measures over the period before, where that is drawn behind this one. */
  readonly earlier?: readonly [Measure, Measure];
}

/** Two measures across a period, each in the chart's own colour for it. */
export function activityOption(chart: ActivityChart, palette: ChartPalette): EChartsCoreOption {
  const columns = chart.drawing === 'columns';
  const quiet = chart.quiet ?? UNQUIETENED;
  const wash = columns ? undefined : washOver(chart.labels, quiet, palette);

  return {
    ...chartFrame(palette, chart.labels, columns, chart.titles, {
      every: chart.granularity === 'year',
    }),
    series: [
      measure(chart.measures[0], palette.series[0], chart, quiet),
      measure(chart.measures[1], palette.series[1], chart, quiet),
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
 * Drawn as columns, each quietened bucket is faded on its own; drawn as a line or an area, the
 * wash the option lays over the ends does the quietening instead. Either way the card a hover opens
 * writes its figures as the chart says.
 */
function measure(
  one: Measure,
  colour: string,
  { drawing, figure }: ActivityChart,
  quiet: Quiet,
): Record<string, unknown> {
  const tooltip = { valueFormatter: figure };

  if (drawing === 'columns') {
    return {
      name: one.name,
      type: 'bar',
      data: quietened(one.values, quiet),
      itemStyle: { color: colour, borderRadius: [2, 2, 0, 0] },
      barMaxWidth: 22,
      tooltip,
    };
  }

  return {
    name: one.name,
    type: 'line',
    tooltip,
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

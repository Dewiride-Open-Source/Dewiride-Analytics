import { color, type EChartsCoreOption } from 'echarts/core';
import type { Drawing } from '@/lib/charts/drawing';
import { chartFrame } from '@/lib/charts/frame';
import { ghostSeries } from '@/lib/charts/ghost';
import type { ChartPalette } from '@/lib/charts/palette';

/** How much of a website was read over a period, ready to be drawn. */
export interface ActivityChart {
  /** What each bucket is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** What the two measures are called, which depends on how wide a bucket is. */
  readonly names: readonly [string, string];
  readonly pageViews: readonly number[];
  readonly visitors: readonly number[];
  readonly drawing: Drawing;
  /** The same two measures over the period before, where that is being drawn behind this one. */
  readonly earlier?: EarlierActivity;
}

/** How much was read over the period before, ready to be drawn behind the period being read. */
export interface EarlierActivity {
  /** What the two measures are called when they are the earlier period's. */
  readonly names: readonly [string, string];
  readonly pageViews: readonly number[];
  readonly visitors: readonly number[];
}

/** Page views and visitors across a period. */
export function activityOption(chart: ActivityChart, palette: ChartPalette): EChartsCoreOption {
  const columns = chart.drawing === 'columns';

  return {
    ...chartFrame(palette, chart.labels, columns),
    series: [
      measure(chart.names[0], chart.pageViews, palette.series[0], chart.drawing),
      measure(chart.names[1], chart.visitors, palette.series[1], chart.drawing),
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

  return [
    {
      name: earlier.names[0],
      values: earlier.pageViews,
      colour: palette.series[0],
      buckets,
      smooth,
    },
    {
      name: earlier.names[1],
      values: earlier.visitors,
      colour: palette.series[1],
      buckets,
      smooth,
    },
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

/** One measure drawn across the period, in whichever style was asked for. */
function measure(
  name: string,
  values: readonly number[],
  colour: string,
  drawing: Drawing,
): Record<string, unknown> {
  if (drawing === 'columns') {
    return {
      name,
      type: 'bar',
      data: [...values],
      itemStyle: { color: colour, borderRadius: [2, 2, 0, 0] },
      barMaxWidth: 22,
    };
  }

  return {
    name,
    type: 'line',
    data: [...values],
    smooth: CURVE,
    symbol: 'circle',
    symbolSize: 6,
    // A dot on every bucket of a quarter is a smear. They earn their place only where there are
    // few enough of them to point at one and read its figure.
    showSymbol: values.length <= 14,
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

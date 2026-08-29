import type { EChartsCoreOption } from 'echarts/core';
import { chartFrame, UNSETTLED } from '@/lib/charts/frame';
import type { ChartPalette } from '@/lib/charts/palette';

/** How much of a website was read minute by minute over the last half hour, ready to be drawn. */
export interface MinuteChart {
  /** What each minute is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** What the columns count, in the reader's own language. */
  readonly name: string;
  readonly pageViews: readonly number[];
  /** The minute still running, which is not finished and is not drawn as though it were. */
  readonly running: number | null;
}

/**
 * The one thing drawn here, named so that it survives being redrawn.
 *
 * The surface replaces what it is drawing rather than matching it up by position, so a part with
 * no name of its own is a new part every time. This chart is drawn again every few seconds, and a
 * new part each time would start its columns at the axis and grow them out again on every reading.
 */
const MINUTES = 'minutes';

/**
 * How much of a website was read, minute by minute.
 *
 * Columns rather than a line. A minute holds a handful of page views at most on the great majority
 * of websites, and a curve drawn through thirty such counts invents readers in the gaps between
 * them — the same reason the bands on the overview are never smoothed.
 */
export function minuteOption(chart: MinuteChart, palette: ChartPalette): EChartsCoreOption {
  return {
    ...chartFrame(palette, chart.labels, true),
    series: [
      {
        id: MINUTES,
        name: chart.name,
        data: chart.pageViews.map((value, minute) =>
          minute === chart.running ? { value, itemStyle: { opacity: UNSETTLED } } : value,
        ),
        type: 'bar',
        itemStyle: { color: palette.series[0], borderRadius: [2, 2, 0, 0] },
        // Thirty columns across a card that is a phone wide at its narrowest. Capped rather than
        // left to the engine, which would otherwise draw one broad column per minute on a wide
        // screen and turn a half hour into a row of slabs.
        barMaxWidth: 18,
      },
    ],
  };
}

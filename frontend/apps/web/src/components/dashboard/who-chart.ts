import { color, type EChartsCoreOption } from 'echarts/core';
import type { Granularity } from '@/lib/analytics/period';
import type { TrafficBand } from '@/lib/analytics/traffic-series';
import type { VerdictTone } from '@/lib/analytics/verdicts';
import type { Drawing } from '@/lib/charts/drawing';
import { chartFrame, type FigureWriter } from '@/lib/charts/frame';
import { ghostSeries } from '@/lib/charts/ghost';
import type { ChartPalette } from '@/lib/charts/palette';
import { type Quiet, quietened, washOver } from '@/lib/charts/wash';

/** What generated a website's traffic over a period, ready to be drawn. */
export interface WhoChart {
  /** What each bucket is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** What each bucket is called in full, over the card a hover opens. */
  readonly titles: readonly string[];
  /** How the period is cut, which for years names every bucket along the axis. */
  readonly granularity: Granularity;
  /** One band per tone the period held, in reading order. */
  readonly bands: readonly TrafficBand[];
  /** What each tone is called, in the reader's own language. */
  readonly names: Readonly<Record<VerdictTone, string>>;
  /** How the card a hover opens writes a band's figure. */
  readonly figure: FigureWriter;
  readonly drawing: Drawing;
  /**
   * The buckets drawn quietly: a week or a month the period cuts short, at either end, and those
   * at the end still being judged.
   */
  readonly quiet: Quiet;
  /** Every visit the period before held, where that is being drawn behind this one. */
  readonly earlier?: EarlierTotal;
}

/**
 * What the period before came to altogether.
 *
 * One line rather than four. Four faded bands behind four solid ones is unreadable, and it is
 * not what a comparison is for: the bands answer what this period was made of, and the earlier
 * period answers whether there is more of it than there was.
 */
export interface EarlierTotal {
  readonly name: string;
  readonly visits: readonly (number | null)[];
}

/**
 * One stack per bucket, so the bands sit on each other and the whole column is the period's
 * traffic.
 */
const STACK = 'who';

/** Who and what visited, bucket by bucket. */
export function whoOption(chart: WhoChart, palette: ChartPalette): EChartsCoreOption {
  const columns = chart.drawing === 'columns';
  const drawn: Record<string, unknown>[] = chart.bands.map((band) =>
    drawBand(chart.names[band.tone], band.visits, palette.tones[band.tone], chart),
  );
  const wash = columns ? undefined : washOver(chart.labels, chart.quiet, palette);

  if (wash) {
    drawn.push(wash);
  }

  // Above the wash as well as above the bands. The earlier period has been judged in full and is
  // cut into buckets of its own, so it is not one of the things the wash is there to quieten.
  if (chart.earlier) {
    drawn.push(
      ghostSeries({
        name: chart.earlier.name,
        values: chart.earlier.visits,
        colour: palette.text,
        buckets: chart.labels.length,
        // Straight, like the bands. A curve drawn through the counted points invents traffic
        // everywhere between them, which is as untrue of the earlier period as of this one.
        smooth: false,
      }),
    );
  }

  return {
    ...chartFrame(palette, chart.labels, columns, chart.titles, {
      every: chart.granularity === 'year',
    }),
    series: drawn,
  };
}

/**
 * One tone's visits, stacked on whatever is beneath it.
 *
 * Drawn as columns, each quietened bucket is faded on its own; drawn as an area, the wash the
 * option lays over the ends does the quietening instead. Either way the card a hover opens writes
 * its figures as the chart says.
 */
function drawBand(
  name: string,
  visits: readonly (number | null)[],
  colour: string,
  chart: WhoChart,
): Record<string, unknown> {
  const tooltip = { valueFormatter: chart.figure };

  if (chart.drawing === 'columns') {
    return {
      name,
      type: 'bar',
      stack: STACK,
      data: quietened(visits, chart.quiet),
      itemStyle: { color: colour },
      barMaxWidth: 28,
      tooltip,
    };
  }

  return {
    name,
    type: 'line',
    stack: STACK,
    tooltip,
    data: [...visits],
    // No smoothing and no dots. A band on a stack is read by its thickness, and a curve drawn
    // through the counted points invents thickness everywhere between them.
    smooth: false,
    symbol: 'none',
    lineStyle: { width: 1, color: colour },
    // The tone's colour for the marker beside its figure in the card a hover opens, which is
    // taken from here rather than from the line or the area. With no dots drawn it shows nowhere
    // else.
    itemStyle: { color: colour },
    areaStyle: { color: color.modifyAlpha(colour, 0.78) },
  };
}

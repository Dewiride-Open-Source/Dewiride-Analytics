import { color, type EChartsCoreOption } from 'echarts/core';
import type { TrafficBand } from '@/lib/analytics/traffic-series';
import type { VerdictTone } from '@/lib/analytics/verdicts';
import type { Drawing } from '@/lib/charts/drawing';
import { chartFrame } from '@/lib/charts/frame';
import { ghostSeries } from '@/lib/charts/ghost';
import type { ChartPalette } from '@/lib/charts/palette';
import { quietened, washOver } from '@/lib/charts/wash';

/** What generated a website's traffic over a period, ready to be drawn. */
export interface WhoChart {
  /** What each bucket is called, in the website's own zone. */
  readonly labels: readonly string[];
  /** One band per tone the period held, in reading order. */
  readonly bands: readonly TrafficBand[];
  /** What each tone is called, in the reader's own language. */
  readonly names: Readonly<Record<VerdictTone, string>>;
  readonly drawing: Drawing;
  /** The first bucket still being judged, or nothing when the whole period has settled. */
  readonly stillJudging: number | null;
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
  readonly visits: readonly number[];
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
  const wash = columns ? undefined : washOver(chart.labels, chart.stillJudging, palette);

  if (wash) {
    drawn.push(wash);
  }

  // Above the wash as well as above the bands. The earlier period has been judged in full, so it
  // is not one of the things the wash is there to quieten.
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

  return { ...chartFrame(palette, chart.labels, columns), series: drawn };
}

/**
 * One tone's visits, stacked on whatever is beneath it.
 *
 * Drawn as columns, each bucket still being judged is faded on its own; drawn as an area, the
 * wash the option lays over the tail does the quietening instead.
 */
function drawBand(
  name: string,
  visits: readonly number[],
  colour: string,
  chart: WhoChart,
): Record<string, unknown> {
  if (chart.drawing === 'columns') {
    return {
      name,
      type: 'bar',
      stack: STACK,
      data: quietened(visits, chart.stillJudging),
      itemStyle: { color: colour },
      barMaxWidth: 28,
    };
  }

  return {
    name,
    type: 'line',
    stack: STACK,
    data: [...visits],
    // No smoothing and no dots. A band on a stack is read by its thickness, and a curve drawn
    // through the counted points invents thickness everywhere between them.
    smooth: false,
    symbol: 'none',
    lineStyle: { width: 1, color: colour },
    areaStyle: { color: color.modifyAlpha(colour, 0.78) },
  };
}

'use client';

import { LineChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import { type EChartsCoreOption, type EChartsType, init, use as register } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import { useTheme } from 'next-themes';
import { useEffect, useRef } from 'react';
import { type ChartPalette, readChartPalette } from '@/lib/charts/palette';
import { cn } from '@/lib/styling';

/**
 * Only the pieces the product actually draws are registered, because the charting engine's
 * full bundle is several times the size of everything else the dashboard ships. A chart type
 * added later registers itself here alongside these.
 */
register([LineChart, GridComponent, TooltipComponent, CanvasRenderer]);

interface ChartProps {
  /**
   * Builds the chart from the palette in force.
   *
   * Taken as a function rather than a finished object so that the same chart can be redrawn in
   * the other theme without its caller having to watch for the change. Memoise it, or the chart
   * rebuilds on every render of the screen around it.
   */
  readonly option: (palette: ChartPalette) => EChartsCoreOption;
  /**
   * What the chart shows, in a sentence.
   *
   * A canvas is opaque to a screen reader, so this is the whole of what one announces. It is not
   * a substitute for the figures themselves, which every chart in this product also publishes as
   * a table.
   */
  readonly label: string;
  readonly className?: string;
}

/**
 * The one charting surface in the product.
 *
 * Every chart goes through here so that theming, resizing, disposal and the reduced-motion
 * setting are decided once instead of per screen.
 */
export function Chart({ option, label, className }: ChartProps) {
  const holder = useRef<HTMLDivElement>(null);
  const { resolvedTheme } = useTheme();

  useEffect(() => {
    const node = holder.current;

    if (!node) {
      return;
    }

    const chart: EChartsType = init(node, undefined, { renderer: 'canvas' });
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    chart.setOption({
      animation: !still,
      // The engine can generate its own spoken description. Ours is written for this chart and
      // the figures are published as a table besides, so its version would only be noise.
      aria: { enabled: false },
      ...option(readChartPalette()),
    });

    const watcher = new ResizeObserver(() => chart.resize());

    watcher.observe(node);

    return () => {
      watcher.disconnect();
      chart.dispose();
    };
  }, [option, resolvedTheme]);

  return (
    <div ref={holder} role="img" aria-label={label} className={cn('h-full w-full', className)} />
  );
}

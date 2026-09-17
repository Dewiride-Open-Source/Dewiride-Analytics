'use client';

import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, MarkAreaComponent, TooltipComponent } from 'echarts/components';
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
register([
  LineChart,
  BarChart,
  PieChart,
  GridComponent,
  MarkAreaComponent,
  TooltipComponent,
  CanvasRenderer,
]);

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
  /**
   * Told which category along the bottom axis was pressed, where pressing one means something.
   *
   * Given as an index rather than a label: the label is written for reading, and what a press
   * means is the caller's to decide. The whole width of a column counts, from the axis to the
   * top of the plot, so a quiet day is as easy to pick as a busy one; a press on the axis labels
   * or in the margin is not a press on a category. A canvas is not a control, so a caller
   * offering this also offers the same action somewhere a keyboard can reach it.
   */
  readonly onPick?: (index: number) => void;
}

/**
 * The one charting surface in the product.
 *
 * Every chart goes through here so that theming, resizing, disposal and the reduced-motion
 * setting are decided once instead of per screen.
 */
export function Chart({ option, label, className, onPick }: ChartProps) {
  const holder = useRef<HTMLDivElement>(null);
  const drawn = useRef<EChartsType | null>(null);
  const { resolvedTheme } = useTheme();

  // The surface outlives whatever is drawn on it, and is rebuilt only when the theme changes the
  // colours it was built with. A chart whose figures move on their own — the live screen's does,
  // every few seconds — has to be redrawn rather than rebuilt: a fresh surface starts its columns
  // at the axis every time, so a busy website would spend the whole half hour growing them back.
  useEffect(() => {
    const node = holder.current;

    if (!node) {
      return;
    }

    const chart: EChartsType = init(node, undefined, { renderer: 'canvas' });

    drawn.current = chart;

    const watcher = new ResizeObserver(() => chart.resize());

    watcher.observe(node);

    return () => {
      watcher.disconnect();
      chart.dispose();
      drawn.current = null;
    };
  }, [resolvedTheme]);

  useEffect(() => {
    const chart = drawn.current;

    if (!chart) {
      return;
    }

    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    chart.setOption(
      {
        animation: !still,
        // The engine can generate its own spoken description. Ours is written for this chart and
        // the figures are published as a table besides, so its version would only be noise.
        aria: { enabled: false },
        ...option(readChartPalette()),
      },
      // What is drawn is replaced rather than matched up by position, so a chart drawn a second
      // time with fewer parts than before does not keep the ones it has dropped. A part that names
      // itself is recognised across the redraw and moved to its new figures instead of being
      // started again, which is what lets a column that has grown by one simply grow by one.
      { replaceMerge: ['series'] },
    );
  }, [option, resolvedTheme]);

  // Listened for on the surface's own element rather than on the chart, so that a surface rebuilt
  // for the other theme is still listened to, and a press that lands between the two reads no
  // chart and does nothing. Attached here rather than written on the element: a picture is not a
  // control, and the way in a keyboard has is whatever its caller offers beside it.
  useEffect(() => {
    const node = holder.current;

    if (!node || !onPick) {
      return;
    }

    const pick = (event: MouseEvent) => {
      const index = pickedIndex(drawn.current, node, event);

      if (index !== null) {
        onPick(index);
      }
    };

    node.addEventListener('click', pick);

    return () => node.removeEventListener('click', pick);
  }, [onPick]);

  // The cursor is the surface's to decide, and it means one thing: a hand says a press does
  // something. The charting engine offers a hand of its own over every column, dot and slice,
  // whether or not anything happens on pressing one, and writes it onto the element it draws in
  // on every move of the mouse — so the surface's own cursor is set on the canvas itself, where
  // the engine's cannot overrule it.
  return (
    <div
      ref={holder}
      role="img"
      aria-label={label}
      className={cn(
        'h-full w-full',
        onPick ? '[&_canvas]:cursor-pointer' : '[&_canvas]:cursor-default',
        className,
      )}
    />
  );
}

/**
 * The category a press landed in, or nothing where it landed outside the plot.
 *
 * Measured from the surface's own corner, which is where the charting engine counts pixels from,
 * and only inside the grid: the axis labels and the margins belong to no category. The engine
 * answers with the nearest category to the point, rounded, so the whole width of a column is that
 * column's whether the buckets sit in bands or at points. A surface with nothing drawn on it
 * places a press on no category at all.
 */
function pickedIndex(
  chart: EChartsType | null,
  node: HTMLElement,
  event: MouseEvent,
): number | null {
  if (!chart) {
    return null;
  }

  const box = node.getBoundingClientRect();
  const x = event.clientX - box.left;
  const y = event.clientY - box.top;

  if (!chart.containPixel('grid', [x, y])) {
    return null;
  }

  const index = chart.convertFromPixel({ xAxisIndex: 0 }, x);

  return Number.isInteger(index) && index >= 0 ? index : null;
}

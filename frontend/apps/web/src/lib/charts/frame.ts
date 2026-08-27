import type { ChartPalette } from '@/lib/charts/palette';

/**
 * What is drawn over what, where the order they are declared in is not enough.
 *
 * A chart draws its series in the order it is handed them, but only among those left at the same
 * depth. Anything given a depth of its own is therefore placed against the rest here rather than
 * by where it happens to sit in a list.
 */
export const LAYERS = {
  /** The wash over the buckets that have not finished being judged, which covers the bands. */
  unsettled: 5,
  /** The period before, which has been judged in full and is not what the wash is quietening. */
  earlier: 6,
} as const;

/**
 * The frame both pictures on the overview are drawn inside.
 *
 * The grid, the two axes and the tooltip are the same whichever question is being answered, and
 * written once so that switching between them changes what is drawn and nothing around it. A
 * chart whose axes shifted as the view changed would read as the period having changed too.
 *
 * @param labels What each bucket is called, already written in the website's own zone.
 * @param columns Whether the buckets are drawn as columns, which sit in a band of the axis rather
 * than at a point on it.
 */
export function chartFrame(palette: ChartPalette, labels: readonly string[], columns: boolean) {
  return {
    grid: { top: 16, right: 4, bottom: 4, left: 4, containLabel: true },
    tooltip: {
      trigger: 'axis' as const,
      backgroundColor: palette.surface,
      borderColor: palette.border,
      textStyle: { color: palette.text, fontSize: 12 },
      axisPointer: columns
        ? { type: 'shadow' as const, shadowStyle: { color: palette.line, opacity: 0.5 } }
        : { type: 'line' as const, lineStyle: { color: palette.border } },
    },
    xAxis: {
      type: 'category' as const,
      data: [...labels],
      boundaryGap: columns,
      axisTick: { show: false },
      axisLine: { lineStyle: { color: palette.line } },
      axisLabel: { color: palette.label, fontSize: 11, hideOverlap: true },
    },
    yAxis: {
      type: 'value' as const,
      minInterval: 1,
      axisLabel: { color: palette.label, fontSize: 11 },
      splitLine: { lineStyle: { color: palette.line } },
    },
  };
}

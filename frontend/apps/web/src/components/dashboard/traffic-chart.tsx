'use client';

import { color } from 'echarts/core';
import { type DateTimeFormatOptions, useFormatter, useTranslations } from 'next-intl';
import { useCallback, useMemo } from 'react';
import { Chart } from '@/components/charts/chart';
import { Card } from '@/components/ui/card';
import type { Granularity } from '@/lib/analytics/period';
import type { ChartPalette } from '@/lib/charts/palette';

/** One bucket of traffic, already lined up across both measures. */
export interface TrafficPoint {
  readonly start: string;
  readonly pageViews: number;
  readonly visitors: number;
}

interface TrafficChartProps {
  readonly points: readonly TrafficPoint[];
  readonly siteName: string;
  /** The site's own zone, so a bucket is read back in the day it was counted in. */
  readonly timeZoneId: string;
  /** The place whose clock those buckets follow, such as `Kolkata`. */
  readonly zone: string;
  readonly granularity: Granularity;
  /** Whether the period covers more than one day, which decides how a time is written. */
  readonly manyDays: boolean;
}

/**
 * Page views and visitors over the chosen period.
 *
 * The same figures are published twice: once as a drawing, and once as a table anybody can open.
 * A canvas tells a screen reader nothing at all, and a chart whose numbers exist only as pixels
 * is a chart some of this product's readers simply do not have.
 */
export function TrafficChart({
  points,
  siteName,
  timeZoneId,
  zone,
  granularity,
  manyDays,
}: TrafficChartProps) {
  const t = useTranslations('dashboard.chart');
  const format = useFormatter();

  // A bucket is cut where the site is, so it has to be read back there too. Written without a zone
  // it is read in whichever one the person looking happens to be in, and a day counted in Kolkata
  // is labelled as the day before for anybody reading in London.
  const labels = useMemo(
    () =>
      points.map((point) =>
        format.dateTime(new Date(point.start), labelling(granularity, manyDays, timeZoneId)),
      ),
    [points, format, granularity, manyDays, timeZoneId],
  );

  // Counted in an hour, distinct visitors are the people who were there in that hour, which is a
  // different figure from the day's. Naming it the day's would be a claim the numbers do not make.
  const names = useMemo(
    () => [t('pageViews'), granularity === 'day' ? t('visitors') : t('visitorsByHour')] as const,
    [t, granularity],
  );

  const pageViews = useMemo(() => points.map((point) => point.pageViews), [points]);
  const visitors = useMemo(() => points.map((point) => point.visitors), [points]);

  const option = useCallback(
    (palette: ChartPalette) => ({
      grid: { top: 16, right: 4, bottom: 4, left: 4, containLabel: true },
      tooltip: {
        trigger: 'axis',
        backgroundColor: palette.surface,
        borderColor: palette.border,
        textStyle: { color: palette.text, fontSize: 12 },
        axisPointer: { type: 'line', lineStyle: { color: palette.border } },
      },
      xAxis: {
        type: 'category',
        data: labels,
        boundaryGap: false,
        axisTick: { show: false },
        axisLine: { lineStyle: { color: palette.line } },
        axisLabel: { color: palette.label, fontSize: 11, hideOverlap: true },
      },
      yAxis: {
        type: 'value',
        minInterval: 1,
        axisLabel: { color: palette.label, fontSize: 11 },
        splitLine: { lineStyle: { color: palette.line } },
      },
      series: [
        line(names[0], pageViews, palette.series[0], true),
        line(names[1], visitors, palette.series[1], false),
      ],
    }),
    [labels, names, pageViews, visitors],
  );

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        <ul className="flex items-center gap-4 text-xs text-foreground-muted">
          <Key tone="bg-chart-1">{names[0]}</Key>
          <Key tone="bg-chart-2">{names[1]}</Key>
        </ul>
      </header>

      <div className="h-56 w-full sm:h-72">
        <Chart option={option} label={t('summary', { site: siteName })} />
      </div>

      <p className="text-xs text-foreground-subtle">
        {granularity === 'day' ? t('days', { zone }) : t('hours', { zone })}
      </p>

      <details className="group border-t border-border pt-3">
        <summary className="cursor-pointer text-sm font-medium text-foreground-muted marker:text-foreground-subtle hover:text-foreground">
          {t('table')}
        </summary>
        <div className="mt-3 max-h-72 overflow-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-foreground-subtle">
              <tr>
                <th scope="col" className="py-1.5 pr-4 font-medium">
                  {granularity === 'day' ? t('columnDay') : t('columnHour')}
                </th>
                <th scope="col" className="py-1.5 pr-4 text-right font-medium">
                  {names[0]}
                </th>
                <th scope="col" className="py-1.5 text-right font-medium">
                  {names[1]}
                </th>
              </tr>
            </thead>
            <tbody className="text-foreground-muted">
              {points.map((point, index) => (
                <tr key={point.start} className="border-t border-border">
                  <th scope="row" className="py-1.5 pr-4 font-normal">
                    {labels[index]}
                  </th>
                  <td className="py-1.5 pr-4 text-right tabular-nums">
                    {format.number(point.pageViews)}
                  </td>
                  <td className="py-1.5 text-right tabular-nums">
                    {format.number(point.visitors)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </Card>
  );
}

/**
 * How a bucket is written, which depends on how wide it is and how many days it sits among.
 *
 * An hour on its own needs no date beside it when every other bucket is from the same day, and
 * needs one the moment they are not.
 */
function labelling(
  granularity: Granularity,
  manyDays: boolean,
  timeZone: string,
): DateTimeFormatOptions {
  if (granularity === 'day') {
    return { timeZone, day: 'numeric', month: 'short' };
  }

  // No minutes on either. A bucket an hour wide always begins on the hour, so the two zeroes say
  // nothing and cost the width that lets a day's worth of labels sit side by side on a phone.
  return manyDays
    ? { timeZone, day: 'numeric', month: 'short', hour: 'numeric' }
    : { timeZone, hour: 'numeric' };
}

/** One measure drawn across the period. */
function line(name: string, values: readonly number[], colour: string, filled: boolean) {
  return {
    name,
    type: 'line' as const,
    data: [...values],
    smooth: 0.3,
    symbol: 'circle',
    symbolSize: 6,
    showSymbol: values.length <= 14,
    lineStyle: { width: 2, color: colour },
    itemStyle: { color: colour },
    areaStyle: filled ? { color: fade(colour) } : undefined,
  };
}

/**
 * The wash beneath the headline measure, fading out towards the axis.
 *
 * A flat fill at one opacity has to be light enough not to muddy the dark theme, which leaves it
 * invisible in the light one. A gradient is legible in both because it is strongest where the
 * line is and gone by the time it reaches the bottom of the chart.
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

function Key({ tone, children }: { readonly tone: string; readonly children: string }) {
  return (
    <li className="flex items-center gap-1.5">
      <span aria-hidden className={`size-2 rounded-full ${tone}`} />
      {children}
    </li>
  );
}

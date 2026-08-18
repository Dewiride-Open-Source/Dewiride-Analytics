'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { PeriodSwitch } from '@/components/dashboard/period-switch';
import { type TrafficDay, TrafficChart } from '@/components/dashboard/traffic-chart';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { DEFAULT_PERIOD, type PeriodDays, windowFor } from '@/lib/analytics/period';
import type { Site } from '@/lib/api/schemas';
import { useDailySeries, useOverview } from '@/lib/queries/sites';
import { readableZone } from '@/lib/time-zones';

/** One website, over one period. */
export function SiteOverview({ site }: { readonly site: Site }) {
  const t = useTranslations('dashboard');
  const metrics = useTranslations('dashboard.metrics');
  const format = useFormatter();
  const [period, setPeriod] = useState<PeriodDays>(DEFAULT_PERIOD);

  // Resolved once per period rather than on every render: the window is part of the name each
  // answer is cached under, and one that moved with the clock would never find a cached answer.
  const window = useMemo(
    () => windowFor(period, site.timeZoneId, new Date()),
    [period, site.timeZoneId],
  );

  const overview = useOverview(site.id, window);
  const views = useDailySeries(site.id, 'pageviews', window);
  const visitors = useDailySeries(site.id, 'visitors', window);

  const days = useMemo(
    () => align(views.data?.points, visitors.data?.points),
    [views.data, visitors.data],
  );
  const totals = overview.data;
  const silent = totals !== undefined && totals.pageViews === 0 && totals.visitors === 0;

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {site.displayName}
          </h1>
          <p className="text-sm text-foreground-muted">{site.domain}</p>
        </div>
        <PeriodSwitch value={period} onChange={setPeriod} />
      </header>

      {overview.isError ? <FailureNotice error={overview.error} /> : null}

      <div className="grid gap-4 sm:grid-cols-3">
        {totals === undefined ? (
          <>
            <MetricCardSkeleton />
            <MetricCardSkeleton />
            <MetricCardSkeleton />
          </>
        ) : (
          <>
            <MetricCard
              label={metrics('pageViews.label')}
              value={format.number(totals.pageViews)}
            />
            <MetricCard
              label={metrics('visitors.label')}
              value={format.number(totals.visitors)}
              note={metrics('visitors.note')}
            />
            <MetricCard
              label={metrics('pagesPerVisitor.label')}
              value={perVisitor(totals.pageViews, totals.visitors, format)}
            />
          </>
        )}
      </div>

      {silent ? (
        <FirstVisit title={t('empty.title')} body={t('empty.body', { site: site.domain })} />
      ) : days.length > 0 ? (
        <TrafficChart
          days={days}
          siteName={site.displayName}
          zone={readableZone(site.timeZoneId)}
        />
      ) : (
        <div className="h-72 animate-pulse rounded-lg border border-border bg-surface-muted" />
      )}
    </div>
  );
}

/** The screen a website shows before anybody has been to it. */
function FirstVisit({ title, body }: { readonly title: string; readonly body: string }) {
  return (
    <Card className="flex flex-col items-center gap-2 px-6 py-16 text-center">
      <span
        aria-hidden
        className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
      >
        <span className="size-2.5 animate-pulse rounded-full bg-accent" />
      </span>
      <h2 className="text-lg font-semibold text-foreground">{title}</h2>
      <p className="max-w-sm text-sm text-foreground-muted">{body}</p>
    </Card>
  );
}

/** Pages read per visitor, on an average day, or nothing when nobody came. */
function perVisitor(
  pageViews: number,
  visitors: number,
  format: ReturnType<typeof useFormatter>,
): string | null {
  return visitors > 0 ? format.number(pageViews / visitors, { maximumFractionDigits: 1 }) : null;
}

/** Two answers about the same days, joined into the rows a chart and a table both read. */
function align(
  views: readonly { readonly bucketStart: string; readonly value: number }[] | undefined,
  visitors: readonly { readonly bucketStart: string; readonly value: number }[] | undefined,
): readonly TrafficDay[] {
  if (!views || !visitors) {
    return [];
  }

  const byDay = new Map(visitors.map((point) => [point.bucketStart, point.value]));

  return views.map((point) => ({
    start: point.bucketStart,
    pageViews: point.value,
    visitors: byDay.get(point.bucketStart) ?? 0,
  }));
}

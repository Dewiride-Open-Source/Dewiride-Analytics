'use client';

import { Code2, KeyRound, SlidersHorizontal } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';
import { JudgedTraffic } from '@/components/dashboard/judged-traffic';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { PeriodSwitch } from '@/components/dashboard/period-switch';
import { ServerKeys } from '@/components/dashboard/server-keys';
import { SiteActions } from '@/components/dashboard/site-actions';
import { SiteDevices } from '@/components/dashboard/site-devices';
import { SiteFlow } from '@/components/dashboard/site-flow';
import { SiteLocations } from '@/components/dashboard/site-locations';
import { SitePages } from '@/components/dashboard/site-pages';
import { SiteReading } from '@/components/dashboard/site-reading';
import { SiteSettings } from '@/components/dashboard/site-settings';
import { SiteSources } from '@/components/dashboard/site-sources';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import { TrafficChart, type TrafficDay } from '@/components/dashboard/traffic-chart';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { DEFAULT_PERIOD, type PeriodDays, windowFor } from '@/lib/analytics/period';
import type { Site } from '@/lib/api/schemas';
import { useDailySeries, useOverview } from '@/lib/queries/sites';
import { readableZone } from '@/lib/time-zones';

interface SiteOverviewProps {
  readonly site: Site;
}

/** One website, over one period. */
export function SiteOverview({ site }: SiteOverviewProps) {
  const t = useTranslations('dashboard');
  const metrics = useTranslations('dashboard.metrics');
  const install = useTranslations('install');
  const serverKeys = useTranslations('serverKeys');
  const settings = useTranslations('siteSettings');
  const format = useFormatter();
  const [period, setPeriod] = useState<PeriodDays>(DEFAULT_PERIOD);
  const [showingCode, setShowingCode] = useState(false);
  const [showingKeys, setShowingKeys] = useState(false);
  const [showingSettings, setShowingSettings] = useState(false);

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
  const listing = `${site.id}:${window.from}:${window.to}`;

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        {/*
          A heading and nothing more. Swapping between websites is a property of the session rather
          than of this screen, and lives in the bar across the top; a name that was also the control
          for changing it had to be the size of a heading to read as one, which made a picker the
          size of a heading.

          The address sits beneath the name only where it says something the name does not. A
          website keeps its address as its name until somebody renames it, and printing the same
          words twice reads as a mistake.
        */}
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {site.displayName}
          </h1>
          {site.displayName === site.domain ? null : (
            <p className="truncate text-sm text-foreground-muted">{site.domain}</p>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <Button tone="secondary" size="sm" onClick={() => setShowingCode(true)}>
            <Code2 aria-hidden className="size-4" />
            {install('action')}
          </Button>
          <Button tone="secondary" size="sm" onClick={() => setShowingKeys(true)}>
            <KeyRound aria-hidden className="size-4" />
            {serverKeys('action')}
          </Button>
          <Button tone="secondary" size="sm" onClick={() => setShowingSettings(true)}>
            <SlidersHorizontal aria-hidden className="size-4" />
            {settings('action')}
          </Button>
          <PeriodSwitch value={period} onChange={setPeriod} />
        </div>
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
        <FirstVisit
          title={t('empty.title')}
          body={t('empty.body', { site: site.domain })}
          action={t('empty.action')}
          onAction={() => setShowingCode(true)}
        />
      ) : (
        <>
          {days.length > 0 ? (
            <TrafficChart
              days={days}
              siteName={site.displayName}
              zone={readableZone(site.timeZoneId)}
            />
          ) : (
            <div className="h-72 animate-pulse rounded-lg border border-border bg-surface-muted" />
          )}

          {/*
            Both lists are given a key that changes with the website and the period, so choosing
            either starts each list at its own beginning instead of leaving somebody on a
            screenful that no longer exists.
          */}
          {/*
            Where the visitors came from sits directly under the chart, above everything about what
            they then did. It is the first thing somebody looks for after seeing the shape of a
            week — a rise is a question, and this is where its answer usually is.
          */}
          <SiteSources key={`sources:${listing}`} siteId={site.id} window={window} />

          {/*
            Where the readers were and what they read on are two halves of the same question and
            sit side by side from a wide screen down, one above the other on anything narrower.
          */}
          <div className="grid gap-6 xl:grid-cols-2 xl:items-start">
            <SiteLocations key={`places:${listing}`} siteId={site.id} window={window} />

            <SiteDevices key={`devices:${listing}`} siteId={site.id} window={window} />
          </div>

          <SitePages key={`pages:${listing}`} siteId={site.id} window={window} />

          <SiteReading
            key={`reading:${listing}`}
            siteId={site.id}
            window={window}
            onShowCode={() => setShowingCode(true)}
          />

          <SiteFlow key={`flow:${listing}`} siteId={site.id} window={window} />

          <SiteActions key={`presses:${listing}`} siteId={site.id} window={window} />

          <JudgedTraffic key={`visits:${listing}`} site={site} window={window} />
        </>
      )}

      <TrackingCode
        open={showingCode}
        onClose={() => setShowingCode(false)}
        siteId={site.id}
        siteDomain={site.domain}
      />

      <ServerKeys
        open={showingKeys}
        onClose={() => setShowingKeys(false)}
        siteId={site.id}
        siteDomain={site.domain}
        timeZoneId={site.timeZoneId}
      />

      {/*
        Removing the website puts the panel away and nothing else. The list of websites is asked
        for again as part of the removal, and the screen settles on whichever one is left.
      */}
      <SiteSettings
        open={showingSettings}
        onClose={() => setShowingSettings(false)}
        site={site}
        onRemoved={() => setShowingSettings(false)}
      />
    </div>
  );
}

interface FirstVisitProps {
  readonly title: string;
  readonly body: string;
  readonly action: string;
  readonly onAction: () => void;
}

/**
 * The screen a website shows before anybody has been to it.
 *
 * It carries the one thing that would change it. A website with nothing on it yet is almost always
 * a website whose owner has not put the code on their pages, and sending them looking for it
 * elsewhere is how a first evening with a new product ends.
 */
function FirstVisit({ title, body, action, onAction }: FirstVisitProps) {
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
      <Button className="mt-4" onClick={onAction}>
        {action}
      </Button>
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

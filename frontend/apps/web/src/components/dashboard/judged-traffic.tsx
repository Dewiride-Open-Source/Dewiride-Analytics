'use client';

import { ArrowRight, ScanSearch } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { TrafficBreakdown } from '@/components/dashboard/traffic-breakdown';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Link } from '@/i18n/navigation';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import type { Site } from '@/lib/api/schemas';
import { useTraffic } from '@/lib/queries/sites';
import { JOURNEYS } from '@/lib/routes';

interface JudgedTrafficProps {
  readonly site: Site;
  readonly window: AnalyticsWindow;
}

/**
 * How a period's traffic divides between the people a website is for and everything else.
 *
 * The summary lives here, on the screen somebody opens first. Every individual visit behind it has
 * a screen of its own, because reading them one at a time is a different activity from looking at
 * the totals — and this is the way through to it.
 */
export function JudgedTraffic({ site, window }: JudgedTrafficProps) {
  const t = useTranslations('dashboard.traffic');
  const traffic = useTraffic(site.id, window);

  if (traffic.isError) {
    return <FailureNotice error={traffic.error} />;
  }

  if (traffic.isPending) {
    return <div className="h-52 animate-pulse rounded-lg border border-border bg-surface-muted" />;
  }

  if (traffic.data.groups.length === 0) {
    return (
      <Card className="flex flex-col items-center gap-2 px-6 py-14 text-center">
        <span
          aria-hidden
          className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
        >
          <ScanSearch className="size-5 text-accent-strong" />
        </span>
        <h2 className="text-base font-semibold text-foreground">{t('empty.title')}</h2>
        <p className="max-w-sm text-sm text-foreground-muted">{t('empty.body')}</p>
      </Card>
    );
  }

  return (
    <>
      <TrafficBreakdown groups={traffic.data.groups} sessions={traffic.data.sessions} />

      <Link
        href={JOURNEYS}
        className="inline-flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-accent-strong hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
      >
        {t('everyVisit')}
        <ArrowRight aria-hidden className="size-4" />
      </Link>
    </>
  );
}

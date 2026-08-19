'use client';

import { ScanSearch } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { TrafficBreakdown } from '@/components/dashboard/traffic-breakdown';
import { VisitList } from '@/components/dashboard/visit-list';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import type { Site } from '@/lib/api/schemas';
import { useTraffic, useVisits } from '@/lib/queries/sites';

interface JudgedTrafficProps {
  readonly site: Site;
  readonly window: AnalyticsWindow;
}

/**
 * How many visits to put on screen at once.
 *
 * Every one of them carries its whole case, so a screenful is a slice rather than the whole
 * period: a hundred of them at once is a slower screen nobody scrolls to the end of. The rest are
 * a press away rather than out of reach.
 */
const PER_PAGE = 25;

/**
 * Everything the engine has decided about a website's traffic over a period.
 *
 * The breakdown and the list are asked for together rather than one after the other, so the
 * slower of the two decides how long the screen takes rather than the two of them added up.
 *
 * How far down the list somebody has read is kept here, and the screen above gives this a key that
 * changes with the website and the period — so choosing either starts the new list at its most
 * recent visit rather than at whichever screenful was open of the old one.
 */
export function JudgedTraffic({ site, window }: JudgedTrafficProps) {
  const t = useTranslations('dashboard.traffic');
  const [offset, setOffset] = useState(0);
  const traffic = useTraffic(site.id, window);
  const visits = useVisits(site.id, window, PER_PAGE, offset);

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

      {visits.isError ? <FailureNotice error={visits.error} /> : null}

      {visits.data && visits.data.visits.length > 0 ? (
        <VisitList
          siteId={site.id}
          visits={visits.data.visits}
          totalVisits={visits.data.totalVisits}
          timeZoneId={site.timeZoneId}
          offset={offset}
          step={PER_PAGE}
          busy={visits.isFetching}
          onMove={setOffset}
        />
      ) : null}
    </>
  );
}

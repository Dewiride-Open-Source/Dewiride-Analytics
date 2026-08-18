'use client';

import { useTranslations } from 'next-intl';
import { MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { SiteOverview } from '@/components/dashboard/site-overview';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { useChosenSite } from '@/lib/analytics/chosen-site';
import { useSites } from '@/lib/queries/sites';

/**
 * The screen somebody lands on once they are signed in.
 *
 * It shows one website at a time, and remembers which one. Where that choice is kept, and what
 * happens when it names a website the account can no longer see, is decided in one place rather
 * than here.
 */
export function Dashboard() {
  const t = useTranslations('dashboard');
  const sites = useSites();
  const { site, choose } = useChosenSite(sites.data);

  if (sites.isPending) {
    return (
      <Shell>
        <div className="flex flex-col gap-6">
          <div className="h-9 w-56 animate-pulse rounded-sm bg-surface-muted" />
          <div className="grid gap-4 sm:grid-cols-3">
            <MetricCardSkeleton />
            <MetricCardSkeleton />
            <MetricCardSkeleton />
          </div>
          <div className="h-72 animate-pulse rounded-lg border border-border bg-surface-muted" />
        </div>
      </Shell>
    );
  }

  if (sites.isError) {
    return (
      <Shell>
        <FailureNotice error={sites.error} />
      </Shell>
    );
  }

  if (!site) {
    return (
      <Shell>
        <Card className="flex flex-col items-center gap-2 px-6 py-16 text-center">
          <h1 className="text-lg font-semibold text-foreground">{t('noSites.title')}</h1>
          <p className="max-w-sm text-sm text-foreground-muted">{t('noSites.body')}</p>
        </Card>
      </Shell>
    );
  }

  return (
    <Shell>
      <SiteOverview site={site} sites={sites.data} onChoose={choose} />
    </Shell>
  );
}

function Shell({ children }: { readonly children: React.ReactNode }) {
  return <div className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6 sm:py-10">{children}</div>;
}

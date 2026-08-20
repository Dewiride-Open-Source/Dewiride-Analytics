'use client';

import { SiteScreen } from '@/components/chrome/site-screen';
import { MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { SiteOverview } from '@/components/dashboard/site-overview';

/**
 * The screen somebody lands on once they are signed in.
 *
 * How much traffic a website had, where it came from, and what was done with it. Who each visitor
 * was is a screen of its own, because it is worked through rather than glanced at.
 */
export function Dashboard() {
  return <SiteScreen waiting={<Waiting />}>{(site) => <SiteOverview site={site} />}</SiteScreen>;
}

/** The shape of this screen, drawn before its figures arrive, so nothing jumps when they do. */
function Waiting() {
  return (
    <div className="flex flex-col gap-6">
      <div className="h-9 w-56 animate-pulse rounded-sm bg-surface-muted" />
      <div className="grid gap-4 sm:grid-cols-3">
        <MetricCardSkeleton />
        <MetricCardSkeleton />
        <MetricCardSkeleton />
      </div>
      <div className="h-72 animate-pulse rounded-lg border border-border bg-surface-muted" />
    </div>
  );
}

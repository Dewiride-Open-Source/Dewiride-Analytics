'use client';

import { SiteScreen } from '@/components/chrome/site-screen';
import { LiveShapes, SiteLive } from '@/components/live/site-live';

/**
 * Who is on a website at this moment.
 *
 * A screen of its own because it is watched rather than read. The overview answers how a week went
 * and the journeys answer who each visitor turned out to be; this one answers the question somebody
 * opens the product in a hurry to ask — is something sweeping my site right now — and it has to
 * keep answering it while they sit there.
 */
export function Live() {
  return <SiteScreen waiting={<Waiting />}>{(site) => <SiteLive site={site} />}</SiteScreen>;
}

/** The shape of this screen, drawn before its first reading arrives, so nothing jumps when it does. */
function Waiting() {
  return (
    <div className="flex flex-col gap-6">
      <div className="h-9 w-56 animate-pulse rounded-sm bg-surface-muted" />

      <LiveShapes />
    </div>
  );
}

'use client';

import { VisitTrail } from '@/components/dashboard/visit-trail';
import { useVisitJourney } from '@/lib/queries/sites';

interface VisitJourneyProps {
  readonly siteId: string;
  /** Which visit, as the list names it. */
  readonly visit: string;
  /** How many pages the visit asked for, so a journey cut short can say so. */
  readonly pageCount: number;
  /** The site's own zone, so a step is stamped with the time it happened where the site is. */
  readonly timeZoneId: string;
  /** Whether the visit has been opened. Nothing is asked for until it has. */
  readonly open: boolean;
}

/**
 * What one judged visit did, from the moment somebody opens it.
 *
 * Asked for only once the visit is opened. A screenful is twenty-five visits, and reading every
 * journey nobody has asked to see would be twenty-five questions of the store for one screen.
 *
 * A visit read once is read for good: it is over, nothing further will be reported about it, and
 * the verdict on it has been reached. So this asks once and keeps the answer for as long as the
 * screen is open, and reopening the same visit costs the store nothing.
 */
export function VisitJourney({ siteId, visit, pageCount, timeZoneId, open }: VisitJourneyProps) {
  const journey = useVisitJourney(siteId, visit, open);

  return (
    <VisitTrail
      context={journey.data?.context}
      steps={journey.data?.steps}
      failed={journey.isError}
      pageCount={pageCount}
      timeZoneId={timeZoneId}
    />
  );
}

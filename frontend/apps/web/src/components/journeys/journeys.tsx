'use client';

import { Filter, ScanSearch } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { PeriodPicker } from '@/components/dashboard/period-picker';
import { ListEmpty, ListWaiting } from '@/components/dashboard/ranked-list';
import { JourneyFilterPanel } from '@/components/journeys/journey-filters';
import { VisitList } from '@/components/journeys/visit-list';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import {
  DEFAULT_PAGE_SIZE,
  EVERY_JOURNEY,
  isNarrowed,
  type JourneyFilters,
  narrowingParams,
  tallyCategories,
} from '@/lib/analytics/journeys';
import { type Period, windowFor, writePeriod } from '@/lib/analytics/period';
import { useJourneyFilters } from '@/lib/analytics/use-journey-filters';
import { usePeriod } from '@/lib/analytics/use-period';
import type { Site, Visits } from '@/lib/api/schemas';
import { useFacets, useTraffic, useVisits } from '@/lib/queries/sites';

/**
 * Everyone and everything that visited a website, one at a time.
 *
 * A screen of its own rather than a card at the foot of the overview, because it is the one people
 * work through rather than glance at: the overview answers how much traffic there was, and this
 * answers who each visitor was and what they did. Those are different questions asked at different
 * moments, and a list somebody scrolls past on the way to their totals is a list nobody reads.
 */
export function Journeys() {
  return <SiteScreen waiting={<Waiting />}>{(site) => <SiteJourneys site={site} />}</SiteScreen>;
}

/** The shape of this screen, drawn before its rows arrive, so nothing jumps when they do. */
function Waiting() {
  return (
    <div className="flex flex-col gap-6">
      <div className="h-9 w-56 animate-pulse rounded-sm bg-surface-muted" />
      <div className="h-40 animate-pulse rounded-lg border border-border bg-surface-muted" />
      <ListWaiting />
    </div>
  );
}

/**
 * What the list on screen is a list of.
 *
 * Everything that decides which journeys are on it and how many of them fit, written down so a
 * change to any of it can be noticed while the screen is being drawn.
 */
function listName(period: Period, filters: JourneyFilters, perPage: number): string {
  return `${writePeriod(period)}/${perPage}?${narrowingParams(filters).toString()}`;
}

/** One website's journeys, over one period, narrowed to whatever was asked for. */
function SiteJourneys({ site }: { readonly site: Site }) {
  const t = useTranslations('journeys');
  const { period, choose } = usePeriod({ seeding: true });
  const { filters, narrow } = useJourneyFilters();
  const [perPage, setPerPage] = useState<number>(DEFAULT_PAGE_SIZE);
  const [offset, setOffset] = useState(0);

  // Asked for the first time somebody reaches for one of the controls that needs it, and not
  // before. Working out what a period held means reading its whole activity a second time — the
  // same work a narrowed list pays for — and a screen most people open to read the list would
  // otherwise pay it every time. Once asked, it stays asked, so closing a menu and opening
  // another does not start again.
  const [wanted, setWanted] = useState(false);

  // Where somebody is in the list only means anything in the list they were reading, so anything
  // that makes it a different one puts them back at the start of it. Reconciled here rather than
  // done as each control is pressed, because the period and the narrowing both live in the
  // address and the browser's own way back changes them without anything on this screen being
  // touched. Page nine of a list that is now four pages long is a screen with nothing on it.
  const asked = listName(period, filters, perPage);
  const [listed, setListed] = useState(asked);

  if (listed !== asked) {
    setListed(asked);
    setOffset(0);
  }

  // Resolved once per period rather than on every render: the window is part of the name each
  // answer is cached under, and one that moved with the clock would never find a cached answer.
  const window = useMemo(
    () => windowFor(period, site.timeZoneId, new Date()),
    [period, site.timeZoneId],
  );

  const traffic = useTraffic(site.id, window);
  const visits = useVisits(site.id, window, perPage, offset, filters);
  const held = useFacets(site.id, window, wanted);
  const available = useMemo(() => tallyCategories(traffic.data?.groups ?? []), [traffic.data]);

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {t('title')}
          </h1>
          <p className="max-w-2xl text-sm text-foreground-muted">
            {t('caption', { site: site.displayName })}
          </p>
        </div>

        <PeriodPicker value={period} onChange={choose} timeZoneId={site.timeZoneId} />
      </header>

      {/*
        Absent rather than empty on a website with nothing judged yet. Controls that could only
        narrow nothing down are chrome in front of the one message that screen has to give.
      */}
      {traffic.isPending || available.length > 0 ? (
        <JourneyFilterPanel
          available={available}
          pending={traffic.isPending}
          held={held.data}
          heldPending={held.isPending}
          onWantOptions={() => setWanted(true)}
          value={filters}
          onChange={narrow}
        />
      ) : null}

      {traffic.isError ? <FailureNotice error={traffic.error} /> : null}
      {visits.isError ? <FailureNotice error={visits.error} /> : null}

      {visits.data === undefined ? (
        <ListWaiting />
      ) : (
        <Listing
          site={site}
          answer={visits.data}
          narrowed={isNarrowed(filters)}
          onClear={() => narrow(EVERY_JOURNEY)}
          busy={visits.isFetching}
          perPage={perPage}
          offset={offset}
          onMove={setOffset}
          onResize={setPerPage}
        />
      )}
    </div>
  );
}

interface ListingProps {
  readonly site: Site;
  readonly answer: Visits;
  readonly narrowed: boolean;
  readonly onClear: () => void;
  readonly busy: boolean;
  readonly perPage: number;
  readonly offset: number;
  readonly onMove: (offset: number) => void;
  readonly onResize: (perPage: number) => void;
}

/**
 * The list itself, or the reason there is nothing on it.
 *
 * Two different nothings, and they are not the same message. A period with no judged traffic at
 * all is a website waiting for its first verdicts; a period whose journeys have all been narrowed
 * away is somebody one press from seeing them again, and that press is on the screen.
 */
function Listing({
  site,
  answer,
  narrowed,
  onClear,
  busy,
  perPage,
  offset,
  onMove,
  onResize,
}: ListingProps) {
  const t = useTranslations('journeys.empty');

  if (answer.visits.length === 0) {
    return narrowed ? (
      <ListEmpty
        icon={Filter}
        title={t('narrowed.title')}
        body={t('narrowed.body')}
        action={
          <Button tone="secondary" size="sm" onClick={onClear}>
            {t('narrowed.action')}
          </Button>
        }
      />
    ) : (
      <ListEmpty icon={ScanSearch} title={t('none.title')} body={t('none.body')} />
    );
  }

  return (
    <VisitList
      siteId={site.id}
      visits={answer.visits}
      totalVisits={answer.totalVisits}
      timeZoneId={site.timeZoneId}
      offset={offset}
      perPage={perPage}
      busy={busy}
      onMove={onMove}
      onResize={onResize}
    />
  );
}

'use client';

import { Funnel, ScanSearch } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useCallback, useMemo, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { PeriodPicker } from '@/components/dashboard/period-picker';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
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
  keptToPeople,
  narrowingParams,
  tallyCategories,
  withoutConclusions,
} from '@/lib/analytics/journeys';
import type { Population } from '@/lib/analytics/people-only';
import { isAllTime, type Period, spanFor, windowFor, writePeriod } from '@/lib/analytics/period';
import { useJourneyFilters } from '@/lib/analytics/use-journey-filters';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import { usePeriod } from '@/lib/analytics/use-period';
import type { Site, Visits } from '@/lib/api/schemas';
import { useAllTime, useFacets, useTraffic, useVisits } from '@/lib/queries/sites';

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
function listName(
  period: Period,
  population: Population,
  filters: JourneyFilters,
  perPage: number,
): string {
  return `${writePeriod(period)}/${population}/${perPage}?${narrowingParams(filters).toString()}`;
}

/** One website's journeys, over one period, narrowed to whatever was asked for. */
function SiteJourneys({ site }: { readonly site: Site }) {
  const t = useTranslations('journeys');
  const { period, choose } = usePeriod({ seeding: true });
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding: true });
  const { filters, narrow } = useJourneyFilters();
  const [perPage, setPerPage] = useState<number>(DEFAULT_PAGE_SIZE);
  const [offset, setOffset] = useState(0);

  // Asked for the first time somebody reaches for one of the controls that needs it, and not
  // before. Working out what a period held means reading its whole activity a second time — the
  // same work a narrowed list pays for — and a screen most people open to read the list would
  // otherwise pay it every time. Once asked, it stays asked, so closing a menu and opening
  // another does not start again.
  const [wanted, setWanted] = useState(false);

  // Kept to people, the screen has already answered what generated the visits, so a conclusion
  // the address carried in is set aside — neither shown nor asked — until everyone is back. What
  // is shown is what the controls hold; what is asked is that, kept to the people by their verdict.
  const shown = useMemo(
    () => (peopleOnly ? withoutConclusions(filters) : filters),
    [peopleOnly, filters],
  );
  const narrowing = useMemo(() => (peopleOnly ? keptToPeople(shown) : shown), [peopleOnly, shown]);

  // Where somebody is in the list only means anything in the list they were reading, so anything
  // that makes it a different one puts them back at the start of it. Reconciled here rather than
  // done as each control is pressed, because the period, the population and the narrowing all
  // live in the address and the browser's own way back changes them without anything on this
  // screen being touched. Page nine of a list that is now four pages long is a screen with
  // nothing on it.
  const asked = listName(period, population, shown, perPage);
  const [listed, setListed] = useState(asked);

  if (listed !== asked) {
    setListed(asked);
    setOffset(0);
  }

  // Resolved once per period rather than on every render: the window is part of the name each
  // answer is cached under, and one that moved with the clock would never find a cached answer.
  // The days it covers are worked out from the same moment, so the two never stand a day apart.
  const { window, span } = useMemo(() => {
    const resolvedAt = new Date();

    return {
      window: windowFor(period, site.timeZoneId, resolvedAt),
      span: spanFor(period, site.timeZoneId, resolvedAt),
    };
  }, [period, site.timeZoneId]);

  // Under all time, where the website's record begins. The overview's all time reaches back to its
  // first month, from what is kept of each month, while a visit is listed here only as long as the
  // visit itself is kept — so the list says it covers less only where the record reaches back past
  // the days still kept. A website first measured since has both over the same days. Asked under
  // the name the overview asks it, so arriving from there asks nothing, and asked as soon as
  // somebody reaches for the period, so all time chosen next finds it waiting. Under all time the
  // list waits for the first answer this screen sees settle, so the sentence and the rows it
  // qualifies arrive together and nothing already on the screen is pushed down by a sentence arriving
  // late — and only for that one: a record asked again, after a refusal or a lost connection, never
  // takes the rows somebody is reading off the screen, nor closes the visits they opened. Settled is
  // an answer, or a refusal with nothing more being asked; a refusal this screen is about to ask
  // again — one left from before it opened, or landing as it opens — is not an answer it saw, so it
  // still waits for its own.
  const allTime = isAllTime(period);
  const [reaching, setReaching] = useState(false);
  const reach = useCallback(() => setReaching(true), []);
  const asking = allTime || reaching;
  const record = useAllTime(site.id, site.timeZoneId, 'everybody', asking);
  const firstDay = record.data?.firstDay ?? null;
  const keptOnly = allTime && firstDay !== null && firstDay < span.first;
  // What was seen settle is one record: one website's, read in its zone. Another website chosen, or
  // the same one moved to another zone, is a record this screen has not yet seen settle.
  const watched = `${site.id}/${site.timeZoneId}`;
  const [seen, setSeen] = useState({ watched, settled: false });
  const settledNow =
    record.data !== undefined || (record.isError && record.fetchStatus !== 'fetching');

  if (seen.watched !== watched) {
    setSeen({ watched, settled: false });
  } else if (asking && !seen.settled && settledNow) {
    setSeen({ watched, settled: true });
  }

  const recordIn =
    !allTime || record.data !== undefined || (seen.watched === watched && seen.settled);

  const traffic = useTraffic(site.id, window);
  const visits = useVisits(site.id, window, perPage, offset, narrowing);
  const held = useFacets(site.id, window, wanted);
  const available = useMemo(() => tallyCategories(traffic.data?.groups ?? []), [traffic.data]);
  // "Nobody has been looked at yet" is a different fact from "no people match", and is the one
  // the screen gives whichever population it is kept to.
  const nothingJudged = traffic.data !== undefined && traffic.data.groups.length === 0;
  // Said once however much was refused. The totals and the list are asked for together, and an
  // engine that cannot answer one usually cannot answer the other: the same sentence twice reads
  // as two different problems. The list's refusal is the one said, the list being what the screen
  // is for.
  const refusal = visits.error ?? traffic.error;
  // A list on its way stands in as its shape; a refused one is not on its way, and a shape pulsing
  // under the notice would promise an answer that is not coming.
  const rows = visits.data !== undefined && recordIn ? visits.data : undefined;
  const waiting = rows === undefined && !visits.isError;

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {t('title')}
          </h1>
          <p className="max-w-2xl text-sm text-foreground-muted">
            {t(peopleOnly ? 'captionPeople' : 'caption', { site: site.displayName })}
          </p>
        </div>

        {/* Aligned by their tops, as on the overview: the period carries a second line. */}
        <div className="flex flex-wrap items-start gap-3">
          <PopulationSwitch peopleOnly={peopleOnly} onChange={showOnlyPeople} />
          <PeriodPicker
            value={period}
            onChange={choose}
            timeZoneId={site.timeZoneId}
            onReach={reach}
          />
        </div>
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
          value={shown}
          onChange={narrow}
          peopleOnly={peopleOnly}
        />
      ) : null}

      {refusal === null ? null : <FailureNotice error={refusal} />}

      {waiting ? <ListWaiting /> : null}

      {/* Said once, where the list begins, as the overview says it where its lists begin. */}
      {rows !== undefined && keptOnly ? (
        <p className="-mb-3 max-w-2xl text-sm text-foreground-muted">{t('keptOnly')}</p>
      ) : null}

      {rows === undefined ? null : (
        <Listing
          site={site}
          answer={rows}
          narrowed={isNarrowed(shown)}
          peopleOnly={peopleOnly && !nothingJudged}
          onClear={() => narrow(EVERY_JOURNEY)}
          onShowEveryone={() => showOnlyPeople(false)}
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
  /** Whether the reader narrowed the list by something about the visits. */
  readonly narrowed: boolean;
  /** Whether the list is kept to people, over a period in which something has been judged. */
  readonly peopleOnly: boolean;
  readonly onClear: () => void;
  readonly onShowEveryone: () => void;
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
 * away — by what the reader picked, by keeping the screen to people, or both — is somebody one
 * press from seeing them again, and each press that would is on the screen.
 */
function Listing({
  site,
  answer,
  narrowed,
  peopleOnly,
  onClear,
  onShowEveryone,
  busy,
  perPage,
  offset,
  onMove,
  onResize,
}: ListingProps) {
  const t = useTranslations('journeys.empty');

  if (answer.visits.length === 0) {
    if (!narrowed && !peopleOnly) {
      return <ListEmpty icon={ScanSearch} title={t('none.title')} body={t('none.body')} />;
    }

    return (
      <ListEmpty
        icon={Funnel}
        title={t('narrowed.title')}
        body={t('narrowed.body')}
        action={
          <div className="flex flex-wrap justify-center gap-2">
            {narrowed ? (
              <Button tone="secondary" size="sm" onClick={onClear}>
                {t('narrowed.action')}
              </Button>
            ) : null}
            {peopleOnly ? (
              <Button tone="secondary" size="sm" onClick={onShowEveryone}>
                {t('narrowed.everyone')}
              </Button>
            ) : null}
          </div>
        }
      />
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

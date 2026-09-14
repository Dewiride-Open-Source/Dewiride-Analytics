'use client';

import { ChevronRight } from 'lucide-react';
import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';
import { ListCredits } from '@/components/dashboard/place-credit';
import { VerdictBadge } from '@/components/dashboard/verdict-badge';
import { VisitEvidence } from '@/components/dashboard/visit-evidence';
import { VisitJourney } from '@/components/dashboard/visit-journey';
import { Whereabouts, type WhereaboutsFact } from '@/components/dashboard/whereabouts';
import { Card } from '@/components/ui/card';
import { Pagination } from '@/components/ui/pagination';
import { PAGE_SIZES } from '@/lib/analytics/journeys';
import { countryNames } from '@/lib/analytics/places';
import { namedAs } from '@/lib/analytics/verdicts';
import type { Visit } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

/**
 * Everything a row can say about its visitor. The panel a row opens onto lays the same four facts
 * out in full under "Who this was", so the row saying every one of them is what lets a reader scan
 * the list without opening anything.
 */
const EVERYTHING: readonly WhereaboutsFact[] = ['source', 'place', 'network', 'readOn'];

interface VisitListProps {
  readonly siteId: string;
  /** The page on screen, newest first, as the engine returned it. */
  readonly visits: readonly Visit[];
  /** How many journeys the period holds altogether, after anything narrowed away. */
  readonly totalVisits: number;
  /** The site's own zone, so a visit is stamped with the time it happened where the site is. */
  readonly timeZoneId: string;
  /** How far down the list this page begins. */
  readonly offset: number;
  /** How many rows a page holds. */
  readonly perPage: number;
  /** Whether the next page is still on its way, so the controls do not invite a second press. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
  readonly onResize: (perPage: number) => void;
}

/**
 * Every journey a period holds, newest first, a page at a time, each openable to show what it was
 * judged on.
 *
 * Every verdict on this list can be taken apart, including the evidence that pointed away from it.
 * A product whose whole proposition is that a number can be explained has to be able to explain
 * one, and a conclusion shown without the case against it is an assertion rather than a finding.
 * That is also why every page of the list is reachable rather than only the next one: a verdict
 * nobody can reach is a verdict nobody can question.
 */
export function VisitList({
  siteId,
  visits,
  totalVisits,
  timeZoneId,
  offset,
  perPage,
  busy,
  onMove,
  onResize,
}: VisitListProps) {
  const t = useTranslations('journeys.list');
  const locale = useLocale();
  const placed = visits.some((visit) => visit.context.countryCode !== '');
  const networked = visits.some((visit) => visit.context.network !== '');

  // Built once for the whole list rather than once per row. Building one of these costs enough to
  // notice down a list, which is precisely what this is.
  const countryName = useMemo(() => countryNames(locale), [locale]);

  return (
    <Card className="flex flex-col gap-3 p-5 sm:p-6">
      <div className="flex flex-col">
        {visits.map((visit) => (
          <VisitRow
            key={visit.id}
            siteId={siteId}
            visit={visit}
            countryName={countryName}
            timeZoneId={timeZoneId}
          />
        ))}
      </div>

      <Pagination
        label={t('nav.label')}
        total={totalVisits}
        perPage={perPage}
        offset={offset}
        shown={visits.length}
        sizes={PAGE_SIZES}
        busy={busy}
        onMove={onMove}
        onResize={onResize}
      />

      <ListCredits placed={placed} networked={networked} />
    </Card>
  );
}

interface VisitRowProps {
  readonly siteId: string;
  readonly visit: Visit;
  /** Writes a country code out in the reader's language, built once for the whole list. */
  readonly countryName: (code: string) => string | null;
  readonly timeZoneId: string;
}

function VisitRow({ siteId, visit, countryName, timeZoneId }: VisitRowProps) {
  const t = useTranslations('journeys.list');
  const [opened, setOpened] = useState(false);
  const strengths = useTranslations('verdicts.strength');
  const evidence = useTranslations('verdicts.evidence');
  const surfaceNames = useTranslations('verdicts.surface');
  const format = useFormatter();

  // Two reporters can watch the same visit and be worth naming as one thing to the person who
  // owns the website — their own server is their own server, whichever framework it runs.
  const seenBy = [...new Set(visit.surfaces.map((surface) => surfaceNames(surface)))];
  const name = namedAs(visit.supporting);

  return (
    <details
      className="group border-t border-border first:border-t-0"
      onToggle={(event) => setOpened(event.currentTarget.open)}
    >
      <summary
        className={cn(
          'flex cursor-pointer list-none items-center gap-3 py-3',
          '[&::-webkit-details-marker]:hidden',
        )}
      >
        <ChevronRight
          aria-hidden
          className="size-4 shrink-0 text-foreground-subtle transition-transform group-open:rotate-90"
        />
        <span className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <VerdictBadge category={visit.category} />
            <span className="text-xs text-foreground-muted">{strengths(visit.strength)}</span>
            {name === null ? null : (
              <bdi className="text-xs font-medium text-foreground">{name}</bdi>
            )}
          </span>

          {/*
            Only while the row is shut. Opening it lays the same facts out in full an inch below,
            and a screen that says "Jaipur, India" twice in two lines reads as a mistake.
          */}
          {opened ? null : (
            <Whereabouts context={visit.context} countryName={countryName} facts={EVERYTHING} />
          )}
        </span>
        {/*
          Stacked on a phone and side by side from a tablet up. Neither figure is dropped at the
          small size: how many pages a visit took and when it happened are the two things somebody
          scans this list for, and a row that answers one of them on a phone is half a row.
        */}
        <span className="flex shrink-0 flex-col items-end gap-0.5 text-xs tabular-nums sm:flex-row sm:items-center sm:gap-3 sm:text-sm">
          <span className="text-foreground-muted">{t('pages', { count: visit.pageCount })}</span>
          <time dateTime={visit.startedAt} className="text-foreground-subtle">
            {format.dateTime(new Date(visit.startedAt), {
              timeZone: timeZoneId,
              day: 'numeric',
              month: 'short',
              hour: 'numeric',
              minute: '2-digit',
            })}
          </time>
        </span>
      </summary>

      <div className="flex flex-col gap-4 pb-4 pl-7">
        {seenBy.length > 0 ? (
          <p className="text-xs text-foreground-subtle">
            {t('seenBy', { surfaces: format.list(seenBy) })}
          </p>
        ) : null}

        <VisitJourney
          siteId={siteId}
          visit={visit.id}
          pageCount={visit.pageCount}
          timeZoneId={timeZoneId}
          open={opened}
        />

        <VisitEvidence title={evidence('supporting')} reasons={visit.supporting} />

        {visit.contradicting.length > 0 ? (
          <VisitEvidence title={evidence('contradicting')} reasons={visit.contradicting} />
        ) : null}
      </div>
    </details>
  );
}

'use client';

import { Bot, ChevronRight, Info, type LucideIcon, UserRound } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import { VerdictBadge } from '@/components/dashboard/verdict-badge';
import { VisitJourney } from '@/components/dashboard/visit-journey';
import { Card } from '@/components/ui/card';
import { Pagination } from '@/components/ui/pagination';
import { PAGE_SIZES } from '@/lib/analytics/journeys';
import { byWeight, reasonKey, reasonValues } from '@/lib/analytics/verdicts';
import type { SignalDirection, Visit, VisitReason } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

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

/** What each observation is marked with: somebody, something, or a qualifier on either. */
const MARKERS: Readonly<Record<SignalDirection, LucideIcon>> = {
  'toward-human': UserRound,
  'toward-automation': Bot,
  neutral: Info,
};

const TINTS: Readonly<Record<SignalDirection, string>> = {
  'toward-human': 'text-positive',
  'toward-automation': 'text-accent-strong',
  neutral: 'text-foreground-subtle',
};

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

  return (
    <Card className="flex flex-col gap-3 p-5 sm:p-6">
      <div className="flex flex-col">
        {visits.map((visit) => (
          <VisitRow key={visit.id} siteId={siteId} visit={visit} timeZoneId={timeZoneId} />
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
    </Card>
  );
}

interface VisitRowProps {
  readonly siteId: string;
  readonly visit: Visit;
  readonly timeZoneId: string;
}

function VisitRow({ siteId, visit, timeZoneId }: VisitRowProps) {
  const t = useTranslations('journeys.list');
  const [opened, setOpened] = useState(false);
  const strengths = useTranslations('verdicts.strength');
  const surfaceNames = useTranslations('verdicts.surface');
  const format = useFormatter();

  // Two reporters can watch the same visit and be worth naming as one thing to the person who
  // owns the website — their own server is their own server, whichever framework it runs.
  const seenBy = [...new Set(visit.surfaces.map((surface) => surfaceNames(surface)))];

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
        <span className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1">
          <VerdictBadge category={visit.category} />
          <span className="text-xs text-foreground-muted">{strengths(visit.strength)}</span>
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

        <Evidence title={t('supporting')} reasons={visit.supporting} />

        {visit.contradicting.length > 0 ? (
          <Evidence title={t('contradicting')} reasons={visit.contradicting} />
        ) : null}
      </div>
    </details>
  );
}

function Evidence({
  title,
  reasons,
}: {
  readonly title: string;
  readonly reasons: readonly VisitReason[];
}) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-xs font-medium tracking-wide text-foreground-muted uppercase">{title}</h3>
      <ul className="flex flex-col gap-1.5">
        {byWeight(reasons).map((reason) => (
          <Reason key={reason.code} reason={reason} />
        ))}
      </ul>
    </div>
  );
}

/**
 * One observation, written out.
 *
 * An observation this build has no sentence for is shown as a plain acknowledgement that
 * something else counted. Its code is a name for our own convenience and would mean nothing to
 * the person reading, and quietly dropping it would leave a verdict looking thinner than the case
 * that actually produced it.
 */
function Reason({ reason }: { readonly reason: VisitReason }) {
  const t = useTranslations('reasons');
  const key = reasonKey(reason);
  const Marker = MARKERS[reason.direction];

  return (
    <li className="flex gap-2 text-sm text-foreground-muted">
      <Marker aria-hidden className={cn('mt-0.5 size-3.5 shrink-0', TINTS[reason.direction])} />
      <span>{t.has(key) ? t(key, reasonValues(reason)) : t('other')}</span>
    </li>
  );
}

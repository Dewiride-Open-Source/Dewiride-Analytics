'use client';

import { Route } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import {
  ListEmpty,
  ListSwitch,
  ListWaiting,
  RankedNav,
  RankedRow,
} from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { readablePath } from '@/lib/analytics/pages';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { shareOf } from '@/lib/analytics/share';
import type { VisitPageRow, VisitPosition } from '@/lib/api/schemas';
import { useVisitPages, useVisitTotals } from '@/lib/queries/sites';

interface SiteFlowProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/** How many pages are shown at once, matching the lists around it. */
const PER_PAGE = 10;

/**
 * How people move through a website, over a period.
 *
 * Two figures about a visit and two lists about its ends, because they answer one question between
 * them: people arrive somewhere, read some number of pages, and leave from somewhere. Only visits
 * that have finished are counted — a visit still in progress has an unfinished page count, and on a
 * quiet website a handful of those would decide both figures on their own.
 *
 * The screen above gives this a key that changes with the website and the period, so choosing
 * either starts afresh rather than leaving somebody on a screenful that no longer exists.
 */
export function SiteFlow({ siteId, window }: SiteFlowProps) {
  const t = useTranslations('dashboard.flow');
  const format = useFormatter();
  const [position, setPosition] = useState<VisitPosition>('entry');
  const [offset, setOffset] = useState(0);
  const totals = useVisitTotals(siteId, window);
  const pages = useVisitPages(siteId, window, position, PER_PAGE, offset);

  function show(next: VisitPosition) {
    setPosition(next);
    setOffset(0);
  }

  if (totals.isError) {
    return <FailureNotice error={totals.error} />;
  }

  if (totals.isPending) {
    return <ListWaiting />;
  }

  if (totals.data.visits === 0) {
    return <ListEmpty icon={Route} title={t('empty.title')} body={t('empty.body')} />;
  }

  return (
    <Card className="flex flex-col gap-5 p-5 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
          <p className="text-sm text-foreground-muted tabular-nums">
            {t('caption', { count: totals.data.visits })}
          </p>
        </div>
        <ListSwitch
          label={t('view.label')}
          options={[
            { value: 'entry', label: t('view.entry') },
            { value: 'exit', label: t('view.exit') },
          ]}
          value={position}
          onChange={show}
        />
      </header>

      <div className="grid gap-4 sm:grid-cols-2">
        <Figure
          label={t('perVisit.label')}
          value={format.number(totals.data.pageViews / totals.data.visits, {
            maximumFractionDigits: 1,
          })}
          note={t('perVisit.note')}
        />
        <Figure
          label={t('singlePage.label')}
          value={format.number(shareOf(totals.data.singlePageVisits, totals.data.visits), {
            style: 'percent',
            maximumFractionDigits: 0,
          })}
        />
      </div>

      <FlowList
        pages={pages.data?.pages}
        failure={pages.error}
        totalVisits={pages.data?.totalVisits ?? 0}
        totalPaths={pages.data?.totalPaths ?? 0}
        most={pages.data?.mostVisits ?? 0}
        offset={offset}
        busy={pages.isFetching}
        onMove={setOffset}
      />
    </Card>
  );
}

interface FigureProps {
  readonly label: string;
  readonly value: string;
  /**
   * The one qualifier the figure genuinely needs, where it needs one.
   *
   * Pages per visit sits on the same screen as pages per visitor and differs from it by a fraction,
   * so without a word saying which is which the pair reads as one number printed twice.
   */
  readonly note?: string;
}

/** One headline figure about a visit. */
function Figure({ label, value, note }: FigureProps) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-border bg-surface-muted px-4 py-3">
      <p className="text-sm text-foreground-muted">{label}</p>
      <p className="text-2xl font-semibold text-foreground tabular-nums">{value}</p>
      {note ? <p className="text-xs text-foreground-subtle">{note}</p> : null}
    </div>
  );
}

interface FlowListProps {
  /** The slice on screen, or nothing while the first one is being read. */
  readonly pages: readonly VisitPageRow[] | undefined;
  /** Why the list could not be read, where it could not be. */
  readonly failure: Error | null;
  /** Visits across the whole period, which every share is taken against. */
  readonly totalVisits: number;
  /** How many distinct pages the period holds at this end of a visit. */
  readonly totalPaths: number;
  /** Visits at the commonest page, which every bar is drawn against. */
  readonly most: number;
  /** How many pages were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/** The pages a period's visits began or ended on, commonest first. */
function FlowList({
  pages,
  failure,
  totalVisits,
  totalPaths,
  most,
  offset,
  busy,
  onMove,
}: FlowListProps) {
  const t = useTranslations('dashboard.flow');

  if (failure) {
    return <FailureNotice error={failure} />;
  }

  if (!pages) {
    return <div className="h-40 animate-pulse rounded-md bg-surface-muted" />;
  }

  return (
    <>
      <ul className="flex flex-col gap-0.5">
        {pages.map((page) => {
          // Written by whoever asked for the page, so it is shown as text and never followed.
          const address = readablePath(page.path);

          return (
            <RankedRow
              key={page.path}
              name={address}
              hint={address}
              detail={t('visits', { count: page.visits })}
              part={page.visits}
              whole={totalVisits}
              most={most}
            />
          );
        })}
      </ul>

      <RankedNav
        label={t('nav.label')}
        offset={offset}
        shown={pages.length}
        total={totalPaths}
        step={PER_PAGE}
        busy={busy}
        onMove={onMove}
      />
    </>
  );
}

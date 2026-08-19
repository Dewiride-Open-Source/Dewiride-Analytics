'use client';

import { ChevronLeft, ChevronRight, FileText } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { readablePath } from '@/lib/analytics/pages';
import type { AnalyticsWindow } from '@/lib/analytics/period';
import { shareOf } from '@/lib/analytics/share';
import type { SitePage } from '@/lib/api/schemas';
import { usePages } from '@/lib/queries/sites';

interface SitePagesProps {
  readonly siteId: string;
  readonly window: AnalyticsWindow;
}

/**
 * How many addresses are shown at once.
 *
 * A screenful somebody reads down without scrolling past the rest of the dashboard, with the
 * remainder a step away rather than a scroll away. A site with a thousand addresses would
 * otherwise bury every panel below this one.
 */
const PER_PAGE = 10;

/**
 * Every page a website's traffic went to, over a period, a screenful at a time.
 *
 * How far down the list somebody has read is kept here, and the screen above gives this a key
 * that changes with the website and the period — so choosing either starts the new list at its
 * busiest page rather than at whichever screenful was open of the old one.
 */
export function SitePages({ siteId, window }: SitePagesProps) {
  const t = useTranslations('dashboard.pages');
  const [offset, setOffset] = useState(0);
  const pages = usePages(siteId, window, PER_PAGE, offset);

  if (pages.isError) {
    return <FailureNotice error={pages.error} />;
  }

  if (pages.isPending) {
    return <div className="h-64 animate-pulse rounded-lg border border-border bg-surface-muted" />;
  }

  if (pages.data.totalPaths === 0) {
    return (
      <Card className="flex flex-col items-center gap-2 px-6 py-14 text-center">
        <span
          aria-hidden
          className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
        >
          <FileText className="size-5 text-accent-strong" />
        </span>
        <h2 className="text-base font-semibold text-foreground">{t('empty.title')}</h2>
        <p className="max-w-sm text-sm text-foreground-muted">{t('empty.body')}</p>
      </Card>
    );
  }

  return (
    <PageList
      pages={pages.data.pages}
      pageViews={pages.data.pageViews}
      totalPaths={pages.data.totalPaths}
      mostPageViews={pages.data.mostPageViews}
      offset={offset}
      busy={pages.isFetching}
      onMove={setOffset}
    />
  );
}

interface PageListProps {
  /** The slice on screen, busiest first, as the engine returned it. */
  readonly pages: readonly SitePage[];
  /** Page views across the whole period, which every share is taken against. */
  readonly pageViews: number;
  /** How many addresses the period holds altogether. */
  readonly totalPaths: number;
  /** Page views at the busiest address, which every bar is drawn against. */
  readonly mostPageViews: number;
  /** How many addresses were passed over to reach this slice. */
  readonly offset: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/**
 * The list itself.
 *
 * The bar behind each row is the share drawn rather than written, and it is hidden from anyone
 * reading rather than looking: the figures beside it say the same thing in words and numbers. It
 * is drawn against the busiest address in the whole period rather than against the busiest on
 * screen, so a row means the same thing wherever in the list it is met.
 */
function PageList({
  pages,
  pageViews,
  totalPaths,
  mostPageViews,
  offset,
  busy,
  onMove,
}: PageListProps) {
  const t = useTranslations('dashboard.pages');
  const format = useFormatter();
  const last = offset + pages.length;

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        <p className="text-sm text-foreground-muted tabular-nums">
          {t('total', { count: pageViews })}
        </p>
      </header>

      <ul className="flex flex-col gap-0.5">
        {pages.map((page) => {
          const address = readablePath(page.path);

          return (
            <li key={page.path} className="relative isolate rounded-md">
              <span
                aria-hidden
                className="absolute inset-y-0 left-0 -z-10 rounded-md bg-accent-soft"
                style={{ width: width(page.pageViews, mostPageViews) }}
              />
              <div className="flex flex-col gap-1 px-2.5 py-2 sm:flex-row sm:items-center sm:gap-5">
                {/*
                  An address is written by whoever asked for the page, so it is shown as text and
                  never followed. Isolating its direction keeps a right-to-left address from
                  rearranging the figures that sit beside it, and a long one that had to be cut
                  short can still be read in full by resting on it.
                */}
                <bdi className="min-w-0 truncate text-sm text-foreground sm:flex-1" title={address}>
                  {address}
                </bdi>
                <span className="flex items-center justify-between gap-3 sm:justify-end">
                  <span className="text-sm text-foreground-muted tabular-nums">
                    {t('views', { count: page.pageViews })}
                    <span aria-hidden> · </span>
                    {t('visitors', { count: page.visitors })}
                  </span>
                  <span className="w-12 shrink-0 text-right text-sm font-medium text-foreground tabular-nums">
                    {share(page.pageViews, pageViews, format)}
                  </span>
                </span>
              </div>
            </li>
          );
        })}
      </ul>

      {/*
        The step through the list is absent rather than disabled when everything already fits on
        one screen. Two dead arrows under a list of four addresses is chrome for its own sake.
      */}
      {totalPaths > pages.length || offset > 0 ? (
        <nav
          aria-label={t('nav.label')}
          className="flex items-center justify-between gap-3 border-t border-border pt-4"
        >
          <Button
            tone="secondary"
            size="sm"
            disabled={offset === 0 || busy}
            onClick={() => onMove(Math.max(offset - PER_PAGE, 0))}
          >
            <ChevronLeft aria-hidden className="size-4" />
            {t('nav.previous')}
          </Button>
          <p aria-live="polite" className="text-sm text-foreground-muted tabular-nums">
            {t('nav.showing', { first: offset + 1, last, total: totalPaths })}
          </p>
          <Button
            tone="secondary"
            size="sm"
            disabled={last >= totalPaths || busy}
            onClick={() => onMove(offset + PER_PAGE)}
          >
            {t('nav.next')}
            <ChevronRight aria-hidden className="size-4" />
          </Button>
        </nav>
      ) : null}
    </Card>
  );
}

/** A row's bar, drawn against the busiest address and never quite vanishing. */
function width(pageViews: number, mostPageViews: number): string {
  return `${Math.max(shareOf(pageViews, mostPageViews) * 100, 2)}%`;
}

/**
 * How much of a period went to one page.
 *
 * A share too small to round to a whole per cent is written with a decimal instead. Further down
 * a long list every row would otherwise read as nought, which says the page had no traffic when
 * what happened is that somebody read it.
 */
function share(pageViews: number, total: number, format: ReturnType<typeof useFormatter>): string {
  const part = shareOf(pageViews, total);

  return format.number(part, {
    style: 'percent',
    maximumFractionDigits: part > 0 && part < 0.01 ? 1 : 0,
  });
}

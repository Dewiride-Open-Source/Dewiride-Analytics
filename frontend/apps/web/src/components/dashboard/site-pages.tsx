'use client';

import { FileText } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { ListEmpty, ListWaiting, RankedNav, RankedRow } from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { readablePath } from '@/lib/analytics/pages';
import type { AnalyticsWindow } from '@/lib/analytics/period';
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
    return <ListWaiting />;
  }

  if (pages.data.totalPaths === 0) {
    return <ListEmpty icon={FileText} title={t('empty.title')} body={t('empty.body')} />;
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
          // Written by whoever asked for the page, so it is shown as text and never followed.
          const address = readablePath(page.path);

          return (
            <RankedRow
              key={page.path}
              name={address}
              hint={address}
              detail={
                <>
                  {t('views', { count: page.pageViews })}
                  <span aria-hidden> · </span>
                  {t('visitors', { count: page.visitors })}
                </>
              }
              part={page.pageViews}
              whole={pageViews}
              most={mostPageViews}
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
    </Card>
  );
}

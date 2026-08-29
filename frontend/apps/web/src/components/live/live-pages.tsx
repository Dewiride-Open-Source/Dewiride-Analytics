'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { RankedRow } from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { readablePath } from '@/lib/analytics/pages';
import type { LivePage } from '@/lib/api/schemas';

interface LivePagesProps {
  /** The busiest pages of the half hour, in the order the engine ranked them. */
  readonly pages: readonly LivePage[];
}

/**
 * What the website's visitors have been reading in the last half hour, busiest first.
 *
 * Each row ends in how many times the page was opened rather than in a share of the half hour.
 * Only the leading handful of pages are carried back, so a share would be taken against the part
 * of the half hour that fitted on the card and would read as a share of all of it.
 */
export function LivePages({ pages }: LivePagesProps) {
  const t = useTranslations('live.pages');
  const format = useFormatter();

  // The engine ranks them, so the leading row is the one every bar is drawn against.
  const most = pages[0]?.pageViews ?? 0;

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>

      {pages.length === 0 ? (
        <p className="py-6 text-center text-sm text-foreground-muted">{t('none')}</p>
      ) : (
        <ul className="flex flex-col gap-0.5">
          {pages.map((page) => {
            // Written by whoever asked for the page, so it is shown as text and never followed.
            const address = readablePath(page.path);

            return (
              <RankedRow
                key={page.path}
                name={address}
                hint={address}
                detail={t('visitors', { count: page.visitors })}
                part={page.pageViews}
                most={most}
                figure={format.number(page.pageViews)}
              />
            );
          })}
        </ul>
      )}
    </Card>
  );
}

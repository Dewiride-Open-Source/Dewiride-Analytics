'use client';

import { useTranslations } from 'next-intl';
import { RankedRow } from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { readablePath } from '@/lib/analytics/pages';
import type { LivePage } from '@/lib/api/schemas';

interface LivePagesProps {
  /** The pages holding most of the visitors here, in the order the engine ranked them. */
  readonly pages: readonly LivePage[];
  /** How many visitors the half hour holds altogether, which every share is taken against. */
  readonly visitorsSeen: number;
}

/**
 * What the website's visitors are on right now, the page holding most first.
 *
 * Each row ends in the share of everybody here who was last on that page, and the shares add up
 * because every visitor is on exactly one. Where the list was cut short, the visitors on the pages
 * it left out are said in one line, so the card and the count above it never silently disagree.
 */
export function LivePages({ pages, visitorsSeen }: LivePagesProps) {
  const t = useTranslations('live.pages');

  // The engine ranks them, so the leading row is the one every bar is drawn against.
  const most = pages[0]?.visitors ?? 0;

  const elsewhere = Math.max(visitorsSeen - pages.reduce((sum, page) => sum + page.visitors, 0), 0);

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>

      {pages.length === 0 ? (
        <p className="py-6 text-center text-sm text-foreground-muted">{t('none')}</p>
      ) : (
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
                  detail={t('visitors', { count: page.visitors })}
                  part={page.visitors}
                  most={most}
                  whole={visitorsSeen}
                />
              );
            })}
          </ul>

          <div className="flex flex-col gap-1">
            {elsewhere === 0 ? null : (
              <p className="text-xs text-foreground-muted tabular-nums">
                {t('elsewhere', { count: elsewhere })}
              </p>
            )}
            <p className="text-xs text-foreground-subtle">{t('caption')}</p>
          </div>
        </>
      )}
    </Card>
  );
}

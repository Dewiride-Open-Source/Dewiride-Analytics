'use client';

import { ArrowRight } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useMemo } from 'react';
import { VerdictBadge } from '@/components/dashboard/verdict-badge';
import { Card } from '@/components/ui/card';
import { Link } from '@/i18n/navigation';
import { anybodyActive, namedNow } from '@/lib/analytics/live';
import type { Live } from '@/lib/api/schemas';
import { JOURNEYS } from '@/lib/routes';

interface LiveHeadlineProps {
  readonly answer: Live;
}

/**
 * How many visitors the half hour holds, and what can be said about them.
 *
 * The number is the whole reason somebody opened the screen, so it is the largest thing on it and
 * sits on the one card the page is built around. What follows it is deliberately not a breakdown of
 * everybody: most visitors carry no conclusion at all while their visit is still running, and a
 * chart dividing them up would have to invent one. So the named ones are named, the rest are
 * counted, and the screen says in one clause when the rest will be answered.
 */
export function LiveHeadline({ answer }: LiveHeadlineProps) {
  const t = useTranslations('live');
  const format = useFormatter();
  const named = useMemo(() => namedNow(answer.visitors), [answer.visitors]);
  const busy = anybodyActive(answer.visitors, answer.at);

  return (
    // Measured against the card rather than the window, so that the arrangement stays right
    // wherever the card is put: side by side while it has the width of a screen, stacked again in a
    // column beside something else.
    <Card focal className="@container p-5 sm:p-6">
      <div className="flex flex-col gap-5 @2xl:flex-row @2xl:items-stretch @2xl:gap-8">
        <div className="flex flex-col gap-1 @2xl:w-56 @2xl:shrink-0">
          {/*
            The only part of this screen that announces itself. It holds the label and the figure
            together so that it is read out as "Visitors, seven" rather than as a bare number, and
            React writes nothing at all when a fresh reading leaves the figure where it was — so a
            quiet half hour is silent rather than counting itself aloud every ten seconds.
          */}
          <div aria-live="polite" aria-atomic="true" className="flex flex-col gap-1">
            <h2 className="text-sm font-medium text-foreground-muted">{t('count.label')}</h2>
            <p className="text-4xl font-semibold tracking-tight text-foreground tabular-nums sm:text-5xl">
              {format.number(answer.visitorsSeen)}
            </p>
          </div>

          {/*
            Which of the two is worth a line depends on the moment. While somebody is here, what the
            figure counts is the thing a reader has to know to trust it; once everybody has left,
            that they have left is the more useful fact and explains the figure at the same time.
          */}
          <p className="text-sm text-foreground-muted">
            {busy ? t('count.note') : t('count.quiet')}
          </p>
        </div>

        {answer.visitors.length === 0 ? null : (
          <div className="flex min-w-0 flex-1 flex-col gap-3 border-t border-border pt-5 @2xl:border-t-0 @2xl:border-l @2xl:pt-0 @2xl:pl-8">
            {/*
              The heading belongs to the badges and appears with them. On the great majority of half
              hours nothing has been settled about anybody, and a heading saying something has been
              recognised standing over a line saying nobody has been is the screen contradicting
              itself in two lines.
            */}
            {named.groups.length === 0 ? null : (
              <>
                <h3 className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
                  {t('named.title')}
                </h3>

                <ul className="flex flex-wrap items-center gap-x-4 gap-y-2">
                  {named.groups.map((group) => (
                    <li key={group.category} className="flex items-center gap-1.5">
                      <VerdictBadge category={group.category} />
                      <span className="text-sm font-medium text-foreground tabular-nums">
                        {format.number(group.visitors)}
                      </span>
                    </li>
                  ))}
                </ul>
              </>
            )}

            {named.unnamed === 0 ? null : (
              <div className="flex flex-col gap-0.5">
                <p className="text-sm text-foreground-muted">
                  {t('named.watching', { count: named.unnamed })}
                </p>
                <p className="text-xs text-foreground-subtle">{t('named.settles')}</p>
              </div>
            )}

            <Link
              href={JOURNEYS}
              className="inline-flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-accent-strong hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
            >
              {t('named.link')}
              <ArrowRight aria-hidden className="size-4" />
            </Link>
          </div>
        )}
      </div>
    </Card>
  );
}

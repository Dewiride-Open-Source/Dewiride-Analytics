'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { fillFor, VerdictBadge } from '@/components/dashboard/verdict-badge';
import { Card } from '@/components/ui/card';
import { shareOf } from '@/lib/analytics/share';
import type { TrafficGroup } from '@/lib/api/schemas';

interface TrafficBreakdownProps {
  /** The groups, busiest first, as the engine returned them. */
  readonly groups: readonly TrafficGroup[];
  /** Visits behind the whole breakdown, which every share is taken against. */
  readonly sessions: number;
}

/**
 * How a period divides up between the people a website is for and everything else.
 *
 * The bar is a summary and the list beneath it is the answer. A bar drawn on its own tells a
 * screen reader nothing, and two categories that share a colour are told apart only by the words
 * beside them — so the words are what carry the figures, and the bar is hidden from anyone
 * reading rather than looking.
 */
export function TrafficBreakdown({ groups, sessions }: TrafficBreakdownProps) {
  const t = useTranslations('dashboard.traffic');
  const strengths = useTranslations('verdicts.strength');
  const format = useFormatter();

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        <p className="text-sm text-foreground-muted tabular-nums">
          {t('judged', { count: sessions })}
        </p>
      </header>

      <div
        aria-hidden
        className="flex h-2.5 w-full gap-0.5 overflow-hidden rounded-full bg-surface-muted"
      >
        {groups.map((group) => (
          <span
            key={identify(group)}
            className={fillFor(group.category)}
            style={{ width: percent(shareOf(group.sessions, sessions)) }}
          />
        ))}
      </div>

      {/*
        A row is stacked on a phone and one line from a tablet up. Left to wrap on its own, the
        share drops onto a line of its own against the left edge, which reads as a mistake rather
        than as the same figure every row above lines up on.
      */}
      <ul className="flex flex-col">
        {groups.map((group) => (
          <li
            key={identify(group)}
            className="flex flex-col gap-1 border-t border-border py-2.5 first:border-t-0 first:pt-0 sm:flex-row sm:items-center sm:gap-3"
          >
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1 sm:flex-1">
              <VerdictBadge category={group.category} />
              <span className="text-xs text-foreground-muted">{strengths(group.strength)}</span>
            </span>
            <span className="flex items-center justify-between gap-3 sm:justify-end">
              <span className="text-sm text-foreground-muted tabular-nums">
                {t('sessions', { count: group.sessions })}
                <span aria-hidden> · </span>
                {t('pages', { count: group.pageViews })}
              </span>
              <span className="w-11 text-right text-sm font-medium text-foreground tabular-nums">
                {format.number(shareOf(group.sessions, sessions), {
                  style: 'percent',
                  maximumFractionDigits: 0,
                })}
              </span>
            </span>
          </li>
        ))}
      </ul>

      <p className="text-xs text-foreground-subtle">{t('pending')}</p>
    </Card>
  );
}

/**
 * What tells one group from another.
 *
 * A category appears more than once when the same conclusion was reached about different visits
 * with different weight behind it, and those are deliberately not merged: a hundred visits called
 * a crawler on slight signs is a different statement from a hundred called one on strong signs.
 */
function identify(group: TrafficGroup): string {
  return `${group.category}/${group.strength}`;
}

/** A share as a width, kept off the percent scale's edges so a sliver is still visible. */
function percent(share: number): string {
  return `${Math.max(share * 100, 1.5)}%`;
}

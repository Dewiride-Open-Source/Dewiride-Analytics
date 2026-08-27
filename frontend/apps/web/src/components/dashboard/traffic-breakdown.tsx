'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useCallback, useMemo } from 'react';
import { Ring } from '@/components/charts/ring';
import { SplitList, SplitRow } from '@/components/dashboard/ranked-list';
import { TONE_FILLS, VerdictBadge } from '@/components/dashboard/verdict-badge';
import { Card } from '@/components/ui/card';
import { shareOf } from '@/lib/analytics/share';
import { tonesIn } from '@/lib/analytics/verdicts';
import type { TrafficGroup } from '@/lib/api/schemas';
import type { ChartPalette } from '@/lib/charts/palette';

interface TrafficBreakdownProps {
  /** The groups, busiest first, as the engine returned them. */
  readonly groups: readonly TrafficGroup[];
  /** Visits behind the whole breakdown, which every share is taken against. */
  readonly sessions: number;
}

/**
 * How a period divides up between the people a website is for and everything else.
 *
 * The ring and the list are deliberately not the same cut. The ring divides the period four ways —
 * the people it was for, machinery, what nobody asked for, and what could not be said — because
 * that is what somebody glances at this card to settle. The list names every category exactly, so
 * a crawler that says it is an AI one is never shown as one that has been confirmed, and two
 * categories that share a colour are told apart by the words beside them.
 */
export function TrafficBreakdown({ groups, sessions }: TrafficBreakdownProps) {
  const t = useTranslations('dashboard.traffic');
  const strengths = useTranslations('verdicts.strength');
  const tones = useTranslations('verdicts.tone');
  const format = useFormatter();

  const portions = useMemo(() => tonesIn(groups), [groups]);

  const slices = useCallback(
    (palette: ChartPalette) =>
      portions.map((portion) => ({
        name: tones(portion.tone),
        value: portion.sessions,
        colour: palette.tones[portion.tone],
      })),
    [portions, tones],
  );

  return (
    <Card className="flex flex-col gap-4 p-5 sm:p-6">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        <p className="text-sm text-foreground-muted tabular-nums">
          {t('judged', { count: sessions })}
        </p>
      </header>

      <SplitList
        ring={
          <>
            <Ring slices={slices} label={t('ring')} />

            {/*
              What the four arcs stand for, and how much of the period each came to. The list
              beside it is the same period cut finer, so this is the only place the four are
              named — without it a colour on the ring would stand for nothing a reader could put
              into words.
            */}
            <ul className="flex w-40 flex-col gap-1.5">
              {portions.map((portion) => (
                <li
                  key={portion.tone}
                  className="flex items-center gap-1.5 text-xs text-foreground-muted"
                >
                  <span
                    aria-hidden
                    className={`size-2 shrink-0 rounded-full ${TONE_FILLS[portion.tone]}`}
                  />
                  {tones(portion.tone)}
                  <span className="ml-auto font-medium text-foreground tabular-nums">
                    {format.number(shareOf(portion.sessions, sessions), {
                      style: 'percent',
                      maximumFractionDigits: 0,
                    })}
                  </span>
                </li>
              ))}
            </ul>
          </>
        }
      >
        {groups.map((group) => (
          <SplitRow
            key={identify(group)}
            name={
              <>
                <VerdictBadge category={group.category} />
                <span className="text-xs text-foreground-muted">{strengths(group.strength)}</span>
              </>
            }
            detail={
              <>
                {t('sessions', { count: group.sessions })}
                <span aria-hidden> · </span>
                {t('pages', { count: group.pageViews })}
              </>
            }
            part={group.sessions}
            whole={sessions}
          />
        ))}
      </SplitList>

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

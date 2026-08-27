'use client';

import { ArrowDownRight, ArrowUpRight, type LucideIcon, Minus } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { Card } from '@/components/ui/card';
import type { Change, Direction } from '@/lib/analytics/change';

interface MetricCardProps {
  readonly label: string;
  /** The number to show, or nothing when the period gives no answer. */
  readonly value: string | null;
  /**
   * How it moved against the same figure over the period before.
   *
   * Nothing at all until that period's answer has arrived, and nothing where neither period held
   * any of it. A number on its own says how much; a number beside the one before it is what
   * somebody opens a dashboard to find out.
   */
  readonly change?: Change | null;
  /** What it is being compared with, named the way somebody would say it out loud. */
  readonly comparedWith?: string;
  /**
   * The one clause a number cannot honestly be shown without.
   *
   * Reserved for exactly that. A note on every card turns a dashboard into a manual, and a note
   * on none of them lets a count of daily visitors be read as a count of people.
   */
  readonly note?: string;
}

/** Stands in for a number that a period with no traffic cannot produce. */
const ABSENT = '—';

/** One headline number. */
export function MetricCard({ label, value, change, comparedWith, note }: MetricCardProps) {
  return (
    <Card className="flex flex-col gap-1 p-5">
      <h3 className="text-sm font-medium text-foreground-muted">{label}</h3>
      <p
        className={
          value === null
            ? 'text-3xl font-semibold text-foreground-subtle sm:text-4xl'
            : 'text-3xl font-semibold tracking-tight tabular-nums text-foreground sm:text-4xl'
        }
      >
        {value ?? ABSENT}
      </p>
      {change && comparedWith ? <Moved change={change} comparedWith={comparedWith} /> : null}
      {note ? <p className="mt-1 text-xs text-foreground-subtle">{note}</p> : null}
    </Card>
  );
}

/** The mark each direction is drawn with. */
const ARROWS: Readonly<Record<Direction, LucideIcon>> = {
  up: ArrowUpRight,
  down: ArrowDownRight,
  level: Minus,
};

/**
 * The colour each direction is written in.
 *
 * Green for more and red for less, which is how everybody already reads a trend on a count. It
 * says which way the figure went and nothing about whether that is good news: what the traffic
 * actually was is the drawing below, and that is where a rise turns out to be readers or crawlers.
 */
const TONES: Readonly<Record<Direction, string>> = {
  up: 'text-positive',
  down: 'text-danger',
  level: 'text-foreground-subtle',
};

interface MovedProps {
  readonly change: Change;
  readonly comparedWith: string;
}

/** Which way a number went, and against what. */
function Moved({ change, comparedWith }: MovedProps) {
  const t = useTranslations('dashboard.metrics.change');
  const format = useFormatter();
  const Arrow = ARROWS[change.direction];
  const tone = TONES[change.direction];

  return (
    <p className="mt-1 flex flex-wrap items-center gap-x-1 text-xs text-foreground-subtle">
      <Arrow aria-hidden className={`size-3.5 shrink-0 ${tone}`} />
      <span>
        {t.rich(change.fraction === null ? 'none' : change.direction, {
          amount: written(change.fraction, format),
          period: comparedWith,
          strong: (words) => <span className={`font-medium ${tone}`}>{words}</span>,
        })}
      </span>
    </p>
  );
}

/** How far a figure moved, written as a share of what it was. */
function written(fraction: number | null, format: ReturnType<typeof useFormatter>): string {
  return fraction === null
    ? ''
    : format.number(Math.abs(fraction), { style: 'percent', maximumFractionDigits: 0 });
}

/** The shape of a metric card while its number is still being fetched. */
export function MetricCardSkeleton() {
  return (
    <Card className="flex flex-col gap-2 p-5">
      <div className="h-4 w-24 animate-pulse rounded-sm bg-surface-muted" />
      <div className="h-9 w-20 animate-pulse rounded-sm bg-surface-muted" />
    </Card>
  );
}

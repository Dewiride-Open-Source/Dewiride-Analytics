'use client';

import { ArrowDownRight, ArrowUpRight, type LucideIcon, Minus } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
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

/**
 * How the line saying which way a number went is laid out, by the card and by its stand-in alike, so
 * the two break their lines in the same places.
 */
const MOVED_LINE = 'mt-1 flex flex-wrap items-center gap-x-1 text-xs';

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
    <p className={`${MOVED_LINE} text-foreground-subtle`}>
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

interface MetricCardSkeletonProps {
  /** The card's label, which no figure changes, or nothing where it is not known yet. */
  readonly label?: string;
  /**
   * What the card's number will be measured against, named as the card names it, where the card
   * will say how its number moved; nothing where it will not.
   */
  readonly comparedWith?: string;
  /** The clause the card carries, written out so the stand-in wraps exactly where the card will. */
  readonly note?: string;
}

/**
 * The move a stand-in's hidden sentence is written for: two digits, the middle of the one to three
 * a move is written with, so the stand-in breaks its line where the card's sentence does to within
 * a digit.
 */
const TYPICAL_MOVE = 0.1;

/** The emphasis in a stand-in's hidden sentence, which takes the room the card's own takes. */
const emphasised = (words: ReactNode) => <span className="font-medium">{words}</span>;

/**
 * The shape of a metric card while its number is still being fetched.
 *
 * Laid out from the card's own words wherever they are known: its label and its note as the card
 * writes them, and the line saying how the number moved as the card's own sentence for a number
 * that rose by a typical amount, laid out as the card lays it out and hidden. On a narrow card,
 * where a label or that line runs to two or three lines, the stand-in runs to as many. Only where a
 * card's sentence is a digit or a word away from breaking its line does the stand-in break it a
 * line apart, and then by that one line.
 */
export function MetricCardSkeleton({ label, comparedWith, note }: MetricCardSkeletonProps) {
  const t = useTranslations('dashboard.metrics.change');
  const format = useFormatter();

  return (
    <Card className="flex flex-col gap-1 p-5">
      {label === undefined ? (
        <div className="flex h-5 items-center">
          <div className="h-3.5 w-24 animate-pulse rounded-sm bg-surface-muted" />
        </div>
      ) : (
        <h3 className="text-sm font-medium text-foreground-muted">{label}</h3>
      )}
      <div className="flex h-9 items-center sm:h-10">
        <div className="h-7 w-20 animate-pulse rounded-sm bg-surface-muted sm:h-8" />
      </div>
      {comparedWith === undefined ? null : (
        <div aria-hidden className="relative">
          <p className={`${MOVED_LINE} invisible`}>
            <span className="size-3.5 shrink-0" />
            <span>
              {t.rich('up', {
                amount: written(TYPICAL_MOVE, format),
                period: comparedWith,
                strong: emphasised,
              })}
            </span>
          </p>
          <div className="absolute top-1.5 left-0 h-3 w-32 animate-pulse rounded-sm bg-surface-muted" />
        </div>
      )}
      {note ? <p className="mt-1 text-xs text-foreground-subtle">{note}</p> : null}
    </Card>
  );
}

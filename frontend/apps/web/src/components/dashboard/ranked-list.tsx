'use client';

import { ChevronLeft, ChevronRight, type LucideIcon } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { shareOf } from '@/lib/analytics/share';
import { cn } from '@/lib/styling';

/**
 * The parts every "busiest first" list on the dashboard is built from.
 *
 * Pages, places and software are four lists of the same shape asking four questions, and the
 * shape is what makes them comparable: a row means the same thing on each, a bar is drawn to the
 * same rule, and a share is taken against the same kind of whole. Keeping the shape in one file
 * is what stops the fourth list drifting a little from the first.
 */

interface RankedRowBase {
  /** What the row is called. Written by the caller, which knows how its own names read. */
  readonly name: ReactNode;
  /** The whole name, where it may have been cut short to fit. */
  readonly hint?: string;
  /** The counts beside it, already written out in the reader's language. */
  readonly detail: ReactNode;
  /** How many this row had. */
  readonly part: number;
  /** The leading row in the whole period, which the bar is drawn against. */
  readonly most: number;
}

/**
 * How a row ends: as a share of the period, or as a figure of its own.
 *
 * Exactly one of the two, and the types say so. A list of pages by how often they were read ends
 * in a share, because a row only means something against everything it was drawn from; a list by
 * how long they held somebody ends in a length of time, which is not a share of anything. Letting
 * a caller supply both would let one silently win.
 */
type RankedRowEnding =
  | { readonly whole: number; readonly figure?: never }
  | { readonly figure: ReactNode; readonly whole?: never };

type RankedRowProps = RankedRowBase & RankedRowEnding;

/**
 * One row of a ranked list.
 *
 * The bar behind it is the row drawn rather than written, and it is hidden from anyone reading
 * rather than looking: the figures beside it say the same thing in words and numbers. It is drawn
 * against the leading row in the whole period rather than the leading row on screen, so a row
 * means the same thing wherever in the list it is met.
 */
export function RankedRow({ name, hint, detail, part, whole, most, figure }: RankedRowProps) {
  const format = useFormatter();

  return (
    <li className="relative isolate rounded-md">
      <span
        aria-hidden
        className="absolute inset-y-0 left-0 -z-10 rounded-md bg-accent-soft"
        style={{ width: width(part, most) }}
      />
      <div className="flex flex-col gap-1 px-2.5 py-2 sm:flex-row sm:items-center sm:gap-5">
        {/*
          Isolating the name's direction keeps a right-to-left one from rearranging the figures
          beside it, and a long one that had to be cut short can still be read by resting on it.
        */}
        <bdi className="min-w-0 truncate text-sm text-foreground sm:flex-1" title={hint}>
          {name}
        </bdi>
        <span className="flex items-center justify-between gap-3 sm:justify-end">
          <span className="text-sm text-foreground-muted tabular-nums">{detail}</span>
          {/*
            Wide enough for a share and free to grow for anything longer, so a page read to the
            very bottom does not wrap its own figure onto a second line.
          */}
          <span className="min-w-12 shrink-0 whitespace-nowrap text-right text-sm font-medium text-foreground tabular-nums">
            {whole === undefined ? figure : writeShare(part, whole, format)}
          </span>
        </span>
      </div>
    </li>
  );
}

interface RankedNavProps {
  /** What this steps through, for somebody who reaches it without seeing the list. */
  readonly label: string;
  /** How many rows were passed over to reach the slice on screen. */
  readonly offset: number;
  /** How many rows are on screen. */
  readonly shown: number;
  /** How many rows the period holds altogether. */
  readonly total: number;
  /** How far one step moves. */
  readonly step: number;
  /** Whether the next slice is still being read. */
  readonly busy: boolean;
  readonly onMove: (offset: number) => void;
}

/**
 * The step through a list too long to show at once.
 *
 * Absent rather than disabled when everything already fits on one screen: two dead arrows under a
 * list of four rows is chrome for its own sake.
 */
export function RankedNav({ label, offset, shown, total, step, busy, onMove }: RankedNavProps) {
  const t = useTranslations('dashboard.list');
  const last = offset + shown;

  if (total <= shown && offset === 0) {
    return null;
  }

  return (
    <nav
      aria-label={label}
      className="flex items-center justify-between gap-3 border-t border-border pt-4"
    >
      <Button
        tone="secondary"
        size="sm"
        disabled={offset === 0 || busy}
        onClick={() => onMove(Math.max(offset - step, 0))}
      >
        <ChevronLeft aria-hidden className="size-4" />
        {t('previous')}
      </Button>
      <p aria-live="polite" className="text-sm text-foreground-muted tabular-nums">
        {t('showing', { first: offset + 1, last, total })}
      </p>
      <Button
        tone="secondary"
        size="sm"
        disabled={last >= total || busy}
        onClick={() => onMove(offset + step)}
      >
        {t('next')}
        <ChevronRight aria-hidden className="size-4" />
      </Button>
    </nav>
  );
}

interface ListSwitchOption<TValue extends string> {
  readonly value: TValue;
  readonly label: string;
}

interface ListSwitchProps<TValue extends string> {
  /** What the choice is about, for somebody who reaches it without seeing the card. */
  readonly label: string;
  /** The ways the list can be read, in the order they are offered. */
  readonly options: readonly ListSwitchOption<TValue>[];
  readonly value: TValue;
  readonly onChange: (value: TValue) => void;
}

/**
 * Which way a list is read.
 *
 * One card read several ways rather than several cards, because they answer the same question at
 * different levels of detail and nobody wants all of them on screen at once. A radio group rather
 * than tabs: what changes is which answer is shown, not which panel is open.
 */
export function ListSwitch<TValue extends string>({
  label,
  options,
  value,
  onChange,
}: ListSwitchProps<TValue>) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="inline-flex rounded-md border border-border bg-surface p-0.5"
    >
      {options.map((option) => {
        const chosen = option.value === value;

        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={chosen}
            onClick={() => onChange(option.value)}
            className={cn(
              'rounded-sm px-3 py-1.5 text-sm font-medium transition-colors',
              'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
              chosen
                ? 'bg-accent-soft text-accent-strong'
                : 'text-foreground-muted hover:text-foreground',
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

interface ListEmptyProps {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly body: string;
  /**
   * The one thing that would change the state, where there is one.
   *
   * Inside the card rather than under it, because it belongs to the message: an action floating
   * beneath a bordered box reads as belonging to whatever comes next on the screen.
   */
  readonly action?: ReactNode;
}

/**
 * What a list shows before it has anything to show.
 *
 * A designed state rather than an absence: a heading, the reason there is nothing, and no
 * apology. Every list on the dashboard wears the same one so that a quiet website reads as a
 * quiet website rather than as four different kinds of nothing.
 */
export function ListEmpty({ icon: Icon, title, body, action }: ListEmptyProps) {
  return (
    <Card className="flex flex-col items-center gap-2 px-6 py-14 text-center">
      <span
        aria-hidden
        className="mb-2 flex size-12 items-center justify-center rounded-full bg-accent-soft"
      >
        <Icon className="size-5 text-accent-strong" />
      </span>
      <h2 className="text-base font-semibold text-foreground">{title}</h2>
      <p className="max-w-sm text-sm text-foreground-muted">{body}</p>
      {action === undefined ? null : <div className="mt-4">{action}</div>}
    </Card>
  );
}

/** The placeholder a list stands in while its first answer is being read. */
export function ListWaiting() {
  return <div className="h-64 animate-pulse rounded-lg border border-border bg-surface-muted" />;
}

/** A row's bar, drawn against the busiest row and never quite vanishing. */
function width(part: number, most: number): string {
  return `${Math.max(shareOf(part, most) * 100, 2)}%`;
}

/**
 * How much of a period one row accounts for.
 *
 * A share too small to round to a whole per cent is written with a decimal instead. Further down
 * a long list every row would otherwise read as nought, which says nobody was there when
 * somebody was.
 */
function writeShare(part: number, whole: number, format: ReturnType<typeof useFormatter>): string {
  const share = shareOf(part, whole);

  return format.number(share, {
    style: 'percent',
    maximumFractionDigits: share > 0 && share < 0.01 ? 1 : 0,
  });
}

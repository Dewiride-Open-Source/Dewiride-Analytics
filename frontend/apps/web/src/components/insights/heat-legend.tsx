'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import type { MonthState, YearRow } from '@/lib/analytics/all-time';
import { HEAT_CELLS, HEAT_STEPS, LEFT_OUT_CELL } from '@/lib/analytics/heat';
import { cn } from '@/lib/styling';

/** A square of the size each key is drawn at. */
const SWATCH = 'size-3.5 shrink-0 rounded-[3px]';

interface HeatLegendProps {
  /** The record the table lays out, which decides which of the keys it needs. */
  readonly rows: readonly YearRow[];
}

/**
 * What the shades and the marks in a table of months mean.
 *
 * The scale always, and each other mark only where the table holds one, so nobody is told what a mark
 * means that they cannot see anywhere above it. Forced colours draw neither a shade nor a hatch, so the
 * scale and the hatched keys go with them. All of it is for somebody looking: every month already says
 * in words what it is, and words about marks a screen reader cannot reach would refer to nothing.
 */
export function HeatLegend({ rows }: HeatLegendProps) {
  const t = useTranslations('insights.months.legend');
  const chart = useTranslations('dashboard.chart');
  const states = new Set<MonthState>(rows.flatMap((row) => row.months.map((cell) => cell.state)));

  return (
    <div
      aria-hidden
      className="flex flex-wrap items-center gap-x-5 gap-y-2 text-xs text-foreground-muted"
    >
      <div className="flex items-center gap-1.5 forced-colors:hidden">
        <span>{t('fewer')}</span>
        <span className="flex gap-0.5">
          {HEAT_STEPS.map((step) => (
            <span key={step} className={cn(SWATCH, HEAT_CELLS[step])} />
          ))}
        </span>
        <span>{t('more')}</span>
      </div>
      {states.has('running') ? (
        <Key mark={<span className={cn(SWATCH, 'border-2 border-dashed border-current')} />}>
          {t('soFar')}
        </Key>
      ) : null}
      {states.has('before') ? <Key mark="–">{t('unmeasured')}</Key> : null}
      {states.has('notCounted') ? <HatchKey>{chart('notCounted')}</HatchKey> : null}
      {states.has('notJudged') ? <HatchKey>{chart('notFullyJudged')}</HatchKey> : null}
    </div>
  );
}

interface KeyProps {
  /** What the mark looks like in the table. */
  readonly mark: ReactNode;
  /** Anything the key needs beyond its own layout. */
  readonly className?: string;
  /** What it means. */
  readonly children: ReactNode;
}

/** One mark and what it means. */
function Key({ mark, className, children }: KeyProps) {
  return (
    <span className={cn('flex items-center gap-1.5', className)}>
      <span className="flex">{mark}</span>
      {children}
    </span>
  );
}

interface HatchKeyProps {
  /** What the hatching means. */
  readonly children: ReactNode;
}

/** The key to a month left out, which has no mark to point at where forced colours are in use. */
function HatchKey({ children }: HatchKeyProps) {
  return (
    <Key className="forced-colors:hidden" mark={<span className={cn(SWATCH, LEFT_OUT_CELL)} />}>
      {children}
    </Key>
  );
}

'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { NO_FIGURE } from '@/lib/analytics/all-time';
import { HEAT_CELLS, HEAT_STEPS, LEFT_OUT_CELL } from '@/lib/analytics/heat';
import { cn } from '@/lib/styling';

/** A square of the size each key is drawn at. */
const SWATCH = 'size-3.5 shrink-0 rounded-[3px]';

/**
 * The marks a table of shaded figures holds besides the shades, each named only where the table holds
 * one: what is still running, what comes before the figures begin, and what was left out.
 */
export interface HeatMarks {
  /** What the dashed edge round a figure still running means. */
  readonly soFar?: string;
  /**
   * What the mark standing in for a figure from before the record began means, once for each thing
   * such a figure in the table is called.
   */
  readonly before?: readonly string[];
  /** Whether the table holds a month that was never counted. */
  readonly notCounted?: boolean;
  /** Whether the table holds a month kept before its visits were all judged. */
  readonly notJudged?: boolean;
}

interface HeatLegendProps {
  /** The marks the table holds, which decide which of the keys it needs. */
  readonly marks: HeatMarks;
}

/**
 * What the shades and the marks in a table of shaded figures mean.
 *
 * The scale always, and each other mark only where the table holds one, so nobody is told what a mark
 * means that they cannot see anywhere above it. Forced colours draw neither a shade nor a hatch, so the
 * scale and the hatched keys go with them. All of it is for somebody looking: every figure already says
 * in words what it is, and words about marks a screen reader cannot reach would refer to nothing.
 */
export function HeatLegend({ marks }: HeatLegendProps) {
  const t = useTranslations('insights.months.legend');
  const chart = useTranslations('dashboard.chart');

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
      {marks.soFar === undefined ? null : (
        <Key mark={<span className={cn(SWATCH, 'border-2 border-dashed border-current')} />}>
          {marks.soFar}
        </Key>
      )}
      {marks.before?.map((meaning) => (
        <Key key={meaning} mark={NO_FIGURE}>
          {meaning}
        </Key>
      ))}
      {marks.notCounted ? <HatchKey>{chart('notCounted')}</HatchKey> : null}
      {marks.notJudged ? <HatchKey>{chart('notFullyJudged')}</HatchKey> : null}
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

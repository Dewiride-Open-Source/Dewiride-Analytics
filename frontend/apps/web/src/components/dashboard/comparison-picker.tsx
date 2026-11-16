'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useCallback, useId } from 'react';
import { DaysBeneath } from '@/components/dashboard/days-beneath';
import { NativeSelect } from '@/components/ui/native-select';
import type { ComparedWith } from '@/lib/analytics/compared-with';
import type { Comparison } from '@/lib/analytics/period';
import { writtenDays } from '@/lib/analytics/written-days';

interface ComparisonPickerProps {
  /** The earlier stretches the period on screen may be set beside, its own first. */
  readonly offered: readonly Comparison[];
  /** Which of them the figures are measured against. */
  readonly value: ComparedWith | null;
  readonly onChange: (against: ComparedWith) => void;
  /** The site's own zone, since the earlier days are a run of days there rather than where the reader is. */
  readonly timeZoneId: string;
  /**
   * Told when somebody reaches for the control — presses it, or works it from the keyboard — so a
   * screen can ask for the figures of every stretch it offers before one is chosen.
   */
  readonly onReach?: () => void;
}

/**
 * Names an earlier stretch the way the cards and this control say it — the same day last week,
 * yesterday, the same days 5 weeks earlier — and the chart's control too, wherever the line it draws
 * is that stretch.
 */
export function useAgainstWords(): (comparison: Comparison) => string {
  const metrics = useTranslations('dashboard.metrics');

  return useCallback(
    ({ words, days, weeks }: Comparison) => metrics(`against.${words}`, { days, weeks }),
    [metrics],
  );
}

/**
 * What the period on screen is compared with, beside the control that names the period.
 *
 * The browser's own list, as the period's is, offering every earlier stretch the period may be set
 * beside — the period's own first — each named the way the cards name it, with the days the one in
 * force covers printed beneath it, as the period's days are printed beneath the period. Where the
 * period is offered one stretch alone there is nothing to choose, and the same two lines are
 * written rather than offered: the reader still sees which days the figures are measured against.
 * Where it is offered none — this year, all time, a stretch whose earlier days are no longer kept —
 * nothing is shown at all, as the cards then say nothing of a change.
 */
export function ComparisonPicker({
  offered,
  value,
  onChange,
  timeZoneId,
  onReach,
}: ComparisonPickerProps) {
  const t = useTranslations('dashboard.compare');
  const called = useAgainstWords();
  const format = useFormatter();
  const daysId = useId();
  const inForce = offered.find((one) => one.against === value) ?? offered[0];

  if (inForce === undefined) {
    return null;
  }

  const days = writtenDays(inForce.span, timeZoneId, format);

  if (offered.length === 1) {
    return (
      <DaysBeneath id={daysId} days={days}>
        <p className="flex h-9 items-center text-sm font-medium text-foreground-muted">
          {t('option', { against: called(inForce) })}
        </p>
      </DaysBeneath>
    );
  }

  return (
    <DaysBeneath id={daysId} days={days}>
      <NativeSelect
        aria-label={t('label')}
        aria-describedby={daysId}
        value={inForce.against}
        onChange={(event) => {
          const chosen = offered.find((one) => one.against === event.target.value);

          if (chosen !== undefined) {
            onChange(chosen.against);
          }
        }}
        onReach={onReach}
        className="w-full sm:w-auto"
      >
        {offered.map((one) => (
          <option key={one.against} value={one.against}>
            {t('option', { against: called(one) })}
          </option>
        ))}
      </NativeSelect>
    </DaysBeneath>
  );
}

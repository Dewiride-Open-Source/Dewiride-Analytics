'use client';

import { useTranslations } from 'next-intl';
import { PERIODS, type PeriodDays } from '@/lib/analytics/period';
import { cn } from '@/lib/styling';

interface PeriodSwitchProps {
  readonly value: PeriodDays;
  readonly onChange: (days: PeriodDays) => void;
}

/** How long a period is called on screen. */
const NAMES: Record<PeriodDays, 'week' | 'month'> = { 7: 'week', 30: 'month' };

/**
 * How far back the screen is looking.
 *
 * A group of radio buttons rather than a dropdown: there are two choices, both worth showing, and
 * a control that answers in one tap beats one that answers in two.
 */
export function PeriodSwitch({ value, onChange }: PeriodSwitchProps) {
  const t = useTranslations('dashboard.period');

  return (
    <div
      role="radiogroup"
      aria-label={t('label')}
      className="inline-flex rounded-md border border-border bg-surface p-0.5"
    >
      {PERIODS.map((days) => {
        const chosen = days === value;

        return (
          <button
            key={days}
            type="button"
            role="radio"
            aria-checked={chosen}
            onClick={() => onChange(days)}
            className={cn(
              'rounded-sm px-3 py-1.5 text-sm font-medium transition-colors',
              'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
              chosen
                ? 'bg-accent-soft text-accent-strong'
                : 'text-foreground-muted hover:text-foreground',
            )}
          >
            {t(NAMES[days])}
          </button>
        );
      })}
    </div>
  );
}

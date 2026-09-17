'use client';

import { UserRound } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';

interface PopulationSwitchProps {
  /** Whether every figure on the screen is kept to the visits judged to be people. */
  readonly peopleOnly: boolean;
  readonly onChange: (peopleOnly: boolean) => void;
}

/**
 * The control that keeps a whole screen to the people a website is for.
 *
 * Beside the period rather than on the chart, because it changes what every figure on the screen
 * counts, as the period does. Green while it is on, because green is the tone a person is drawn in
 * everywhere else on the dashboard: pressed, it reads as the green band rather than as a second
 * accent beside the one the comparison wears.
 */
export function PopulationSwitch({ peopleOnly, onChange }: PopulationSwitchProps) {
  const t = useTranslations('dashboard.population');

  return (
    <Button
      tone="secondary"
      size="sm"
      aria-pressed={peopleOnly}
      aria-label={t('label')}
      onClick={() => onChange(!peopleOnly)}
      className={peopleOnly ? 'border-positive/40 bg-positive/12 text-positive' : undefined}
    >
      <UserRound aria-hidden className="size-4" />
      {t('action')}
    </Button>
  );
}

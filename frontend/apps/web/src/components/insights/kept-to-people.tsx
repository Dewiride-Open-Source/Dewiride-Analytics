'use client';

import { useTranslations } from 'next-intl';
import { sentences } from '@/lib/charts/use-left-out';

interface KeptToPeopleProps {
  /** Whether the figures are further behind than visits waiting their turn explains. */
  readonly fallenBehind: boolean;
}

/**
 * The one clause every figure below is read under, where a screen is kept to people. Said once,
 * here, rather than on each card.
 */
export function KeptToPeople({ fallenBehind }: KeptToPeopleProps) {
  const words = useTranslations('dashboard.population');
  const chart = useTranslations('dashboard.chart');

  return (
    <p className="-mt-2 text-sm text-foreground-muted">
      {sentences(words('caption'), fallenBehind ? chart('people.judging') : null)}
    </p>
  );
}

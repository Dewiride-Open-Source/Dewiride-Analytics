'use client';

import { useTranslations } from 'next-intl';
import { CATEGORY_TONES, type VerdictTone } from '@/lib/analytics/verdicts';
import type { TrafficCategory } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

/**
 * What a category looks like when it is named on a screen.
 *
 * Green for the people the website is for, violet for machinery, red for what nobody asked for,
 * and grey for what cannot be said. The colour is a summary and never the whole answer: the words
 * inside the pill always name the category exactly, so a crawler that says it is an AI one is
 * never mistaken for one that has been confirmed.
 */
const PILLS: Readonly<Record<VerdictTone, string>> = {
  people: 'border-positive/30 bg-positive/12 text-positive',
  automation: 'border-accent/40 bg-accent-soft text-accent-strong',
  unwanted: 'border-danger/35 bg-danger-soft text-danger',
  unclear: 'border-border bg-surface-muted text-foreground-muted',
};

/**
 * The shape every pill on the product wears, whatever it says.
 *
 * Exported so that a screen with something to mark that is not a category — a visitor still being
 * watched, on the screen about the present moment — marks it at exactly the same weight rather
 * than at one that happens to look similar. Two markers meaning "nothing can be said yet" drawn a
 * shade apart would read as two different kinds of answer.
 */
export const PILL =
  'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium';

/** The quiet one, for anything a category cannot be put to. */
export const QUIET_PILL = `${PILL} ${PILLS.unclear}`;

/** The same four tones as a solid fill, for the bar that shows how a period divides up. */
export const TONE_FILLS: Readonly<Record<VerdictTone, string>> = {
  people: 'bg-positive',
  automation: 'bg-accent',
  unwanted: 'bg-danger',
  unclear: 'bg-foreground-subtle',
};

/** What generated a visit, named. */
export function VerdictBadge({ category }: { readonly category: TrafficCategory }) {
  const t = useTranslations('verdicts.category');

  return <span className={cn(PILL, PILLS[CATEGORY_TONES[category]])}>{t(category)}</span>;
}

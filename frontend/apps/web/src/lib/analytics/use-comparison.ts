'use client';

import { parseAsStringLiteral } from 'nuqs';
import { useCallback } from 'react';
import { COMPARISON_KEY, COMPARISONS } from '@/lib/analytics/comparison';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * Whether the drawing carries the earlier days the period is set beside, carried in the address and
 * remembered by the browser.
 *
 * It changes what is drawn rather than how it looks, so it travels: somebody who found a rise by
 * setting two periods beside each other can send exactly that. Remembered as well, so somebody
 * who reads their weeks against the ones before finds the earlier days already drawn. Left off,
 * it says nothing in the address at all. Which earlier days are drawn is the cards' choice, made
 * beneath the period, and carried under a key of its own.
 */

/** Which of the closed set is in force, or nothing when this period is drawn alone. */
type Against = (typeof COMPARISONS)[number] | null;

const WRITTEN = parseAsStringLiteral(COMPARISONS)
  // A new entry in the history rather than a rewritten one. Putting the earlier period behind the
  // current one is a decision, and the way out of it should be the button somebody already has.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<Against>('dewiride.chart-against', oneOf(COMPARISONS), null);

export interface ChosenComparison {
  /** Whether the earlier days the period is set beside are drawn behind the one being read. */
  readonly against: boolean;
  readonly compare: (against: boolean) => void;
}

/** Reads whether the earlier period is being drawn, and writes the answer back. */
export function useComparison(): ChosenComparison {
  const [written, write] = useRememberedAddress<'before', Against>(
    COMPARISON_KEY,
    WRITTEN,
    REMEMBERED,
  );

  const compare = useCallback(
    (against: boolean) => {
      write(against ? 'before' : null);
    },
    [write],
  );

  return { against: written !== null, compare };
}

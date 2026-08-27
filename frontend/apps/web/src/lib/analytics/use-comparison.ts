'use client';

import { parseAsStringLiteral, useQueryState } from 'nuqs';
import { useCallback } from 'react';
import { COMPARISON_KEY, COMPARISONS } from '@/lib/analytics/comparison';

/**
 * Whether the drawing carries the period before it, kept in the address itself.
 *
 * It changes what is drawn rather than how it looks, so it travels: somebody who found a rise by
 * setting two periods beside each other can send exactly that. Left off, it says nothing in the
 * address at all.
 */
const WRITTEN = parseAsStringLiteral(COMPARISONS)
  // A new entry in the history rather than a rewritten one. Putting the earlier period behind the
  // current one is a decision, and the way out of it should be the button somebody already has.
  .withOptions({ history: 'push' });

export interface ChosenComparison {
  /** Whether the period before is drawn behind the one being read. */
  readonly against: boolean;
  readonly compare: (against: boolean) => void;
}

/** Reads whether the earlier period is being drawn, and writes the answer back. */
export function useComparison(): ChosenComparison {
  const [written, write] = useQueryState(COMPARISON_KEY, WRITTEN);

  const compare = useCallback(
    (against: boolean) => {
      void write(against ? 'before' : null);
    },
    [write],
  );

  return { against: written !== null, compare };
}

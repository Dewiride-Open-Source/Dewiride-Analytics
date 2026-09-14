'use client';

import { parseAsStringLiteral } from 'nuqs';
import { useCallback } from 'react';
import { PEOPLE, PEOPLE_KEY } from '@/lib/analytics/people-only';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * Whether the picture is kept to people, carried in the address and remembered by the browser.
 *
 * It changes what is drawn rather than how it looks, so it travels: somebody who found a rise in
 * their readers can send exactly that. Remembered as well, so the next visit to the overview opens
 * on it. Left off, it says nothing in the address at all.
 */

/** Which of the closed set is in force, or nothing when everybody is drawn. */
type Kept = (typeof PEOPLE)[number] | null;

const WRITTEN = parseAsStringLiteral(PEOPLE)
  // A new entry in the history rather than a rewritten one. Keeping the picture to people is a
  // decision, and the way out of it should be the button somebody already has.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<Kept>('dewiride.chart-people', oneOf(PEOPLE), null);

export interface ChosenPeople {
  /** Whether only the visits judged to be people are drawn. */
  readonly peopleOnly: boolean;
  readonly showOnlyPeople: (peopleOnly: boolean) => void;
}

/** Reads whether the picture is kept to people, and writes the answer back. */
export function usePeopleOnly(): ChosenPeople {
  const [kept, keep] = useRememberedAddress<'people', Kept>(PEOPLE_KEY, WRITTEN, REMEMBERED);

  const showOnlyPeople = useCallback(
    (peopleOnly: boolean) => {
      keep(peopleOnly ? 'people' : null);
    },
    [keep],
  );

  return { peopleOnly: kept !== null, showOnlyPeople };
}

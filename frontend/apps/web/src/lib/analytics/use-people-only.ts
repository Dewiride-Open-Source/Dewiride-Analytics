'use client';

import { parseAsStringLiteral } from 'nuqs';
import { useCallback } from 'react';
import { PEOPLE, PEOPLE_KEY, type Population } from '@/lib/analytics/people-only';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * Whether a screen is kept to people, carried in the address and remembered by the browser.
 *
 * It changes what every figure counts rather than how anything looks, so it travels: somebody who
 * found a rise in their readers can send exactly that. Remembered as well, so the next visit to
 * either screen about a period opens on it. Left off, it says nothing in the address at all.
 */

/** Which of the closed set is in force, or nothing when everybody is counted. */
type Kept = (typeof PEOPLE)[number] | null;

const WRITTEN = parseAsStringLiteral(PEOPLE)
  // A new entry in the history rather than a rewritten one. Keeping a screen to people is a
  // decision, and the way out of it should be the button somebody already has.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<Kept>('dewiride.population', oneOf(PEOPLE), null);

export interface PeopleOptions {
  /**
   * Whether a remembered population is written into an address that says nothing.
   *
   * Asked for by the two screens the population is the subject of, and by nothing else: the bar
   * across the top reads it on every screen so its links can carry it, and must not write it into
   * the address of a screen it means nothing on.
   */
  readonly seeding?: boolean;
}

export interface ChosenPeople {
  /** Whether every figure is kept to the visits judged to be people. */
  readonly peopleOnly: boolean;
  /** The same, as the word every question is asked with. */
  readonly population: Population;
  readonly showOnlyPeople: (peopleOnly: boolean) => void;
}

/** Reads whether the screen is kept to people, and writes the answer back. */
export function usePeopleOnly({ seeding = false }: PeopleOptions = {}): ChosenPeople {
  const [kept, keep] = useRememberedAddress<'people', Kept>(
    PEOPLE_KEY,
    WRITTEN,
    REMEMBERED,
    seeding,
  );

  const showOnlyPeople = useCallback(
    (peopleOnly: boolean) => {
      keep(peopleOnly ? 'people' : null);
    },
    [keep],
  );

  return { peopleOnly: kept !== null, population: kept ?? 'everybody', showOnlyPeople };
}

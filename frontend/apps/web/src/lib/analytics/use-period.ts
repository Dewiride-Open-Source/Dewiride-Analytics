'use client';

import { createParser, useQueryState } from 'nuqs';
import {
  DEFAULT_PERIOD,
  type Period,
  PERIOD_KEY,
  readPeriod,
  samePeriod,
  writePeriod,
} from '@/lib/analytics/period';

/**
 * The period a screen is on, kept in the address itself.
 *
 * Held there rather than on the screen for three things it gives at once: a link somebody sends
 * opens on what they were looking at, the browser's own way back undoes a change to it, and two
 * screens about the same website agree without either telling the other. None of the three works
 * if the period is state a screen owns, because a screen is thrown away when somebody leaves it.
 *
 * Which website is being looked at deliberately does not travel this way. That is a property of
 * the machine somebody is sitting at rather than of what they are looking at, so it stays where
 * the browser keeps it.
 */
const WRITTEN = createParser<Period>({
  parse: readPeriod,
  serialize: writePeriod,
  // Without this, every period read back would differ from the one every screen opens on — they
  // are never the same object — and the address would never be free of it.
  eq: samePeriod,
})
  .withDefault(DEFAULT_PERIOD)
  // A new entry in the history rather than a rewritten one, so that the way back from a period
  // somebody picked by mistake is the button they already reach for.
  .withOptions({ history: 'push' });

export interface ChosenPeriod {
  /** The period being looked at, which is the one everything opens on until somebody says otherwise. */
  readonly period: Period;
  readonly choose: (period: Period) => void;
}

/** Reads the period out of the address, and writes one back into it. */
export function usePeriod(): ChosenPeriod {
  const [period, choose] = useQueryState(PERIOD_KEY, WRITTEN);

  return { period, choose };
}

'use client';

import { createParser } from 'nuqs';
import {
  DEFAULT_PERIOD,
  type Period,
  PERIOD_KEY,
  readPeriod,
  samePeriod,
  writePeriod,
} from '@/lib/analytics/period';
import { remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * The period a screen is on, carried in the address and remembered by the browser.
 *
 * Held in the address for three things it gives at once: a link somebody sends opens on what they
 * were looking at, the browser's own way back undoes a change to it, and two screens about the
 * same website agree without either telling the other. None of the three works if the period is
 * state a screen owns, because a screen is thrown away when somebody leaves it.
 *
 * Remembered as well, because the stretch of days somebody reads their website over is a habit
 * rather than a decision made afresh each morning, and an address that says nothing opens on it.
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
  // A new entry in the history rather than a rewritten one, so that the way back from a period
  // somebody picked by mistake is the button they already reach for.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<Period>('dewiride.period', readPeriod, DEFAULT_PERIOD, writePeriod);

export interface PeriodOptions {
  /**
   * Whether a remembered period is written into an address that says nothing.
   *
   * Asked for by the screens the period is the subject of, and by nothing else: the bar across
   * the top reads the period on every screen, including the ones with no stretch of days to ask
   * about, and must not write one into their address.
   */
  readonly seeding?: boolean;
}

export interface ChosenPeriod {
  /** The period being looked at, which is the one everything opens on until somebody says otherwise. */
  readonly period: Period;
  readonly choose: (period: Period) => void;
}

/** Reads the period out of the address, or out of what was remembered, and writes one back. */
export function usePeriod({ seeding = false }: PeriodOptions = {}): ChosenPeriod {
  const [period, choose] = useRememberedAddress(PERIOD_KEY, WRITTEN, REMEMBERED, seeding);

  return { period, choose };
}

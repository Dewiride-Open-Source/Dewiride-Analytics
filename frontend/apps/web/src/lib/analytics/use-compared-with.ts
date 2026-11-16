'use client';

import { parseAsStringLiteral } from 'nuqs';
import { useCallback } from 'react';
import {
  CHOSEN_EARLIER_DAYS,
  type ChosenEarlierDays,
  COMPARED_WITH_KEY,
  type ComparedWith,
} from '@/lib/analytics/compared-with';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * Which earlier days somebody chose to set the period beside, carried in the address and
 * remembered by the browser.
 *
 * It changes what the figures are measured against rather than how they look, so it travels: a day
 * somebody read against yesterday opens against yesterday for whoever they send it to, and the
 * browser's own way back undoes the choice. Remembered as well, so somebody who reads every morning
 * against the morning before finds it that way.
 *
 * Nothing chosen is the ordinary state, and means each period is set beside the first stretch it is
 * offered: its own, wherever that is still kept. A period that does not offer what somebody chose is
 * set beside that first stretch too, without the choice being forgotten: it comes back as soon as a
 * period that offers it is on screen again.
 *
 * Read by the overview alone. The other screens set nothing beside their period, and a link from
 * the overview to one of them leaves the choice behind.
 */
const WRITTEN = parseAsStringLiteral(CHOSEN_EARLIER_DAYS)
  // A new entry in the history rather than a rewritten one. Setting the period beside other days is
  // a decision, and the way back from it should be the button somebody already reaches for.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<ChosenEarlierDays | null>(
  'dewiride.compared-with',
  oneOf(CHOSEN_EARLIER_DAYS),
  null,
);

export interface EarlierDaysChoice {
  /** The earlier days somebody chose, or nothing where each period is set beside its first. */
  readonly comparedWith: ChosenEarlierDays | null;
  /**
   * Records a choice. The period's own stretch is recorded as nothing chosen, which also leaves
   * the address plain.
   */
  readonly compareWith: (against: ComparedWith) => void;
}

/** Reads which earlier days the period is set beside out of the address, or out of what was remembered. */
export function useComparedWith(): EarlierDaysChoice {
  const [comparedWith, write] = useRememberedAddress<ChosenEarlierDays, ChosenEarlierDays | null>(
    COMPARED_WITH_KEY,
    WRITTEN,
    REMEMBERED,
  );

  const compareWith = useCallback(
    (against: ComparedWith) => {
      write(against === 'before' ? null : against);
    },
    [write],
  );

  return { comparedWith, compareWith };
}

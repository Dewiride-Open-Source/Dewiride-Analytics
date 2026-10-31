'use client';

import { parseAsStringLiteral } from 'nuqs';
import { BY_KEY, CHART_BYS, type ChartBy } from '@/lib/analytics/chart-by';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * How somebody chose to cut the picture, carried in the address and remembered by the browser.
 *
 * It changes what is drawn rather than how it looks, so it travels: a quarter somebody read week by
 * week opens week by week for whoever they send it to, and the browser's own way back undoes the
 * choice. Remembered as well, so somebody who reads their quarters by the week finds them that way.
 *
 * Nothing chosen is the ordinary state, and means each period is drawn its own way. A period too
 * short for the way somebody chose is drawn its own way too, without the choice being forgotten:
 * it comes back as soon as the period is long enough for it again.
 */
const WRITTEN = parseAsStringLiteral(CHART_BYS)
  // A new entry in the history rather than a rewritten one. Cutting the picture another way is a
  // decision, and the way back from it should be the button somebody already reaches for.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<ChartBy | null>('dewiride.chart-by', oneOf(CHART_BYS), null);

export interface ChosenCut {
  /** The way somebody chose, or nothing where each period is drawn its own way. */
  readonly by: ChartBy | null;
  /** Records a way, or nothing to leave it to the period — which also leaves the address plain. */
  readonly showBy: (by: ChartBy | null) => void;
}

/** Reads the way the picture is cut out of the address, or out of what was remembered. */
export function useChartBy(): ChosenCut {
  const [by, showBy] = useRememberedAddress<ChartBy, ChartBy | null>(BY_KEY, WRITTEN, REMEMBERED);

  return { by, showBy };
}

'use client';

import { parseAsStringLiteral } from 'nuqs';
import { CHART_VIEWS, type ChartView, DEFAULT_VIEW, VIEW_KEY } from '@/lib/analytics/chart-view';
import { oneOf, remembered } from '@/lib/preferences/remembered';
import { useRememberedAddress } from '@/lib/preferences/use-remembered-address';

/**
 * Which view the picture is on, carried in the address and remembered by the browser.
 *
 * It changes what is drawn rather than how it looks, so it travels: a link somebody sends opens
 * on the view they were reading, and the browser's own way back undoes a switch. Remembered as
 * well, so somebody who reads how much rather than who finds the picture on it. The default is
 * left out of the address altogether, so the plain address of the overview stays plain.
 */
const WRITTEN = parseAsStringLiteral(CHART_VIEWS)
  // A new entry in the history rather than a rewritten one. Switching views is one decision, and
  // the way back from it should be the button somebody already reaches for.
  .withOptions({ history: 'push' });

const REMEMBERED = remembered<ChartView>('dewiride.chart-view', oneOf(CHART_VIEWS), DEFAULT_VIEW);

export interface ChosenView {
  /** The view being drawn, which is the one the screen opens on until somebody says otherwise. */
  readonly view: ChartView;
  readonly show: (view: ChartView) => void;
}

/** Reads the view out of the address, or out of what was remembered, and writes one back. */
export function useChartView(): ChosenView {
  const [view, show] = useRememberedAddress(VIEW_KEY, WRITTEN, REMEMBERED);

  return { view, show };
}

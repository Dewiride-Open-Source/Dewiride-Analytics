'use client';

import { parseAsStringLiteral, useQueryState } from 'nuqs';
import { CHART_VIEWS, type ChartView, DEFAULT_VIEW, VIEW_KEY } from '@/lib/analytics/chart-view';

/**
 * Which view the picture is on, kept in the address itself.
 *
 * It changes what is drawn rather than how it looks, so it travels: a link somebody sends opens
 * on the view they were reading, and the browser's own way back undoes a switch. The default is
 * left out of the address altogether, so the plain address of the overview stays plain.
 */
const WRITTEN = parseAsStringLiteral(CHART_VIEWS)
  .withDefault(DEFAULT_VIEW)
  // A new entry in the history rather than a rewritten one. Switching views is one decision, and
  // the way back from it should be the button somebody already reaches for.
  .withOptions({ history: 'push' });

export interface ChosenView {
  /** The view being drawn, which is the one the screen opens on until somebody says otherwise. */
  readonly view: ChartView;
  readonly show: (view: ChartView) => void;
}

/** Reads the view out of the address, and writes one back into it. */
export function useChartView(): ChosenView {
  const [view, show] = useQueryState(VIEW_KEY, WRITTEN);

  return { view, show };
}

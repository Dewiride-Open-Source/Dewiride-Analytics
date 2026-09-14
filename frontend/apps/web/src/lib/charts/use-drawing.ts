'use client';

import { DEFAULT_DRAWING, type Drawing, DRAWINGS } from '@/lib/charts/drawing';
import { oneOf, remembered, useRemembered } from '@/lib/preferences/remembered';

/**
 * How somebody likes their charts drawn, remembered by the browser they read them in.
 *
 * Not in the address. It changes nothing about what is being looked at — the same counts over
 * the same days — so a link somebody sends should open on the reader's own preference rather
 * than overriding it.
 */
const REMEMBERED = remembered<Drawing>('dewiride.chart-drawing', oneOf(DRAWINGS), DEFAULT_DRAWING);

export interface ChosenDrawing {
  /** The style to draw in. */
  readonly drawing: Drawing;
  readonly choose: (drawing: Drawing) => void;
}

/** Reads the remembered drawing style, and records a new one. */
export function useDrawing(): ChosenDrawing {
  const [drawing, choose] = useRemembered(REMEMBERED);

  return { drawing, choose };
}

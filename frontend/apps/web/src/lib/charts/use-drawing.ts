'use client';

import { useSyncExternalStore } from 'react';
import { DEFAULT_DRAWING, type Drawing, DRAWINGS } from '@/lib/charts/drawing';

/**
 * How somebody likes their charts drawn, remembered by the browser they read them in.
 *
 * Not in the address, and not on the account. It changes nothing about what is being looked at —
 * the same counts over the same days — so a link somebody sends should open on the reader's own
 * preference rather than overriding it, and the machine they read on is where a preference about
 * appearance belongs.
 *
 * Read as an external store rather than copied into state, so that the server and the browser can
 * agree on a first paint and the browser then corrects it, and so that a second tab which changes
 * the style tells the first.
 */

/** Where the last choice is written. */
const REMEMBERED = 'dewiride.chart-drawing';

const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  window.addEventListener('storage', listener);

  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', listener);
  };
}

/** What the browser has recorded, or the usual one where it holds nothing this reader wrote. */
function readChoice(): Drawing {
  const written = window.localStorage.getItem(REMEMBERED);

  return DRAWINGS.find((one) => one === written) ?? DEFAULT_DRAWING;
}

/** What the server has to assume, so that both sides draw the same thing first. */
function assumeUsual(): Drawing {
  return DEFAULT_DRAWING;
}

/** Records a choice, and tells every open tab about it. */
function choose(drawing: Drawing): void {
  window.localStorage.setItem(REMEMBERED, drawing);

  for (const listener of listeners) {
    listener();
  }
}

export interface ChosenDrawing {
  /** The style to draw in. */
  readonly drawing: Drawing;
  readonly choose: (drawing: Drawing) => void;
}

/** Reads the remembered drawing style, and records a new one. */
export function useDrawing(): ChosenDrawing {
  const drawing = useSyncExternalStore(subscribe, readChoice, assumeUsual);

  return { drawing, choose };
}

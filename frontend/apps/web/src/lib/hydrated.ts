'use client';

import { useSyncExternalStore } from 'react';

/** Nothing here ever changes, so a subscriber is never called back. */
const never = () => () => {};

/**
 * Whether this component is now running in the browser rather than being rendered on the server.
 *
 * Some things can only be known in a browser — which appearance somebody chose, which time zone
 * their device is set to — and drawing a guess at them on the server and the truth in the browser
 * produces two different first paints, which React refuses to reconcile. Asking this first means
 * both sides agree on the first paint and only the browser draws the second.
 */
export function useHydrated(): boolean {
  return useSyncExternalStore(
    never,
    () => true,
    () => false,
  );
}

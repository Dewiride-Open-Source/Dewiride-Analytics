'use client';

import { useSyncExternalStore } from 'react';
import type { Site } from '@/lib/api/schemas';

/**
 * Which website the dashboard is showing, and remembering it.
 *
 * Kept in the browser rather than on the account: which website somebody was last looking at is a
 * property of the machine they were looking on, not of who they are, and storing it centrally
 * would make one person's tab change what another sees.
 *
 * Read as an external store rather than copied into state, for two reasons. Nothing is known about
 * the browser's storage while a page is being rendered on the server, and this is the one shape
 * that lets the server and the browser agree on a first paint and then let the browser correct it.
 * And a second tab that changes websites tells the first, because the browser announces the write.
 */

/** Where the last choice is written. */
const REMEMBERED = 'dewiride.chosen-site';

const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  window.addEventListener('storage', listener);

  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', listener);
  };
}

/** What the browser has recorded, or nothing when it has never been asked. */
function readChoice(): string | null {
  return window.localStorage.getItem(REMEMBERED);
}

/**
 * What the server has to assume.
 *
 * Always nothing, so that the first website is what gets drawn on both sides and the browser
 * corrects it once it is running. A guess here would be two different first paints.
 */
function assumeNothing(): null {
  return null;
}

/** Records a choice, and tells every open tab about it. */
function choose(siteId: string): void {
  window.localStorage.setItem(REMEMBERED, siteId);

  for (const listener of listeners) {
    listener();
  }
}

export interface ChosenSite {
  /** The website to show, or nothing while the list is still on its way. */
  readonly site: Site | undefined;
  readonly choose: (siteId: string) => void;
}

/**
 * Resolves which of the caller's websites to show.
 *
 * Falls back to the first one whenever the remembered choice names a website this account can no
 * longer see, so a website that was removed or transferred leaves somebody on a working screen
 * rather than an empty one.
 *
 * @param sites Every website the signed-in person may look at.
 */
export function useChosenSite(sites: readonly Site[] | undefined): ChosenSite {
  const chosenId = useSyncExternalStore(subscribe, readChoice, assumeNothing);

  return { site: sites?.find((one) => one.id === chosenId) ?? sites?.[0], choose };
}

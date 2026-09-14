'use client';

import type { Site } from '@/lib/api/schemas';
import { remembered, useRemembered } from '@/lib/preferences/remembered';

/**
 * Which website the dashboard is showing, remembered by the browser it is shown in.
 *
 * Not in the address. A colleague following a link may not be able to see that website at all,
 * and the only spelling available for it is an identifier that would then be on screen in the
 * address bar. Nothing is assumed until the browser has answered, so that the first website is
 * what gets drawn on both sides and the browser corrects it once it is running.
 */
const REMEMBERED = remembered<string | null>('dewiride.chosen-site', (raw) => raw, null);

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
  const [chosenId, choose] = useRemembered(REMEMBERED);

  return { site: sites?.find((one) => one.id === chosenId) ?? sites?.[0], choose };
}

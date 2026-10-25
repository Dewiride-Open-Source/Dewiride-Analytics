'use client';

import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { readSiteSettings, removeSite, updateSiteSettings } from '@/lib/api/endpoints';
import type { Session, SiteSettings } from '@/lib/api/schemas';
import { siteSettingsKey, sitesKey } from './keys';
import { sessionKey } from './session';

/** Everything about one website that its owner decides. */
export function useSiteSettings(siteId: string, enabled: boolean) {
  return useQuery({
    queryKey: siteSettingsKey(siteId),
    queryFn: () => readSiteSettings(siteId),
    retry: false,
    enabled,

    // Never held. The settings panel has to open on the setting in force rather than the one
    // somebody last saw, and the clicks card has to say whether clicks are being recorded now. It is
    // one small row, read when the panel opens and whenever the clicks card is drawn afresh — on
    // each change of period or of whose figures it counts — and never on a timer.
    staleTime: 0,
  });
}

/**
 * Changes one website's settings.
 *
 * The answer carries the settings as they now stand, and is written straight into the cache so the
 * panel settles on what the engine did rather than on what it was asked to do.
 *
 * The list of websites is asked for again as well, and only the list: it carries the name and the
 * zone too, so without this the heading and the picker across the top would go on calling a
 * website what it used to be called, and the screen would go on cutting its days where they used
 * to fall. Asked for exactly rather than by prefix, so that renaming a website does not send every
 * question already answered about it round again.
 */
export function useUpdateSiteSettings(siteId: string) {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: (settings: Partial<SiteSettings>) =>
      updateSiteSettings(siteId, settings, proofFrom(cache)),
    onSuccess: (settings) => {
      cache.setQueryData(siteSettingsKey(siteId), settings);
      void cache.invalidateQueries({ queryKey: sitesKey, exact: true });
    },
  });
}

/**
 * Stops measuring a website altogether.
 *
 * The list of websites is asked for again rather than patched, because the bar across the top and
 * the screen beneath it both go on naming a website that no longer exists until it is. Exactly the
 * list and nothing under it: asking by prefix would send every question already answered about the
 * removed website round again, and each of them would come back refused on a screen that is about
 * to be replaced anyway. Its settings are dropped only where nothing on the screen is reading them:
 * a copy the clicks card is still drawing from would be asked for again the moment it went, and
 * come back refused. That copy goes with the screen.
 */
export function useRemoveSite(siteId: string) {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: () => removeSite(siteId, proofFrom(cache)),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: sitesKey, exact: true });
      cache.removeQueries({ queryKey: siteSettingsKey(siteId), type: 'inactive' });
    },
  });
}

/**
 * The proof-of-origin value the engine last issued.
 *
 * Read at the moment of use rather than held, because it belongs to the identity it was issued to
 * and a fresh one arrives with every answer that changes who is signed in.
 */
function proofFrom(cache: QueryClient): string {
  const proof = cache.getQueryData<Session>(sessionKey)?.token;

  if (!proof) {
    throw new Error('No session has been read yet, so nothing can be submitted.');
  }

  return proof;
}

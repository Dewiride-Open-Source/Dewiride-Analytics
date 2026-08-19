'use client';

import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { readSiteSettings, updateSiteSettings } from '@/lib/api/endpoints';
import type { Session, SiteSettings } from '@/lib/api/schemas';
import { siteSettingsKey } from './keys';
import { sessionKey } from './session';

/** What a website collects. */
export function useSiteSettings(siteId: string, enabled: boolean) {
  return useQuery({
    queryKey: siteSettingsKey(siteId),
    queryFn: () => readSiteSettings(siteId),
    retry: false,
    enabled,

    // Never held. A panel that opens on the setting somebody last saw rather than the one that is
    // in force would be worse than a panel that takes a moment, and this is read when a panel is
    // opened rather than repeatedly.
    staleTime: 0,
  });
}

/**
 * Changes what a website collects.
 *
 * The answer carries the settings as they now stand, and is written straight into the cache so the
 * switch on the screen settles on what the engine did rather than on what it was asked to do.
 */
export function useUpdateSiteSettings(siteId: string) {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: (settings: Partial<SiteSettings>) =>
      updateSiteSettings(siteId, settings, proofFrom(cache)),
    onSuccess: (settings) => {
      cache.setQueryData(siteSettingsKey(siteId), settings);
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

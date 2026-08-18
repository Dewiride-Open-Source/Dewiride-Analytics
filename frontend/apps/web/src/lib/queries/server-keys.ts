'use client';

import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createServerKey, listServerKeys, revokeServerKey } from '@/lib/api/endpoints';
import type { Session } from '@/lib/api/schemas';
import { serverKeysKey } from './keys';
import { sessionKey } from './session';

/** The keys a website's own server may report with. */
export function useServerKeys(siteId: string, enabled: boolean) {
  return useQuery({
    queryKey: serverKeysKey(siteId),
    queryFn: () => listServerKeys(siteId),
    retry: false,
    enabled,

    // Never held. A list that shows a key somebody has just taken away is worse than a list that
    // takes a moment to arrive, and this is read when a panel is opened rather than repeatedly.
    staleTime: 0,
  });
}

export function useCreateServerKey(siteId: string) {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => createServerKey(siteId, name, proofFrom(cache)),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: serverKeysKey(siteId) });
    },
  });
}

export function useRevokeServerKey(siteId: string) {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: (keyId: string) => revokeServerKey(siteId, keyId, proofFrom(cache)),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: serverKeysKey(siteId) });
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

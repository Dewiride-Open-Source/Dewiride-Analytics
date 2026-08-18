'use client';

import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  claimInstall,
  type Credentials,
  describeSession,
  type InstallationDetails,
  signIn,
  signOut,
} from '@/lib/api/endpoints';
import type { Session } from '@/lib/api/schemas';
import { sitesKey } from './keys';

export const sessionKey = ['session'] as const;

/**
 * Who is here, and whether this install has an owner yet.
 *
 * Every screen waits on this before deciding what to draw, so it is deliberately not retried: a
 * stopped engine should say so at once rather than after three silent attempts.
 */
export function useSession() {
  return useQuery({
    queryKey: sessionKey,
    queryFn: describeSession,
    retry: false,
    staleTime: 60_000,
  });
}

export function useSignIn() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (credentials: Credentials) => signIn(credentials, proofFrom(cache)),
    onSuccess: (session) => {
      cache.setQueryData(sessionKey, session);
      void cache.invalidateQueries({ queryKey: sitesKey });
    },
  });
}

export function useSignOut() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async () => signOut(proofFrom(cache)),
    onSuccess: (session) => {
      cache.setQueryData(sessionKey, session);
      // Removed rather than marked stale: leaving one person's list of websites in memory while
      // the next person signs in is how the wrong name appears for a moment on a shared machine.
      cache.removeQueries({ queryKey: sitesKey });
    },
  });
}

export function useClaimInstall() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (details: InstallationDetails) => claimInstall(details, proofFrom(cache)),
    onSuccess: (installation) => {
      // Claiming an install signs the new owner in, so what is known about the session afterwards
      // is written straight from the answer instead of being asked for again.
      cache.setQueryData<Session>(sessionKey, {
        setupCompleted: true,
        user: installation.user,
        token: installation.token,
      });
      void cache.invalidateQueries({ queryKey: sitesKey });
    },
  });
}

/**
 * The proof-of-origin value the engine last issued.
 *
 * It belongs to the identity it was issued to and a new one arrives with every answer that
 * changes who is signed in, so it is read at the moment of use rather than held in a component.
 */
function proofFrom(cache: QueryClient): string {
  const proof = cache.getQueryData<Session>(sessionKey)?.token;

  if (!proof) {
    throw new Error('No session has been read yet, so nothing can be submitted.');
  }

  return proof;
}

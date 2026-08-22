'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  type Acceptance,
  acceptInvitation,
  changePassword,
  type PasswordChange,
  previewInvitation,
  renameAccount,
} from '@/lib/api/endpoints';
import type { Session } from '@/lib/api/schemas';
import { organizationKey, sitesKey } from './keys';
import { proofFrom, sessionKey } from './session';

/**
 * Changes the name the signed-in person is shown under.
 *
 * What is known about the session is written straight from the answer, because the name in the bar
 * across the top comes from there and re-reading it would leave the old one on screen for as long
 * as the round trip took.
 */
export function useRenameAccount() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (displayName: string) => renameAccount(displayName, proofFrom(cache)),
    onSuccess: (user) => {
      cache.setQueryData<Session>(sessionKey, (session) =>
        session ? { ...session, user } : session,
      );
      void cache.invalidateQueries({ queryKey: organizationKey });
    },
  });
}

/**
 * Replaces the signed-in person's password.
 *
 * The engine renews this device's sign-in as it answers and ends every other one, so there is
 * nothing to put in the cache and nobody is sent back to the sign-in screen.
 */
export function useChangePassword() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (change: PasswordChange) => changePassword(change, proofFrom(cache)),
  });
}

/**
 * Reads what an invitation is for.
 *
 * Held for as long as the screen is open rather than re-read: an invitation does not change while
 * somebody is filling the form in, and asking twice would only be a second chance to fail. It is
 * not retried either — a link that will not do says so at once rather than after three silent
 * attempts.
 *
 * @param token The secret from the link.
 * @param enabled Whether a session has been read yet, which is what carries proof of origin.
 */
export function usePreviewInvitation(token: string, enabled: boolean) {
  const cache = useQueryClient();

  return useQuery({
    queryKey: ['invitation', token],
    queryFn: () => previewInvitation(token, proofFrom(cache)),
    enabled: enabled && token.length > 0,
    retry: false,
    staleTime: Number.POSITIVE_INFINITY,
  });
}

/**
 * Takes an invitation up.
 *
 * Somebody who has just chosen a password is signed in by the engine as it answers, so what is
 * known about the session is written from the answer and the lists that depend on who is here are
 * read again. Somebody who already had an account is not signed in, and nothing changes here.
 */
export function useAcceptInvitation() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (acceptance: Acceptance) => acceptInvitation(acceptance, proofFrom(cache)),
    onSuccess: (join) => {
      if (!join.signedIn) {
        return;
      }

      cache.setQueryData<Session>(sessionKey, {
        setupCompleted: true,
        user: join.user,
        token: join.token,
      });
      void cache.invalidateQueries({ queryKey: sitesKey });
      void cache.invalidateQueries({ queryKey: organizationKey });
    },
  });
}

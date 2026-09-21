'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  type Acceptance,
  acceptInvitation,
  changePassword,
  closeAccount,
  type PasswordChange,
  previewInvitation,
  renameAccount,
  restoreAccount,
} from '@/lib/api/endpoints';
import { ApiError } from '@/lib/api/problem';
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
 * read again. Somebody who already had an account and is signed in on this device may have just
 * gained somewhere else to be — the standing goes to the account the invitation was sent to, which
 * is usually but not always theirs — so what is known about them is read again before the gate
 * decides where that is, which matters most to somebody whose only other account was closed.
 * Somebody who is not signed in at all changes nothing here.
 */
export function useAcceptInvitation() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (acceptance: Acceptance) => acceptInvitation(acceptance, proofFrom(cache)),
    onSuccess: async (join) => {
      if (join.signedIn) {
        cache.setQueryData<Session>(sessionKey, {
          setupCompleted: true,
          user: join.user,
          token: join.token,
          closure: null,
        });
      } else if (cache.getQueryData<Session>(sessionKey)?.user) {
        await cache.invalidateQueries({ queryKey: sessionKey });
      } else {
        return;
      }

      void cache.invalidateQueries({ queryKey: sitesKey });
      void cache.invalidateQueries({ queryKey: organizationKey });
    },
  });
}

/**
 * Closes the account the signed-in person runs.
 *
 * The session is written from the answer, because what it now says about the closed account is
 * what moves the reader to the screen that explains it. The websites and the account itself are
 * taken out of the cache rather than marked stale: nothing about a closed account can be read,
 * and a screen that briefly showed the old list before an empty one arrived would be showing
 * something the person had just been told they could no longer see. Everything else read about
 * the account — an edition's plan, say — is marked stale, because closing changes what is true of
 * all of it.
 *
 * A refusal because somebody else closed it a moment earlier is answered by reading the session
 * again — the account is closed either way, and the gate moves the reader from that.
 */
export function useCloseAccount() {
  const cache = useQueryClient();

  function forget() {
    cache.removeQueries({ queryKey: sitesKey });
    cache.removeQueries({ queryKey: organizationKey });
    void cache.invalidateQueries({ predicate: aboutTheAccount });
  }

  return useMutation({
    mutationFn: async () => closeAccount(proofFrom(cache)),
    onSuccess: (session) => {
      cache.setQueryData(sessionKey, session);
      forget();
    },
    onError: (error) => settleFrom(cache, error, forget),
  });
}

/**
 * Brings a closed account back.
 *
 * The session is written from the answer and everything read about the account is read again,
 * so the websites reappear as the reader is moved back to them and an edition's plan says what
 * bringing the account back did to it. A refusal because the account is already open — somebody
 * else brought it back first — is answered by reading the session again, which is what moves the
 * reader off the closed screen.
 */
export function useRestoreAccount() {
  const cache = useQueryClient();

  function reread() {
    void cache.invalidateQueries({ predicate: aboutTheAccount });
  }

  return useMutation({
    mutationFn: async () => restoreAccount(proofFrom(cache)),
    onSuccess: (session) => {
      cache.setQueryData(sessionKey, session);
      reread();
    },
    onError: (error) => settleFrom(cache, error, reread),
  });
}

/**
 * Every cached answer except the session itself.
 *
 * Closing or bringing back an account changes what is true of everything the dashboard has read
 * about it, in whichever edition. The session is left out because the act has just written it
 * from the engine's own answer, and marking it stale would only ask the same question again.
 */
function aboutTheAccount(query: { readonly queryKey: readonly unknown[] }): boolean {
  return query.queryKey[0] !== sessionKey[0];
}

/**
 * Settles the cache after a refusal that means the act has already happened.
 *
 * Closing an account somebody else has just closed and restoring one somebody else has just
 * restored are both answered by the engine as a conflict. In either case the account is in the
 * state the reader wanted, so the cache is settled exactly as it would have been had this press
 * been the one that did it, and the session is read again so the gate can put the reader where
 * that state belongs.
 *
 * @param settle What the act does to the rest of the cache when it succeeds.
 */
function settleFrom(
  cache: ReturnType<typeof useQueryClient>,
  error: unknown,
  settle: () => void,
): void {
  if (error instanceof ApiError && error.alreadyDone) {
    settle();
    void cache.invalidateQueries({ queryKey: sessionKey });
  }
}

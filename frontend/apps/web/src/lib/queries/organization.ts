'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  changeStanding,
  describeOrganization,
  type Invitation,
  invitePerson,
  removePerson,
  renameOrganization,
  revokeInvitation,
  type Standing,
} from '@/lib/api/endpoints';
import { organizationKey, sitesKey } from './keys';
import { proofFrom } from './session';

/**
 * The account somebody belongs to, and everybody in it.
 *
 * Read once and written to by every change on the people screen, so that what is on the screen is
 * always the account as the engine holds it. Nothing here assembles a row of its own from what was
 * just submitted: two people editing the same account at once would otherwise each see their own
 * half of it.
 */
export function useOrganization(enabled = true) {
  return useQuery({
    queryKey: organizationKey,
    queryFn: describeOrganization,
    enabled,
    staleTime: 60_000,
  });
}

export function useRenameOrganization() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (name: string) => renameOrganization(name, proofFrom(cache)),
    onSuccess: () => cache.invalidateQueries({ queryKey: organizationKey }),
  });
}

/**
 * Changes what somebody may do.
 *
 * The list of websites is read again as well as the account, because a standing in an account is
 * one of the two things that decide which websites somebody can see — so a person moved up or down
 * is looking at a different list from the moment it takes effect.
 */
export function useChangeStanding() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (standing: Standing) => changeStanding(standing, proofFrom(cache)),
    onSuccess: () => invalidateAccess(cache),
  });
}

export function useRemovePerson() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (userId: string) => removePerson(userId, proofFrom(cache)),
    onSuccess: () => invalidateAccess(cache),
  });
}

export function useInvitePerson() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (invitation: Invitation) => invitePerson(invitation, proofFrom(cache)),
    onSuccess: () => cache.invalidateQueries({ queryKey: organizationKey }),
  });
}

export function useRevokeInvitation() {
  const cache = useQueryClient();

  return useMutation({
    mutationFn: async (invitationId: string) => revokeInvitation(invitationId, proofFrom(cache)),
    onSuccess: () => cache.invalidateQueries({ queryKey: organizationKey }),
  });
}

function invalidateAccess(cache: ReturnType<typeof useQueryClient>): void {
  void cache.invalidateQueries({ queryKey: organizationKey });
  void cache.invalidateQueries({ queryKey: sitesKey });
}

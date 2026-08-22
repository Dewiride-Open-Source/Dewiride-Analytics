'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, SelectInput, TextInput } from '@/components/ui/field';
import type { OrganizationRole, PendingInvitation } from '@/lib/api/schemas';
import { checkEmail, type ValidationKey } from '@/lib/forms/validation';
import { useInvitePerson, useRevokeInvitation } from '@/lib/queries/organization';

/** What somebody can be asked to be, from the least they can do to the most. */
const STANDINGS: readonly OrganizationRole[] = ['member', 'admin', 'owner'];

interface InvitationsProps {
  readonly invitations: readonly PendingInvitation[];
}

/**
 * Asking somebody to join, and everybody who has been asked and has not yet.
 *
 * Nothing is created in their name by this. What it sends is a link, and the account changes only
 * when they open it — which is what the empty state says rather than leaving somebody wondering
 * why the person they invited is not on the list above.
 */
export function Invitations({ invitations }: InvitationsProps) {
  const t = useTranslations('settings.invitations');
  const standing = useTranslations('settings.standings');
  const validation = useTranslations('validation');
  const format = useFormatter();
  const invite = useInvitePerson();
  const revoke = useRevokeInvitation();
  const [address, setAddress] = useState('');
  const [role, setRole] = useState<OrganizationRole>('member');
  const [refusal, setRefusal] = useState<ValidationKey | null>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const bad = checkEmail(address);

    setRefusal(bad);

    if (bad) {
      return;
    }

    invite.mutate(
      { emailAddress: address.trim(), role },
      {
        onSuccess: () => {
          setAddress('');
          setRole('member');
        },
      },
    );
  }

  return (
    <Card className="overflow-hidden">
      <header className="border-b border-border px-5 py-4 sm:px-6">
        <h2 className="text-sm font-medium text-foreground-muted">{t('label')}</h2>
      </header>

      <form onSubmit={submit} noValidate className="flex flex-col gap-4 px-5 py-5 sm:px-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
          <div className="flex-1">
            <Field label={t('address.label')} problem={refusal ? validation(refusal) : undefined}>
              {(attributes) => (
                <TextInput
                  {...attributes}
                  type="email"
                  name="emailAddress"
                  value={address}
                  onChange={(event) => setAddress(event.target.value)}
                  placeholder={t('address.placeholder')}
                  autoComplete="off"
                  required
                />
              )}
            </Field>
          </div>

          <div className="sm:w-48">
            <Field label={t('standing.label')}>
              {(attributes) => (
                <SelectInput
                  {...attributes}
                  name="role"
                  value={role}
                  onChange={(event) => setRole(event.target.value as OrganizationRole)}
                >
                  {STANDINGS.map((offered) => (
                    <option key={offered} value={offered}>
                      {standing(offered)}
                    </option>
                  ))}
                </SelectInput>
              )}
            </Field>
          </div>
        </div>

        {invite.isError ? <FailureNotice error={invite.error} /> : null}

        <div className="flex items-center gap-3">
          <Button type="submit" size="sm" busy={invite.isPending} disabled={!address.trim()}>
            {t('submit')}
          </Button>

          {invite.isSuccess && !address ? (
            <span role="status" className="text-sm text-foreground-muted">
              {t('sent')}
            </span>
          ) : null}
        </div>
      </form>

      {invitations.length > 0 ? (
        <>
          {/*
            A heading of its own, because what follows is not part of the form above it: these are
            people who have been asked and have not arrived, and without a label the first of them
            reads as something the form just did.
          */}
          <h3 className="border-t border-border px-5 py-3 text-sm font-medium text-foreground-muted sm:px-6">
            {t('waitingLabel')}
          </h3>
          <ul className="flex flex-col divide-y divide-border/70 border-t border-border">
            {invitations.map((invitation) => (
              <li
                key={invitation.id}
                className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between sm:gap-6 sm:px-6"
              >
                <div className="flex min-w-0 flex-col">
                  <p className="truncate font-medium text-foreground">{invitation.emailAddress}</p>
                  <p className="text-sm text-foreground-muted">
                    {t('waiting', {
                      standing: standing(invitation.role),
                      date: day(format, invitation.expiresAt),
                    })}
                  </p>
                </div>

                <Button
                  size="sm"
                  tone="quiet"
                  busy={revoke.isPending && revoke.variables === invitation.id}
                  onClick={() => revoke.mutate(invitation.id)}
                  aria-label={t('cancelFor', { address: invitation.emailAddress })}
                >
                  {t('cancel')}
                </Button>
              </li>
            ))}
          </ul>
        </>
      ) : null}

      {revoke.isError ? (
        <div className="border-t border-border px-5 py-4 sm:px-6">
          <FailureNotice error={revoke.error} />
        </div>
      ) : null}
    </Card>
  );
}

/**
 * A date somebody can act on, written the way their language writes one.
 *
 * Fixed to one zone rather than the reader's own, so that two people on the same account never
 * disagree about the day an invitation runs out.
 */
function day(format: ReturnType<typeof useFormatter>, instant: string): string {
  return format.dateTime(new Date(instant), {
    day: 'numeric',
    month: 'long',
    timeZone: 'UTC',
  });
}

'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Dialog } from '@/components/ui/dialog';
import { FailureNotice } from '@/components/ui/failure-notice';
import { SelectInput } from '@/components/ui/field';
import type { OrganizationRole, Person } from '@/lib/api/schemas';
import { useChangeStanding, useRemovePerson } from '@/lib/queries/organization';

/** What somebody can be, from the least they can do to the most. */
const STANDINGS: readonly OrganizationRole[] = ['member', 'admin', 'owner'];

interface PeopleListProps {
  readonly people: readonly Person[];
  /** Whether the person reading this may change what anybody can do. */
  readonly owner: boolean;
  /** Who is reading, so that nobody is offered a control that would lock them out of their own account. */
  readonly yourId: string | null;
}

/**
 * Everybody who can see what this account measures.
 *
 * A list rather than a table, so that on a phone each person stacks into their own block instead
 * of a row that has to be dragged sideways — which would put the controls that matter off the edge
 * on exactly the device somebody is most likely to be holding when they need to take somebody's
 * access away in a hurry.
 */
export function PeopleList({ people, owner, yourId }: PeopleListProps) {
  const t = useTranslations('settings.people');
  const standing = useTranslations('settings.standings');
  const change = useChangeStanding();
  const remove = useRemovePerson();
  const [leaving, setLeaving] = useState<Person | null>(null);

  const refusal = change.error ?? remove.error;

  function confirmRemoval() {
    if (!leaving) {
      return;
    }

    remove.mutate(leaving.id, { onSuccess: () => setLeaving(null) });
  }

  return (
    <Card className="overflow-hidden">
      <header className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 border-b border-border px-5 py-4 sm:px-6">
        <h2 className="text-sm font-medium text-foreground-muted">{t('label')}</h2>
        <p className="text-sm text-foreground-subtle">{t('count', { count: people.length })}</p>
      </header>

      {refusal ? (
        <div className="border-b border-border px-5 py-4 sm:px-6">
          <FailureNotice error={refusal} />
        </div>
      ) : null}

      <ul className="flex flex-col divide-y divide-border/70">
        {people.map((person) => (
          <li
            key={person.id}
            className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between sm:gap-6 sm:px-6"
          >
            <div className="flex min-w-0 flex-col">
              <p className="truncate font-medium text-foreground">
                {person.displayName}
                {person.id === yourId ? (
                  <span className="ml-2 text-xs font-normal text-accent-strong">{t('you')}</span>
                ) : null}
              </p>
              <p className="truncate text-sm text-foreground-muted">{person.emailAddress}</p>
            </div>

            <div className="flex shrink-0 items-center gap-2">
              {owner ? (
                <>
                  <label className="sr-only" htmlFor={`standing-${person.id}`}>
                    {t('standingFor', { name: person.displayName })}
                  </label>
                  <SelectInput
                    id={`standing-${person.id}`}
                    className="h-9 w-52 text-sm"
                    value={person.role}
                    disabled={change.isPending}
                    onChange={(event) =>
                      change.mutate({
                        userId: person.id,
                        role: event.target.value as OrganizationRole,
                      })
                    }
                  >
                    {STANDINGS.map((offered) => (
                      <option key={offered} value={offered}>
                        {standing(offered)}
                      </option>
                    ))}
                  </SelectInput>

                  {/*
                    The slot is kept whether or not it holds anything, so that every row's control
                    lines up with the one above it. Nobody is offered it on their own row: taking
                    yourself out of the account you are reading would leave you signed in with
                    nothing to look at, and on an account with one owner it is the one change the
                    engine will always refuse.
                  */}
                  <span className="flex w-24 shrink-0 justify-end">
                    {person.id === yourId ? null : (
                      <Button
                        size="sm"
                        tone="quiet"
                        onClick={() => setLeaving(person)}
                        aria-label={t('removeFor', { name: person.displayName })}
                      >
                        {t('remove')}
                      </Button>
                    )}
                  </span>
                </>
              ) : (
                <p className="text-sm text-foreground-muted">{standing(person.role)}</p>
              )}
            </div>
          </li>
        ))}
      </ul>

      <Dialog
        open={leaving !== null}
        onClose={() => setLeaving(null)}
        title={t('confirm.title')}
        closeLabel={t('confirm.close')}
        className="max-w-md"
      >
        <div className="flex flex-col gap-5">
          <p className="text-sm text-foreground-muted">
            {t('confirm.body', { name: leaving?.displayName ?? '' })}
          </p>

          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button tone="secondary" onClick={() => setLeaving(null)}>
              {t('confirm.keep')}
            </Button>
            <Button tone="danger" busy={remove.isPending} onClick={confirmRemoval}>
              {t('confirm.remove')}
            </Button>
          </div>
        </div>
      </Dialog>
    </Card>
  );
}

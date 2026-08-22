'use client';

import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, TextInput } from '@/components/ui/field';
import { useRenameOrganization } from '@/lib/queries/organization';

interface AccountNameProps {
  readonly name: string;
  /** Whether the person reading this may change it. */
  readonly editable: boolean;
}

/**
 * What the account is called.
 *
 * Shown to everybody and changed by an owner. It is the name on every message this product sends
 * about the account, which is why somebody who cannot change it is still shown it rather than
 * shown nothing.
 */
export function AccountName({ name, editable }: AccountNameProps) {
  const t = useTranslations('settings.name');
  const rename = useRenameOrganization();
  const [value, setValue] = useState(name);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const trimmed = value.trim();

    if (trimmed && trimmed !== name) {
      rename.mutate(trimmed);
    }
  }

  if (!editable) {
    return (
      <Card className="flex flex-col gap-1.5 p-5 sm:p-6">
        <h2 className="text-sm font-medium text-foreground-muted">{t('label')}</h2>
        <p className="text-lg text-foreground">{name}</p>
      </Card>
    );
  }

  return (
    <Card className="p-5 sm:p-6">
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <Field label={t('label')}>
          {(attributes) => (
            <TextInput
              {...attributes}
              name="name"
              value={value}
              onChange={(event) => setValue(event.target.value)}
              maxLength={200}
              autoComplete="organization"
              required
            />
          )}
        </Field>

        {rename.isError ? <FailureNotice error={rename.error} /> : null}

        <div className="flex items-center gap-3">
          <Button
            type="submit"
            size="sm"
            busy={rename.isPending}
            disabled={!value.trim() || value.trim() === name}
          >
            {t('submit')}
          </Button>

          {/*
            Only worth saying once the name on screen is the one that was saved. A confirmation
            that lingered beside a box somebody had since typed into would be describing something
            that is no longer true.
          */}
          {rename.isSuccess && value.trim() === name ? (
            <span role="status" className="text-sm text-foreground-muted">
              {t('saved')}
            </span>
          ) : null}
        </div>
      </form>
    </Card>
  );
}

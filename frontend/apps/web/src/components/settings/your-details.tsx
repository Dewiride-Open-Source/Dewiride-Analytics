'use client';

import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, PasswordInput, TextInput } from '@/components/ui/field';
import { Waiting } from '@/components/ui/waiting';
import { checkPassword, type ValidationKey } from '@/lib/forms/validation';
import { useChangePassword, useRenameAccount } from '@/lib/queries/account';
import { useSession } from '@/lib/queries/session';

/**
 * The name somebody is shown under, and their password.
 *
 * The address they sign in with is shown and cannot be changed here. It is what every message this
 * product sends is addressed to, so moving it is a change of address to be confirmed rather than a
 * box to type in — and nothing yet asks for one.
 */
export function YourDetails() {
  const t = useTranslations('settings.you');
  const session = useSession();
  const user = session.data?.user ?? null;

  if (session.isPending) {
    return <Waiting label={t('loading')} />;
  }

  if (!user) {
    return <FailureNotice error={session.error} />;
  }

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-1.5">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          {t('title')}
        </h1>
        <p className="text-sm text-foreground-muted">
          {t('subtitle', { address: user.emailAddress })}
        </p>
      </header>

      <YourName current={user.displayName} />
      <YourPassword />
    </div>
  );
}

/** What everybody else on the account sees beside anything this person does. */
function YourName({ current }: { readonly current: string }) {
  const t = useTranslations('settings.you.name');
  const rename = useRenameAccount();
  const [value, setValue] = useState(current);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const trimmed = value.trim();

    if (trimmed && trimmed !== current) {
      rename.mutate(trimmed);
    }
  }

  return (
    <Card className="p-5 sm:p-6">
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <Field label={t('label')}>
          {(attributes) => (
            <TextInput
              {...attributes}
              name="displayName"
              value={value}
              onChange={(event) => setValue(event.target.value)}
              maxLength={100}
              autoComplete="name"
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
            disabled={!value.trim() || value.trim() === current}
          >
            {t('submit')}
          </Button>

          {rename.isSuccess && value.trim() === current ? (
            <span role="status" className="text-sm text-foreground-muted">
              {t('saved')}
            </span>
          ) : null}
        </div>
      </form>
    </Card>
  );
}

/**
 * Replacing a password.
 *
 * The current one is asked for even though the person is already signed in, because a browser left
 * open on somebody else's desk is exactly the case this protects against. Every other sign-in the
 * account has open ends the moment it changes, and this one carries on.
 */
function YourPassword() {
  const t = useTranslations('settings.you.password');
  const validation = useTranslations('validation');
  const change = useChangePassword();
  const [refusal, setRefusal] = useState<ValidationKey | null>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const form = event.currentTarget;
    const entered = new FormData(form);
    const currentPassword = String(entered.get('currentPassword') ?? '');
    const newPassword = String(entered.get('newPassword') ?? '');
    const bad = checkPassword(newPassword);

    setRefusal(bad);

    if (bad) {
      return;
    }

    change.mutate({ currentPassword, newPassword }, { onSuccess: () => form.reset() });
  }

  return (
    <Card className="p-5 sm:p-6">
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <h2 className="text-sm font-medium text-foreground-muted">{t('label')}</h2>

        <Field label={t('current.label')}>
          {(attributes) => (
            <PasswordInput
              {...attributes}
              name="currentPassword"
              autoComplete="current-password"
              showLabel={t('show')}
              hideLabel={t('hide')}
              required
            />
          )}
        </Field>

        <Field
          label={t('replacement.label')}
          hint={t('replacement.hint')}
          problem={refusal ? validation(refusal) : undefined}
        >
          {(attributes) => (
            <PasswordInput
              {...attributes}
              name="newPassword"
              autoComplete="new-password"
              showLabel={t('show')}
              hideLabel={t('hide')}
              required
            />
          )}
        </Field>

        {change.isError ? <FailureNotice error={change.error} /> : null}

        <div className="flex items-center gap-3">
          <Button type="submit" size="sm" busy={change.isPending}>
            {t('submit')}
          </Button>

          {change.isSuccess ? (
            <span role="status" className="text-sm text-foreground-muted">
              {t('saved')}
            </span>
          ) : null}
        </div>
      </form>
    </Card>
  );
}

'use client';

import { useTranslations } from 'next-intl';
import { type FormEvent, useId, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, PasswordInput, TextInput } from '@/components/ui/field';
import { Notice } from '@/components/ui/notice';
import { Link } from '@/i18n/navigation';
import { ApiError } from '@/lib/api/problem';
import { checkEmail, checkPresent, type ValidationKey } from '@/lib/forms/validation';
import { useSignIn } from '@/lib/queries/session';
import { FORGOT_PASSWORD } from '@/lib/routes';

interface Refusals {
  readonly emailAddress?: ValidationKey;
  readonly password?: ValidationKey;
}

/**
 * The way back in for somebody who already has an account.
 *
 * Every failure the engine can answer with — wrong password, an address nobody has registered, an
 * account paused after too many attempts — comes back as the same refusal, and is shown here as
 * the same sentence. Saying more would turn this form into a way of finding out who has an
 * account on somebody else's installation.
 */
export function SignInForm() {
  const t = useTranslations('signIn');
  const validation = useTranslations('validation');
  const signIn = useSignIn();
  const [refusals, setRefusals] = useState<Refusals>({});
  const stayId = useId();

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const entered = new FormData(event.currentTarget);
    const emailAddress = String(entered.get('emailAddress') ?? '').trim();
    const password = String(entered.get('password') ?? '');

    const badEmail = checkEmail(emailAddress);
    const badPassword = checkPresent(password, 'passwordRequired');

    setRefusals({ emailAddress: badEmail ?? undefined, password: badPassword ?? undefined });

    if (badEmail || badPassword) {
      return;
    }

    signIn.mutate({
      emailAddress,
      password,
      staySignedIn: entered.get('staySignedIn') === 'on',
    });
  }

  const wrongDetails = signIn.error instanceof ApiError && signIn.error.unauthorised;

  return (
    <Card focal className="w-full max-w-md p-6 sm:p-8">
      <header className="mb-6 flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">{t('title')}</h1>
        <p className="text-sm text-foreground-muted">{t('subtitle')}</p>
      </header>

      {signIn.isError ? (
        wrongDetails ? (
          <Notice title={t('refused.title')} className="mb-5">
            {t('refused.body')}
          </Notice>
        ) : (
          <FailureNotice error={signIn.error} className="mb-5" />
        )
      ) : null}

      <form onSubmit={submit} noValidate className="flex flex-col gap-5">
        <Field
          label={t('email.label')}
          problem={refusals.emailAddress ? validation(refusals.emailAddress) : undefined}
        >
          {(attributes) => (
            <TextInput
              {...attributes}
              name="emailAddress"
              type="email"
              autoComplete="username"
              inputMode="email"
              placeholder={t('email.placeholder')}
              // This screen exists to be typed into and has one first box. The rule guards
              // against stealing focus on a page somebody is reading, which is not this.
              // eslint-disable-next-line jsx-a11y/no-autofocus
              autoFocus
              required
            />
          )}
        </Field>

        <Field
          label={t('password.label')}
          problem={refusals.password ? validation(refusals.password) : undefined}
        >
          {(attributes) => (
            <PasswordInput
              {...attributes}
              name="password"
              autoComplete="current-password"
              showLabel={t('password.show')}
              hideLabel={t('password.hide')}
              required
            />
          )}
        </Field>

        <div className="flex items-center justify-between gap-4">
          <div className="flex items-center gap-2.5">
            <input
              id={stayId}
              type="checkbox"
              name="staySignedIn"
              className="size-4 shrink-0 rounded-sm accent-[var(--accent)]"
            />
            <label htmlFor={stayId} className="text-sm text-foreground-muted">
              {t('stay.label')}
            </label>
          </div>

          <Link
            href={FORGOT_PASSWORD}
            className="text-sm font-medium text-accent-strong underline-offset-4 hover:underline"
          >
            {t('forgotten')}
          </Link>
        </div>

        <Button type="submit" size="lg" block busy={signIn.isPending}>
          {signIn.isPending ? t('submitting') : t('submit')}
        </Button>
      </form>
    </Card>
  );
}

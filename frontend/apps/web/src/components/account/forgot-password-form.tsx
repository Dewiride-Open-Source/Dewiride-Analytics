'use client';

import { MailCheck } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button, buttonStyle } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, TextInput } from '@/components/ui/field';
import { Link } from '@/i18n/navigation';
import { checkEmail, type ValidationKey } from '@/lib/forms/validation';
import { useBeginPasswordReset } from '@/lib/queries/session';
import { SIGN_IN } from '@/lib/routes';

/**
 * The way back in for somebody who cannot remember their password.
 *
 * What is shown afterwards is the same sentence whether or not the address belongs to an account,
 * because that is all the engine will say — and it is all it should say, since anybody at all can
 * open this screen on somebody else's installation.
 */
export function ForgotPasswordForm() {
  const t = useTranslations('forgotPassword');
  const validation = useTranslations('validation');
  const asked = useBeginPasswordReset();
  const [refusal, setRefusal] = useState<ValidationKey | null>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const emailAddress = String(new FormData(event.currentTarget).get('emailAddress') ?? '').trim();
    const bad = checkEmail(emailAddress);

    setRefusal(bad);

    if (bad) {
      return;
    }

    asked.mutate(emailAddress);
  }

  // Once a link has been asked for there is nothing left to do here, so the heading becomes what
  // happened rather than staying an invitation to do it again.
  if (asked.isSuccess) {
    return (
      <Card focal className="w-full max-w-md p-6 sm:p-8">
        <header className="mb-6 flex flex-col gap-1">
          <span
            aria-hidden
            className="mb-3 grid size-10 place-items-center rounded-full bg-surface-muted text-accent-strong"
          >
            <MailCheck className="size-5" />
          </span>
          <h1 className="text-2xl font-semibold tracking-tight text-foreground">
            {t('sent.title')}
          </h1>
          <p aria-live="polite" className="text-sm text-foreground-muted">
            {t('sent.body')}
          </p>
        </header>
        <Link
          href={SIGN_IN}
          className={buttonStyle({ tone: 'secondary', size: 'lg', block: true })}
        >
          {t('back')}
        </Link>
      </Card>
    );
  }

  return (
    <Card focal className="w-full max-w-md p-6 sm:p-8">
      <header className="mb-6 flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">{t('title')}</h1>
        <p className="text-sm text-foreground-muted">{t('subtitle')}</p>
      </header>

      {asked.isError ? <FailureNotice error={asked.error} className="mb-5" /> : null}

      <form onSubmit={submit} noValidate className="flex flex-col gap-5">
        <Field label={t('email.label')} problem={refusal ? validation(refusal) : undefined}>
          {(attributes) => (
            <TextInput
              {...attributes}
              name="emailAddress"
              type="email"
              inputMode="email"
              autoComplete="username"
              placeholder={t('email.placeholder')}
              // This screen exists to be typed into and has one box. The rule guards against
              // stealing focus on a page somebody is reading, which is not this.
              // eslint-disable-next-line jsx-a11y/no-autofocus
              autoFocus
              required
            />
          )}
        </Field>

        <Button type="submit" size="lg" block busy={asked.isPending}>
          {asked.isPending ? t('submitting') : t('submit')}
        </Button>
      </form>

      <p className="mt-6 text-center text-sm">
        <Link
          href={SIGN_IN}
          className="font-medium text-accent-strong underline-offset-4 hover:underline"
        >
          {t('back')}
        </Link>
      </p>
    </Card>
  );
}

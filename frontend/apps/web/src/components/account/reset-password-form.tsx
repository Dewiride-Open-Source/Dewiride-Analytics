'use client';

import { AlertTriangle } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { type FormEvent, type ReactNode, useState } from 'react';
import { Button, buttonStyle } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, PasswordInput } from '@/components/ui/field';
import { Link } from '@/i18n/navigation';
import { ApiError } from '@/lib/api/problem';
import { checkPassword, type ValidationKey } from '@/lib/forms/validation';
import { useCompletePasswordReset } from '@/lib/queries/session';
import { FORGOT_PASSWORD, SIGN_IN } from '@/lib/routes';

/** The code the engine reports for a link that has expired, been used, or was never valid. */
const LINK_NOT_USABLE = 'ResetLinkNotUsable';

/**
 * Choosing a new password from a link that arrived by email.
 *
 * Both halves of the link are read from the address rather than asked for again: somebody halfway
 * through getting back into their account should not have to remember which of their addresses
 * they registered with. A link missing either half, or one the engine will not accept, is a screen
 * that says so plainly and carries the single action that would put it right.
 */
export function ResetPasswordForm() {
  const t = useTranslations('resetPassword');
  const validation = useTranslations('validation');
  const asked = useSearchParams();
  const reset = useCompletePasswordReset();
  const [refusal, setRefusal] = useState<ValidationKey | null>(null);

  const emailAddress = asked.get('address') ?? '';
  const token = asked.get('token') ?? '';

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const password = String(new FormData(event.currentTarget).get('password') ?? '');
    const bad = checkPassword(password);

    setRefusal(bad);

    if (bad) {
      return;
    }

    reset.mutate({ emailAddress, token, password });
  }

  if (!emailAddress || !token) {
    return (
      <DeadEnd title={t('missing.title')} body={t('missing.body')} action={t('missing.action')} />
    );
  }

  if (reset.isSuccess) {
    return (
      <Shell title={t('done.title')} subtitle={t('done.body')}>
        <Link href={SIGN_IN} className={buttonStyle({ size: 'lg', block: true })}>
          {t('done.action')}
        </Link>
      </Shell>
    );
  }

  if (reset.error instanceof ApiError && reset.error.reasons.some(isLinkRefusal)) {
    return (
      <DeadEnd title={t('expired.title')} body={t('expired.body')} action={t('expired.action')} />
    );
  }

  return (
    <Shell title={t('title')} subtitle={t('subtitle', { emailAddress })}>
      {reset.isError ? <FailureNotice error={reset.error} className="mb-5" /> : null}

      <form onSubmit={submit} noValidate className="flex flex-col gap-5">
        <Field
          label={t('password.label')}
          hint={t('password.hint')}
          problem={refusal ? validation(refusal) : undefined}
        >
          {(attributes) => (
            <PasswordInput
              {...attributes}
              name="password"
              autoComplete="new-password"
              showLabel={t('password.show')}
              hideLabel={t('password.hide')}
              // This screen exists to be typed into and has one box. The rule guards against
              // stealing focus on a page somebody is reading, which is not this.
              // eslint-disable-next-line jsx-a11y/no-autofocus
              autoFocus
              required
            />
          )}
        </Field>

        <Button type="submit" size="lg" block busy={reset.isPending}>
          {reset.isPending ? t('submitting') : t('submit')}
        </Button>
      </form>
    </Shell>
  );
}

function isLinkRefusal(reason: { readonly code: string }): boolean {
  return reason.code === LINK_NOT_USABLE;
}

/**
 * A link that leads nowhere, and the one thing that would put it right.
 *
 * An expired link and an incomplete one are the same shape of screen: nothing can be done on it,
 * and asking for another takes a single press.
 */
function DeadEnd({
  title,
  body,
  action,
}: {
  readonly title: string;
  readonly body: string;
  readonly action: string;
}) {
  return (
    <Shell title={title} subtitle={body} tone="problem">
      <Link href={FORGOT_PASSWORD} className={buttonStyle({ size: 'lg', block: true })}>
        {action}
      </Link>
    </Shell>
  );
}

function Shell({
  title,
  subtitle,
  tone = 'plain',
  children,
}: {
  readonly title: string;
  readonly subtitle?: string;
  readonly tone?: 'plain' | 'problem';
  readonly children: ReactNode;
}) {
  return (
    <Card focal className="w-full max-w-md p-6 sm:p-8">
      <header className="mb-6 flex flex-col gap-1">
        {tone === 'problem' ? (
          <span
            aria-hidden
            className="mb-3 grid size-10 place-items-center rounded-full bg-danger-soft text-danger"
          >
            <AlertTriangle className="size-5" />
          </span>
        ) : null}
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">{title}</h1>
        {subtitle ? <p className="text-sm text-foreground-muted">{subtitle}</p> : null}
      </header>
      {children}
    </Card>
  );
}

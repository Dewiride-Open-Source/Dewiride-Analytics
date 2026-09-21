'use client';

import { useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button, buttonStyle } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Field, PasswordInput, TextInput } from '@/components/ui/field';
import { DeadEnd, OutcomeCard } from '@/components/ui/outcome-card';
import { Waiting } from '@/components/ui/waiting';
import { Link } from '@/i18n/navigation';
import { checkPassword, type ValidationKey } from '@/lib/forms/validation';
import { useAcceptInvitation, usePreviewInvitation } from '@/lib/queries/account';
import { useSession } from '@/lib/queries/session';
import { DASHBOARD, SIGN_IN } from '@/lib/routes';

/**
 * Taking up an invitation to join an account.
 *
 * The screen has to answer two quite different people. Somebody with no account here chooses a
 * password and is signed in on the spot; somebody who already has one is added to the account and
 * signs in the way they always do. Which of the two they are is settled by the engine from the
 * link, never guessed at here.
 */
export function JoinForm() {
  const t = useTranslations('join');
  const asked = useSearchParams();
  const session = useSession();
  const token = asked.get('token') ?? '';
  const invitation = usePreviewInvitation(token, session.isSuccess);
  const accept = useAcceptInvitation();

  // A link that was never one of ours, one that has run out and one somebody has already used are
  // the same dead end. Telling them apart would say whether it had been used by somebody else, and
  // what to do about it is the same in every case.
  if (!token) {
    return (
      <DeadEnd
        title={t('missing.title')}
        body={t('missing.body')}
        action={t('action')}
        href={SIGN_IN}
      />
    );
  }

  if (session.isPending || invitation.isPending) {
    return <Waiting label={t('loading')} />;
  }

  if (invitation.isError) {
    return (
      <DeadEnd
        title={t('expired.title')}
        body={t('expired.body')}
        action={t('action')}
        href={SIGN_IN}
      />
    );
  }

  // Somebody signed in on this device is inside the account they just joined only when the
  // invitation was theirs: the standing goes to the account it was sent to, never to whoever
  // happened to open the link.
  if (accept.isSuccess) {
    return (
      <Joined
        signedIn={
          accept.data.signedIn ||
          sameAddress(session.data?.user?.emailAddress, invitation.data.emailAddress)
        }
        name={invitation.data.organizationName}
      />
    );
  }

  return (
    <OutcomeCard
      title={t('title', { organization: invitation.data.organizationName })}
      subtitle={t('subtitle', { address: invitation.data.emailAddress })}
    >
      {accept.isError ? <FailureNotice error={accept.error} className="mb-5" /> : null}

      {invitation.data.needsAccount ? (
        <NewAccount token={token} accept={accept} />
      ) : (
        <Button
          size="lg"
          block
          busy={accept.isPending}
          onClick={() => accept.mutate({ token, displayName: null, password: null })}
        >
          {accept.isPending ? t('joining') : t('join')}
        </Button>
      )}
    </OutcomeCard>
  );
}

/** Whether two addresses name one mailbox, however either was typed. */
function sameAddress(one: string | undefined, other: string): boolean {
  return one !== undefined && one.trim().toLowerCase() === other.trim().toLowerCase();
}

/** Everything somebody with no account here has to choose before they can be signed in. */
function NewAccount({
  token,
  accept,
}: {
  readonly token: string;
  readonly accept: ReturnType<typeof useAcceptInvitation>;
}) {
  const t = useTranslations('join');
  const validation = useTranslations('validation');
  const [refusal, setRefusal] = useState<ValidationKey | null>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const entered = new FormData(event.currentTarget);
    const password = String(entered.get('password') ?? '');
    const displayName = String(entered.get('displayName') ?? '').trim();
    const bad = checkPassword(password);

    setRefusal(bad);

    if (bad) {
      return;
    }

    accept.mutate({ token, displayName: displayName || null, password });
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <Field label={t('name.label')} optionalLabel={t('name.optional')}>
        {(attributes) => (
          <TextInput
            {...attributes}
            name="displayName"
            placeholder={t('name.placeholder')}
            autoComplete="name"
            maxLength={100}
          />
        )}
      </Field>

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
            required
          />
        )}
      </Field>

      <Button type="submit" size="lg" block busy={accept.isPending}>
        {accept.isPending ? t('joining') : t('submit')}
      </Button>
    </form>
  );
}

/**
 * What follows joining.
 *
 * Somebody who is signed in — because they have just chosen a password, or because they already
 * were on this device — goes straight to the numbers. Somebody who already had an account here
 * and is not signed in is sent to sign in with the password they already have rather than being
 * asked for it a second time on this screen.
 */
function Joined({ signedIn, name }: { readonly signedIn: boolean; readonly name: string }) {
  const t = useTranslations('join');

  return (
    <OutcomeCard
      title={t('done.title', { organization: name })}
      subtitle={signedIn ? t('done.ready') : t('done.signIn')}
    >
      <Link
        href={signedIn ? DASHBOARD : SIGN_IN}
        className={buttonStyle({ size: 'lg', block: true })}
      >
        {signedIn ? t('done.open') : t('done.action')}
      </Link>
    </OutcomeCard>
  );
}

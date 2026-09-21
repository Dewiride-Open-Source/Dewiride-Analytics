'use client';

import { edition } from '@edition';
import { useFormatter, useTranslations } from 'next-intl';
import { Button, buttonStyle } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { OutcomeCard } from '@/components/ui/outcome-card';
import { Link, useRouter } from '@/i18n/navigation';
import type { ClosedAccount as Closure } from '@/lib/api/schemas';
import { useRestoreAccount } from '@/lib/queries/account';
import { useSession, useSignOut } from '@/lib/queries/session';
import { DASHBOARD } from '@/lib/routes';

/**
 * What somebody sees when the account they belong to has been closed.
 *
 * One card, because there is one fact to take in and at most two things to do about it. It says
 * who closed the account and when, when everything about it is deleted, and — for an owner —
 * offers to bring it back. Somebody who also belongs to an open account is given the way back to
 * their dashboard; somebody who belongs nowhere else is given the way out.
 *
 * Everything on it comes from the session, which is the one answer the engine gives about a
 * closed account to somebody who belongs to it. Nothing else about the account can be read while
 * it is closed, so nothing else is asked for.
 */
export function ClosedAccount() {
  const t = useTranslations('closed');
  const format = useFormatter();
  const session = useSession();
  const restore = useRestoreAccount();
  const signOut = useSignOut();
  const router = useRouter();
  const closure = session.data?.closure ?? null;

  // The gate only shows this screen to somebody with a closed account, so this is never drawn;
  // it keeps the rest honest about what it can assume.
  if (!closure) {
    return null;
  }

  const Edition = edition.closure;

  /**
   * Brings the account back and goes to the dashboard.
   *
   * Moved explicitly rather than left to the gate. The gate moves somebody off this screen only
   * when nothing of theirs is closed any more, and somebody who belongs to a second closed account
   * would otherwise be left here reading about that one, with nothing saying the first came back.
   */
  function bringBack() {
    restore.mutate(undefined, { onSuccess: () => router.replace(DASHBOARD) });
  }

  return (
    <OutcomeCard
      tone="kept"
      title={t('title', { account: closure.name })}
      subtitle={closedBy(t, closure, day(format, closure.closedAt))}
    >
      <div className="flex flex-col gap-5">
        <p className="rounded-lg border border-border bg-surface-muted/60 p-4 text-sm text-foreground">
          {t('deletion', { date: day(format, closure.deletionDue) })}
        </p>

        {/*
          Only for an owner with nowhere else to be. The edition describes the arrangement of the
          account somebody is in, and for somebody who also has an open account that is the open
          one; for somebody who cannot bring this one back, what happens to its arrangement is
          not theirs to act on.
        */}
        {Edition && closure.canRestore && !closure.hasOpenAccount ? <Edition /> : null}

        {restore.isError ? <FailureNotice error={restore.error} /> : null}

        <div className="flex flex-col gap-3">
          {closure.canRestore ? (
            <Button size="lg" block busy={restore.isPending} onClick={bringBack}>
              {restore.isPending ? t('restoring') : t('restore')}
            </Button>
          ) : (
            <p className="text-center text-sm text-foreground-muted">{t('onlyOwner')}</p>
          )}

          {closure.hasOpenAccount ? (
            <Link
              href={DASHBOARD}
              className={buttonStyle({ tone: 'secondary', size: 'lg', block: true })}
            >
              {t('back')}
            </Link>
          ) : (
            <Button
              tone="secondary"
              size="lg"
              block
              busy={signOut.isPending}
              onClick={() => signOut.mutate()}
            >
              {signOut.isPending ? t('signingOut') : t('signOut')}
            </Button>
          )}
        </div>
      </div>
    </OutcomeCard>
  );
}

/**
 * Who closed the account, and when.
 *
 * Said in the second person to the person who did it, by name where the person who did it is
 * still on the account, and without a name where they are not: a name that has since been
 * removed from the account is not one this screen is told.
 */
function closedBy(
  t: ReturnType<typeof useTranslations<'closed'>>,
  closure: Closure,
  date: string,
): string {
  if (closure.closedByYou) {
    return t('bodyYou', { date });
  }

  return closure.closedBy === null
    ? t('bodyUnattributed', { date })
    : t('body', { closedBy: closure.closedBy, date });
}

/**
 * A day, written out in full.
 *
 * Read in universal time rather than in the browser's zone, because it is the day the email
 * about the same account names, and the two must not disagree by one for somebody reading them
 * side by side.
 */
function day(format: ReturnType<typeof useFormatter>, instant: string): string {
  return format.dateTime(new Date(instant), {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}

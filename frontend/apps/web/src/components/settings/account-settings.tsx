'use client';

import { useTranslations } from 'next-intl';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Waiting } from '@/components/ui/waiting';
import { useSession } from '@/lib/queries/session';
import { useOrganization } from '@/lib/queries/organization';
import { AccountName } from './account-name';
import { Invitations } from './invitations';
import { PeopleList } from './people-list';

/**
 * The account itself: what it is called, and who can see what it measures.
 *
 * Whether the person reading it may change any of this comes from the engine's answer rather than
 * from anything the browser decides, so a screen that offers a control is a screen where the
 * engine will accept it.
 */
export function AccountSettings() {
  const t = useTranslations('settings.account');
  const account = useOrganization();
  const session = useSession();

  if (account.isPending) {
    return <Waiting label={t('loading')} />;
  }

  if (account.isError) {
    return <FailureNotice error={account.error} />;
  }

  const organization = account.data;
  const owner = organization.role === 'owner';

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-1.5">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          {t('title')}
        </h1>
        <p className="text-sm text-foreground-muted">{t('subtitle')}</p>
      </header>

      <AccountName name={organization.name} editable={owner} />

      <PeopleList
        people={organization.people}
        owner={owner}
        yourId={session.data?.user?.id ?? null}
      />

      {owner ? <Invitations invitations={organization.invitations} /> : null}
    </div>
  );
}

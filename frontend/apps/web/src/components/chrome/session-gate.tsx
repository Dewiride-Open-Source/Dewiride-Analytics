'use client';

import { useTranslations } from 'next-intl';
import { type ReactNode, useEffect } from 'react';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Waiting } from '@/components/ui/waiting';
import { usePathname, useRouter } from '@/i18n/navigation';
import { useSession } from '@/lib/queries/session';
import { destinationFor } from '@/lib/routes';

/**
 * Puts the person on the screen that matches the state of this install.
 *
 * There is exactly one screen that makes sense at any moment — set the product up, sign in, or
 * look at the numbers — and which one it is depends on an answer only the engine has. Deciding it
 * here means every screen below can assume it is the right one to be showing.
 */
export function SessionGate({ children }: { readonly children: ReactNode }) {
  const t = useTranslations();
  const session = useSession();
  const pathname = usePathname();
  const router = useRouter();

  const destination = session.data ? destinationFor(session.data, pathname) : null;

  useEffect(() => {
    if (destination) {
      router.replace(destination);
    }
  }, [destination, router]);

  if (session.isPending || destination) {
    return <Waiting label={t('app.loading')} />;
  }

  if (session.isError) {
    return (
      <div className="mx-auto flex min-h-[60vh] max-w-md flex-col justify-center gap-4 px-4">
        <FailureNotice error={session.error} />
        <Button tone="secondary" onClick={() => void session.refetch()}>
          {t('errors.retry')}
        </Button>
      </div>
    );
  }

  return children;
}

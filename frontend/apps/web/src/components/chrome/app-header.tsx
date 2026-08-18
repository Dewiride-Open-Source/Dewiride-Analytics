'use client';

import { LogOut } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { BrandMark } from '@/components/chrome/brand-mark';
import { ThemeSwitch } from '@/components/chrome/theme-switch';
import { Button } from '@/components/ui/button';
import { useSession, useSignOut } from '@/lib/queries/session';

/**
 * The bar across the top of every screen, signed in or not.
 *
 * It stays in place on the setup and sign-in screens as well, so the product does not appear to
 * change identity between the page somebody arrives on and the one they end up on.
 */
export function AppHeader() {
  const t = useTranslations();
  const session = useSession();
  const signOut = useSignOut();
  const user = session.data?.user ?? null;

  return (
    <header className="sticky top-0 z-20 border-b border-border/70 bg-background/75 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
        <BrandMark name={t('app.name')} compactOnMobile />

        <div className="flex items-center gap-2 sm:gap-3">
          <ThemeSwitch />

          {user ? (
            <>
              <span className="hidden text-sm text-foreground-muted md:inline">
                {t('header.signedInAs', { name: user.displayName })}
              </span>
              <Button
                tone="secondary"
                size="sm"
                busy={signOut.isPending}
                onClick={() => signOut.mutate()}
              >
                <LogOut aria-hidden className="size-4" />
                <span className="hidden sm:inline">
                  {signOut.isPending ? t('header.signingOut') : t('header.signOut')}
                </span>
              </Button>
            </>
          ) : null}
        </div>
      </div>
    </header>
  );
}

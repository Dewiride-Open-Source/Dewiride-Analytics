'use client';

import { LogOut } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { BrandMark } from '@/components/chrome/brand-mark';
import { ThemeSwitch } from '@/components/chrome/theme-switch';
import { AddSite } from '@/components/dashboard/add-site';
import { SiteSwitch } from '@/components/dashboard/site-switch';
import { Button } from '@/components/ui/button';
import { Link, usePathname } from '@/i18n/navigation';
import { useChosenSite } from '@/lib/analytics/chosen-site';
import { useSession, useSignOut } from '@/lib/queries/session';
import { useSites } from '@/lib/queries/sites';
import { SECTIONS } from '@/lib/routes';
import { cn } from '@/lib/styling';

/**
 * The bar across the top of every screen, signed in or not.
 *
 * It stays in place on the setup and sign-in screens as well, so the product does not appear to
 * change identity between the page somebody arrives on and the one they end up on.
 *
 * Which website is being looked at lives here rather than on the screen below it, because it is
 * true of the whole session rather than of one screen — and because the heading below is then a
 * heading rather than a control the size of one. Which website is chosen is kept in one place the
 * browser owns, so the bar and the screen cannot disagree about it.
 */
export function AppHeader() {
  const t = useTranslations();
  const session = useSession();
  const signOut = useSignOut();
  const user = session.data?.user ?? null;
  const sites = useSites(Boolean(user));
  const { site, choose } = useChosenSite(sites.data);
  const [adding, setAdding] = useState(false);
  const here = usePathname();

  function show(siteId: string) {
    choose(siteId);
    setAdding(false);
  }

  return (
    <header className="sticky top-0 z-20 border-b border-border/70 bg-background/75 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
        {/*
          The picker takes whatever room is left rather than a width of its own, and everything to
          the right of it keeps its own. On a phone there is very little left, and a picker that
          insisted on a comfortable width would simply sit on top of the controls beside it.
        */}
        <div className="flex min-w-0 flex-1 items-center gap-3 sm:gap-4">
          <BrandMark name={t('app.name')} compactOnMobile />

          {site && sites.data ? (
            <SiteSwitch
              sites={sites.data}
              chosen={site}
              onChoose={choose}
              onAdd={() => setAdding(true)}
            />
          ) : null}
        </div>

        <div className="flex shrink-0 items-center gap-2 sm:gap-3">
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

      {/*
        A row of its own rather than squeezed in beside the picker. On a phone the bar above is
        already full, and a way between the screens that only appears on a wide window is a way
        half the people using the product never find.
      */}
      {user ? (
        <nav
          aria-label={t('header.sections')}
          className="border-t border-border/60 bg-background/40"
        >
          <ul className="mx-auto flex max-w-6xl items-center gap-1 px-2 sm:px-4">
            {SECTIONS.map((section) => {
              const current = here === section.path;

              return (
                <li key={section.path}>
                  <Link
                    href={section.path}
                    aria-current={current ? 'page' : undefined}
                    className={cn(
                      'relative flex h-11 items-center rounded-sm px-3 text-sm font-medium transition-colors',
                      'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-strong',
                      'after:absolute after:inset-x-3 after:bottom-0 after:h-0.5 after:rounded-full',
                      current
                        ? 'text-foreground after:bg-accent'
                        : 'text-foreground-muted hover:text-foreground',
                    )}
                  >
                    {t(`header.section.${section.name}`)}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
      ) : null}

      <AddSite
        open={adding}
        onClose={() => setAdding(false)}
        likelyTimeZoneId={site?.timeZoneId}
        onAdded={show}
      />
    </header>
  );
}

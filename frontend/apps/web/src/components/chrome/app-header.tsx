'use client';

import { ArrowRight, LogOut } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { BrandMark } from '@/components/chrome/brand-mark';
import { ThemeSwitch } from '@/components/chrome/theme-switch';
import { AddSite } from '@/components/dashboard/add-site';
import { SiteSwitch } from '@/components/dashboard/site-switch';
import { Button } from '@/components/ui/button';
import { Link, usePathname } from '@/i18n/navigation';
import { useChosenSite } from '@/lib/analytics/chosen-site';
import { withChoices } from '@/lib/analytics/period';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import { usePeriod } from '@/lib/analytics/use-period';
import { useSession, useSignOut } from '@/lib/queries/session';
import { useSites } from '@/lib/queries/sites';
import { CLOSED, currentSection, SECTIONS } from '@/lib/routes';
import { cn } from '@/lib/styling';

/**
 * The bar across the top of every screen, signed in or not.
 *
 * It stays in place on the setup and sign-in screens as well, so the product does not appear to
 * change identity between the page somebody arrives on and the one they end up on. Somebody
 * whose only account is closed keeps the bar and loses the way between screens they cannot open:
 * what is left is the product's name, who they are, and the way out.
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
  const closure = session.data?.closure ?? null;
  const walled = closure !== null && !closure.hasOpenAccount;
  const sites = useSites(Boolean(user) && !walled);
  const { site, choose } = useChosenSite(sites.data);
  const [adding, setAdding] = useState(false);
  const here = usePathname();
  const { period } = usePeriod();
  const { population } = usePeopleOnly();

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

          {site && sites.data && !walled ? (
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
                {/* Out of sight on a phone, never out of the name a screen reader gives the button. */}
                <span className="sr-only sm:not-sr-only">
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
      {user && !walled ? (
        <nav
          aria-label={t('header.sections')}
          className="border-t border-border/60 bg-background/40"
        >
          {/*
            The row scrolls sideways rather than wrapping. On the narrowest phones the tabs are a
            little wider than the screen, and a bar that folded onto a second line would push the
            heading below it down on exactly the devices with the least room to spare.
          */}
          <ul className="mx-auto flex max-w-6xl items-center gap-1 overflow-x-auto px-2 [-ms-overflow-style:none] [scrollbar-width:none] sm:px-4 [&::-webkit-scrollbar]:hidden">
            {SECTIONS.map((section) => {
              const current = here !== null && currentSection(here) === section.path;

              return (
                <li key={section.path}>
                  {/*
                    The screens that answer about a stretch of days hand each other the period and
                    the population, so that moving between them is moving between two questions
                    about the same days and the same people rather than starting again. The one
                    about the present moment takes neither: there is no stretch of days for it to
                    be asked about, and what is happening now is counted rather than judged.
                  */}
                  <Link
                    href={
                      section.aboutSite
                        ? withChoices(section.path, { period, population })
                        : section.path
                    }
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

      {/*
        One line for somebody who belongs to a closed account and an open one, so that the account
        they cannot open is not simply missing from the picker with nothing saying where it went.
        Not above the screen it leads to, where the same words are the first thing on the page.
      */}
      {closure && !walled && here !== CLOSED ? (
        <div role="status" className="border-t border-border/60 bg-surface-muted">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-4 gap-y-1 px-4 py-2.5 sm:px-6">
            <p className="min-w-0 break-words text-sm text-foreground">
              {t('header.closed', { name: closure.name })}
            </p>
            <Link
              href={CLOSED}
              className="inline-flex items-center gap-1 text-sm font-medium text-accent-strong underline-offset-4 hover:underline"
            >
              {closure.canRestore ? t('header.closedRestore') : t('header.closedSee')}
              <ArrowRight aria-hidden className="size-3.5" />
            </Link>
          </div>
        </div>
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

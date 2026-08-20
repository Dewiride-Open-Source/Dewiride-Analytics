'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { useChosenSite } from '@/lib/analytics/chosen-site';
import type { Site } from '@/lib/api/schemas';
import { useSites } from '@/lib/queries/sites';

interface SiteScreenProps {
  /** What stands in the screen's place while the list of websites is still on its way. */
  readonly waiting: ReactNode;
  /** The screen itself, once there is a website to show it for. */
  readonly children: (site: Site) => ReactNode;
}

/**
 * The frame around every screen that is about one website.
 *
 * Which website is being looked at, what to show while that is still being read, what to say when
 * it cannot be, and what an account with no website at all sees — four answers each screen would
 * otherwise give for itself, and would eventually give differently. Written once so that moving
 * between screens is moving between two views of the same website rather than two products.
 */
export function SiteScreen({ waiting, children }: SiteScreenProps) {
  const t = useTranslations('dashboard');
  const sites = useSites();
  const { site } = useChosenSite(sites.data);

  if (sites.isPending) {
    return <Shell>{waiting}</Shell>;
  }

  if (sites.isError) {
    return (
      <Shell>
        <FailureNotice error={sites.error} />
      </Shell>
    );
  }

  if (!site) {
    return (
      <Shell>
        <Card className="flex flex-col items-center gap-2 px-6 py-16 text-center">
          <h1 className="text-lg font-semibold text-foreground">{t('noSites.title')}</h1>
          <p className="max-w-sm text-sm text-foreground-muted">{t('noSites.body')}</p>
        </Card>
      </Shell>
    );
  }

  return <Shell>{children(site)}</Shell>;
}

function Shell({ children }: { readonly children: ReactNode }) {
  return <div className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6 sm:py-10">{children}</div>;
}

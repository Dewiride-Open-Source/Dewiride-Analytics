'use client';

import { ChevronDown } from 'lucide-react';
import { useTranslations } from 'next-intl';
import type { Site } from '@/lib/api/schemas';

interface SiteSwitchProps {
  readonly sites: readonly Site[];
  readonly chosen: Site;
  readonly onChoose: (siteId: string) => void;
}

/**
 * Which website the screen is about.
 *
 * The name of the website is the control, rather than a separate box beside it repeating the same
 * word. It is the browser's own list rather than one built here: it opens correctly on a phone, it
 * is reachable by keyboard and by voice without anything being wired up, and a hundred websites
 * scroll in it.
 */
export function SiteSwitch({ sites, chosen, onChoose }: SiteSwitchProps) {
  const t = useTranslations('dashboard.site');

  return (
    <span className="relative inline-flex items-center">
      <select
        aria-label={t('label')}
        value={chosen.id}
        onChange={(event) => onChoose(event.target.value)}
        className="select-trigger -ml-2 max-w-[min(20rem,calc(100vw-6rem))] cursor-pointer appearance-none truncate rounded-md border border-transparent bg-transparent py-1 pr-9 pl-2 text-2xl font-semibold tracking-tight text-foreground sm:text-3xl"
      >
        {sites.map((site) => (
          <option key={site.id} value={site.id}>
            {site.displayName}
          </option>
        ))}
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute right-2 size-5 text-foreground-muted"
      />
    </span>
  );
}

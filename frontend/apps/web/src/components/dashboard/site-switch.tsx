'use client';

import { ChevronDown } from 'lucide-react';
import { useTranslations } from 'next-intl';
import type { Site } from '@/lib/api/schemas';

interface SiteSwitchProps {
  readonly sites: readonly Site[];
  readonly chosen: Site;
  readonly onChoose: (siteId: string) => void;
  readonly onAdd: () => void;
}

/**
 * What choosing the last entry means.
 *
 * A word rather than an identifier, and a word no identifier can ever be: every website is named
 * by a generated identifier of a fixed shape, so there is nothing for this to collide with.
 */
const ADD = 'add';

/**
 * Which website the screen is about, and where another one is added.
 *
 * It is the browser's own list rather than one built here: it opens correctly on a phone, it is
 * reachable by keyboard and by voice without anything being wired up, and a hundred websites
 * scroll in it. Adding sits at the end of the same list rather than beside it, because somebody
 * looking for a website they have not added yet looks where the websites are.
 *
 * Shown even when there is only one website, which is the whole point: an installation with one
 * website is exactly the one that needs somewhere to add a second.
 */
export function SiteSwitch({ sites, chosen, onChoose, onAdd }: SiteSwitchProps) {
  const t = useTranslations('dashboard.site');

  return (
    <span className="relative flex min-w-0 flex-1 items-center sm:flex-none">
      <select
        aria-label={t('label')}
        value={chosen.id}
        onChange={(event) => (event.target.value === ADD ? onAdd() : onChoose(event.target.value))}
        className="select-trigger w-full cursor-pointer appearance-none truncate rounded-md border border-border bg-surface py-1.5 pr-8 pl-3 text-sm font-medium text-foreground sm:w-auto sm:max-w-56"
      >
        {sites.map((site) => (
          <option key={site.id} value={site.id}>
            {site.displayName}
          </option>
        ))}
        <option value={ADD}>{t('add')}</option>
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute right-2.5 size-4 text-foreground-muted"
      />
    </span>
  );
}

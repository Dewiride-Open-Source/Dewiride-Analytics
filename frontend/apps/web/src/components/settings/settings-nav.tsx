'use client';

import { edition } from '@edition';
import { useTranslations } from 'next-intl';
import type { EditionSection } from '@/edition/contract';
import { Link, usePathname } from '@/i18n/navigation';
import { SETTINGS_SECTIONS } from '@/lib/routes';
import { cn } from '@/lib/styling';

/**
 * Every screen inside the account, the product's own first and then whatever the compiled edition
 * adds.
 *
 * Built once, outside the component, because neither half of it can change while the product is
 * running: the product's screens are a fixed list and the edition was decided when the bundle was
 * built.
 */
const SECTION_LINKS: readonly EditionSection[] = [
  ...SETTINGS_SECTIONS,
  ...edition.settingsSections,
];

/**
 * The way between the screens inside the account.
 *
 * Drawn as a row of pills sitting in a tray rather than as underlined tabs, so that it cannot be
 * mistaken for the bar above it: one says which part of the product you are in, and this says
 * which part of your account. It wraps rather than scrolling sideways, because two or three short
 * words fit on a phone and a row that had to be dragged would hide the last of them.
 */
export function SettingsNav() {
  const t = useTranslations();
  const here = usePathname();

  return (
    <nav aria-label={t('settings.sections.label')}>
      <ul className="flex w-fit flex-wrap items-center gap-1 rounded-lg border border-border bg-surface-muted/60 p-1">
        {SECTION_LINKS.map((section) => {
          const current = here === section.path;

          return (
            <li key={section.path}>
              <Link
                href={section.path}
                aria-current={current ? 'page' : undefined}
                className={cn(
                  'flex h-9 items-center rounded-md px-3 text-sm font-medium transition-colors',
                  'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-strong',
                  current
                    ? 'border border-border bg-surface text-foreground'
                    : 'border border-transparent text-foreground-muted hover:text-foreground',
                )}
              >
                {t(section.label)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

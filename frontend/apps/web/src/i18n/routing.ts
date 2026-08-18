import { defineRouting } from 'next-intl/routing';

/**
 * Which languages the dashboard is published in, and how they appear in the address.
 *
 * English is the only one filled in. The machinery around it is complete on purpose: every string
 * already comes from a catalogue and every route already carries a language, so publishing a
 * second language is a translation job rather than a rebuild of the interface.
 *
 * `as-needed` keeps English at the root of the address. A second language would appear under its
 * own prefix without moving a single English address.
 */
export const routing = defineRouting({
  locales: ['en'],
  defaultLocale: 'en',
  localePrefix: 'as-needed',
});

export type Locale = (typeof routing.locales)[number];

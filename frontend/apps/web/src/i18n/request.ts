import { hasLocale } from 'next-intl';
import { getRequestConfig } from 'next-intl/server';
import { routing } from './routing';

/**
 * Resolves the language for the request being rendered and loads its catalogue.
 *
 * An address naming a language nobody publishes falls back to the default rather than failing.
 * The alternative — a not-found page — turns a mistyped address into a dead end for somebody who
 * only wanted the dashboard.
 */
export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = hasLocale(routing.locales, requested) ? requested : routing.defaultLocale;

  return {
    locale,
    messages: (await import(`../../messages/${locale}.json`)).default,
  };
});

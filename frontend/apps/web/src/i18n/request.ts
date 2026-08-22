import { edition } from '@edition';
import { hasLocale } from 'next-intl';
import { getRequestConfig } from 'next-intl/server';
import { routing } from './routing';

/**
 * Resolves the language for the request being rendered and loads its catalogue.
 *
 * An address naming a language nobody publishes falls back to the default rather than failing.
 * The alternative — a not-found page — turns a mistyped address into a dead end for somebody who
 * only wanted the dashboard.
 *
 * The compiled edition's own wording is laid over the product's. It is kept in whichever repository
 * owns the screens that read it, so the open-source one carries no copy for a screen it does not
 * have — and the open-source edition adds nothing here, because it adds no screens.
 */
export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = hasLocale(routing.locales, requested) ? requested : routing.defaultLocale;

  return {
    locale,
    messages: {
      ...(await import(`../../messages/${locale}.json`)).default,
      ...(edition.messages[locale] ?? {}),
    },
  };
});

import type { Session } from '@/lib/api/schemas';

/** The three screens that exist, named once. */
export const DASHBOARD = '/';
export const SIGN_IN = '/sign-in';
export const SET_UP = '/set-up';

/** Where every capture surface files what it saw. Not a screen: the engine answers it. */
const COLLECT = '/collect';

/**
 * Every address that leads to a screen.
 *
 * An address outside this set is answered before anything is rendered, so a mistyped one never
 * reaches the framework's own bare page. A new screen has to be added here as well as given a
 * file, and a test fails if one is left out.
 */
export const SCREENS: ReadonlySet<string> = new Set([DASHBOARD, SIGN_IN, SET_UP]);

/**
 * Whether an address belongs to the engine rather than to a screen.
 *
 * Three things live behind the dashboard's own address: the data every screen reads, the
 * collector, and the image a page asks for when it cannot run the tracker. They are forwarded
 * rather than published separately, so that a website only ever has one address to be told about
 * and so that the sign-in cookie — which a browser returns only to the address that set it —
 * keeps working with no cross-origin arrangement to get wrong.
 *
 * @param pathname The address as it arrived, never carrying a language prefix.
 */
export function isEngineAddress(pathname: string): boolean {
  return pathname.startsWith('/api/') || pathname === COLLECT || pathname.startsWith(`${COLLECT}/`);
}

/**
 * Strips the language from the front of an address, leaving the screen it names.
 *
 * The default language has no prefix, so most addresses arrive without one and are returned
 * unchanged.
 *
 * @param pathname The address as it arrived.
 * @param locales The languages that may appear as a prefix.
 */
export function screenPath(pathname: string, locales: readonly string[]): string {
  for (const locale of locales) {
    if (pathname === `/${locale}`) {
      return DASHBOARD;
    }

    if (pathname.startsWith(`/${locale}/`)) {
      return pathname.slice(locale.length + 1);
    }
  }

  return pathname;
}

/** Whether an address, however it is prefixed, names a screen that exists. */
export function isScreen(pathname: string, locales: readonly string[]): boolean {
  const path = screenPath(pathname, locales);

  return SCREENS.has(path.length > 1 ? path.replace(/\/$/, '') : path);
}

/**
 * Where somebody in this state belongs, or nothing if they are already there.
 *
 * Written as a plain function of what is known rather than as a chain of effects inside the
 * screens, so that the rules are all visible at once and can be checked without a browser.
 *
 * @param session What the engine reported about this install and this person.
 * @param pathname The address currently being shown, without any language prefix.
 * @returns The address to move to, or null to stay.
 */
export function destinationFor(session: Session, pathname: string): string | null {
  if (!session.setupCompleted) {
    return pathname === SET_UP ? null : SET_UP;
  }

  if (!session.user) {
    return pathname === SIGN_IN ? null : SIGN_IN;
  }

  return pathname === SIGN_IN || pathname === SET_UP ? DASHBOARD : null;
}

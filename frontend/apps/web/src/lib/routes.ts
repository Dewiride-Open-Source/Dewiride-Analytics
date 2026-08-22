import type { Session } from '@/lib/api/schemas';

/**
 * Every screen that exists, named once.
 *
 * All of them sit under one segment rather than at the root, so that the root is free for
 * whatever a deployment chooses to put in front of the product. The address a measured website
 * reports to is unaffected by this: the beacon and the collector are the engine's addresses, not
 * screens, and they stay where they are.
 */
export const DASHBOARD = '/app';
export const JOURNEYS = '/app/journeys';
export const SIGN_IN = '/app/sign-in';
export const SIGN_UP = '/app/sign-up';
export const SET_UP = '/app/set-up';
export const FORGOT_PASSWORD = '/app/forgot-password';
export const RESET_PASSWORD = '/app/reset-password';
export const JOIN = '/app/join';
export const SETTINGS = '/app/settings';
export const SETTINGS_YOU = '/app/settings/you';
export const PLAN = '/app/settings/plan';

/** The address the product is reached from, which names no screen of its own. */
const ROOT = '/';

/** Where every capture surface files what it saw. Not a screen: the engine answers it. */
const COLLECT = '/collect';

/**
 * Everything a website in front of the product publishes that is not one of its pages.
 *
 * Two Next.js applications on one address would both ask the browser for their compiled files
 * under `/_next`, and the first to answer would hand the other's pages the wrong bundle — so the
 * website publishes all of its under one prefix, and everything else it serves as a file lives
 * behind the same prefix so that one rule reaches all of it.
 */
const SITE_ASSETS = '/site-assets';

/**
 * The two files a website has to publish at the root of an address to be indexed properly.
 *
 * They cannot move behind the prefix above — a crawler looks for them at the root and nowhere
 * else — so they are named here instead.
 */
const SITE_FILES: ReadonlySet<string> = new Set(['/robots.txt', '/sitemap.xml']);

/**
 * Every address that leads to a screen.
 *
 * An address outside this set is answered before anything is rendered, so a mistyped one never
 * reaches the framework's own bare page. A new screen has to be added here as well as given a
 * file, and a test fails if one is left out.
 */
export const SCREENS: ReadonlySet<string> = new Set([
  DASHBOARD,
  JOURNEYS,
  SIGN_IN,
  SIGN_UP,
  SET_UP,
  FORGOT_PASSWORD,
  RESET_PASSWORD,
  JOIN,
  SETTINGS,
  SETTINGS_YOU,
  PLAN,
]);

/**
 * The screens somebody who is not signed in is allowed to be on.
 *
 * Five doors rather than one, because the ways into an account are not all the same act: signing
 * in, creating one, asking for a way back, taking it, and accepting an invitation somebody sent.
 * Everything else waits behind them.
 */
const DOORS: ReadonlySet<string> = new Set([
  SIGN_IN,
  SIGN_UP,
  FORGOT_PASSWORD,
  RESET_PASSWORD,
  JOIN,
]);

/**
 * The doors a signed-in person has no use for, and is moved off.
 *
 * Choosing a new password is not among them. Somebody may already be signed in on this device and
 * still be following a link sent to their mailbox, and turning them away at that moment would
 * leave the link they were sent unusable for no reason they could see.
 */
const SPENT: ReadonlySet<string> = new Set([SIGN_IN, SIGN_UP, SET_UP]);

/**
 * The screens somebody moves between once they are signed in, in the order the bar lists them.
 *
 * Two questions about a website — how much traffic it had, and who each of its visitors was — and
 * then everything about the account itself. Journeys is a screen of its own rather than a card at
 * the foot of the first, because it is the one people come back to and work through rather than
 * glance at; the account is one because nobody visits it daily and a bar that grew a tab for every
 * setting would push the two that matter to the edge.
 */
export const SECTIONS = [
  { path: DASHBOARD, name: 'overview' },
  { path: JOURNEYS, name: 'journeys' },
  { path: SETTINGS, name: 'settings' },
] as const;

/**
 * The screens inside the account, in the order they are listed.
 *
 * The compiled edition appends its own, so an installation somebody runs themselves has two and
 * the hosted service has three. The label is a key into the merged catalogue rather than a string,
 * because every word anybody reads comes from a catalogue.
 */
export const SETTINGS_SECTIONS = [
  { path: SETTINGS, label: 'settings.sections.account' },
  { path: SETTINGS_YOU, label: 'settings.sections.you' },
] as const;

/**
 * Which section of the product an address belongs to, or nothing where it belongs to none.
 *
 * The longest section whose path the address sits under, so that a screen inside the account marks
 * the account. The first section is matched exactly rather than as a prefix, because every address
 * in the product begins with it — sitting under it says nothing, and treating it as a prefix would
 * mark the numbers as the current screen while somebody was signing in.
 *
 * Written here and tested without a browser, because the bar at the top is the one thing on every
 * screen and an address that highlighted the wrong tab would be wrong everywhere at once.
 *
 * @param pathname The address being shown, without any language prefix.
 */
export function currentSection(pathname: string): string | null {
  let longest: string | null = null;

  for (const section of SECTIONS) {
    const inside =
      section.path === DASHBOARD
        ? pathname === section.path
        : pathname === section.path || pathname.startsWith(`${section.path}/`);

    if (inside && (longest === null || section.path.length > longest.length)) {
      longest = section.path;
    }
  }

  return longest;
}

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
 * Whether an address is a file the website in front of the product serves rather than a page.
 *
 * Written separately from the rule that recognises a screen because the two are told apart by
 * different things. A screen is one of a fixed list of addresses; these are shaped like files, and
 * the screen rule deliberately passes over anything with a full stop in it so that a request for a
 * file is never given a language prefix.
 *
 * @param pathname The address as it arrived, never carrying a language prefix.
 */
export function isSiteFile(pathname: string): boolean {
  return (
    pathname === SITE_ASSETS || pathname.startsWith(`${SITE_ASSETS}/`) || SITE_FILES.has(pathname)
  );
}

/**
 * Strips the language from the front of an address, leaving the screen it names.
 *
 * The default language has no prefix, so most addresses arrive without one and are returned
 * unchanged. A bare prefix names the root, which is not a screen.
 *
 * @param pathname The address as it arrived.
 * @param locales The languages that may appear as a prefix.
 */
export function screenPath(pathname: string, locales: readonly string[]): string {
  for (const locale of locales) {
    if (pathname === `/${locale}`) {
      return ROOT;
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
    return DOORS.has(pathname) ? null : SIGN_IN;
  }

  return SPENT.has(pathname) ? DASHBOARD : null;
}

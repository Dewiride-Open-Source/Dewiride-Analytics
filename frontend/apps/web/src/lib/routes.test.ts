import { readdirSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { routing } from '@/i18n/routing';
import type { Session } from '@/lib/api/schemas';
import {
  currentSection,
  DASHBOARD,
  destinationFor,
  FORGOT_PASSWORD,
  JOIN,
  JOURNEYS,
  isEngineAddress,
  isScreen,
  isSiteFile,
  PLAN,
  RESET_PASSWORD,
  SCREENS,
  SET_UP,
  SETTINGS,
  SETTINGS_YOU,
  SIGN_IN,
  SIGN_UP,
} from '@/lib/routes';

/**
 * Every address a page file under one directory would answer, however deep it sits.
 *
 * A directory holding only more directories is a segment rather than a screen — the account is
 * one — so only the ones carrying a page of their own are counted.
 */
function screensUnder(directory: string, address: string): string[] {
  return readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .flatMap((entry) => {
      const inside = path.join(directory, entry.name);
      const here = `${address}/${entry.name}`;
      const own = readdirSync(inside).includes('page.tsx') ? [here] : [];

      return [...own, ...screensUnder(inside, here)];
    });
}

const somebody = {
  id: '0195f7e0-0000-7000-8000-000000000000',
  emailAddress: 'a@b.test',
  displayName: 'A',
};

function session(overrides: Partial<Session>): Session {
  return { setupCompleted: true, user: null, token: 'proof', ...overrides };
}

describe('where somebody belongs', () => {
  it('sends anybody to the setup screen while the install has no owner', () => {
    expect(destinationFor(session({ setupCompleted: false }), DASHBOARD)).toBe(SET_UP);
    expect(destinationFor(session({ setupCompleted: false }), SIGN_IN)).toBe(SET_UP);
  });

  it('leaves somebody on the setup screen once they are already there', () => {
    expect(destinationFor(session({ setupCompleted: false }), SET_UP)).toBeNull();
  });

  it('sends a signed-out visitor to the sign-in screen', () => {
    expect(destinationFor(session({ user: null }), DASHBOARD)).toBe(SIGN_IN);
  });

  it('refuses the setup screen once the install has an owner', () => {
    expect(destinationFor(session({ user: null }), SET_UP)).toBe(SIGN_IN);
  });

  it.each([SIGN_IN, SIGN_UP, FORGOT_PASSWORD, RESET_PASSWORD])(
    'leaves a signed-out visitor on %s',
    (door) => {
      expect(destinationFor(session({ user: null }), door)).toBeNull();
    },
  );

  it('moves a signed-in person off the screens they no longer need', () => {
    expect(destinationFor(session({ user: somebody }), SIGN_IN)).toBe(DASHBOARD);
    expect(destinationFor(session({ user: somebody }), SIGN_UP)).toBe(DASHBOARD);
    expect(destinationFor(session({ user: somebody }), SET_UP)).toBe(DASHBOARD);
  });

  /**
   * Somebody may already be signed in on this device and still be following a link sent to their
   * mailbox. Turning them away at that moment would spend the link for nothing.
   */
  it('lets a signed-in person finish choosing a new password', () => {
    expect(destinationFor(session({ user: somebody }), RESET_PASSWORD)).toBeNull();
    expect(destinationFor(session({ user: somebody }), FORGOT_PASSWORD)).toBeNull();
  });

  it('leaves a signed-in person wherever else they are', () => {
    expect(destinationFor(session({ user: somebody }), DASHBOARD)).toBeNull();
    expect(destinationFor(session({ user: somebody }), PLAN)).toBeNull();
    expect(destinationFor(session({ user: somebody }), '/somewhere-else')).toBeNull();
  });

  /**
   * What an account is entitled to is nobody's business until they have proved who they are, so
   * the screen sits behind the gate with everything else rather than beside the sign-in form.
   */
  it('keeps a signed-out visitor away from the plan screen', () => {
    expect(destinationFor(session({ user: null }), PLAN)).toBe(SIGN_IN);
  });
});

describe('which addresses name a screen', () => {
  it.each([DASHBOARD, SIGN_IN, SIGN_UP, SET_UP, FORGOT_PASSWORD, RESET_PASSWORD, PLAN])(
    'recognises %s',
    (screen) => {
      expect(isScreen(screen, routing.locales)).toBe(true);
    },
  );

  it.each(['/en/app', '/en/app/sign-in', '/en/app/reset-password'])(
    'recognises %s behind a language',
    (screen) => {
      expect(isScreen(screen, ['en'])).toBe(true);
    },
  );

  it('ignores a trailing slash', () => {
    expect(isScreen('/app/sign-in/', routing.locales)).toBe(true);
  });

  it.each(['/', '/en', '/nowhere', '/app/sign', '/app/sign-in/extra', '/a/b/c', '/appendix'])(
    'does not recognise %s',
    (typed) => {
      expect(isScreen(typed, ['en'])).toBe(false);
    },
  );

  /**
   * A screen with a file but no entry in the list is unreachable: the address is turned away
   * before the file is ever looked for. This is what stops that happening silently.
   *
   * The whole tree is walked rather than its first level, because the screens inside the account
   * are a segment deeper and a check that stopped at the top would pass while missing all of them.
   */
  it('lists every screen that has a file', () => {
    const segment = path.join(import.meta.dirname, '..', 'app', '[locale]', 'app');

    expect([...SCREENS].sort()).toEqual([DASHBOARD, ...screensUnder(segment, DASHBOARD)].sort());
  });
});

describe('what the engine answers rather than the dashboard', () => {
  it.each(['/api/session', '/api/sites/1/overview', '/collect', '/collect/pixel.gif'])(
    'forwards %s',
    (address) => {
      expect(isEngineAddress(address)).toBe(true);
    },
  );

  /**
   * The beacon is a file the dashboard serves itself, and the collector's name must not be able to
   * shadow a screen that merely begins with the same letters.
   */
  it.each(['/', '/app/sign-in', '/dw.js', '/collections', '/apiary'])(
    'answers %s itself',
    (address) => {
      expect(isEngineAddress(address)).toBe(false);
    },
  );
});

/**
 * The bar across the top is on every screen, so an address that marked the wrong tab would be
 * wrong everywhere at once. The rule is the longest section an address sits under, because every
 * screen sits under the first one.
 */
describe('which part of the product an address is in', () => {
  it.each([
    [DASHBOARD, DASHBOARD],
    [JOURNEYS, JOURNEYS],
    [SETTINGS, SETTINGS],
    [SETTINGS_YOU, SETTINGS],
    ['/app/settings/plan', SETTINGS],
  ])('puts %s in %s', (pathname, section) => {
    expect(currentSection(pathname)).toBe(section);
  });

  it.each([SIGN_IN, JOIN, '/nowhere'])('puts %s in no section at all', (pathname) => {
    expect(currentSection(pathname)).toBeNull();
  });
});

/**
 * The addresses a website in front of the product serves that are not pages of it.
 *
 * They are told apart from screens by a different rule, because the screen rule deliberately
 * passes over anything with a full stop in it — so every one of these has to be recognised here
 * and named in the proxy's matcher, or it never reaches the proxy at all.
 */
describe('what the website in front of the product serves', () => {
  it.each([
    '/site-assets',
    '/site-assets/_next/static/chunks/main.js',
    '/site-assets/icon.svg',
    '/robots.txt',
    '/sitemap.xml',
  ])('recognises %s as the website\u2019s', (address) => {
    expect(isSiteFile(address)).toBe(true);
  });

  /**
   * The prefix must not be able to shadow a screen that merely begins with the same letters, and
   * the two named files are exactly those two — not every file at the root.
   */
  it.each(['/', '/app', '/app/settings', '/site-assetsx/main.js', '/favicon.ico', '/dw.js'])(
    'leaves %s alone',
    (address) => {
      expect(isSiteFile(address)).toBe(false);
    },
  );
});

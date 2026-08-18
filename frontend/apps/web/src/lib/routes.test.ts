import { readdirSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { routing } from '@/i18n/routing';
import type { Session } from '@/lib/api/schemas';
import { DASHBOARD, destinationFor, isScreen, SCREENS, SET_UP, SIGN_IN } from '@/lib/routes';

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

  it('leaves a signed-out visitor on the sign-in screen', () => {
    expect(destinationFor(session({ user: null }), SIGN_IN)).toBeNull();
  });

  it('moves a signed-in person off the two screens they no longer need', () => {
    expect(destinationFor(session({ user: somebody }), SIGN_IN)).toBe(DASHBOARD);
    expect(destinationFor(session({ user: somebody }), SET_UP)).toBe(DASHBOARD);
  });

  it('leaves a signed-in person wherever else they are', () => {
    expect(destinationFor(session({ user: somebody }), DASHBOARD)).toBeNull();
    expect(destinationFor(session({ user: somebody }), '/somewhere-else')).toBeNull();
  });
});

describe('which addresses name a screen', () => {
  it.each([DASHBOARD, SIGN_IN, SET_UP])('recognises %s', (screen) => {
    expect(isScreen(screen, routing.locales)).toBe(true);
  });

  it.each(['/en', '/en/sign-in', '/en/set-up'])('recognises %s behind a language', (screen) => {
    expect(isScreen(screen, ['en'])).toBe(true);
  });

  it('ignores a trailing slash', () => {
    expect(isScreen('/sign-in/', routing.locales)).toBe(true);
  });

  it.each(['/nowhere', '/sign', '/sign-in/extra', '/a/b/c', '/enormous'])(
    'does not recognise %s',
    (typed) => {
      expect(isScreen(typed, ['en'])).toBe(false);
    },
  );

  /**
   * A screen with a file but no entry in the list is unreachable: the address is turned away
   * before the file is ever looked for. This is what stops that happening silently.
   */
  it('lists every screen that has a file', () => {
    const segment = path.join(import.meta.dirname, '..', 'app', '[locale]');
    const onDisk = readdirSync(segment, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => `/${entry.name}`);

    expect([...SCREENS].sort()).toEqual([DASHBOARD, ...onDisk].sort());
  });
});

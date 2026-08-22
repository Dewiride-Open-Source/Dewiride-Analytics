import { edition } from '@edition';
import { describe, expect, it } from 'vitest';
import { routing } from '@/i18n/routing';
import { SCREENS } from '@/lib/routes';
import product from '../../messages/en.json';

/**
 * Proves the seam resolved, and resolved to the right side of it.
 *
 * A misconfigured alias would not fail the build — it would quietly bring in whichever module
 * happened to be reachable, and the first sign of it would be a commercial screen appearing in the
 * open-source product.
 */
describe('the compiled edition', () => {
  it('is the open-source one, in this repository', () => {
    expect(edition.name).toBe('community');
  });

  /**
   * Not an omission. An installation somebody runs themselves is claimed once by whoever put it
   * there; a form that let a passer-by create an account on it would be a way in.
   */
  it('offers no way for a stranger to create an account', () => {
    expect(edition.signUp).toBeNull();
  });

  /**
   * Also not an omission. An installation somebody runs themselves measures whatever they point at
   * it, so there is no allowance to show them, nothing that could run out, and nothing to warn them
   * about above every screen.
   */
  it('shows no allowance and says nothing above the screens', () => {
    expect(edition.plan).toBeNull();
    expect(edition.notice).toBeNull();
    expect(edition.settingsSections).toEqual([]);
  });

  /**
   * A section pointing at an address the product does not answer would be a link to a redirect.
   * Every screen exists in both editions; which of them has anything to put on one does not.
   */
  it('only leads to screens the product declares', () => {
    for (const section of edition.settingsSections) {
      expect(SCREENS.has(section.path)).toBe(true);
    }
  });

  /**
   * An edition's wording is laid over the product's one level deep, so a name used by both would
   * take the product's whole section with it — every sentence under it would come back as its own
   * key on somebody's screen. Nothing to see in the open-source edition, which adds no wording;
   * the commercial one runs this suite too.
   */
  it('adds no wording under a name the product already uses', () => {
    const taken = new Set(Object.keys(product));

    const clashes = routing.locales.flatMap((locale) =>
      Object.keys(edition.messages[locale] ?? {}).filter((name) => taken.has(name)),
    );

    expect(clashes).toEqual([]);
  });
});

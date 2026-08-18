// @vitest-environment node
import { gzipSync } from 'node:zlib';
import { describe, expect, it } from 'vitest';
import { bundle, OLDEST_BROWSERS } from '../build.mjs';

/**
 * The size of the compiled beacon.
 *
 * Compiled here rather than read off the disk, so the figure is always the one this source
 * produces and never one left over from a build somebody ran a fortnight ago.
 */

/** What the beacon may cost a page, compressed, in bytes. */
const BUDGET = 2048;

describe('the compiled beacon', () => {
  it('costs a page less than the agreed budget', async () => {
    const { code } = await bundle();
    const transported = gzipSync(code).length;

    expect(
      transported,
      `the beacon is ${transported} bytes compressed, over the ${BUDGET}-byte budget`,
    ).toBeLessThanOrEqual(BUDGET);
  });

  /**
   * Syntax a browser cannot read fails before any check inside the file can run, so an accidental
   * bump of the floor does not degrade — it silently stops measuring a whole class of visitor.
   * These two spellings are the ones this source uses that the floor predates.
   */
  it('is written in syntax the oldest browsers it names can read', async () => {
    expect(OLDEST_BROWSERS).toContain('safari11.1');

    const { code } = await bundle();

    expect(code, 'a shortcut for a missing value was left in').not.toMatch(/\?\./);
    expect(code, 'a fallback for a missing value was left in').not.toMatch(/\?\?/);
  });

  /**
   * A browser only says which script is running while a plain one runs. Compiled as a module the
   * answer is nothing at all, the beacon never finds its own tag, and every page reports nothing.
   */
  it('is a plain script rather than a module, or it could not find its own tag', async () => {
    const { code } = await bundle();

    expect(code).toContain('(()=>{');
    expect(code).not.toMatch(/^\s*export[\s{]/m);
  });

  it('carries its licence, which minifying must not remove', async () => {
    const { code } = await bundle();

    expect(code).toContain('MIT');
  });

  it('declares nothing on the page beyond the two marks it needs', async () => {
    const { code } = await bundle();

    expect(code.match(/__dw\w+/g)?.sort()).toStrictEqual(['__dwHistory', '__dwMeasuring']);
  });
});

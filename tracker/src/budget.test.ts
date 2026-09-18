// @vitest-environment node
import { gzipSync } from 'node:zlib';
import { beforeAll, describe, expect, it } from 'vitest';
import { bundle, OLDEST_BROWSERS } from '../build.mjs';

/**
 * The size of the compiled beacon.
 *
 * Compiled here rather than read off the disk, so the figure is always the one this source
 * produces and never one left over from a build somebody ran a fortnight ago.
 */

/** What the beacon may cost a page, compressed, in bytes. */
const BUDGET = 2048;

/**
 * How long compiling may take before something is wrong.
 *
 * The first compile also starts the compiler, a separate program, and on a machine already running
 * every other suite that start alone can outlast the default allowance — so the allowance says what
 * the work is, rather than turning a busy machine into a failing test.
 */
const COMPILE_TIMEOUT = 60_000;

describe('the compiled beacon', () => {
  /** The beacon as a page would receive it. One compile serves every check below. */
  let code: string;

  beforeAll(async () => {
    ({ code } = await bundle());
  }, COMPILE_TIMEOUT);

  it('costs a page less than the agreed budget', () => {
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
  it('is written in syntax the oldest browsers it names can read', () => {
    expect(OLDEST_BROWSERS).toContain('safari11.1');

    expect(code, 'a shortcut for a missing value was left in').not.toMatch(/\?\./);
    expect(code, 'a fallback for a missing value was left in').not.toMatch(/\?\?/);
  });

  /**
   * A browser only says which script is running while a plain one runs. Compiled as a module the
   * answer is nothing at all, the beacon never finds its own tag, and every page reports nothing.
   */
  it('is a plain script rather than a module, or it could not find its own tag', () => {
    expect(code).toContain('(()=>{');
    expect(code).not.toMatch(/^\s*export[\s{]/m);
  });

  it('carries its licence, which minifying must not remove', () => {
    expect(code).toContain('MIT');
  });

  it('declares nothing on the page beyond the two marks it needs', () => {
    expect(code.match(/__dw\w+/g)?.sort()).toStrictEqual(['__dwHistory', '__dwMeasuring']);
  });
});

import { describe, expect, it } from 'vitest';
import { readablePath } from '@/lib/analytics/pages';

/** Every character Unicode marks as controlling the direction of text, and how each is stored. */
const DIRECTION_CONTROLS = [
  { point: 'U+061C', written: '%D8%9C' },
  { point: 'U+200E', written: '%E2%80%8E' },
  { point: 'U+200F', written: '%E2%80%8F' },
  { point: 'U+202A', written: '%E2%80%AA' },
  { point: 'U+202B', written: '%E2%80%AB' },
  { point: 'U+202C', written: '%E2%80%AC' },
  { point: 'U+202D', written: '%E2%80%AD' },
  { point: 'U+202E', written: '%E2%80%AE' },
  { point: 'U+2066', written: '%E2%81%A6' },
  { point: 'U+2067', written: '%E2%81%A7' },
  { point: 'U+2068', written: '%E2%81%A8' },
  { point: 'U+2069', written: '%E2%81%A9' },
];

describe('showing the address of a page', () => {
  it('leaves an ordinary address exactly as it is', () => {
    expect(readablePath('/posts/hello-world')).toBe('/posts/hello-world');
  });

  it('calls the front page the front page', () => {
    expect(readablePath('')).toBe('/');
  });

  it('turns letters written as codes back into letters', () => {
    expect(readablePath('/blog/caf%C3%A9')).toBe('/blog/café');
    expect(readablePath('/%E3%81%93%E3%82%93%E3%81%AB%E3%81%A1%E3%81%AF')).toBe('/こんにちは');
  });

  /**
   * A slash written as a code is part of a page's name, not a step down into a folder. Turning it
   * back would let anybody who can request a page make it appear somewhere on the site it is not.
   */
  it('does not turn a written-out separator into a real one', () => {
    expect(readablePath('/pricing%2F..%2Fadmin')).toBe('/pricing%2F..%2Fadmin');
    expect(readablePath('/search%3Fq%3Dhello')).toBe('/search%3Fq%3Dhello');
  });

  it('shows an address that was written wrongly exactly as it arrived', () => {
    expect(readablePath('/broken%zz')).toBe('/broken%zz');
    expect(readablePath('/half%C3')).toBe('/half%C3');
  });

  it('turns letters written right to left back into letters', () => {
    expect(readablePath('/%D8%B3%D9%84%D8%A7%D9%85')).toBe('/سلام');
  });

  /**
   * A character that changes the direction of the text is invisible, and one placed part-way
   * through a page's name turns the rest of it around: "/invoice", the control, then "fdp.exe" is
   * drawn as "/invoiceexe.pdf". Kept as a code, the address reads in the order it was asked for.
   */
  it('leaves a character that changes the direction of the text written as a code', () => {
    expect(readablePath('/invoice%E2%80%AEfdp.exe')).toBe('/invoice%E2%80%AEfdp.exe');
  });

  it.each(DIRECTION_CONTROLS)(
    'writes $point as a code whether it arrived as one or as itself',
    ({ point, written }) => {
      const itself = String.fromCodePoint(Number.parseInt(point.slice(2), 16));

      expect(readablePath(`/caf%C3%A9${written}menu`)).toBe(`/café${written}menu`);
      expect(readablePath(`/café${itself}menu`)).toBe(`/café${written}menu`);
    },
  );

  /** An address written wrongly is shown as it arrived, but never with the power to reorder itself. */
  it('writes such a character as a code even in an address that was written wrongly', () => {
    expect(readablePath('/broken%zz\u202Efdp.exe')).toBe('/broken%zz%E2%80%AEfdp.exe');
  });
});

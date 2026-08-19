import { describe, expect, it } from 'vitest';
import { readablePath } from '@/lib/analytics/pages';

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
});

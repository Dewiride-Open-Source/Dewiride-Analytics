import { describe, expect, it } from 'vitest';
import { countryNames } from '@/lib/analytics/places';

describe('writing a country out for somebody to read', () => {
  it('turns a stored code into a name', () => {
    const named = countryNames('en');

    expect(named('IN')).toBe('India');
    expect(named('GB')).toBe('United Kingdom');
  });

  /**
   * The whole reason the code is what gets stored: one row, read in whatever language somebody
   * happens to be using.
   */
  it('writes the same country differently for a different reader', () => {
    expect(countryNames('fr')('DE')).toBe('Allemagne');
    expect(countryNames('en')('DE')).toBe('Germany');
  });

  it('accepts a code however it was written', () => {
    expect(countryNames('en')('in')).toBe('India');
  });

  /**
   * The engine sends an empty code for an address it could not place. That row says so in words,
   * and it must never fall through to showing a reader an empty string or a raw code.
   */
  it.each(['', ' ', 'X', 'XYZ', 'IN1', '12'])('has no name for %p', (code) => {
    expect(countryNames('en')(code)).toBeNull();
  });

  /**
   * A code that is shaped like a country and is not one is handed straight back by the runtime.
   * Passing that through would put two letters nobody recognises on the screen.
   */
  it('has no name for two letters that name no country', () => {
    expect(countryNames('en')('ZY')).toBeNull();
  });

  /**
   * The standard reserves one code for a region nobody could establish, and it has a name —
   * "Unknown Region" — which would otherwise appear on the screen as though it were a place
   * somebody was, beside India and the United Kingdom.
   */
  it('treats the code that means nowhere as no country at all', () => {
    expect(countryNames('en')('ZZ')).toBeNull();
  });
});

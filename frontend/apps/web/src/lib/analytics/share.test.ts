import { describe, expect, it } from 'vitest';
import { shareOf } from '@/lib/analytics/share';

describe('working out a share', () => {
  it('answers with the part over the whole', () => {
    expect(shareOf(3, 4)).toBe(0.75);
  });

  it('answers with nothing rather than dividing by nothing', () => {
    expect(shareOf(0, 0)).toBe(0);
  });
});

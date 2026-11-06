import { createFormatter } from 'next-intl';
import { describe, expect, it } from 'vitest';
import { writeShare } from '@/components/dashboard/ranked-list';

const format = createFormatter({ locale: 'en' });

describe('how much of a whole one row accounts for', () => {
  /** A row that holds something never reads as nought. */
  it.each([
    [80, 252_304, '0.03%'],
    [1, 3000, '0.03%'],
    [1, 1999, '0.05%'],
    [1, 1000, '0.1%'],
    [44, 10_000, '0.4%'],
    [9, 1000, '0.9%'],
  ])('writes %i of %i, too small to round, as its first digit: %s', (part, whole, written) => {
    expect(writeShare(part, whole, format)).toBe(written);
  });

  /** Nor does a row beside one that holds something read as the whole. */
  it.each([
    [995, 1000, '99.5%'],
    [999, 1000, '99.9%'],
    [9999, 10_000, '99.9%'],
  ])(
    'writes %i of %i, just short of the whole, as just short of it: %s',
    (part, whole, written) => {
      expect(writeShare(part, whole, format)).toBe(written);
    },
  );

  it.each([
    [0, 100, '0%'],
    [0, 0, '0%'],
    [100, 100, '100%'],
    [1, 100, '1%'],
    [19, 100, '19%'],
    [994, 1000, '99%'],
  ])('writes %i of %i as a whole per cent: %s', (part, whole, written) => {
    expect(writeShare(part, whole, format)).toBe(written);
  });
});

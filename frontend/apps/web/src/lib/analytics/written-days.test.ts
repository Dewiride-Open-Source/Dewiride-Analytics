import { createFormatter } from 'next-intl';
import { describe, expect, it } from 'vitest';
import { writtenDays } from '@/lib/analytics/written-days';

const FORMAT = createFormatter({ locale: 'en' });

/** Normalises the thin spaces the reader's language puts around a dash, so a test can name them. */
function plainly(written: string): string {
  return written.replace(/\s+/g, ' ');
}

describe('a run of days, written', () => {
  it('names its first and last day, with the year once', () => {
    expect(
      plainly(writtenDays({ first: '2026-06-14', last: '2026-07-13' }, 'Asia/Kolkata', FORMAT)),
    ).toBe('Jun 14 – Jul 13, 2026');
  });

  it('names a single day as the one date', () => {
    expect(writtenDays({ first: '2026-08-10', last: '2026-08-10' }, 'Asia/Kolkata', FORMAT)).toBe(
      'Aug 10, 2026',
    );
  });

  it('writes the year on both sides where the days run across one', () => {
    expect(
      plainly(writtenDays({ first: '2025-12-29', last: '2026-01-04' }, 'Asia/Kolkata', FORMAT)),
    ).toBe('Dec 29, 2025 – Jan 4, 2026');
  });

  /**
   * In Kolkata the first of August begins the evening before in UTC, and in Auckland half a day
   * before that. Each day is written from the midnight it begins at where the website is, so it is
   * the first of August whichever zone the website keeps its days in.
   */
  it.each(['Asia/Kolkata', 'America/New_York', 'Pacific/Auckland'])(
    'writes the website’s own days whatever the website’s zone, %s',
    (zone) => {
      expect(writtenDays({ first: '2026-08-01', last: '2026-08-01' }, zone, FORMAT)).toBe(
        'Aug 1, 2026',
      );
    },
  );
});

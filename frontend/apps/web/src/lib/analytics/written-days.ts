import type { createFormatter } from 'next-intl';
import { type DaySpan, spanInstants } from '@/lib/analytics/period';

/** What a screen is handed to write a moment in the reader's language. */
type Formatter = ReturnType<typeof createFormatter>;

/**
 * A run of days written the way the overview prints the days a choice works out to: day, month and
 * year, in the reader's language — `Jun 14 – Jul 13, 2026`, or the one date of a single day.
 *
 * Written in the website's own calendar, from the midnight each day begins at where the website is,
 * so a reader far enough east or west of it never sees a day turn into the one before or after it.
 * Never assembled from pieces: the reader's language puts the parts of a run of dates in its own
 * order and joins them with its own words.
 *
 * @param span The days in question.
 * @param timeZoneId The website's zone.
 * @param format The reader's own formatter.
 */
export function writtenDays(span: DaySpan, timeZoneId: string, format: Formatter): string {
  const { first, last } = spanInstants(span, timeZoneId);

  return format.dateTimeRange(first, last, {
    timeZone: timeZoneId,
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}

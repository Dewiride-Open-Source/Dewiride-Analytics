/**
 * The periods the dashboard can be looked at over, and the exact window each one means.
 *
 * A window is resolved here rather than left to the engine's defaults because two questions are
 * asked about the same period — the totals and the daily graph — and they have to agree. Reading
 * a graph whose days do not add up to the total above it is worse than not showing the graph.
 */

/** How many days back each period reaches. */
export const PERIODS = [7, 30] as const;

/** One of the periods the dashboard offers. */
export type PeriodDays = (typeof PERIODS)[number];

/** The default a screen opens on. */
export const DEFAULT_PERIOD: PeriodDays = 7;

/** An inclusive start and an exclusive end, written the way the engine reads them. */
export interface AnalyticsWindow {
  readonly from: string;
  readonly to: string;
}

const HOUR = 3_600_000;
const DAY = 86_400_000;

/**
 * The window a period covers for a site, in that site's own days.
 *
 * The start is pinned to midnight in the site's zone so that the first day of the graph is a
 * whole day. Left unpinned it would begin part-way through a day and draw a first column that
 * looks like a collapse in traffic rather than a window that started at lunchtime.
 *
 * The end is rounded up to the next hour. That keeps everything up to this moment inside the
 * window while giving the same answer for a whole hour, so a screen left open does not re-ask
 * the same question on every render.
 *
 * @param days How far back to reach.
 * @param timeZone The site's reporting zone.
 * @param now The moment to measure back from.
 * @returns The window to ask about.
 */
export function windowFor(days: PeriodDays, timeZone: string, now: Date): AnalyticsWindow {
  const to = new Date(Math.ceil(now.getTime() / HOUR) * HOUR);
  const from = startOfDayIn(timeZone, new Date(now.getTime() - (days - 1) * DAY));

  return { from: from.toISOString(), to: to.toISOString() };
}

/**
 * The moment a calendar day begins in a given zone.
 *
 * Resolved in two passes. The first pass places midnight using the offset in force right now,
 * which is wrong by an hour if the clocks changed between then and the day being asked about;
 * the second pass re-reads the offset at that first answer and places midnight again with it.
 */
function startOfDayIn(timeZone: string, moment: Date): Date {
  const [year, month, day] = calendarDayIn(timeZone, moment);
  const midnightAsUtc = Date.UTC(year, month - 1, day);
  const firstPass = midnightAsUtc - offsetAt(timeZone, moment);

  return new Date(midnightAsUtc - offsetAt(timeZone, new Date(firstPass)));
}

/** The year, month and day a moment falls on in a zone. */
function calendarDayIn(timeZone: string, moment: Date): readonly [number, number, number] {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(moment);

  const value = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((part) => part.type === type)?.value ?? '0');

  return [value('year'), value('month'), value('day')];
}

/** How far a zone stands from UTC at a given moment, in milliseconds. */
function offsetAt(timeZone: string, moment: Date): number {
  const named = new Intl.DateTimeFormat('en', {
    timeZone,
    timeZoneName: 'longOffset',
  })
    .formatToParts(moment)
    .find((part) => part.type === 'timeZoneName')?.value;

  const measured = /GMT([+-])(\d{1,2}):(\d{2})/.exec(named ?? '');

  if (!measured) {
    return 0;
  }

  const [, sign, hours, minutes] = measured;
  const size = Number(hours) * HOUR + Number(minutes) * 60_000;

  return sign === '-' ? -size : size;
}

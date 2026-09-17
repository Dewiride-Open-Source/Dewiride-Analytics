import { describe, expect, it } from 'vitest';
import {
  dayOf,
  daysIn,
  DEFAULT_PERIOD,
  granularityFor,
  isPreset,
  LONGEST_SPAN_DAYS,
  type Period,
  type PeriodPreset,
  PRESETS,
  previousSpan,
  previousWindow,
  problemWith,
  readPeriod,
  samePeriod,
  spanFor,
  spanInstants,
  todayIn,
  windowFor,
  windowForSpan,
  withChoices,
  writePeriod,
} from '@/lib/analytics/period';

/** Mid-afternoon in Kolkata, mid-morning in London, and nowhere near a day boundary. */
const NOW = new Date('2026-08-18T09:37:12Z');

/** One of the named periods, written the way a screen holds it. */
function named(preset: PeriodPreset): Period {
  return { kind: 'preset', preset };
}

const WEEK = named('last-7-days');
const MONTH = named('last-30-days');
const TODAY = named('today');
const YESTERDAY = named('yesterday');

describe('the window a period asks about', () => {
  it("starts at midnight in the site's own day, not at the moment of asking", () => {
    const { from } = windowFor(WEEK, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-08-11T18:30:00.000Z');
  });

  it('reaches back a whole month when asked for one', () => {
    const { from } = windowFor(MONTH, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-07-19T18:30:00.000Z');
  });

  /**
   * The end has to cover everything up to now, and has to be the same answer for a while: it names
   * the cached copy of every number on the screen, and one that moved with the clock would make
   * the dashboard re-ask the engine on every render.
   */
  it('runs to the top of the next hour, so the same question is asked all hour', () => {
    expect(windowFor(WEEK, 'Etc/UTC', NOW).to).toBe('2026-08-18T10:00:00.000Z');
    expect(windowFor(WEEK, 'Etc/UTC', new Date('2026-08-18T09:02:00Z')).to).toBe(
      '2026-08-18T10:00:00.000Z',
    );
  });

  /**
   * Rounded to the nearest hour at or after now, a period covering today would end where it began
   * at exactly midnight, and the engine would refuse a window with no length in it — so the one
   * moment of the day somebody is most likely to be looking at last night's traffic is the moment
   * the screen would break.
   */
  it('has length in it even when asked for at exactly midnight', () => {
    const { from, to } = windowFor(TODAY, 'Etc/UTC', new Date('2026-08-18T00:00:00Z'));

    expect(from).toBe('2026-08-18T00:00:00.000Z');
    expect(to).toBe('2026-08-18T01:00:00.000Z');
  });

  it('counts a day west of the meridian in that place, not in UTC', () => {
    const { from } = windowFor(WEEK, 'America/New_York', NOW);

    expect(from).toBe('2026-08-12T04:00:00.000Z');
  });

  /**
   * The clocks in New York went forward at 02:00 on 8 March 2026. Midnight that morning was still
   * on the old offset, so placing it with the offset in force later the same day lands an hour
   * early — which is the whole reason the offset is read twice.
   */
  it('places midnight on the day the clocks change with the offset that morning', () => {
    const { from } = windowFor(WEEK, 'America/New_York', new Date('2026-03-14T12:00:00Z'));

    expect(from).toBe('2026-03-08T05:00:00.000Z');
  });

  it('treats a zone it cannot measure as UTC rather than refusing to draw anything', () => {
    const { from } = windowFor(WEEK, 'Etc/UTC', NOW);

    expect(from).toBe('2026-08-12T00:00:00.000Z');
  });
});

describe('a period that has already finished', () => {
  it('stops at the end of its last day rather than at this moment', () => {
    const { from, to } = windowFor(YESTERDAY, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-08-16T18:30:00.000Z');
    expect(to).toBe('2026-08-17T18:30:00.000Z');
  });

  /** Nothing about it moves with the clock, so it can be cached for as long as anybody looks. */
  it('gives the same answer whenever it is asked for on the same day', () => {
    const morning = windowFor(YESTERDAY, 'Asia/Kolkata', NOW);
    const evening = windowFor(YESTERDAY, 'Asia/Kolkata', new Date('2026-08-18T12:00:00Z'));

    expect(evening).toStrictEqual(morning);
  });
});

describe('the days a named period covers', () => {
  it('is the one day for today', () => {
    expect(spanFor(TODAY, 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-08-18',
      last: '2026-08-18',
    });
  });

  it('is the whole of the month before for last month', () => {
    expect(spanFor(named('last-month'), 'Etc/UTC', NOW)).toStrictEqual({
      first: '2026-07-01',
      last: '2026-07-31',
    });
  });

  /**
   * A month is not a length. Counted as thirty-one days back from the thirty-first of March, last
   * month would begin on the twenty-eighth of February and report four fifths of it.
   */
  it('ends February on the twenty-eighth when asked on the thirty-first of March', () => {
    expect(spanFor(named('last-month'), 'Etc/UTC', new Date('2026-03-31T12:00:00Z'))).toStrictEqual(
      { first: '2026-02-01', last: '2026-02-28' },
    );
  });

  it('is a single day for this month on the first of it', () => {
    expect(daysIn(spanFor(named('this-month'), 'Etc/UTC', new Date('2026-08-01T05:00:00Z')))).toBe(
      1,
    );
  });

  it('is a single day for this year on the first of January', () => {
    expect(daysIn(spanFor(named('this-year'), 'Etc/UTC', new Date('2026-01-01T05:00:00Z')))).toBe(
      1,
    );
  });

  it('counts both ends of a run of days', () => {
    expect(daysIn(spanFor(WEEK, 'Etc/UTC', NOW))).toBe(7);
    expect(daysIn(spanFor(named('last-90-days'), 'Etc/UTC', NOW))).toBe(90);
  });
});

describe('a stretch somebody chose', () => {
  it('is the days they named, both included', () => {
    const chosen: Period = { kind: 'chosen', first: '2026-07-01', last: '2026-07-07' };

    expect(daysIn(spanFor(chosen, 'Etc/UTC', NOW))).toBe(7);
    expect(windowFor(chosen, 'Etc/UTC', NOW)).toStrictEqual({
      from: '2026-07-01T00:00:00.000Z',
      to: '2026-07-08T00:00:00.000Z',
    });
  });

  it('puts the two ends the right way round when they arrive the wrong way', () => {
    expect(
      spanFor({ kind: 'chosen', first: '2026-08-18', last: '2026-08-11' }, 'Etc/UTC', NOW),
    ).toStrictEqual({ first: '2026-08-11', last: '2026-08-18' });
  });

  it('stops at today rather than showing traffic that has not happened', () => {
    expect(
      spanFor({ kind: 'chosen', first: '2026-08-11', last: '2099-01-01' }, 'Etc/UTC', NOW),
    ).toStrictEqual({ first: '2026-08-11', last: '2026-08-18' });
  });

  it('reaches back no further than a website answers for', () => {
    const span = spanFor(
      { kind: 'chosen', first: '2000-01-01', last: '2026-08-18' },
      'Etc/UTC',
      NOW,
    );

    expect(daysIn(span)).toBe(LONGEST_SPAN_DAYS);
    expect(span.last).toBe('2026-08-18');
  });

  /** A date is one of the few things somebody can put in an address by hand. */
  it('still gives a workable period when a date cannot be read at all', () => {
    const span = spanFor({ kind: 'chosen', first: 'whenever', last: '2026-08-18' }, 'Etc/UTC', NOW);

    expect(daysIn(span)).toBe(LONGEST_SPAN_DAYS);
  });
});

describe('how finely a period is cut', () => {
  it('is by the hour for one day and for two', () => {
    expect(granularityFor(spanFor(TODAY, 'Etc/UTC', NOW))).toBe('hour');
    expect(
      granularityFor(
        spanFor({ kind: 'chosen', first: '2026-08-17', last: '2026-08-18' }, 'Etc/UTC', NOW),
      ),
    ).toBe('hour');
  });

  it('is by the day from three days upwards', () => {
    expect(
      granularityFor(
        spanFor({ kind: 'chosen', first: '2026-08-16', last: '2026-08-18' }, 'Etc/UTC', NOW),
      ),
    ).toBe('day');
    expect(granularityFor(spanFor(WEEK, 'Etc/UTC', NOW))).toBe('day');
  });
});

describe('what day it is where the site is', () => {
  it('is the day there, not the day where the reader is sitting', () => {
    const lateEvening = new Date('2026-08-18T19:00:00Z');

    expect(todayIn('Asia/Kolkata', lateEvening)).toBe('2026-08-19');
    expect(todayIn('America/New_York', lateEvening)).toBe('2026-08-18');
  });
});

describe('the day a moment falls in where the site is', () => {
  /** Half past six in the evening in London is already the next day in Kolkata. */
  it('is the next day east of the meridian once the evening there has begun', () => {
    expect(dayOf('Asia/Kolkata', new Date('2026-08-12T18:30:00Z'))).toBe('2026-08-13');
  });

  /** The clocks in London stand an hour ahead in August, and the day is cut where they stand. */
  it('follows the clocks as they stand that day rather than the zone’s winter offset', () => {
    expect(dayOf('Europe/London', new Date('2026-08-12T23:30:00Z'))).toBe('2026-08-13');
  });

  it('is that day at midnight where the zone is UTC', () => {
    expect(dayOf('Etc/UTC', new Date('2026-08-12T00:00:00Z'))).toBe('2026-08-12');
  });

  /** The instant a day begins at, written back out, is that day — the round trip a drill relies on. */
  it('gives back the day a bucket was cut at', () => {
    const { first } = spanInstants({ first: '2026-08-12', last: '2026-08-12' }, 'Asia/Kolkata');

    expect(dayOf('Asia/Kolkata', first)).toBe('2026-08-12');
  });
});

describe('the moments a run of days is written between', () => {
  /**
   * The only reason these exist is to be written back out. Taken from any other instant, a day is
   * the one before or the one after for a reader far enough east or west of the site.
   */
  it('are the midnights those days begin at where the site is', () => {
    const covered = spanInstants({ first: '2026-08-11', last: '2026-08-18' }, 'Asia/Kolkata');

    expect(covered.first.toISOString()).toBe('2026-08-10T18:30:00.000Z');
    expect(covered.last.toISOString()).toBe('2026-08-17T18:30:00.000Z');
  });

  it('write back out as the days they came from', () => {
    const covered = spanInstants({ first: '2026-08-11', last: '2026-08-18' }, 'Asia/Kolkata');
    const written = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });

    expect(written.format(covered.first)).toBe('2026-08-11');
    expect(written.format(covered.last)).toBe('2026-08-18');
  });
});

describe('a stretch that cannot be shown', () => {
  const TODAY = '2026-08-18';

  it('is nothing at all when the two days are a period', () => {
    expect(problemWith('2026-08-11', '2026-08-18', TODAY)).toBeNull();
    expect(problemWith(TODAY, TODAY, TODAY)).toBeNull();
  });

  it('says so when the far end comes first', () => {
    expect(problemWith('2026-08-18', '2026-08-11', TODAY)).toBe('backwards');
  });

  it('says so when the far end has not happened', () => {
    expect(problemWith('2026-08-11', '2026-08-19', TODAY)).toBe('future');
  });

  it('allows a year and a day, and refuses the day after that', () => {
    expect(problemWith('2026-01-01', '2027-01-01', '2027-12-31')).toBeNull();
    expect(problemWith('2026-01-01', '2027-01-02', '2027-12-31')).toBe('too-long');
  });

  it('says nothing readable when a box is still empty', () => {
    expect(problemWith('', TODAY, TODAY)).toBe('unreadable');
    expect(problemWith(TODAY, '', TODAY)).toBe('unreadable');
  });

  /**
   * Every impossible day matches the shape of a date, and the calendar is the only thing that
   * knows which of them exist. Read without checking, the thirtieth of February quietly becomes
   * the second of March and a period nobody asked for is drawn without a word.
   */
  it('refuses a day that is not on the calendar rather than moving it', () => {
    expect(problemWith('2026-02-30', TODAY, TODAY)).toBe('unreadable');
    expect(spanFor({ kind: 'chosen', first: '2026-02-30', last: TODAY }, 'Etc/UTC', NOW)).toEqual({
      first: '2025-08-18',
      last: TODAY,
    });
  });
});

describe('a period written into an address', () => {
  const CHOSEN: Period = { kind: 'chosen', first: '2026-08-01', last: '2026-08-14' };

  it('is a named period under the name it is offered by', () => {
    expect(writePeriod(WEEK)).toBe('last-7-days');
    expect(writePeriod(TODAY)).toBe('today');
  });

  it('is a chosen stretch as the two days it runs between', () => {
    expect(writePeriod(CHOSEN)).toBe('2026-08-01..2026-08-14');
  });

  /**
   * A link is only worth sending if it opens on what the sender was looking at, so every period a
   * screen can be on has to survive the round trip rather than most of them.
   */
  it('comes back as the period it went in as, whichever one it was', () => {
    for (const preset of PRESETS) {
      expect(readPeriod(writePeriod(named(preset)))).toEqual(named(preset));
    }

    expect(readPeriod(writePeriod(CHOSEN))).toEqual(CHOSEN);
  });

  it('recognises the periods offered by name and nothing else', () => {
    expect(isPreset('last-30-days')).toBe(true);
    expect(isPreset('last-31-days')).toBe(false);
  });

  /**
   * Two periods read out of an address are never the same object, so a screen comparing them as
   * objects would find every one different from the one before it.
   */
  it('is what tells two periods apart, rather than whether they are the same object', () => {
    expect(samePeriod(named('today'), { kind: 'preset', preset: 'today' })).toBe(true);
    expect(samePeriod(CHOSEN, { ...CHOSEN })).toBe(true);
    expect(samePeriod(CHOSEN, { ...CHOSEN, last: '2026-08-15' })).toBe(false);
    expect(samePeriod(TODAY, YESTERDAY)).toBe(false);
  });
});

describe('a period read back out of an address', () => {
  /**
   * Everything in an address is written by whoever sent the link. Nothing here is a period, and
   * every one of them has to leave the screen on the period it would have opened on anyway rather
   * than on a window nobody chose.
   */
  it('is nothing at all when the address is not a period', () => {
    expect(readPeriod('last-31-days')).toBeNull();
    expect(readPeriod('')).toBeNull();
    expect(readPeriod('2026-08-01')).toBeNull();
    expect(readPeriod('2026-08-01..')).toBeNull();
    expect(readPeriod('2026-08-01..2026-08-14..2026-08-20')).toBeNull();
    expect(readPeriod('<script>..</script>')).toBeNull();
  });

  it('refuses a day that is not on the calendar rather than moving it', () => {
    expect(readPeriod('2026-02-30..2026-03-05')).toBeNull();
    expect(readPeriod('2026-08-01..2026-13-01')).toBeNull();
  });
});

describe('the address of a screen carrying what is being looked at', () => {
  it('names the period being looked at', () => {
    expect(withChoices('/app/journeys', { period: TODAY, population: 'everybody' })).toBe(
      '/app/journeys?period=today',
    );
    expect(
      withChoices('/app', {
        period: { kind: 'chosen', first: '2026-08-01', last: '2026-08-14' },
        population: 'everybody',
      }),
    ).toBe('/app?period=2026-08-01..2026-08-14');
  });

  /**
   * An address that says what it would have said anyway is one more thing in the bar for nothing,
   * so the period every screen opens on and the people it counts leave no trace.
   */
  it('says nothing at all about what every screen opens on', () => {
    expect(withChoices('/app/journeys', { period: DEFAULT_PERIOD, population: 'everybody' })).toBe(
      '/app/journeys',
    );
  });

  it('names the people beside the period when the figures are kept to them', () => {
    expect(withChoices('/app/journeys', { period: TODAY, population: 'people' })).toBe(
      '/app/journeys?period=today&only=people',
    );
  });

  it('names the people alone on the usual period', () => {
    expect(withChoices('/app/journeys', { period: DEFAULT_PERIOD, population: 'people' })).toBe(
      '/app/journeys?only=people',
    );
  });
});

describe('the whole window a run of days covers', () => {
  /**
   * Closed at both ends whether or not the last of those days has finished, because it is the
   * calendar shape of a period rather than the part of it that has happened. The cutting back to
   * this moment belongs to the period being looked at, not to the days themselves.
   */
  it('runs from the first midnight to the one after the last day', () => {
    const { from, to } = windowForSpan({ first: '2026-08-11', last: '2026-08-18' }, 'Asia/Kolkata');

    expect(from).toBe('2026-08-10T18:30:00.000Z');
    expect(to).toBe('2026-08-18T18:30:00.000Z');
  });
});

describe('the days a period is measured against', () => {
  it('sets today beside yesterday', () => {
    expect(previousSpan(TODAY, 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-08-17',
      last: '2026-08-17',
    });
  });

  it('sets a run of days beside the same number of days immediately before it', () => {
    expect(previousSpan(WEEK, 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-08-05',
      last: '2026-08-11',
    });
  });

  it('sets a stretch somebody chose beside the same number of days before it', () => {
    const chosen: Period = { kind: 'chosen', first: '2026-08-01', last: '2026-08-14' };

    expect(previousSpan(chosen, 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-07-18',
      last: '2026-07-31',
    });
  });

  /** A period named after the calendar steps back by one of those, so the dates line up. */
  it('sets this month beside the same run of days in the month before', () => {
    expect(previousSpan(named('this-month'), 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-07-01',
      last: '2026-07-18',
    });
  });

  it('sets last month beside the whole of the month before it', () => {
    expect(previousSpan(named('last-month'), 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2026-06-01',
      last: '2026-06-30',
    });
  });

  it('sets this year beside the same stretch of the year before', () => {
    expect(previousSpan(named('this-year'), 'Asia/Kolkata', NOW)).toStrictEqual({
      first: '2025-01-01',
      last: '2025-08-18',
    });
  });

  /**
   * February has no thirty-first, and the thirty-first of February is not a date this product may
   * quietly turn into the third of March.
   */
  it('pulls a date back to the last day a shorter month has', () => {
    const lastOfMarch = new Date('2026-03-31T09:00:00Z');

    expect(previousSpan(named('this-month'), 'Asia/Kolkata', lastOfMarch)).toStrictEqual({
      first: '2026-02-01',
      last: '2026-02-28',
    });
  });

  it('does the same for a leap day, which the year before did not have', () => {
    const leapDay = new Date('2028-02-29T09:00:00Z');

    expect(previousSpan(named('this-year'), 'Asia/Kolkata', leapDay)).toStrictEqual({
      first: '2027-01-01',
      last: '2027-02-28',
    });
  });
});

describe('the window a period is measured against', () => {
  it('covers the whole of the earlier period once this one has finished', () => {
    const { from, to } = previousWindow(YESTERDAY, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-08-15T18:30:00.000Z');
    expect(to).toBe('2026-08-16T18:30:00.000Z');
  });

  /**
   * Today at ten in the morning has had ten hours in it and yesterday had twenty-four. Measured
   * against the whole of yesterday, every morning would report a collapse in traffic and every
   * night a recovery.
   */
  it('is cut to the same length as the part of this period that has happened', () => {
    const { from, to } = previousWindow(TODAY, 'Etc/UTC', NOW);

    expect(from).toBe('2026-08-17T00:00:00.000Z');
    expect(to).toBe('2026-08-17T10:00:00.000Z');
  });

  it('is exactly as long as the period it is measured against', () => {
    const current = windowFor(WEEK, 'Asia/Kolkata', NOW);
    const earlier = previousWindow(WEEK, 'Asia/Kolkata', NOW);

    expect(Date.parse(earlier.to) - Date.parse(earlier.from)).toBe(
      Date.parse(current.to) - Date.parse(current.from),
    );
  });

  it('ends where the period being looked at begins', () => {
    const current = windowFor(WEEK, 'Asia/Kolkata', NOW);
    const earlier = previousWindow(WEEK, 'Asia/Kolkata', NOW);

    expect(Date.parse(earlier.to)).toBeLessThanOrEqual(Date.parse(current.from));
  });
});

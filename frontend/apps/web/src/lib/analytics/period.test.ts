import { describe, expect, it } from 'vitest';
import { CHART_BYS } from '@/lib/analytics/chart-by';
import {
  allTimeCutsFor,
  bucketsIn,
  comparedSpan,
  comparedWindow,
  cutOf,
  cutsFor,
  type DaySpan,
  DEFAULT_PERIOD,
  dayOf,
  daysIn,
  GRANULARITIES,
  type Granularity,
  granularityFor,
  hasEarlier,
  isAllTime,
  isPreset,
  keptFrom,
  monthOf,
  namedPeriod,
  type Period,
  type PeriodPreset,
  PRESETS,
  previousSpan,
  previousWindow,
  problemWith,
  readPeriod,
  samePeriod,
  shortestPeriodFor,
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

/**
 * Half past nine in the morning in UTC on a Friday, with the last thirty days beginning on a
 * Thursday — so a week at each end of them is only partly inside.
 */
const LATER = new Date('2026-10-30T09:30:00Z');

/** One of the named periods, written the way a screen holds it. */
function named(preset: PeriodPreset): Period {
  return { kind: 'preset', preset };
}

/** A stretch somebody chose, written the way a screen holds it. */
function chosen(first: string, last: string): Period {
  return { kind: 'chosen', first, last };
}

/** Something the test needs to be there, read as itself. */
function present<T>(value: T | null): T {
  if (value === null) {
    throw new Error('Expected a stretch to measure against, and there was none.');
  }

  return value;
}

const WEEK = named('last-7-days');
const MONTH = named('last-30-days');
const TODAY = named('today');
const YESTERDAY = named('yesterday');

/**
 * How many buckets a picture is cut into is what settles whether it can be drawn as anything but
 * columns, so it is worked out before the engine answers — and has to come to what the engine cuts.
 */
describe('how many buckets a picture of some days is cut into', () => {
  /** A run of days, and the window it is counted over in Kolkata. */
  function cut(first: string, last: string, granularity: Granularity): number {
    const span = { first, last };

    return bucketsIn(span, windowForSpan(span, 'Asia/Kolkata'), granularity);
  }

  /** Today in Kolkata, as many minutes after its midnight as given. */
  function today(minutes: number): number {
    const at = new Date(Date.parse('2026-08-17T18:30:00Z') + minutes * 60_000);

    return bucketsIn(
      spanFor(TODAY, 'Asia/Kolkata', at),
      windowFor(TODAY, 'Asia/Kolkata', at),
      'hour',
    );
  }

  /**
   * Today's window ends on the hour in UTC, which is half past in Kolkata: by half past midnight it
   * reaches into the hour from one o'clock as well.
   */
  it('is every hour of today the window reaches into, an hour at a time', () => {
    expect(today(15)).toBe(1);
    expect(today(30)).toBe(2);
    expect(today(89)).toBe(2);
    expect(today(90)).toBe(3);
    expect(
      bucketsIn(spanFor(TODAY, 'Asia/Kolkata', NOW), windowFor(TODAY, 'Asia/Kolkata', NOW), 'hour'),
    ).toBe(16);
  });

  it('counts an hour the window reaches only a quarter of the way into', () => {
    const day = { first: '2026-08-18', last: '2026-08-18' };

    expect(
      bucketsIn(day, { from: '2026-08-18T00:00:00Z', to: '2026-08-18T01:15:00Z' }, 'hour'),
    ).toBe(2);
  });

  it('is every hour of a whole day, an hour at a time', () => {
    expect(
      bucketsIn(
        spanFor(YESTERDAY, 'Asia/Kolkata', NOW),
        windowFor(YESTERDAY, 'Asia/Kolkata', NOW),
        'hour',
      ),
    ).toBe(24);
  });

  it('is every day, a day at a time', () => {
    expect(cut('2026-08-12', '2026-08-18', 'day')).toBe(7);
  });

  /** The sixteenth of August 2026 is a Sunday, and the seventeenth a Monday. */
  it('is every week the days reach into, each from its Monday', () => {
    expect(cut('2026-08-17', '2026-08-23', 'week')).toBe(1);
    expect(cut('2026-08-16', '2026-08-17', 'week')).toBe(2);
    expect(cut('2026-08-19', '2026-09-15', 'week')).toBe(5);
  });

  it('is every calendar month the days reach into', () => {
    expect(cut('2026-10-12', '2026-10-30', 'month')).toBe(1);
    expect(cut('2025-03-19', '2026-08-17', 'month')).toBe(18);
  });

  it('is every calendar year the days reach into', () => {
    expect(cut('2026-02-03', '2026-08-17', 'year')).toBe(1);
    expect(cut('2025-03-19', '2026-08-17', 'year')).toBe(2);
  });
});

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

  /**
   * Santiago's clocks went forward at midnight on 6 September 2026, and Cairo's on 24 April: those
   * days have no midnight, and begin when the clocks resume at one in the morning, which is where
   * the engine begins them too.
   */
  it('begins a day whose midnight the clocks skip when they resume', () => {
    const santiago = windowForSpan({ first: '2026-09-06', last: '2026-09-06' }, 'America/Santiago');
    const cairo = windowForSpan({ first: '2026-04-24', last: '2026-04-24' }, 'Africa/Cairo');

    expect(santiago.from).toBe('2026-09-06T04:00:00.000Z');
    expect(cairo.from).toBe('2026-04-23T22:00:00.000Z');
  });

  /**
   * Havana's clocks go back from one in the morning to midnight on 1 November 2026, and the Azores'
   * on 25 October, so midnight comes twice. The day begins at the first, where the engine begins it,
   * and a period ending the day before stops there rather than reaching an hour into the next day.
   */
  it('begins a day the clocks go back to midnight on at its first midnight', () => {
    const havana = windowForSpan({ first: '2026-11-01', last: '2026-11-01' }, 'America/Havana');
    const october = windowForSpan({ first: '2026-10-01', last: '2026-10-31' }, 'America/Havana');
    const azores = windowForSpan({ first: '2026-10-25', last: '2026-10-25' }, 'Atlantic/Azores');

    expect(havana.from).toBe('2026-11-01T04:00:00.000Z');
    expect(october.to).toBe('2026-11-01T04:00:00.000Z');
    expect(azores.from).toBe('2026-10-25T00:00:00.000Z');
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

  /**
   * On the last day of the year, a year back reaches into the first of January and a whole visit
   * after that is the second — so the first is already partly gone, and the year is read from the
   * first day still kept rather than from a day the chooser would refuse.
   */
  it.each(['Etc/UTC', 'Asia/Kolkata'])(
    'begins this year on the first day still kept on the last day of the year in %s',
    (zone) => {
      const now = new Date('2026-12-31T12:00:00Z');
      const year = spanFor(named('this-year'), zone, now);
      const kept = { first: keptFrom(zone, now), last: todayIn(zone, now) };

      expect(year).toStrictEqual({ first: '2026-01-02', last: '2026-12-31' });
      expect(kept.first).toBe('2026-01-02');
      expect(problemWith(year.first, year.last, kept)).toBeNull();
    },
  );

  it('begins this year on the first of January on every other day of it', () => {
    expect(spanFor(named('this-year'), 'Etc/UTC', new Date('2026-12-30T23:00:00Z')).first).toBe(
      '2026-01-01',
    );
  });

  it('counts both ends of a run of days', () => {
    expect(daysIn(spanFor(WEEK, 'Etc/UTC', NOW))).toBe(7);
    expect(daysIn(spanFor(named('last-90-days'), 'Etc/UTC', NOW))).toBe(90);
  });
});

describe('a stretch somebody chose', () => {
  it('is the days they named, both included', () => {
    const stretch = chosen('2026-07-01', '2026-07-07');

    expect(daysIn(spanFor(stretch, 'Etc/UTC', NOW))).toBe(7);
    expect(windowFor(stretch, 'Etc/UTC', NOW)).toStrictEqual({
      from: '2026-07-01T00:00:00.000Z',
      to: '2026-07-08T00:00:00.000Z',
    });
  });

  it('puts the two ends the right way round when they arrive the wrong way', () => {
    expect(spanFor(chosen('2026-08-18', '2026-08-11'), 'Etc/UTC', NOW)).toStrictEqual({
      first: '2026-08-11',
      last: '2026-08-18',
    });
  });

  it('stops at today rather than showing traffic that has not happened', () => {
    expect(spanFor(chosen('2026-08-11', '2099-01-01'), 'Etc/UTC', NOW)).toStrictEqual({
      first: '2026-08-11',
      last: '2026-08-18',
    });
  });

  /** Reports are deleted a year after they arrive, so an older day would be drawn as nothing at all. */
  it('reaches back no further than the first day still kept', () => {
    expect(spanFor(chosen('2000-01-01', '2026-08-18'), 'Etc/UTC', NOW)).toStrictEqual({
      first: keptFrom('Etc/UTC', NOW),
      last: '2026-08-18',
    });
  });

  /**
   * A link kept for long enough names days that are no longer held. It opens on the first days that
   * are, rather than on a stretch running backwards or on a refusal.
   */
  it('opens an old link on the first days still kept', () => {
    expect(spanFor(chosen('2025-06-01', '2025-06-30'), 'Etc/UTC', NOW)).toStrictEqual({
      first: '2025-08-20',
      last: '2025-08-20',
    });
  });

  /** A date is one of the few things somebody can put in an address by hand. */
  it('still gives a workable period when a date cannot be read at all', () => {
    const span = spanFor(chosen('whenever', '2026-08-18'), 'Etc/UTC', NOW);

    expect(span).toStrictEqual({ first: keptFrom('Etc/UTC', NOW), last: '2026-08-18' });
  });
});

describe('the first day a website still holds in full', () => {
  /**
   * A year back from twenty to ten on the eighteenth is the eighteenth, and a whole day after that
   * is the nineteenth, part of which is already gone. The first whole day is the twentieth.
   */
  it('is the first midnight a whole day after the oldest report still kept', () => {
    expect(keptFrom('Etc/UTC', NOW)).toBe('2025-08-20');
  });

  it('is that midnight’s own day when the moment falls exactly on one', () => {
    expect(keptFrom('Etc/UTC', new Date('2026-10-30T00:00:00Z'))).toBe('2025-10-31');
  });

  /** Eight in the evening in UTC is already half past one the next morning in Kolkata. */
  it('counts the midnight where the website is', () => {
    const evening = new Date('2026-08-18T20:00:00Z');

    expect(keptFrom('Etc/UTC', evening)).toBe('2025-08-20');
    expect(keptFrom('Asia/Kolkata', evening)).toBe('2025-08-21');
    expect(keptFrom('America/New_York', evening)).toBe('2025-08-20');
  });

  /** The year before has no twenty-ninth of February, and the engine counts it as the twenty-eighth. */
  it('reads a year before the twenty-ninth of February as the twenty-eighth', () => {
    expect(keptFrom('Etc/UTC', new Date('2028-02-29T12:00:00Z'))).toBe('2027-03-02');
  });
});

describe('how finely a period is cut', () => {
  /** A run of days of the given length, ending on the eighteenth of August. */
  function days(length: number): DaySpan {
    const first = new Date(Date.UTC(2026, 7, 18 - (length - 1)));

    return { first: first.toISOString().slice(0, 10), last: '2026-08-18' };
  }

  it('offers nothing to choose on a period of two days or less', () => {
    expect(cutsFor(days(1))).toStrictEqual([]);
    expect(cutsFor(days(2))).toStrictEqual([]);
  });

  it('offers only days below four weeks', () => {
    expect(cutsFor(days(3))).toStrictEqual(['day']);
    expect(cutsFor(days(27))).toStrictEqual(['day']);
  });

  it('offers weeks from four weeks', () => {
    expect(cutsFor(days(28))).toStrictEqual(['day', 'week']);
    expect(cutsFor(days(89))).toStrictEqual(['day', 'week']);
  });

  it('offers months from ninety days', () => {
    expect(cutsFor(days(90))).toStrictEqual(['day', 'week', 'month']);
    expect(cutsFor(days(366))).toStrictEqual(['day', 'week', 'month']);
  });

  it('is by the hour for one day and for two', () => {
    expect(granularityFor(spanFor(TODAY, 'Etc/UTC', NOW), null)).toBe('hour');
    expect(granularityFor(days(2), null)).toBe('hour');
  });

  it('is by the day from three days upwards, until somebody chooses otherwise', () => {
    expect(granularityFor(days(3), null)).toBe('day');
    expect(granularityFor(spanFor(WEEK, 'Etc/UTC', NOW), null)).toBe('day');
    expect(granularityFor(spanFor(named('last-90-days'), 'Etc/UTC', NOW), null)).toBe('day');
  });

  it('is cut the way somebody chose where that way is offered', () => {
    const quarter = spanFor(named('last-90-days'), 'Etc/UTC', NOW);

    expect(granularityFor(quarter, 'week')).toBe('week');
    expect(granularityFor(quarter, 'month')).toBe('month');
  });

  /** The choice is set aside rather than forgotten, so a longer period has it back. */
  it("falls back to the period's own way where the choice is not offered", () => {
    expect(granularityFor(spanFor(WEEK, 'Etc/UTC', NOW), 'week')).toBe('day');
    expect(granularityFor(spanFor(MONTH, 'Etc/UTC', NOW), 'month')).toBe('day');
    expect(granularityFor(spanFor(TODAY, 'Etc/UTC', NOW), 'month')).toBe('hour');
  });

  /**
   * The period a way of cutting moves the screen to has to offer it — or choosing it would move
   * the screen and draw it the same way as before — and is the shortest running period that does.
   */
  it.each(CHART_BYS.filter((cut) => cut !== 'year'))(
    'moves to the shortest running period that can be cut by the %s',
    (cut) => {
      const running: readonly PeriodPreset[] = ['last-7-days', 'last-30-days', 'last-90-days'];
      const offering = running.filter((preset) =>
        cutsFor(spanFor(named(preset), 'Etc/UTC', NOW)).includes(cut),
      );

      expect(shortestPeriodFor(cut)).toBe(offering[0]);
    },
  );

  /** No running period is long enough to hold two years, and all time is the one that does. */
  it('moves to all time for years, which no other period offers', () => {
    const running: readonly PeriodPreset[] = ['last-7-days', 'last-30-days', 'last-90-days'];

    expect(shortestPeriodFor('year')).toBe('all-time');
    expect(
      running.some((preset) => cutsFor(spanFor(named(preset), 'Etc/UTC', NOW)).includes('year')),
    ).toBe(false);
  });

  it('is cut the way somebody chose where the picture offers it, and its own way where not', () => {
    expect(cutOf(['month', 'year'], null)).toBe('month');
    expect(cutOf(['month', 'year'], 'year')).toBe('year');
    expect(cutOf(['month'], 'year')).toBe('month');
    expect(cutOf(['month', 'year'], 'week')).toBe('month');
    expect(cutOf([], 'week')).toBe('hour');
  });
});

/**
 * All time is the website's whole record, from its first month kept, or still held, to now, from
 * the totals kept of each month. Its lists are counted from the visits themselves, which go after
 * a year.
 */
describe('all time', () => {
  const ALL_TIME = named('all-time');

  /** Its lists go exactly as far back as the visits they are counted from are kept. */
  it('lists every day still kept, to today', () => {
    expect(spanFor(ALL_TIME, 'Etc/UTC', NOW)).toStrictEqual({
      first: keptFrom('Etc/UTC', NOW),
      last: '2026-08-18',
    });
    expect(keptFrom('Etc/UTC', NOW)).toBe('2025-08-20');
  });

  /** Whatever day of the month it is, the lists begin on the first day still kept. */
  it('begins its lists on the first day still kept on the last day of a month too', () => {
    const lastOfMonth = new Date('2026-10-31T10:00:00Z');

    expect(spanFor(ALL_TIME, 'Etc/UTC', lastOfMonth)).toStrictEqual({
      first: '2025-11-02',
      last: '2026-10-31',
    });
  });

  /**
   * Late on the thirty-first of August in London it is already the first of September in Kolkata,
   * and the first day still kept falls a day later there.
   */
  it('counts its days where the website is', () => {
    const evening = new Date('2026-08-31T20:00:00Z');

    expect(spanFor(ALL_TIME, 'Etc/UTC', evening)).toStrictEqual({
      first: '2025-09-02',
      last: '2026-08-31',
    });
    expect(spanFor(ALL_TIME, 'Asia/Kolkata', evening)).toStrictEqual({
      first: '2025-09-03',
      last: '2026-09-01',
    });
  });

  it('begins its lists on the first day still kept in the zones furthest from London', () => {
    const morning = new Date('2026-08-18T11:00:00Z');

    for (const zone of ['Pacific/Kiritimati', 'Etc/GMT+12']) {
      expect(spanFor(ALL_TIME, zone, morning).first).toBe(keptFrom(zone, morning));
    }
  });

  it('has nothing before it to be measured against', () => {
    expect(hasEarlier('all-time')).toBe(false);
    expect(previousSpan(ALL_TIME, 'Etc/UTC', NOW)).toBeNull();
    expect(previousWindow(ALL_TIME, 'Etc/UTC', NOW)).toBeNull();

    for (const granularity of GRANULARITIES) {
      expect(comparedSpan(ALL_TIME, granularity, 'Etc/UTC', NOW)).toBeNull();
      expect(comparedWindow(ALL_TIME, granularity, 'Etc/UTC', NOW)).toBeNull();
    }
  });

  it('is the one named period with nothing before it', () => {
    expect(PRESETS.filter((preset) => !hasEarlier(preset))).toStrictEqual(['all-time']);
  });

  it('is told apart from every other period', () => {
    expect(isAllTime(ALL_TIME)).toBe(true);
    expect(isAllTime(named('this-year'))).toBe(false);
    expect(isAllTime(chosen('2025-09-01', '2026-08-18'))).toBe(false);
  });

  it('is cut by the month while every day it covers falls in one year', () => {
    expect(allTimeCutsFor({ first: '2026-01-01', last: '2026-08-18' })).toStrictEqual(['month']);
  });

  it('is cut by the year as well once it runs across the first of January', () => {
    expect(allTimeCutsFor({ first: '2025-12-31', last: '2026-08-18' })).toStrictEqual([
      'month',
      'year',
    ]);
  });

  /** Months are the one way any record can be cut, so they are all a record not yet known offers. */
  it('is cut by the month alone while the days it covers are not known', () => {
    expect(allTimeCutsFor(null)).toStrictEqual(['month']);
  });

  /** A stretch somebody typed in is theirs, however much of the record it happens to cover. */
  it('is never what a chosen stretch is named', () => {
    const stretch = chosen('2025-09-01', '2026-08-18');

    expect(namedPeriod(stretch, 'Etc/UTC', NOW)).toStrictEqual(stretch);
  });

  it('travels in an address under its own name', () => {
    expect(withChoices('/app', { period: ALL_TIME, population: 'everybody' })).toBe(
      '/app?period=all-time',
    );
    expect(readPeriod('all-time')).toStrictEqual(ALL_TIME);
  });
});

describe('what day it is where the site is', () => {
  it('is the day there, not the day where the reader is sitting', () => {
    const lateEvening = new Date('2026-08-18T19:00:00Z');

    expect(todayIn('Asia/Kolkata', lateEvening)).toBe('2026-08-19');
    expect(todayIn('America/New_York', lateEvening)).toBe('2026-08-18');
  });
});

/**
 * A day the list already has a name for is that named period, and is called by the name rather
 * than as a stretch of one day somebody chose.
 */
describe('a single day under the name it is offered by', () => {
  /** One day somebody chose, written the way a screen holds it. */
  function day(date: string): Period {
    return chosen(date, date);
  }

  it('is Today when the day is today where the site is', () => {
    expect(namedPeriod(day('2026-08-18'), 'Asia/Kolkata', NOW)).toStrictEqual(TODAY);
  });

  it('is Yesterday when it is the day before', () => {
    expect(namedPeriod(day('2026-08-17'), 'Asia/Kolkata', NOW)).toStrictEqual(YESTERDAY);
  });

  it('is the day itself on any other day', () => {
    const older = day('2026-08-16');

    expect(namedPeriod(older, 'Asia/Kolkata', NOW)).toBe(older);
  });

  /** A stretch that ends today is still the stretch somebody chose, and no name covers it. */
  it('leaves a stretch of more than one day as it was, even one ending today', () => {
    const stretch = chosen('2026-08-17', '2026-08-18');

    expect(namedPeriod(stretch, 'Asia/Kolkata', NOW)).toBe(stretch);
  });

  it('leaves a named period as it was', () => {
    expect(namedPeriod(WEEK, 'Asia/Kolkata', NOW)).toBe(WEEK);
  });

  /**
   * At seven in the evening in UTC it is already the small hours of the nineteenth in Kolkata and
   * still the afternoon of the eighteenth in New York, and each website's today is its own.
   */
  it('counts today where the site is once its midnight has passed, whatever the day is in UTC', () => {
    const lateEvening = new Date('2026-08-18T19:00:00Z');

    expect(namedPeriod(day('2026-08-19'), 'Asia/Kolkata', lateEvening)).toStrictEqual(TODAY);
    expect(namedPeriod(day('2026-08-18'), 'Asia/Kolkata', lateEvening)).toStrictEqual(YESTERDAY);
    expect(namedPeriod(day('2026-08-18'), 'America/New_York', lateEvening)).toStrictEqual(TODAY);
  });

  it('finds yesterday across the end of a month', () => {
    const firstOfSeptember = new Date('2026-09-01T03:00:00Z');

    expect(namedPeriod(day('2026-08-31'), 'Asia/Kolkata', firstOfSeptember)).toStrictEqual(
      YESTERDAY,
    );
  });

  /** On the first of a month, today is the whole of this month so far, and is called today. */
  it('names a single day before a month', () => {
    const firstOfAugust = new Date('2026-08-01T09:00:00Z');

    expect(namedPeriod(day('2026-08-01'), 'Asia/Kolkata', firstOfAugust)).toStrictEqual(TODAY);
  });
});

/**
 * A pressed month and a typed one arrive under the name the list offers them by, so the control
 * reads the same whichever way somebody got there.
 */
describe('a stretch under the name it is offered by', () => {
  it('is This month when it runs from the first of the month to today', () => {
    expect(namedPeriod(chosen('2026-08-01', '2026-08-18'), 'Asia/Kolkata', NOW)).toStrictEqual(
      named('this-month'),
    );
  });

  it('is Last month when it is the whole of the month before', () => {
    expect(namedPeriod(chosen('2026-07-01', '2026-07-31'), 'Asia/Kolkata', NOW)).toStrictEqual(
      named('last-month'),
    );
  });

  it('is This year when it runs from the first of January to today', () => {
    expect(namedPeriod(chosen('2026-01-01', '2026-08-18'), 'Asia/Kolkata', NOW)).toStrictEqual(
      named('this-year'),
    );
  });

  /** This year is read from the second of January on its last day, and is called that there. */
  it('is This year on its last day when it runs from the first day still kept', () => {
    const now = new Date('2026-12-31T12:00:00Z');

    expect(namedPeriod(chosen('2026-01-02', '2026-12-31'), 'Etc/UTC', now)).toStrictEqual(
      named('this-year'),
    );
  });

  it('stays the days somebody chose when they are only part of a month', () => {
    const stretch = chosen('2026-07-01', '2026-07-30');

    expect(namedPeriod(stretch, 'Asia/Kolkata', NOW)).toBe(stretch);
  });

  /** Thirty days ending today is a stretch somebody chose, and would move tomorrow if it were named. */
  it('never names a running period', () => {
    const stretch = chosen('2026-07-20', '2026-08-18');

    expect(namedPeriod(stretch, 'Asia/Kolkata', NOW)).toBe(stretch);
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

describe('the calendar month a day falls in', () => {
  it('runs from its first day to its last', () => {
    expect(monthOf('2026-08-18')).toStrictEqual({ first: '2026-08-01', last: '2026-08-31' });
  });

  it('knows how long February is in a leap year and out of one', () => {
    expect(monthOf('2028-02-10').last).toBe('2028-02-29');
    expect(monthOf('2026-02-10').last).toBe('2026-02-28');
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

  /** The days a stretch may be chosen from on the eighteenth of August. */
  const KEPT: DaySpan = { first: '2025-08-20', last: TODAY };

  it('is nothing at all when the two days are a period', () => {
    expect(problemWith('2026-08-11', '2026-08-18', KEPT)).toBeNull();
    expect(problemWith(TODAY, TODAY, KEPT)).toBeNull();
  });

  it('says so when the far end comes first', () => {
    expect(problemWith('2026-08-18', '2026-08-11', KEPT)).toBe('backwards');
  });

  it('says so when the far end has not happened', () => {
    expect(problemWith('2026-08-11', '2026-08-19', KEPT)).toBe('future');
  });

  it('allows a year and a day, and refuses the day after that', () => {
    const longKept: DaySpan = { first: '2025-01-01', last: '2027-12-31' };

    expect(problemWith('2026-01-01', '2027-01-01', longKept)).toBeNull();
    expect(problemWith('2026-01-01', '2027-01-02', longKept)).toBe('too-long');
  });

  it('refuses a first day older than the first day still kept, and allows that day itself', () => {
    expect(problemWith('2025-08-19', TODAY, KEPT)).toBe('too-old');
    expect(problemWith('2025-08-20', TODAY, KEPT)).toBeNull();
  });

  /**
   * Every stretch too long to be shown also begins before the first day still kept. Told it was too
   * long, somebody shortening it to a year would only be refused again; told how far back the days
   * go, they are given the first day that works.
   */
  it('calls a stretch too old before it calls it too long', () => {
    expect(problemWith('2024-08-18', TODAY, KEPT)).toBe('too-old');
  });

  /**
   * Both days of it are before the first day still kept, and the one sentence names the day both
   * have to be moved to: advice about the first day alone would turn the stretch backwards.
   */
  it('calls a stretch wholly older than the first day still kept too old', () => {
    expect(problemWith('2025-06-01', '2025-06-30', KEPT)).toBe('too-old');
  });

  it('says nothing readable when a box is still empty', () => {
    expect(problemWith('', TODAY, KEPT)).toBe('unreadable');
    expect(problemWith(TODAY, '', KEPT)).toBe('unreadable');
  });

  /**
   * Every impossible day matches the shape of a date, and the calendar is the only thing that
   * knows which of them exist. Read without checking, the thirtieth of February quietly becomes
   * the second of March and a period nobody asked for is drawn without a word.
   */
  it('refuses a day that is not on the calendar rather than moving it', () => {
    expect(problemWith('2026-02-30', TODAY, KEPT)).toBe('unreadable');
    expect(spanFor(chosen('2026-02-30', TODAY), 'Etc/UTC', NOW)).toEqual({
      first: '2025-08-20',
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
        period: chosen('2026-08-01', '2026-08-14'),
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

  /** A screen a choice cannot narrow is not given it, and its address says nothing of it. */
  it('writes only what it is given', () => {
    expect(withChoices('/app/insights', { population: 'people' })).toBe(
      '/app/insights?only=people',
    );
    expect(withChoices('/app/journeys', { period: TODAY })).toBe('/app/journeys?period=today');
    expect(withChoices('/app/live', {})).toBe('/app/live');
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
    expect(previousSpan(chosen('2026-08-01', '2026-08-14'), 'Asia/Kolkata', NOW)).toStrictEqual({
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
});

/**
 * Reports are deleted a year after they arrive, so the same days last year are always partly gone.
 * Set beside what is left of them, any year reads as growth from almost nothing.
 */
describe('a comparison that would reach into days no longer kept', () => {
  it("hides the comparison when last year's same days are no longer kept", () => {
    const year = named('this-year');

    expect(previousSpan(year, 'Asia/Kolkata', NOW)).toBeNull();
    expect(previousWindow(year, 'Asia/Kolkata', NOW)).toBeNull();

    for (const granularity of GRANULARITIES) {
      expect(comparedSpan(year, granularity, 'Asia/Kolkata', NOW)).toBeNull();
      expect(comparedWindow(year, granularity, 'Asia/Kolkata', NOW)).toBeNull();
    }
  });

  it('measures a stretch against the days before it only while every one of them is kept', () => {
    expect(previousSpan(chosen('2025-08-30', '2025-09-08'), 'Etc/UTC', NOW)).toStrictEqual({
      first: '2025-08-20',
      last: '2025-08-29',
    });
    expect(previousSpan(chosen('2025-08-29', '2025-09-07'), 'Etc/UTC', NOW)).toBeNull();
  });

  /**
   * Set week beside week, the dashed lines reach five days further back than the stretch the cards
   * are measured against. At midnight on the thirtieth of October the cards' stretch begins on the
   * first day still kept, and the lines' would begin five days before it.
   */
  it('never draws the earlier weeks from days no longer kept, while the cards still compare', () => {
    const midnight = new Date('2026-10-30T00:00:00Z');
    const stretch = chosen('2025-11-30', '2025-12-29');

    expect(previousSpan(stretch, 'Etc/UTC', midnight)).toStrictEqual({
      first: '2025-10-31',
      last: '2025-11-29',
    });
    expect(previousWindow(stretch, 'Etc/UTC', midnight)).not.toBeNull();
    expect(comparedSpan(stretch, 'day', 'Etc/UTC', midnight)).not.toBeNull();
    expect(comparedSpan(stretch, 'week', 'Etc/UTC', midnight)).toBeNull();
    expect(comparedWindow(stretch, 'week', 'Etc/UTC', midnight)).toBeNull();
  });
});

describe('the window a period is measured against', () => {
  it('covers the whole of the earlier period once this one has finished', () => {
    const { from, to } = present(previousWindow(YESTERDAY, 'Asia/Kolkata', NOW));

    expect(from).toBe('2026-08-15T18:30:00.000Z');
    expect(to).toBe('2026-08-16T18:30:00.000Z');
  });

  /**
   * Today at ten in the morning has had ten hours in it and yesterday had twenty-four. Measured
   * against the whole of yesterday, every morning would report a collapse in traffic and every
   * night a recovery.
   */
  it('is cut to the same length as the part of this period that has happened', () => {
    const { from, to } = present(previousWindow(TODAY, 'Etc/UTC', NOW));

    expect(from).toBe('2026-08-17T00:00:00.000Z');
    expect(to).toBe('2026-08-17T10:00:00.000Z');
  });

  it('is exactly as long as the period it is measured against', () => {
    const current = windowFor(WEEK, 'Asia/Kolkata', NOW);
    const earlier = present(previousWindow(WEEK, 'Asia/Kolkata', NOW));

    expect(Date.parse(earlier.to) - Date.parse(earlier.from)).toBe(
      Date.parse(current.to) - Date.parse(current.from),
    );
  });

  it('ends where the period being looked at begins', () => {
    const current = windowFor(WEEK, 'Asia/Kolkata', NOW);
    const earlier = present(previousWindow(WEEK, 'Asia/Kolkata', NOW));

    expect(Date.parse(earlier.to)).toBeLessThanOrEqual(Date.parse(current.from));
  });
});

/**
 * A dashed line is read off the bucket it is drawn beside, so each earlier bucket has to cover the
 * same weekdays, or the same days of the month, as its partner.
 */
describe('the days the dashed lines are drawn from', () => {
  it('are the days the cards are measured against, by the day and by the hour', () => {
    expect(comparedSpan(WEEK, 'day', 'Asia/Kolkata', NOW)).toStrictEqual(
      previousSpan(WEEK, 'Asia/Kolkata', NOW),
    );
    expect(comparedSpan(TODAY, 'hour', 'Asia/Kolkata', NOW)).toStrictEqual(
      previousSpan(TODAY, 'Asia/Kolkata', NOW),
    );
  });

  /**
   * The first to the thirtieth of October begins on a Thursday. The thirty days before begin on a
   * Tuesday, which would set every earlier week two days out; thirty-five days before is a Thursday
   * again.
   */
  it('sets the dashed lines on the same weekday when cut by the week', () => {
    const lines = present(comparedSpan(MONTH, 'week', 'Etc/UTC', LATER));
    const weekday = (day: string) => new Date(`${day}T12:00:00Z`).getUTCDay();

    expect(lines).toStrictEqual({ first: '2026-08-27', last: '2026-09-25' });
    expect(weekday(lines.first)).toBe(weekday('2026-10-01'));
    expect(weekday(lines.last)).toBe(weekday('2026-10-30'));
  });

  it('moves a quarter back by thirteen whole weeks', () => {
    expect(comparedSpan(named('last-90-days'), 'week', 'Etc/UTC', LATER)).toStrictEqual({
      first: '2026-05-03',
      last: '2026-07-31',
    });
  });

  /** The second of August to the thirtieth of October is set beside the same days of May to July. */
  it('sets the dashed lines on the same day of the month when cut by the month', () => {
    expect(comparedSpan(named('last-90-days'), 'month', 'Etc/UTC', LATER)).toStrictEqual({
      first: '2026-05-02',
      last: '2026-07-30',
    });
  });

  it('moves a stretch somebody chose back by as many months as it reaches into', () => {
    expect(
      comparedSpan(chosen('2026-05-15', '2026-09-10'), 'month', 'Etc/UTC', LATER),
    ).toStrictEqual({ first: '2025-12-15', last: '2026-04-10' });
  });

  it('pulls a day back to the last one a shorter month has', () => {
    expect(
      comparedSpan(chosen('2026-03-31', '2026-06-30'), 'month', 'Etc/UTC', LATER),
    ).toStrictEqual({ first: '2025-11-30', last: '2026-02-28' });
  });

  /** The cards and the lines may name slightly different days, and each says which it means. */
  it('is settled apart from the days the cards are measured against', () => {
    expect(previousSpan(MONTH, 'Etc/UTC', LATER)).toStrictEqual({
      first: '2026-09-01',
      last: '2026-09-30',
    });
    expect(comparedSpan(MONTH, 'week', 'Etc/UTC', LATER)).toStrictEqual({
      first: '2026-08-27',
      last: '2026-09-25',
    });
  });

  it('never covers days this period also covers', () => {
    for (const preset of PRESETS) {
      const span = spanFor(named(preset), 'Etc/UTC', LATER);

      for (const granularity of GRANULARITIES) {
        const lines = comparedSpan(named(preset), granularity, 'Etc/UTC', LATER);

        if (lines !== null) {
          expect(lines.last < span.first).toBe(true);
        }
      }
    }
  });

  it('is cut to the same length as the part of this period that has happened', () => {
    const current = windowFor(MONTH, 'Etc/UTC', LATER);
    const lines = present(comparedWindow(MONTH, 'week', 'Etc/UTC', LATER));

    expect(lines).toStrictEqual({
      from: '2026-08-27T00:00:00.000Z',
      to: '2026-09-25T10:00:00.000Z',
    });
    expect(Date.parse(lines.to) - Date.parse(lines.from)).toBe(
      Date.parse(current.to) - Date.parse(current.from),
    );
  });
});

/**
 * Moved back by whole months, the earlier stretch is not always as long as the period: the third
 * of September to the first of December is ninety days, and the third of May to the first of August
 * ninety-one. Cut after as long as the period has run, its last month would end a day early, cover
 * a different part of its last day, or not be drawn at all.
 */
describe('the window the dashed months are drawn from', () => {
  const QUARTER = named('last-90-days');

  /** The earlier window a period's dashed months are asked about, at a given moment in UTC. */
  function linedUp(period: Period, at: string) {
    return present(comparedWindow(period, 'month', 'Etc/UTC', new Date(at)));
  }

  it('ends on its last day at the hour this period has reached on its own', () => {
    expect(linedUp(QUARTER, '2026-12-01T10:30:00Z')).toStrictEqual({
      from: '2026-05-03T00:00:00.000Z',
      to: '2026-08-01T11:00:00.000Z',
    });
  });

  it('reaches the last day of a month the earlier stretch ends on', () => {
    expect(linedUp(QUARTER, '2027-03-31T10:30:00Z').to).toBe('2026-12-31T11:00:00.000Z');
  });

  it('covers no more of its last day than this period has of its own', () => {
    expect(linedUp(QUARTER, '2026-11-01T10:30:00Z').to).toBe('2026-07-01T11:00:00.000Z');
  });

  it('is the whole of the earlier stretch once this period has finished', () => {
    expect(linedUp(chosen('2027-01-01', '2027-03-31'), '2027-04-15T10:30:00Z')).toStrictEqual({
      from: '2026-10-01T00:00:00.000Z',
      to: '2027-01-01T00:00:00.000Z',
    });
  });
});

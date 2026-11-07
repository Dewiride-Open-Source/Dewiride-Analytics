import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Insights } from '@/components/insights/insights';
import type { Insights as InsightsAnswer, SitemapStatus } from '@/lib/api/schemas';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

const SITE = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const TO = '2026-10-30T09:30:00+00:00';
const EARLIER = '2026-10-29T12:00:00+00:00';

/** Where judging reaches on any ordinary day: a visit is judged once it has been quiet this long. */
const HALF_AN_HOUR_BEHIND = '2026-10-30T09:00:00+00:00';

/** The longest an installation may let a visit stay quiet before judging it. */
const TWELVE_HOURS_BEHIND = '2026-10-29T21:30:00+00:00';

/** That, and the longest an installation may leave between one run of judging and the next. */
const THIRTEEN_HOURS_BEHIND = '2026-10-29T20:30:00+00:00';

/** The line itself, which a record has to be past before judging is called behind. */
const A_DAY_BEHIND = '2026-10-29T09:30:00+00:00';

/** Further behind than a visit waiting its turn explains. */
const TWO_DAYS_BEHIND = '2026-10-28T09:30:00+00:00';

/** Page views over each hour of the day, half of them in the one at two in the afternoon. */
const READ_AT = new Map([
  [9, 30],
  [14, 50],
  [20, 20],
]);
const HOURS = Array.from({ length: 24 }, (_, hour) => READ_AT.get(hour) ?? 0);

/** A website with a year of history: a partial first month, a quiet one, and one running now. */
const RECORD: InsightsAnswer = {
  firstDay: '2025-11-19',
  to: TO,
  completeTo: TO,
  pageViews: 4321,
  visitors: 1234,
  bestDay: { day: '2026-09-16', pageViews: 412 },
  weekdays: [10, 20, 40, 10, 10, 5, 5],
  busiestWeekdays: [2],
  hours: HOURS,
  busiestHours: [14],
  months: [
    { month: '2025-11', pageViews: 120, daysMeasured: 12 },
    { month: '2025-12', pageViews: 0, daysMeasured: 31 },
    { month: '2026-01', pageViews: 310, daysMeasured: 31 },
    { month: '2026-02', pageViews: 280, daysMeasured: 28 },
    { month: '2026-03', pageViews: 400, daysMeasured: 31 },
    { month: '2026-04', pageViews: 450, daysMeasured: 30 },
    { month: '2026-05', pageViews: 500, daysMeasured: 31 },
    { month: '2026-06', pageViews: 450, daysMeasured: 30 },
    { month: '2026-07', pageViews: 400, daysMeasured: 31 },
    { month: '2026-08', pageViews: 300, daysMeasured: 31 },
    { month: '2026-09', pageViews: 2000, daysMeasured: 30 },
    { month: '2026-10', pageViews: 90, daysMeasured: 30 },
  ],
  missing: [],
  unjudged: [],
};

/** The same website read among people, whose figures are smaller. */
const PEOPLE: InsightsAnswer = {
  ...RECORD,
  completeTo: HALF_AN_HOUR_BEHIND,
  pageViews: 1000,
  visitors: 300,
  bestDay: { day: '2026-09-17', pageViews: 90 },
};

/** The same months, with nobody judged a person in any of them. */
const NOBODY: InsightsAnswer = {
  ...RECORD,
  pageViews: 0,
  visitors: 0,
  bestDay: null,
  weekdays: [0, 0, 0, 0, 0, 0, 0],
  busiestWeekdays: [],
  hours: Array.from({ length: 24 }, () => 0),
  busiestHours: [],
  months: RECORD.months.map((month) => ({ ...month, pageViews: 0 })),
};

const NEVER: InsightsAnswer = {
  ...NOBODY,
  firstDay: null,
  months: [],
};

/** A website nobody has named a sitemap for, which asks nothing about its posts. */
const NO_SITEMAP: SitemapStatus = {
  address: null,
  state: 'none',
  problem: null,
  readAt: null,
  succeededAt: null,
  posts: 0,
  dated: 0,
  reading: 0,
  turnedAway: 0,
};

interface Engine {
  readonly everybody?: InsightsAnswer;
  readonly people?: InsightsAnswer;
  /** Who came across the record, for a screen with no people to show. */
  readonly who?: unknown;
  /** The addresses it refuses. */
  readonly refusing?: readonly string[];
  /** The addresses it never answers. */
  readonly holding?: readonly string[];
}

function engineWith({
  everybody = RECORD,
  people = everybody,
  who,
  refusing = [],
  holding = [],
}: Engine) {
  return engineDoing(async (path) => {
    if (holding.some((address) => path.includes(address))) {
      return new Promise<Response>(() => {});
    }

    if (refusing.some((address) => path.includes(address))) {
      return respondWith(500, { status: 500, title: 'The engine is having trouble.' });
    }

    if (path.endsWith('/sitemap')) {
      return respondWith(200, NO_SITEMAP);
    }

    if (path.includes('/insights')) {
      return respondWith(200, path.includes('only=people') ? people : everybody);
    }

    if (path.includes('/all-time/traffic/series')) {
      return respondWith(200, who);
    }

    return respondWith(200, [SITE]);
  });
}

/**
 * An engine that keeps one answer back until the test lets it go: what the record says, or which
 * websites there are.
 *
 * @returns What lets the answer go.
 */
function engineKeepingBack(kept: 'record' | 'website'): () => void {
  let release: () => void = () => {};
  const released = new Promise<void>((resolve) => {
    release = resolve;
  });

  engineDoing(async (path) => {
    if (path.endsWith('/sitemap')) {
      return respondWith(200, NO_SITEMAP);
    }

    const record = path.includes('/insights');

    if (record === (kept === 'record')) {
      await released;
    }

    return respondWith(200, record ? RECORD : [SITE]);
  });

  return () => release();
}

/** Who came across two months, judged as far as a test says, the visits of the kinds it names. */
function whoCame(completeTo: string, categories: readonly string[]) {
  return {
    from: '2026-08-31T18:30:00+00:00',
    to: TO,
    granularity: 'month',
    completeTo,
    buckets: ['2026-08-31T18:30:00+00:00', '2026-09-30T18:30:00+00:00'],
    groups: categories.map((category) => ({
      category,
      sessions: [4, 2],
      pageViews: [9, 3],
    })),
    missing: [],
    unjudged: [],
  };
}

/** What on the screen takes the keyboard before its figures are in, and how a test finds each. */
const HELD_THROUGH_THE_WAIT: readonly (readonly [string, () => HTMLElement])[] = [
  [
    'the switch for how the months are read',
    () => screen.getByRole('radio', { name: 'Total views' }),
  ],
  ['the line that unfolds the figures', () => screen.getByText('Show these figures as a table')],
  ['People only', () => screen.getByRole('button', { name: /People only/ })],
];

const EVERY_CAPTION = /Page views (in each month|per day in each month)/;

/** The table of months, once the record has arrived. */
async function monthsTable() {
  return screen.findByRole('table', { name: EVERY_CAPTION });
}

/** One year's row of a table of months. */
function yearRow(table: HTMLElement, year: string): HTMLElement {
  return within(table).getByRole('row', { name: new RegExp(`^${year}\\b`) });
}

/** One month of a year in the table: January is the first, and the figure for the year comes last. */
function monthCell(table: HTMLElement, year: string, place: number): HTMLElement {
  const cell = within(yearRow(table, year)).getAllByRole('cell')[place - 1];

  if (cell === undefined) {
    throw new Error(`No month ${place} in ${year}.`);
  }

  return cell;
}

/** The figure a year's row ends on. */
function yearTotal(table: HTMLElement, year: string): string | null {
  return within(yearRow(table, year)).getAllByRole('cell').at(-1)?.textContent ?? null;
}

/**
 * Whether a name says these words, whichever of the many kinds of space the reader's language writes
 * a time with.
 */
function saying(words: string) {
  return (name: string) => name.replace(/\s+/g, ' ') === words;
}

/** The tile a record's label heads. */
function tile(label: string): HTMLElement {
  const heading = screen.getByRole('heading', { name: label });
  const holding = heading.parentElement;

  if (holding === null) {
    throw new Error(`${label} is not on a tile.`);
  }

  return holding;
}

describe('the insights screen', () => {
  it('names the screen and how far back it reaches', async () => {
    engineWith({});

    renderScreen(<Insights />);

    expect(await screen.findByRole('heading', { name: 'Insights', level: 1 })).toBeInTheDocument();
    expect(
      await screen.findByText('My Blog, from November 19, 2025 to today.'),
    ).toBeInTheDocument();
  });

  it('shows all-time page views and daily visitors', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const views = await screen.findByText('4,321');
    const visitors = screen.getByText('1,234');

    expect(views.parentElement).toHaveTextContent('Page views');
    expect(visitors.parentElement).toHaveTextContent('Daily visitors');
    expect(visitors.parentElement).toHaveTextContent('Someone who returns tomorrow counts again.');
  });

  it('names the best day ever with its page views', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const day = await screen.findByText('Wednesday, September 16, 2026');

    expect(day.parentElement?.parentElement).toHaveTextContent('412 page views');
    expect(day.parentElement).toHaveTextContent('Best day ever');
  });

  it("names the busiest weekday and hour on the website's clock", async () => {
    engineWith({});

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    expect(tile('Busiest day of the week')).toHaveTextContent('Wednesday');
    expect(tile('Busiest day of the week')).toHaveTextContent('40% of page views');
    expect(tile('Busiest hour')).toHaveTextContent('2:00 – 2:59 PM');
    expect(tile('Busiest hour')).toHaveTextContent('50% of page views');
    expect(screen.getByText('Days and hours are counted in Kolkata.')).toBeInTheDocument();
    expect(screen.queryByText(/Asia\/Kolkata/)).not.toBeInTheDocument();
  });

  /** Every drawing publishes the numbers behind it, and these strips are drawings. */
  it('publishes those figures as a table', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    const summary = screen.getByText('Show these figures as a table');
    const table = within(summary.parentElement as HTMLElement).getByRole('table');

    expect(
      within(table).getByRole('row', { name: saying('Wednesday 40 40%') }),
    ).toBeInTheDocument();
    expect(
      within(table).getByRole('row', { name: saying('2:00 – 2:59 PM 50 50%') }),
    ).toBeInTheDocument();
    expect(
      within(table).getByRole('row', { name: saying('12:00 – 12:59 AM 0 0%') }),
    ).toBeInTheDocument();
    expect(within(table).getAllByRole('row')).toHaveLength(1 + 1 + 7 + 1 + 24);
  });

  /**
   * This is the only text form of the hour bars, and a box of its own height would stop just after
   * Sunday and look finished, with the hours out of sight below it.
   */
  it('shows every row of the figures when they are opened, with no box to scroll', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    const summary = screen.getByText('Show these figures as a table');
    const table = within(summary.parentElement as HTMLElement).getByRole('table');

    expect(table.parentElement).not.toHaveClass('max-h-72');
  });

  /** The strips hold the same figures as the table, and say so to whoever points at a bar. */
  it('names each bar to whoever points at it', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    expect(
      within(tile('Busiest day of the week')).getByTitle('Wednesday: 40 page views'),
    ).toBeInTheDocument();
    expect(
      within(tile('Busiest hour')).getByTitle('2:00 – 2:59 PM: 50 page views'),
    ).toBeInTheDocument();
    expect(
      within(tile('Busiest hour')).getByTitle('12:00 – 12:59 AM: 0 page views'),
    ).toBeInTheDocument();
  });

  /** Ties name every place sharing the most: choosing one would say what the figures do not. */
  it('names every weekday and hour sharing the most', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        weekdays: [10, 30, 30, 10, 10, 5, 5],
        busiestWeekdays: [1, 2],
        hours: Array.from({ length: 24 }, (_, hour) => ([9, 14].includes(hour) ? 50 : 0)),
        busiestHours: [9, 14],
      },
    });

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    expect(tile('Busiest day of the week')).toHaveTextContent('Tuesday and Wednesday');
    expect(tile('Busiest day of the week')).toHaveTextContent('30% of page views each');
    expect(tile('Busiest hour')).toHaveTextContent('9:00 – 9:59 AM and 2:00 – 2:59 PM');
    expect(tile('Busiest hour')).toHaveTextContent('50% of page views each');
  });

  /** Several hours wrap on a phone, and a line ending at `9:00` reads as an hour of its own. */
  it('lets a line end between the hours it names, and never inside one', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        hours: Array.from({ length: 24 }, (_, hour) => ([9, 14].includes(hour) ? 50 : 0)),
        busiestHours: [9, 14],
      },
    });

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    for (const hour of ['9:00 – 9:59 AM', '2:00 – 2:59 PM']) {
      expect(within(tile('Busiest hour')).getByText(hour)).toHaveClass('whitespace-nowrap');
    }
  });

  /** Several long names would not fit their tile at the size one name is set at. */
  it('sets names smaller only where together they run long', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        weekdays: [10, 30, 30, 30, 10, 5, 5],
        busiestWeekdays: [1, 2, 3],
      },
    });

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    expect(within(tile('Busiest day of the week')).getByText('Tuesday').parentElement).toHaveClass(
      'text-base',
    );
    expect(within(tile('Busiest hour')).getByText('2:00 – 2:59 PM').parentElement).toHaveClass(
      'text-xl',
    );
  });

  /** Nothing read names no day, and the screen says so rather than inventing one. */
  it('names no day and no time where every month is left out', async () => {
    engineWith({
      everybody: {
        ...NOBODY,
        months: RECORD.months.map((month) => ({ ...month, pageViews: null, daysMeasured: 0 })),
        missing: RECORD.months.map((month) => month.month),
      },
    });

    renderScreen(<Insights />);

    await waitFor(() => expect(tile('Busiest day of the week')).toHaveTextContent('—'));
    expect(screen.getByText('Best day ever').parentElement).toHaveTextContent('—');
    expect(tile('Busiest hour')).toHaveTextContent('—');
    expect(tile('Busiest hour')).not.toHaveTextContent('of page views');
  });
});

describe('the months and years', () => {
  it('lays the months out newest year first and totals each year', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await monthsTable();
    const headers = within(table)
      .getAllByRole('rowheader')
      .map((header) => header.textContent);

    expect(headers).toEqual(['2026', '2025']);
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((column) => column.textContent),
    ).toEqual([
      'Year',
      'Jan',
      'Feb',
      'Mar',
      'Apr',
      'May',
      'Jun',
      'Jul',
      'Aug',
      'Sep',
      'Oct',
      'Nov',
      'Dec',
      'Total',
    ]);
    expect(monthCell(table, '2026', 9)).toHaveTextContent('2,000');
    expect(yearTotal(table, '2026')).toBe('5,180');
    expect(yearTotal(table, '2025')).toBe('120');
  });

  it('switches the same table to an average per day', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await monthsTable();

    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    const averaged = await screen.findByRole('table', {
      name: 'Page views per day in each month, counting only the days measured.',
    });

    expect(averaged).toBe(table);
    expect(within(averaged).getAllByRole('columnheader').at(-1)).toHaveTextContent('Whole year');
    // 120 over the twelve days measured of November, and a decimal on what is small enough to need one.
    expect(monthCell(averaged, '2025', 11)).toHaveTextContent('10');
    expect(monthCell(averaged, '2026', 10)).toHaveTextContent('3');
    expect(monthCell(averaged, '2026', 8)).toHaveTextContent('9.7');
    // 120 over the 43 days the year's figures cover, never the average of two months' averages.
    expect(yearTotal(averaged, '2025')).toBe('2.8');
  });

  /** One view in a long month is a quiet month, and nought is what a month nobody read says. */
  it('writes an average too small to round as its first digit, and a true nought as nought', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: [
          { month: '2026-08', pageViews: 1, daysMeasured: 31 },
          { month: '2026-09', pageViews: 0, daysMeasured: 30 },
          { month: '2026-10', pageViews: 1, daysMeasured: 20 },
        ],
      },
    });

    renderScreen(<Insights />);

    const table = await monthsTable();

    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    expect(monthCell(table, '2026', 8)).toHaveTextContent('0.03');
    expect(monthCell(table, '2026', 9)).toHaveTextContent(/^0$/);
    // Exactly at the line, where a decimal no longer rounds it away.
    expect(monthCell(table, '2026', 10)).toHaveTextContent('0.1');
    expect(yearTotal(table, '2026')).toBe('0.02');
  });

  it('shades a busier month more strongly', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await monthsTable();

    expect(monthCell(table, '2026', 9)).toHaveClass('bg-heat-5');
    expect(monthCell(table, '2026', 3)).toHaveClass('bg-heat-3');
    expect(monthCell(table, '2025', 11)).toHaveClass('bg-heat-2');
  });

  it('tells nought from before measuring and from still to come', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await monthsTable();

    expect(monthCell(table, '2025', 12)).toHaveTextContent('0');
    expect(monthCell(table, '2025', 12)).toHaveClass('bg-heat-0');
    expect(monthCell(table, '2025', 1)).toHaveTextContent('Not measured');
    expect(monthCell(table, '2025', 1).className).not.toContain('bg-heat');
    expect(monthCell(table, '2025', 1)).toHaveClass('text-foreground-muted');
    expect(monthCell(table, '2026', 11)).toHaveTextContent('Not yet');
    expect(monthCell(table, '2026', 11).className).not.toContain('bg-heat');
    expect(screen.getByText('Before measuring began')).toBeInTheDocument();
  });

  it('marks this month as so far', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await monthsTable();

    expect(monthCell(table, '2026', 10)).toHaveTextContent('90 so far');
    expect(monthCell(table, '2026', 10)).toHaveClass('outline-dashed');
    expect(monthCell(table, '2026', 9)).not.toHaveTextContent('so far');
    expect(screen.getByText('This month so far')).toBeInTheDocument();
  });

  it('names the scroll region and the table', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const table = await screen.findByRole('table', {
      name: 'Page views in each month, newest year first.',
    });

    expect(screen.getByRole('region', { name: 'Months and years' })).toContainElement(table);
  });

  it('says in words why a month has no figure, and leaves it out of its year', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: RECORD.months.map((month) =>
          month.month === '2026-02' ? { ...month, pageViews: null, daysMeasured: 0 } : month,
        ),
        missing: ['2026-02'],
      },
    });

    renderScreen(<Insights />);

    const table = await monthsTable();

    expect(monthCell(table, '2026', 2)).toHaveTextContent('Not counted');
    expect(monthCell(table, '2026', 2).className).not.toContain('bg-heat');
    expect(yearTotal(table, '2026')).toBe('4,900');
    expect(
      screen.getByText("February 2026 wasn't counted and is left out of these figures."),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Not counted').length).toBeGreaterThan(2);
    expect(screen.queryByText('Not fully judged')).not.toBeInTheDocument();
  });

  it('tells a month kept before its visits were all judged from one never counted', async () => {
    const left: InsightsAnswer = {
      ...PEOPLE,
      months: RECORD.months.map((month) =>
        month.month === '2026-03' ? { ...month, pageViews: null, daysMeasured: 0 } : month,
      ),
      unjudged: ['2026-03'],
    };

    engineWith({ people: left });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    const table = await monthsTable();

    expect(monthCell(table, '2026', 3)).toHaveTextContent('Not fully judged');
    expect(
      screen.getByText("March 2026 wasn't fully judged and is left out of these figures."),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Not fully judged').length).toBeGreaterThan(2);
    expect(screen.queryByText('Not counted')).not.toBeInTheDocument();
  });

  it('gives a year with no figures a mark rather than nought', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: RECORD.months.map((month) =>
          month.month.startsWith('2025') ? { ...month, pageViews: null, daysMeasured: 0 } : month,
        ),
        missing: ['2025-11', '2025-12'],
      },
    });

    renderScreen(<Insights />);

    const table = await monthsTable();

    expect(yearTotal(table, '2025')).toBe('–');

    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    expect(yearTotal(table, '2025')).toBe('–');
  });

  /** Every month already says in words what it is, and these words are about marks it cannot reach. */
  it('keeps the words of the legend from a screen reader', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await monthsTable();

    for (const words of ['Fewer', 'More', 'This month so far', 'Before measuring began']) {
      expect(screen.getByText(words).closest('[aria-hidden="true"]')).not.toBeNull();
    }
  });

  /** Forced colours paint shades and hatching out, so a key to either would point at nothing. */
  it('leaves out the keys to what forced colours do not draw, and keeps the rest', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: RECORD.months.map((month) =>
          month.month === '2026-02' ? { ...month, pageViews: null, daysMeasured: 0 } : month,
        ),
        missing: ['2026-02'],
      },
    });

    renderScreen(<Insights />);

    await monthsTable();

    const legend = screen.getByText('Fewer').closest<HTMLElement>('[aria-hidden="true"]');

    expect(legend).not.toBeNull();

    const leftOut = (words: string) =>
      within(legend as HTMLElement)
        .getByText(words)
        .closest('.forced-colors\\:hidden') !== null;

    expect(leftOut('Fewer')).toBe(true);
    expect(leftOut('Not counted')).toBe(true);
    expect(leftOut('This month so far')).toBe(false);
    expect(leftOut('Before measuring began')).toBe(false);
  });

  /** A table scrolled sideways on a phone shows three months and hides the rest. */
  it('lays each year out as a card of twelve tiles for a phone', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await monthsTable();

    const card = screen.getByRole('heading', { name: '2026', level: 3 }).closest('li');

    expect(card).not.toBeNull();

    const tiles = within(card as HTMLElement).getAllByRole('listitem');

    expect(tiles).toHaveLength(12);
    expect(tiles[8]).toHaveTextContent('Sep2,000');
    expect(tiles[8]).toHaveClass('bg-heat-5');
    expect(tiles[9]).toHaveTextContent('Oct90 so far');
    expect(tiles[10]).toHaveTextContent('NovNot yet');
    // The only place on a phone that this month is named, so it is written where it can be read.
    expect(tiles[10]).toHaveClass('text-foreground-muted');
    expect(card).toHaveTextContent('Total 5,180');
  });
});

describe('the insights screen kept to people', () => {
  it("asks for people's figures and says so once", async () => {
    const engine = engineWith({ people: PEOPLE });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('1,000')).toBeInTheDocument();
    expect(screen.getByText('Page views by people')).toBeInTheDocument();
    expect(screen.getByText('Daily visitors judged to be people')).toBeInTheDocument();
    expect(
      screen.getAllByText('Every figure counts only the visits judged to be people.'),
    ).toHaveLength(1);
    expect(engine.all().some(({ path }) => path.endsWith('/insights?only=people'))).toBe(true);
    expect(screen.getByRole('button', { name: /People only/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  /** With no number on the screen the clause qualifies nothing. */
  it.each([
    [
      'nobody among the visits',
      {
        people: { ...NOBODY, completeTo: EARLIER },
        who: whoCame(EARLIER, ['known-search-crawler']),
      },
      'No people yet',
    ],
    ['nothing to look back on', { everybody: NEVER }, 'Nothing to look back on yet'],
    ['a refusal', { refusing: ['/insights'] }, 'Something went wrong'],
  ])('leaves out that every figure counts only people above %s', async (_, engine, state) => {
    engineWith(engine);

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText(state)).toBeInTheDocument();
    expect(screen.queryByText(/Every figure counts only/)).not.toBeInTheDocument();
  });

  it('says every figure counts only people above their stand-ins too', async () => {
    engineWith({ holding: ['only=people'] });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    // Once the website is known, which is what names the clock its days are counted on.
    await screen.findByText('Days and hours are counted in Kolkata.');

    expect(screen.getByText('Page views by people')).toBeInTheDocument();
    expect(
      screen.getByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
  });

  /**
   * One press has one result, whether or not the answer it asks for is already held. It is the
   * comparison the switch exists for: the average among everybody against the average among people.
   */
  it('keeps how the months are read and the open figures through a first press of People only', async () => {
    engineWith({ people: PEOPLE });

    renderScreen(<Insights />);

    await monthsTable();
    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(await screen.findByText('1,000')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Average per day' })).toBeChecked();
    expect(
      await screen.findByRole('table', {
        name: 'Page views per day in each month, counting only the days measured.',
      }),
    ).toBeInTheDocument();
    expect(screen.getByText('Show these figures as a table').closest('details')).toHaveAttribute(
      'open',
    );
  });

  /**
   * A visit is judged once it has gone quiet and the next run comes round, so every answer is behind by
   * as much as its installation is set to wait, and none of that is judging falling behind.
   */
  it.each([
    ['half an hour', HALF_AN_HOUR_BEHIND],
    ['twelve hours', TWELVE_HOURS_BEHIND],
    ['thirteen hours', THIRTEEN_HOURS_BEHIND],
    ['exactly a day', A_DAY_BEHIND],
  ])('says nothing of judging where it is %s behind', async (_, completeTo) => {
    engineWith({ people: { ...PEOPLE, completeTo } });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    await screen.findByText('1,000');

    expect(
      screen.getByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
  });

  /** Further behind than a visit waiting its turn explains, something is behind, and that is worth a clause. */
  it('says once that the newest are still being judged where judging is two days behind', async () => {
    engineWith({ people: { ...PEOPLE, completeTo: TWO_DAYS_BEHIND } });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(
      await screen.findByText(
        'Every figure counts only the visits judged to be people. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
  });

  /** Under everybody the check and the screen's own answer are one question, asked once. */
  it('asks once under everybody', async () => {
    const engine = engineWith({});

    renderScreen(<Insights />);

    await screen.findByText('4,321');

    expect(engine.all().filter(({ path }) => path.includes('/insights'))).toHaveLength(1);
  });

  it('opens on the people when they were chosen last', async () => {
    const engine = engineWith({ people: PEOPLE });

    window.localStorage.setItem('dewiride.population', 'people');

    renderScreen(<Insights />);

    expect(await screen.findByText('1,000')).toBeInTheDocument();
    expect(engine.all().some(({ path }) => path.endsWith('/insights?only=people'))).toBe(true);
  });

  /**
   * Written into an address that says nothing, so that the link in the bar says what is on the
   * screen, and with no entry in the history, since nothing anybody pressed put it there.
   */
  it('writes the people chosen last into its address once its website is known', async () => {
    const watching = vi.fn();

    engineWith({ people: PEOPLE });
    window.localStorage.setItem('dewiride.population', 'people');

    renderScreen(<Insights />, { watchingAddress: watching });

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('only=people');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  it('offers everyone back when nobody was a person', async () => {
    engineWith({
      people: { ...NOBODY, completeTo: EARLIER },
      who: whoCame(EARLIER, ['known-search-crawler']),
    });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show everyone' }));

    expect(await screen.findByText('4,321')).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  /**
   * The cards are off the screen while it says there were no people, and how they were being read is
   * still how they are read once they are back.
   */
  it('keeps how the months are read and the open figures through a look at the people where there were none', async () => {
    engineWith({
      people: { ...NOBODY, completeTo: EARLIER },
      who: whoCame(EARLIER, ['known-search-crawler']),
    });

    renderScreen(<Insights />);

    await monthsTable();
    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(await screen.findByText('No people yet')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show everyone' }));

    expect(
      await screen.findByRole('table', {
        name: 'Page views per day in each month, counting only the days measured.',
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Average per day' })).toBeChecked();
    expect(screen.getByText('Show these figures as a table').closest('details')).toHaveAttribute(
      'open',
    );
  });

  it('says there are no people yet where every visit judged was something else', async () => {
    engineWith({
      people: { ...NOBODY, completeTo: EARLIER },
      who: whoCame(EARLIER, ['known-search-crawler']),
    });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(
      screen.getByText('None of the visits so far has been judged to be a person.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show everyone' })).toBeInTheDocument();
  });

  /**
   * A record runs up to the present, which is always still being judged, so it is never in a position
   * to say that nobody at all was a person, however far judging has reached.
   */
  it('says no people yet however far judging has reached', async () => {
    engineWith({ people: NOBODY, who: whoCame(TO, ['known-search-crawler']) });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(
      screen.getByText('None of the visits so far has been judged to be a person.'),
    ).toBeInTheDocument();
  });

  it('says nothing has been judged yet where judging has reached none of the visits', async () => {
    engineWith({ people: { ...NOBODY, completeTo: EARLIER }, who: whoCame(EARLIER, []) });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(
      screen.getByText('People appear here once their visits have finished and been judged.'),
    ).toBeInTheDocument();
  });

  it('says everything is still being judged where not one month has settled', async () => {
    const stopped = '2026-09-02T00:00:00+00:00';

    engineWith({ people: { ...NOBODY, completeTo: stopped }, who: whoCame(stopped, []) });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText('Everything so far is still being judged.')).toBeInTheDocument();
  });

  /** Nought is then only what was counted, and the table says which months were not. */
  it('does not call the people absent while months are left out of their figures', async () => {
    const engine = engineWith({
      people: {
        ...NOBODY,
        months: NOBODY.months.map((month) =>
          month.month === '2026-03' ? { ...month, pageViews: null, daysMeasured: 0 } : month,
        ),
        unjudged: ['2026-03'],
      },
    });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    const table = await monthsTable();

    expect(monthCell(table, '2026', 3)).toHaveTextContent('Not fully judged');
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
    expect(engine.all().some(({ path }) => path.includes('/all-time/traffic/series'))).toBe(false);
  });

  it('says what went wrong where who came could not be had', async () => {
    engineWith({ people: NOBODY, refusing: ['/all-time/traffic/series'] });

    const { container } = renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).toBeNull();
  });

  /** An answer already read is still the answer, as the record's own is when asking again fails. */
  it('keeps saying no people yet where asking who came a second time fails', async () => {
    const refusing: string[] = [];
    const engine = engineWith({
      people: { ...NOBODY, completeTo: EARLIER },
      who: whoCame(EARLIER, ['known-search-crawler']),
      refusing,
    });

    const { cache } = renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();

    refusing.push('/all-time/traffic/series');

    await act(async () => {
      await cache.invalidateQueries();
      // What the cache has heard reaches the screen a tick later, and nothing here is there to wait for.
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(
      engine.all().filter(({ path }) => path.includes('/all-time/traffic/series')),
    ).toHaveLength(2);
    expect(
      cache
        .getQueryCache()
        .getAll()
        .some(({ state }) => state.error !== null),
    ).toBe(true);
    expect(screen.getByText('No people yet')).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });
});

describe('the insights screen with nothing to show yet', () => {
  it('offers the tracking code before any visit', async () => {
    engineWith({ everybody: NEVER });

    renderScreen(<Insights />);

    expect(await screen.findByText('Nothing to look back on yet')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Once example.com has its first visit, its history builds up here, month by month.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Get your tracking code' }));

    expect(await screen.findByRole('dialog')).toHaveTextContent(SITE.id);
  });

  it('says the same kept to people, since a website nobody has visited has no people to miss', async () => {
    engineWith({ everybody: NEVER });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    expect(await screen.findByText('Nothing to look back on yet')).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  it('says what went wrong when the engine refuses', async () => {
    engineWith({ refusing: ['/insights'] });

    const { container } = renderScreen(<Insights />);

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Insights', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    // Nothing goes on standing in for an answer that is not coming.
    expect(container.querySelector('.animate-pulse')).toBeNull();
  });

  it('holds the record’s place, in its own words, while it is on its way', async () => {
    engineWith({ holding: ['/insights'] });

    const { container } = renderScreen(<Insights />);

    const highlights = await screen.findByRole('region', { name: 'All-time highlights' });

    expect(within(highlights).getByText('Page views')).toBeInTheDocument();
    expect(within(highlights).getByText('Daily visitors')).toBeInTheDocument();
    await waitFor(() => expect(container.querySelector('.animate-pulse')).not.toBeNull());
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();

    // The cards themselves, so nothing below them moves when their figures land.
    expect(screen.getByRole('heading', { name: 'Most popular times' })).toBeInTheDocument();
    expect(screen.getByText('Days and hours are counted in Kolkata.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Best day ever' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Busiest day of the week' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Busiest hour' })).toBeInTheDocument();
    expect(screen.getByText('Show these figures as a table')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Months and years' })).toBeInTheDocument();
    expect(screen.getAllByText('Page views in each month, newest year first.')).not.toHaveLength(0);
    // A figure on its way is nothing to read, so nothing about it reaches a screen reader as a table.
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  /**
   * A box too narrow for its table is a stop for the keyboard in a browser that lets one be scrolled by
   * it, and the stand-in's goes when the figures land, taking the keyboard with it. No test here can
   * see which boxes overflow, so what is asserted is that this one could never hold the keyboard.
   */
  it('keeps the keyboard out of the months while their figures are on their way', async () => {
    engineWith({ holding: ['/insights'] });

    const { container } = renderScreen(<Insights />);

    await screen.findByRole('heading', { name: 'Months and years' });
    await waitFor(() => expect(container.querySelector('.overflow-x-auto')).not.toBeNull());

    for (const box of container.querySelectorAll('.overflow-x-auto')) {
      expect(box.closest('[inert]')).not.toBeNull();
    }
  });

  /**
   * The best day's stand-in is a day written out and hidden, and whatever is below it moves when the
   * real one takes more lines than it did or fewer. One about as long as most are written is right for
   * most days, where one of the longest is wrong for most of them.
   */
  it('stands in for the best day with a day about as long as most are written', async () => {
    engineWith({ holding: ['/insights'] });

    renderScreen(<Insights />);

    const standIn = (await screen.findByRole('heading', { name: 'Best day ever' }))
      .nextElementSibling;
    const written = new Intl.DateTimeFormat('en', { dateStyle: 'full', timeZone: 'UTC' });
    const lengths = Array.from(
      { length: 365 },
      (_, day) => written.format(new Date(Date.UTC(2026, 0, day + 1))).length,
    ).sort((one, other) => one - other);
    const quarter = Math.floor(lengths.length / 4);

    // No shorter than a quarter of the year's days are written, and no longer than a quarter are.
    expect(standIn?.textContent?.length).toBeGreaterThanOrEqual(lengths[quarter] ?? 0);
    expect(standIn?.textContent?.length).toBeLessThanOrEqual(lengths.at(-quarter - 1) ?? 0);
  });

  /** The switch is already there, so what it chooses has to be what the figures arrive under. */
  it('takes how the months are read while the figures are on their way', async () => {
    const answer = engineKeepingBack('record');

    renderScreen(<Insights />);

    await screen.findByText('Days and hours are counted in Kolkata.');
    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    answer();

    expect(
      await screen.findByRole('table', {
        name: 'Page views per day in each month, counting only the days measured.',
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Average per day' })).toBeChecked();
  });

  /** The same, before the website is known: there is one choice, whenever it was made. */
  it('takes how the months are read before the website is known', async () => {
    const known = engineKeepingBack('website');

    renderScreen(<Insights />);

    await userEvent.click(await screen.findByRole('radio', { name: 'Average per day' }));

    known();

    expect(
      await screen.findByRole('table', {
        name: 'Page views per day in each month, counting only the days measured.',
      }),
    ).toBeInTheDocument();
  });

  /**
   * The line that unfolds the figures is already there as well, and it works: what it is left at is
   * how the figures arrive. Until they do, what stands in for them is nothing to read.
   */
  it('takes whether the figures are unfolded while they are on their way', async () => {
    const answer = engineKeepingBack('record');

    renderScreen(<Insights />);

    await screen.findByText('Days and hours are counted in Kolkata.');
    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getByText('Show these figures as a table').closest('details')).toHaveAttribute(
      'open',
    );
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    answer();

    expect(
      await screen.findByRole('row', { name: saying('Wednesday 40 40%') }),
    ).toBeInTheDocument();
    expect(screen.getByText('Show these figures as a table').closest('details')).toHaveAttribute(
      'open',
    );
  });

  /**
   * The screen is drawn once and then filled in, so whatever somebody is on while it waits is the
   * same thing afterwards and the keyboard is still on it. Drawn again in its place, it would be
   * another thing that looks the same, and the keyboard would be left on the page.
   */
  it.each(
    (
      [
        ['the figures land', 'record'],
        ['the website becomes known', 'website'],
      ] as const
    ).flatMap(([arrival, kept]) =>
      HELD_THROUGH_THE_WAIT.map(([name, control]) => [name, arrival, control, kept] as const),
    ),
  )('keeps the keyboard on %s when %s', async (_, __, control, kept) => {
    const arrive = engineKeepingBack(kept);

    renderScreen(<Insights />);

    if (kept === 'record') {
      // Once the website is known, which is what names the clock its days are counted on.
      await screen.findByText('Days and hours are counted in Kolkata.');
    }

    const held = await waitFor(control);

    held.focus();

    expect(held).toHaveFocus();

    arrive();

    await screen.findByText('4,321');

    expect(control()).toBe(held);
    expect(held).toHaveFocus();
  });

  it('draws the same screen before the website is known', async () => {
    engineWith({ holding: ['/api/sites'] });

    renderScreen(<Insights />);

    expect(await screen.findByRole('heading', { name: 'Insights', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /People only/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'All-time highlights' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Most popular times' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Months and years' })).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });
});

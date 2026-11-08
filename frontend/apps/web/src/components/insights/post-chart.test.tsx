import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PostChart, PostChartWaiting } from '@/components/insights/post-chart';
import type { Population } from '@/lib/analytics/people-only';
import type { PostInsights, Site } from '@/lib/api/schemas';
import { drawn } from '@/test/drawing';
import { type Engine, engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * The drawing itself needs a canvas, which this document does not have. Everything around it — the
 * key, the line under it, and the table the same figures are published in — is ordinary markup and is
 * exercised for real; what the drawing would have been told to draw is kept where a test can read it.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

/**
 * Saturday 7 November 2026, three in the afternoon in Kolkata. Which days a period covers, and what a
 * pressed day is called, depend on what day it is where the website is, so the clock is stood still —
 * only the clock, since pressing things and waiting for answers need the timers beneath it.
 */
const NOW = new Date('2026-11-07T09:30:00Z');

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.localStorage.clear();
  drawn.pick = undefined;
  drawn.picks.clear();
  drawn.options.clear();
});

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const PATH = '/hello-world/';

/** When the post's record was read. */
const TO = '2026-11-07T09:30:00+00:00';

/** One month as the engine answers it. */
function month(written: string, pageViews: number | null, daysMeasured: number) {
  return { month: written, pageViews, daysMeasured };
}

/** Every month of a post published on 12 March 2025, read most in April that year. */
const MONTHS: PostInsights['months'] = [
  month('2025-03', 120, 20),
  month('2025-04', 300, 30),
  month('2025-05', 0, 31),
  month('2025-06', 150, 30),
  month('2025-07', 90, 31),
  month('2025-08', 60, 31),
  month('2025-09', 30, 30),
  month('2025-10', 31, 31),
  month('2025-11', 45, 30),
  month('2025-12', 62, 31),
  month('2026-01', 40, 31),
  month('2026-02', 28, 28),
  month('2026-03', 62, 31),
  month('2026-04', 30, 30),
  month('2026-05', 31, 31),
  month('2026-06', 30, 30),
  month('2026-07', 31, 31),
  month('2026-08', 45, 31),
  month('2026-09', 21, 30),
  month('2026-10', 150, 31),
  month('2026-11', 33, 7),
];

/**
 * The post's record. Its website was first measured on 19 November 2024, months before the post's own
 * first day, so the two days a picture could be wrongly opened on are never the same day.
 */
const RECORD: PostInsights = {
  post: {
    path: PATH,
    address: 'https://example.com/hello-world/',
    title: 'Hello, world',
    published: '2025-03-12',
    pageViews: 1380,
  },
  viewsFrom: '2024-11',
  firstDay: '2024-11-19',
  from: '2025-03-12',
  to: TO,
  completeTo: TO,
  months: MONTHS,
  days: [],
};

/** How far ahead of the engine's clock the website's clock in Kolkata always is. */
const KOLKATA_AHEAD = 5.5 * 3_600_000;

const AN_HOUR = 3_600_000;
const A_DAY = 24 * AN_HOUR;

/**
 * Where each way of cutting a picture begins the bucket holding a moment, and the bucket after one,
 * both read on the website's calendar: a moment held five and a half hours on, whose own calendar is
 * then Kolkata's.
 */
const CUTS: Readonly<
  Record<
    string,
    { readonly holding: (local: Date) => number; readonly after: (local: Date) => number }
  >
> = {
  hour: { holding: (local) => local.getTime(), after: (local) => local.getTime() + AN_HOUR },
  day: {
    holding: (local) => Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate()),
    after: (local) => local.getTime() + A_DAY,
  },
  week: {
    holding: (local) =>
      Date.UTC(
        local.getUTCFullYear(),
        local.getUTCMonth(),
        local.getUTCDate() - ((local.getUTCDay() + 6) % 7),
      ),
    after: (local) => local.getTime() + 7 * A_DAY,
  },
  month: {
    holding: (local) => Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), 1),
    after: (local) => Date.UTC(local.getUTCFullYear(), local.getUTCMonth() + 1, 1),
  },
};

/**
 * The moment each bucket of a window begins, as the engine names them: every hour, day, Monday to
 * Sunday week or calendar month the window reaches into where the website is.
 */
function bucketsOver(from: string, to: string, granularity: string): readonly string[] {
  const cut = CUTS[granularity];
  const ends = Date.parse(to);
  const starts: string[] = [];

  if (cut === undefined) {
    return starts;
  }

  for (
    let local = cut.holding(new Date(Date.parse(from) + KOLKATA_AHEAD));
    local - KOLKATA_AHEAD < ends;
    local = cut.after(new Date(local))
  ) {
    starts.push(new Date(local - KOLKATA_AHEAD).toISOString());
  }

  return starts;
}

/** What a question asked, read back the way the engine reads it. */
function askedIn(path: string): URLSearchParams {
  return new URLSearchParams(path.slice(path.indexOf('?') + 1));
}

interface Answering {
  /** Each bucket's views, by its place in the picture: ten, twenty, thirty and on by default. */
  readonly views?: (at: number) => number;
  /** Whether every question is refused. */
  readonly refusing?: boolean;
  /** Whether no question is ever answered. */
  readonly holding?: boolean;
}

/**
 * The engine, answering the post's views over whatever window and buckets it is asked about, as it
 * cuts them. Kept to people, judging has yet to reach the newest bucket.
 */
function engineWith({
  views = (at) => 10 * (at + 1),
  refusing = false,
  holding = false,
}: Answering = {}): Engine {
  return engineDoing(async (path) => {
    if (holding) {
      return new Promise<Response>(() => {});
    }

    if (refusing) {
      return respondWith(500, { status: 500, title: 'The engine is having trouble.' });
    }

    const asked = askedIn(path);
    const from = asked.get('from') ?? '';
    const to = asked.get('to') ?? '';
    const granularity = asked.get('granularity') ?? '';
    const starts = bucketsOver(from, to, granularity);

    return respondWith(200, {
      from,
      to,
      metric: 'pageviews',
      granularity,
      completeTo: asked.get('only') === 'people' ? (starts.at(-1) ?? to) : to,
      points: starts.map((bucketStart, at) => ({ bucketStart, value: views(at) })),
    });
  });
}

/** Every question put to the engine about the post's views, read back. */
function seriesAsked(engine: Engine): readonly URLSearchParams[] {
  return engine
    .all()
    .filter(({ path }) => path.includes(`/api/sites/${SITE.id}/insights/post/series?`))
    .map(({ path }) => askedIn(path));
}

/** Whether the post's views were asked for over exactly this window, cut this way. */
function askedOver(engine: Engine, granularity: string, from: string, to: string): boolean {
  return seriesAsked(engine).some(
    (asked) =>
      asked.get('granularity') === granularity &&
      asked.get('from') === from &&
      asked.get('to') === to,
  );
}

/** The picture of the post's views, for whoever the screen is kept to. */
function drawChart(
  answer: PostInsights = RECORD,
  population: Population = 'everybody',
  options: Parameters<typeof renderScreen>[1] = {},
) {
  return renderScreen(
    <PostChart
      site={SITE}
      answer={answer}
      population={population}
      peopleOnly={population === 'people'}
    />,
    options,
  );
}

/** What the picture announces itself as, by whose views it draws. */
const EVERYBODY_DRAWN = 'Views of this post over the chosen period.';
const PEOPLE_DRAWN = 'Views by people of this post over the chosen period.';

/** The list of ways the picture can be cut, as it is reached without seeing the card. */
function ways() {
  return screen.findByRole('combobox', { name: 'Show the chart by' });
}

/** What a list offers, in order. */
function options(list: HTMLElement): readonly (string | null)[] {
  return within(list)
    .getAllByRole('option')
    .map((option) => option.textContent);
}

/** Unfolds the figures the picture was drawn from. */
async function unfold() {
  await userEvent.click(await screen.findByText('Show these figures as a table'));
}

/** The name each row of the table is headed with, with what is marked beneath it. */
function rowheaders(): readonly (string | undefined)[] {
  return screen.getAllByRole('rowheader').map((row) => row.textContent?.replace(/\s+/g, ' '));
}

/** The series a drawing would have been told to draw, by what it announces. */
function drawnSeries(label: string): readonly Record<string, unknown>[] {
  return (drawn.options.get(label)?.series as readonly Record<string, unknown>[] | undefined) ?? [];
}

/** A post first counted in February this year, on a website measured since well before it. */
const THIS_YEAR: PostInsights = {
  ...RECORD,
  post: { ...RECORD.post, published: '2026-02-03' },
  from: '2026-02-03',
  months: MONTHS.filter((one) => one.month >= '2026-02'),
};

describe('the picture of a post’s views', () => {
  it('opens on the last 7 days by day where nothing is remembered', async () => {
    const engine = engineWith();

    drawChart();

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('last-7-days');
    expect(await ways()).toHaveValue('day');
    expect(await screen.findByRole('img', { name: EVERYBODY_DRAWN })).toBeInTheDocument();
    expect(askedOver(engine, 'day', '2026-10-31T18:30:00.000Z', '2026-11-07T10:00:00.000Z')).toBe(
      true,
    );

    await unfold();

    expect(rowheaders()).toStrictEqual([
      'Nov 1',
      'Nov 2',
      'Nov 3',
      'Nov 4',
      'Nov 5',
      'Nov 6',
      'Nov 7',
    ]);
  });

  /**
   * The overview's own period and cut, which the post's picture shares: the stretch somebody reads
   * over is a habit, and the link in the bar still says what is on the screen, written in over the
   * entry somebody is already on, since nothing they pressed put it there.
   */
  it('opens on the period and the cut remembered last, and writes them into its address', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.period', 'last-90-days');
    window.localStorage.setItem('dewiride.chart-by', 'week');

    const engine = engineWith();

    drawChart(RECORD, 'everybody', { watchingAddress: watching, arrivingAfterAddress: true });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('last-90-days');
    expect(await ways()).toHaveValue('week');
    await waitFor(() =>
      expect(
        askedOver(engine, 'week', '2026-08-09T18:30:00.000Z', '2026-11-07T10:00:00.000Z'),
      ).toBe(true),
    );
    await waitFor(() => {
      const written = watching.mock.calls.at(-1)?.[0];

      expect(written?.searchParams.get('period')).toBe('last-90-days');
      expect(written?.searchParams.get('by')).toBe('week');
    });
    expect(watching.mock.calls.every(([written]) => written.options.history === 'replace')).toBe(
      true,
    );
  });

  it('asks for the post’s views over the window of the period on screen, of the people where the screen is kept to them', async () => {
    const engine = engineWith();
    const { unmount } = drawChart(RECORD, 'everybody', { searchParams: '?period=last-30-days' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });
    unmount();

    drawChart(RECORD, 'people', { searchParams: '?period=last-30-days' });

    await screen.findByRole('img', { name: PEOPLE_DRAWN });

    const [everybody, people] = seriesAsked(engine).map((asked) => [...asked.entries()]);
    const window = [
      ['path', PATH],
      ['granularity', 'day'],
      ['from', '2026-10-08T18:30:00.000Z'],
      ['to', '2026-11-07T10:00:00.000Z'],
    ];

    expect(everybody).toStrictEqual(window);
    expect(people).toStrictEqual([...window, ['only', 'people']]);
  });

  it('draws one measure, keyed and tabled under the post’s own name for it', async () => {
    engineWith();

    drawChart();

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(screen.getAllByRole('listitem').map((key) => key.textContent)).toStrictEqual(['Views']);
    expect(
      drawnSeries(EVERYBODY_DRAWN)
        .filter((one) => one.markArea === undefined)
        .map((one) => one.name),
    ).toStrictEqual(['Views']);

    await unfold();

    expect(screen.getAllByRole('columnheader').map((heading) => heading.textContent)).toStrictEqual(
      ['Day', 'Views'],
    );
    expect(screen.getByRole('row', { name: 'Nov 3 30' })).toBeInTheDocument();
    expect(screen.queryByText(/Page views|visitors/)).not.toBeInTheDocument();
  });

  /** A post's views are what is counted, so nothing is said about who came back to read them. */
  it('says weeks run Monday to Sunday and nothing about visitors coming back', async () => {
    engineWith();

    drawChart(RECORD, 'everybody', { searchParams: '?period=last-30-days&by=week' });

    expect(await screen.findByText(/^Weeks run Monday to Sunday in Kolkata\./)).toBeInTheDocument();
    expect(screen.queryByText(/comes back/)).not.toBeInTheDocument();
  });

  /**
   * The last seven days are too short for weeks, and the way to weeks is in the list itself: choosing
   * it moves to the shortest period that has weeks to show and keeps the choice, and anybody choosing
   * without looking hears which period the card moved to.
   */
  it('moves to a period long enough for a cut it cannot show, and says so to anybody listening', async () => {
    const watching = vi.fn();
    const engine = engineWith();

    drawChart(RECORD, 'everybody', { watchingAddress: watching });

    const list = await ways();

    expect(
      within(list).getByRole('option', { name: 'By week · Last 30 days' }),
    ).toBeInTheDocument();

    fireEvent.change(list, { target: { value: 'week' } });

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('last-30-days'),
    );
    expect(await screen.findByText('Period changed to Last 30 days.')).toHaveAttribute(
      'aria-live',
      'polite',
    );
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toHaveValue('week'),
    );
    await waitFor(() =>
      expect(
        askedOver(engine, 'week', '2026-10-08T18:30:00.000Z', '2026-11-07T10:00:00.000Z'),
      ).toBe(true),
    );

    const written = watching.mock.calls.at(-1)?.[0];

    expect(written?.queryString).toContain('period=last-30-days');
    expect(written?.queryString).toContain('by=week');
  });

  it('draws all time from the post’s months without asking the engine anything', async () => {
    const engine = engineWith();

    drawChart(RECORD, 'everybody', { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(drawnSeries(EVERYBODY_DRAWN)[0]?.data).toStrictEqual(MONTHS.map((one) => one.pageViews));

    await unfold();

    expect(screen.getByRole('columnheader', { name: 'Month' })).toBeInTheDocument();
    expect(rowheaders()).toHaveLength(MONTHS.length);
    expect(screen.getByRole('row', { name: 'Apr 2025 300' })).toBeInTheDocument();
    expect(engine.count).toBe(0);
  });

  /** The website was measured from November 2024; the post's own record begins on 12 March 2025. */
  it('cuts all time’s first month at the post’s first day', async () => {
    engineWith();

    drawChart(RECORD, 'everybody', { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });
    await unfold();

    expect(rowheaders()[0]).toBe('Mar 12 – 31, 2025');
    expect(rowheaders().at(-1)).toBe('Nov 1 – 7, 2026');
  });

  /**
   * A year adds up the months of it that were counted, as the table of months does, so its figure
   * stands with no mark of its own: the line under the picture names the month, and the year's row
   * says it leaves it out.
   */
  it('adds all time up into years, naming the months a year leaves out', async () => {
    engineWith();

    drawChart(
      {
        ...RECORD,
        months: MONTHS.map((one) =>
          one.month === '2025-06' ? { ...one, pageViews: null, daysMeasured: 0 } : one,
        ),
      },
      'everybody',
      { searchParams: '?period=all-time&by=year' },
    );

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(
      screen.getByText(/June 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();

    await unfold();

    expect(screen.getByRole('columnheader', { name: 'Year' })).toBeInTheDocument();
    expect(rowheaders()).toStrictEqual([
      'Mar 12 – Dec 31, 2025leaves out June',
      'Jan 1 – Nov 7, 2026',
    ]);
    expect(screen.getAllByRole('cell').map((cell) => cell.textContent)).toStrictEqual([
      '738',
      '501',
    ]);
    expect(screen.queryByText('Not counted')).not.toBeInTheDocument();
  });

  /**
   * Years are all time's alone, and offered only where the post's own days run across a first of
   * January: its website running across one says nothing about the post.
   */
  it('offers years only where the post’s record runs across a new year', async () => {
    engineWith();

    const shorter = drawChart(THIS_YEAR, 'everybody', { searchParams: '?period=last-90-days' });

    expect(options(await ways())).toStrictEqual(['By day', 'By week', 'By month']);
    shorter.unmount();

    const allTime = drawChart(THIS_YEAR, 'everybody', { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).not.toBeInTheDocument();
    allTime.unmount();

    drawChart(RECORD, 'everybody', { searchParams: '?period=last-90-days' });

    expect(options(await ways())).toStrictEqual([
      'By day',
      'By week',
      'By month',
      'By year · All time',
    ]);
  });

  /**
   * The row pressed has gone with the old period, so the reader is handed to the control that names
   * the new one, brought into view.
   */
  it('narrows the period to the days of a pressed bucket and hands the reader to the period', async () => {
    const watching = vi.fn();
    const focusing = vi.spyOn(HTMLSelectElement.prototype, 'focus');
    const engine = engineWith();

    drawChart(RECORD, 'everybody', { watchingAddress: watching });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });
    await unfold();
    await userEvent.click(screen.getByRole('button', { name: 'Nov 3, look at this day' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({
          queryString: '?period=2026-11-03..2026-11-03',
          options: expect.objectContaining({ history: 'push' }),
        }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('chosen');
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();
    expect(focusing).toHaveBeenCalledWith({ preventScroll: false });
    await waitFor(() =>
      expect(
        askedOver(engine, 'hour', '2026-11-02T18:30:00.000Z', '2026-11-03T18:30:00.000Z'),
      ).toBe(true),
    );
  });

  /** A month held as its totals alone has no days to narrow the period to. */
  it('offers only the months still held day by day to be pressed under all time', async () => {
    engineWith();

    drawChart(RECORD, 'everybody', { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });
    await unfold();

    const pressable = screen
      .getAllByRole('button', { name: /look at these days$/ })
      .map((row) => row.textContent?.replace(/\s+/g, ' '));

    expect(pressable[0]).toBe('Dec 2025');
    expect(pressable.at(-1)).toBe('Nov 1 – 7, 2026');
    expect(pressable).not.toContain('Nov 2025');
    expect(screen.getByRole('rowheader', { name: 'Nov 2025' })).toBeInTheDocument();
  });

  it('washes the buckets still being judged where the screen is kept to people', async () => {
    engineWith();

    drawChart(RECORD, 'people');

    expect(
      await screen.findByText(
        'Days run midnight to midnight in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    expect(drawnSeries(PEOPLE_DRAWN).some((one) => one.markArea !== undefined)).toBe(true);
    expect(screen.getAllByRole('listitem').map((key) => key.textContent)).toStrictEqual([
      'Views by people',
    ]);

    await unfold();

    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  /**
   * A run of noughts is honest and reads as a fault. Kept to people, a period nobody was counted in
   * may yet hold people in the buckets still being judged, and says so. All time over a record with
   * no month in it has nothing to draw at all, and says the same rather than waiting for ever.
   */
  it('puts a sentence in place of a period nobody read the post in', async () => {
    engineWith({ views: () => 0 });

    const everybody = drawChart();

    expect(await screen.findByText('Nobody read this post in this period.')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText('Show these figures as a table')).not.toBeInTheDocument();
    everybody.unmount();

    const people = drawChart(RECORD, 'people');

    expect(
      await screen.findByText(
        'No people read this post in this period. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    people.unmount();

    const unmeasured = drawChart({ ...RECORD, from: null, months: [] }, 'everybody', {
      searchParams: '?period=all-time',
    });

    expect(await screen.findByText('Nobody read this post in this period.')).toBeInTheDocument();
    expect(unmeasured.container.querySelector('.animate-pulse')).toBeNull();
    expect(screen.queryByText(/^Since /)).not.toBeInTheDocument();
  });

  /** The controls stay, so another period is a choice away rather than a reload. */
  it('says what went wrong inside the card and keeps its controls', async () => {
    engineWith({ refusing: true });

    drawChart();

    const card = (await screen.findByText('Something went wrong')).closest('section');

    expect(card).not.toBeNull();

    const inside = within(card as HTMLElement);

    expect(inside.getByRole('heading', { name: 'Views over time' })).toBeInTheDocument();
    expect(inside.getByRole('combobox', { name: 'Period' })).toBeInTheDocument();
    expect(inside.getByRole('combobox', { name: 'Show the chart by' })).toBeInTheDocument();
    expect(inside.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  /**
   * The controls are offered at once, and the ways of drawing settled from how many buckets the
   * period is due to have; only the picture waits. Before the post's record is in there is no period
   * to offer yet, and its place is held as well.
   */
  it('holds the picture’s place while the views are on their way', async () => {
    engineWith({ holding: true });

    const waiting = drawChart();

    expect(await screen.findByRole('combobox', { name: 'Period' })).toBeInTheDocument();
    expect(await ways()).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBeInTheDocument();
    expect(waiting.container.querySelector('.animate-pulse')).not.toBeNull();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText('Show these figures as a table')).not.toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
    waiting.unmount();

    const record = renderScreen(<PostChartWaiting />);

    expect(screen.getByRole('heading', { name: 'Views over time' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    // The period, how the picture is cut and how it is drawn, and the picture: the card the record
    // brings in, so nothing below it moves when it lands.
    expect(record.container.querySelectorAll('.animate-pulse')).toHaveLength(4);
  });

  it('names all time by the post’s first day', async () => {
    engineWith();

    drawChart(RECORD, 'everybody', { searchParams: '?period=all-time' });

    expect(await screen.findByText('Since Mar 12, 2025')).toBeInTheDocument();
    expect(screen.queryByText('Since Nov 19, 2024')).not.toBeInTheDocument();
  });
});

/** A post published on Tuesday 3 November, its record beginning that day. */
const PUBLISHED_TUESDAY: PostInsights = {
  ...RECORD,
  post: { ...RECORD.post, published: '2026-11-03' },
  from: '2026-11-03',
  months: [month('2026-11', 412, 5)],
};

/** A post published on Wednesday 14 October, its record beginning that day. */
const PUBLISHED_IN_OCTOBER: PostInsights = {
  ...RECORD,
  post: { ...RECORD.post, published: '2026-10-14' },
  from: '2026-10-14',
  months: [month('2026-10', 300, 18), month('2026-11', 112, 7)],
};

/** The window of the last 30 days, ending at the top of the hour after now. */
const LAST_30_DAYS_FROM = '2026-10-08T18:30:00.000Z';
const NEXT_HOUR = '2026-11-07T10:00:00.000Z';

/**
 * A day before a post was published is not a day nobody read it. The Months card calls those months
 * before it was published; the picture does not draw them as nought.
 */
describe('a period that begins before the post was published', () => {
  it('opens on the day the post was published, and says so beneath the picture', async () => {
    const engine = engineWith();

    drawChart(PUBLISHED_TUESDAY, 'everybody', { searchParams: '?period=last-30-days' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(seriesAsked(engine).map((asked) => asked.get('from'))).toStrictEqual([
      '2026-11-02T18:30:00.000Z',
    ]);
    expect(askedOver(engine, 'day', '2026-11-02T18:30:00.000Z', NEXT_HOUR)).toBe(true);
    expect(
      screen.getByText(
        'Days run midnight to midnight in Kolkata. It begins on November 3, 2026, the day the post was published.',
      ),
    ).toBeInTheDocument();
    // The period on screen is still the one chosen: only the picture opens later.
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('last-30-days');

    await unfold();

    expect(rowheaders()).toStrictEqual(['Nov 3', 'Nov 4', 'Nov 5', 'Nov 6', 'Nov 7']);
  });

  /** Its first week holds only the days the post was there for, and is drawn as one cut short. */
  it('cuts the first week at the day the post was published, and keeps the ways the period offers', async () => {
    const engine = engineWith();

    drawChart(PUBLISHED_IN_OCTOBER, 'everybody', {
      searchParams: '?period=last-90-days&by=week',
    });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(askedOver(engine, 'week', '2026-10-13T18:30:00.000Z', NEXT_HOUR)).toBe(true);
    // The ninety days offer all three, and the twenty-five the post was there for would not: weeks
    // and months would each be offered only by moving to a longer period.
    expect(options(await ways())).toStrictEqual(['By day', 'By week', 'By month']);
    expect(
      screen.getByText(
        'Weeks run Monday to Sunday in Kolkata. It begins on October 14, 2026, the day the post was published. Weeks the period cuts short are drawn fainter.',
      ),
    ).toBeInTheDocument();

    await unfold();

    expect(rowheaders()).toStrictEqual([
      'Oct 14 – 18',
      'Oct 19 – 25',
      'Oct 26 – Nov 1',
      'Nov 2 – 7',
    ]);
  });

  it.each(['everybody', 'people'] as const)(
    'says a period that ends before the post was published, and asks nothing about it (%s)',
    async (population) => {
      const engine = engineWith();

      drawChart(PUBLISHED_TUESDAY, population, { searchParams: '?period=last-month' });

      expect(
        await screen.findByText('This post was published on November 3, 2026, after this period.'),
      ).toBeInTheDocument();
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('last-month');
      expect(screen.queryByRole('img')).not.toBeInTheDocument();
      expect(screen.queryByText('Show these figures as a table')).not.toBeInTheDocument();
      expect(screen.queryByText(/Nobody read this post/)).not.toBeInTheDocument();
      expect(seriesAsked(engine)).toStrictEqual([]);
    },
  );

  /** The period still holds more days than the picture, and a press narrows the period. */
  it('narrows the period from a picture of the one day since the post was published', async () => {
    engineWith();

    drawChart({
      ...RECORD,
      post: { ...RECORD.post, published: '2026-11-07' },
      from: '2026-11-07',
      months: [month('2026-11', 12, 1)],
    });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });
    await unfold();
    await userEvent.click(screen.getByRole('button', { name: 'Nov 7, look at this day' }));

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('today'),
    );
  });

  /** The date a post gives never keeps a view of it off the picture. */
  it('draws every day of the period for a post read before the day it gives', async () => {
    const engine = engineWith();

    drawChart({ ...PUBLISHED_TUESDAY, days: [{ day: '2026-11-01', pageViews: 4 }] }, 'everybody', {
      searchParams: '?period=last-30-days',
    });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(askedOver(engine, 'day', LAST_30_DAYS_FROM, NEXT_HOUR)).toBe(true);
    expect(screen.queryByText(/It begins on/)).not.toBeInTheDocument();
  });

  /** Any day before such a post's record begins may have held its views. */
  it.each([
    [
      'says no date',
      {
        ...PUBLISHED_TUESDAY,
        post: { ...PUBLISHED_TUESDAY.post, published: null },
        from: '2026-11-01',
      },
    ],
    [
      'was published before its views were counted',
      {
        ...PUBLISHED_TUESDAY,
        post: { ...PUBLISHED_TUESDAY.post, published: '2024-06-01' },
        viewsFrom: '2026-11',
        from: '2026-11-01',
      },
    ],
  ])('draws every day of the period for a post that %s', async (_, answer) => {
    const engine = engineWith();

    drawChart(answer, 'everybody', { searchParams: '?period=last-30-days' });

    await screen.findByRole('img', { name: EVERYBODY_DRAWN });

    expect(askedOver(engine, 'day', LAST_30_DAYS_FROM, NEXT_HOUR)).toBe(true);
    expect(screen.queryByText(/It begins on/)).not.toBeInTheDocument();
  });
});

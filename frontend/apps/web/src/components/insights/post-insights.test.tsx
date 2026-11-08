import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PostInsights } from '@/components/insights/post-insights';
import type { PostInsights as PostAnswer, Site } from '@/lib/api/schemas';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * The picture of the post's views is drawn on a canvas, which this document does not have, so the
 * surface is stood in for; the picture's own suite reads what it would have been told to draw.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const SHOP: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df99',
  domain: 'shop.example.com',
  displayName: 'The Shop',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

/** Saturday 7 November 2026, half past three in the afternoon in Kolkata. */
const TO = '2026-11-07T09:30:00+00:00';

/** Where judging reaches on any ordinary day. */
const HALF_AN_HOUR_BEHIND = '2026-11-07T09:00:00+00:00';

/** Further behind than a visit waiting its turn explains. */
const TWO_DAYS_BEHIND = '2026-11-05T09:30:00+00:00';

const PATH = '/hello-world/';

/** The address of the screen about the post. */
const ABOUT_THE_POST = { path: PATH };

const A_DAY = 86_400_000;

/** The day so many days after another, on the website's calendar. */
function after(day: string, days: number): string {
  return new Date(Date.parse(`${day}T00:00:00Z`) + days * A_DAY).toISOString().slice(0, 10);
}

/** Days as the engine answers them, one figure to each, from the first on. */
function daysFrom(first: string, figures: readonly (number | null)[]): PostAnswer['days'] {
  return figures.map((pageViews, at) => ({ day: after(first, at), pageViews }));
}

/**
 * Thirteen weeks of views a day, Monday first and the oldest week first, from Monday 10 August to
 * today: a week of one a day, two of two, one of one, two of nought, a lone three, and then four,
 * six with a busy Wednesday, four, four and six, and this week up to its Saturday.
 */
const WEEKS: readonly (readonly number[])[] = [
  [1, 1, 1, 1, 1, 1, 1],
  [2, 2, 2, 2, 2, 2, 2],
  [2, 2, 2, 2, 2, 2, 2],
  [1, 1, 1, 1, 1, 1, 1],
  [0, 0, 0, 0, 0, 0, 0],
  [0, 0, 0, 0, 0, 0, 0],
  [3, 0, 0, 0, 0, 0, 0],
  [4, 4, 4, 4, 4, 4, 4],
  [5, 5, 12, 5, 5, 5, 5],
  [4, 4, 4, 4, 4, 4, 4],
  [4, 4, 4, 4, 4, 4, 4],
  [6, 6, 6, 6, 6, 6, 6],
  [3, 5, 4, 6, 2, 7],
];

const DAYS = daysFrom('2026-08-10', WEEKS.flat());

/** One month as the engine answers it. */
function month(written: string, pageViews: number | null, daysMeasured: number) {
  return { month: written, pageViews, daysMeasured };
}

/** Every month of a post published on 12 March 2025, read most in April that year. */
const MONTHS: PostAnswer['months'] = [
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
 * The post's record. Its views are the figure Insights gave it, a little short of what its months
 * add up to, as months counted from the reports can be.
 */
const RECORD: PostAnswer = {
  post: {
    path: PATH,
    address: 'https://example.com/hello-world/',
    title: 'Hello, world',
    published: '2025-03-12',
    pageViews: 1380,
  },
  viewsFrom: '2025-03',
  firstDay: '2024-11-19',
  from: '2025-03-12',
  to: TO,
  completeTo: TO,
  months: MONTHS,
  days: DAYS,
};

/** The same post, published long before its views were counted. */
const PUBLISHED_EARLIER: PostAnswer = {
  ...RECORD,
  post: { ...RECORD.post, published: '2024-06-01' },
  from: '2025-03-01',
};

/** The same post read among people, whose figures are smaller. */
const PEOPLE: PostAnswer = {
  ...RECORD,
  post: { ...RECORD.post, pageViews: 512 },
  completeTo: HALF_AN_HOUR_BEHIND,
};

/** The same post published on Wednesday 28 October, nobody having read it before it was. */
const PUBLISHED_LAST_WEEK: PostAnswer = {
  ...RECORD,
  post: { ...RECORD.post, published: '2026-10-28' },
  from: '2026-10-28',
  months: [month('2026-10', 24, 4), month('2026-11', 33, 7)],
  days: DAYS.map((day) => ({ ...day, pageViews: day.day < '2026-10-28' ? 0 : day.pageViews })),
};

/** The same post published on Tuesday 3 November, this week, nobody having read it before it was. */
const PUBLISHED_TUESDAY: PostAnswer = {
  ...RECORD,
  post: { ...RECORD.post, published: '2026-11-03', pageViews: 24 },
  from: '2026-11-03',
  months: [month('2026-11', 24, 5)],
  days: DAYS.map((day) => ({ ...day, pageViews: day.day < '2026-11-03' ? 0 : day.pageViews })),
};

/** The same months and days, with nobody reading the post in any of them. */
const NOBODY: PostAnswer = {
  ...RECORD,
  post: { ...RECORD.post, pageViews: 0 },
  months: MONTHS.map((one) => ({ ...one, pageViews: 0 })),
  days: DAYS.map((day) => ({ ...day, pageViews: 0 })),
};

/** What the engine answers for a post its website's sitemap does not list. */
const NOT_FOUND = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  title: 'Not Found',
  status: 404,
  traceId: '00-6b1f2a5b0d5e4c3c8a7f0e1d2c3b4a59-1a2b3c4d5e6f7a8b-00',
};

interface Engine {
  readonly everybody?: PostAnswer;
  readonly people?: PostAnswer;
  /** The websites somebody may look at. */
  readonly sites?: readonly Site[];
  /** The websites whose sitemap lists no such post. */
  readonly unlisted?: readonly string[];
  /** The addresses it refuses. */
  readonly refusing?: readonly string[];
  /** The addresses it never answers. */
  readonly holding?: readonly string[];
}

function engineWith({
  everybody = RECORD,
  people = everybody,
  sites = [SITE],
  unlisted = [],
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

    if (path.includes('/insights/post/series?')) {
      return respondWith(200, viewsOver(path));
    }

    if (path.includes('/insights/post?')) {
      if (unlisted.some((id) => path.includes(id))) {
        return respondWith(404, NOT_FOUND);
      }

      return respondWith(200, path.includes('only=people') ? people : everybody);
    }

    return respondWith(200, sites);
  });
}

/**
 * The post's views over whatever period its picture asks about, as the engine answers them: none in
 * any bucket. The picture has a suite of its own; here it only has to be answered.
 */
function viewsOver(path: string) {
  const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));

  return {
    from: asked.get('from'),
    to: asked.get('to'),
    metric: 'pageviews',
    granularity: asked.get('granularity'),
    completeTo: asked.get('to'),
    points: [],
  };
}

/** The questions put to the engine about the post, read back the way the engine reads them. */
function askedAbout(engine: ReturnType<typeof engineWith>): URLSearchParams[] {
  return engine
    .all()
    .map(({ path }) => path)
    .filter((path) => path.includes('/insights/post?'))
    .map((path) => new URLSearchParams(path.slice(path.indexOf('?') + 1)));
}

/** The questions put to the engine about the post's views over a period, read back the same way. */
function seriesAsked(engine: ReturnType<typeof engineWith>): URLSearchParams[] {
  return engine
    .all()
    .map(({ path }) => path)
    .filter((path) => path.includes('/insights/post/series?'))
    .map((path) => new URLSearchParams(path.slice(path.indexOf('?') + 1)));
}

/** Whether one part of the screen comes after another, in the order it is read. */
function follows(earlier: Node, later: Node): boolean {
  return (earlier.compareDocumentPosition(later) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
}

/**
 * Whether a name says these words, whichever of the many kinds of space the reader's language writes
 * a stretch of days with.
 */
function saying(words: string) {
  return (name: string) => name.replace(/\s+/g, ' ') === words;
}

/** The highlights, once they are on the screen. */
async function highlights(): Promise<HTMLElement> {
  return screen.findByRole('region', { name: 'All-time highlights' });
}

/**
 * Waits for the post's record to land: its tables carry a caption, and their stand-ins do not.
 *
 * Looked for by the caption rather than by the role, because a screen holding two tables of figures
 * is a large document, and asking it for a role over and over while it settles is slow enough to run
 * out of time on a busy machine. What is on the screen is then asked for by its role once.
 */
async function landed(): Promise<void> {
  await waitFor(() => expect(document.querySelector('table caption')).not.toBeNull());
}

const TOTAL_CAPTION = 'Views of this post in each month, newest year first.';

/** The table of months, once the record has arrived. */
async function monthsTable(): Promise<HTMLElement> {
  await landed();

  return screen.getByRole('table', { name: TOTAL_CAPTION });
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

const WEEKS_CAPTION =
  'Views each day, newest week first. Change compares views per day with the week before.';

/** The table of recent weeks, once the record has arrived. */
async function weeksTable(): Promise<HTMLElement> {
  await landed();

  return screen.getByRole('table', { name: WEEKS_CAPTION });
}

/** The cells of one week's row: Monday to Sunday, then its total, its average and how it moved. */
function weekCells(table: HTMLElement, week: string): HTMLElement[] {
  const row = within(table)
    .getByRole('rowheader', { name: saying(week) })
    .closest('tr');

  if (row === null) {
    throw new Error(`No week ${week}.`);
  }

  return within(row).getAllByRole('cell');
}

/** One cell of a week's row, by its place: Monday is the first, and how it moved the last. */
function weekCell(table: HTMLElement, week: string, place: number): HTMLElement {
  const cell = weekCells(table, week)[place - 1];

  if (cell === undefined) {
    throw new Error(`No cell ${place} in ${week}.`);
  }

  return cell;
}

/** Where a week's total, its average and how it moved stand in its row. */
const TOTAL = 8;
const PER_DAY = 9;
const MOVED = 10;

describe('the screen about one post', () => {
  it('names the post, when it was published and where it is read', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await landed();

    expect(screen.getByRole('heading', { name: 'Hello, world', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Published March 12, 2025')).toBeInTheDocument();
    expect(screen.getByText(PATH)).toHaveAttribute('dir', 'ltr');
  });

  it('writes a title in its own direction', async () => {
    engineWith({ everybody: { ...RECORD, post: { ...RECORD.post, title: 'مرحبا بالعالم' } } });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(await screen.findByText('مرحبا بالعالم')).toHaveAttribute('dir', 'auto');
  });

  it('names a post by its address where its page gave no title', async () => {
    engineWith({ everybody: { ...RECORD, post: { ...RECORD.post, title: null } } });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await landed();

    const heading = screen.getByRole('heading', { name: PATH, level: 1 });

    expect(within(heading).getByText(PATH)).toHaveAttribute('dir', 'ltr');
    // The address is the post's name, so it is not said a second time beneath it.
    expect(screen.getAllByText(PATH)).toHaveLength(1);
  });

  it('says a post has no publishing date where its page gave none', async () => {
    engineWith({ everybody: { ...RECORD, post: { ...RECORD.post, published: null } } });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(await screen.findByText('No publishing date')).toBeInTheDocument();
    expect(screen.queryByText(/Published/)).not.toBeInTheDocument();
  });

  it('opens the post itself in a tab of its own, passing nothing on about where it was opened from', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await landed();

    const link = screen.getByRole('link', { name: 'Open the post (opens in a new tab)' });

    expect(link).toHaveAttribute('href', 'https://example.com/hello-world/');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')?.split(' ')).toEqual(
      expect.arrayContaining(['noopener', 'noreferrer']),
    );
  });

  /** An address from somebody's sitemap that would run something on this screen is never a link. */
  it.each(['javascript:alert(document.cookie)', 'data:text/html,<script>alert(1)</script>', PATH])(
    'offers no way to open a post whose address is %j',
    async (address) => {
      engineWith({ everybody: { ...RECORD, post: { ...RECORD.post, address } } });

      renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

      await landed();

      expect(screen.getByRole('heading', { name: 'Hello, world', level: 1 })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /Open the post/ })).not.toBeInTheDocument();
    },
  );

  it.each([
    ['everybody', ABOUT_THE_POST, '/app/insights'],
    ['people', { ...ABOUT_THE_POST, only: 'people' }, '/app/insights?only=people'],
  ])(
    'leads back to Insights, carrying the people the screen is kept to (%s)',
    async (_, at, back) => {
      engineWith({});

      renderScreen(<PostInsights />, { searchParams: at });

      expect(await screen.findByRole('link', { name: 'Back to Insights' })).toHaveAttribute(
        'href',
        back,
      );
    },
  );

  /** A post opened from the list of every post leads back to the same page of the same list. */
  it.each([
    ['the list as it opens', '', 'everybody', '/app/insights/posts'],
    [
      'a later page of a search, newest first',
      'search=number+4&order=newest&page=2',
      'people',
      '/app/insights/posts?search=number+4&order=newest&page=2&only=people',
    ],
    ['a larger page', 'page=3&size=50', 'everybody', '/app/insights/posts?page=3&size=50'],
  ])(
    'leads back to the page of the list it was opened from, counting whoever the screen counts: %s',
    async (_, list, population, back) => {
      engineWith({});

      renderScreen(<PostInsights />, {
        searchParams: {
          ...ABOUT_THE_POST,
          list,
          ...(population === 'people' ? { only: 'people' } : {}),
        },
      });

      expect(await screen.findByRole('link', { name: 'Back to All posts' })).toHaveAttribute(
        'href',
        back,
      );
      expect(screen.queryByRole('link', { name: 'Back to Insights' })).not.toBeInTheDocument();
    },
  );

  /** The address is whatever somebody sent, and only ever leads back to a page of the list. */
  it.each([
    [
      'what the list would never read',
      'search=%20%20number%20%20%204%20&order=oldest&page=0&size=7&elsewhere=1',
      '/app/insights/posts?search=number+4',
    ],
    [
      'another website altogether',
      'https://elsewhere.example/steal?page=3',
      '/app/insights/posts?page=3',
    ],
    ['a script', 'javascript:alert(1)', '/app/insights/posts'],
  ])(
    'leads back only to a page of the list, read by the list’s own rules, from %s',
    async (_, list, back) => {
      engineWith({});

      renderScreen(<PostInsights />, { searchParams: { ...ABOUT_THE_POST, list } });

      expect(await screen.findByRole('link', { name: 'Back to All posts' })).toHaveAttribute(
        'href',
        back,
      );
    },
  );

  it('leads back to the list it was opened from where the post can no longer be found', async () => {
    engineWith({ unlisted: [SITE.id] });

    renderScreen(<PostInsights />, {
      searchParams: { ...ABOUT_THE_POST, list: 'page=2' },
    });

    expect(
      await screen.findByRole('heading', { name: "We can't find this post" }),
    ).toBeInTheDocument();

    const back = screen.getAllByRole('link', { name: 'Back to All posts' });

    expect(back).toHaveLength(2);

    for (const link of back) {
      expect(link).toHaveAttribute('href', '/app/insights/posts?page=2');
    }
  });

  /** A path is written by whoever wrote the website, and the engine reads back what it listed. */
  it('asks about the post the address names, exactly as it is written, on the website chosen', async () => {
    const written = '/caf%C3%A9/?a=b+c&only=robots';
    const engine = engineWith({
      everybody: { ...RECORD, post: { ...RECORD.post, path: written } },
    });

    renderScreen(<PostInsights />, { searchParams: { path: written } });

    await landed();

    const sent = engine.all().find(({ path }) => path.includes('/insights/post?'));

    expect(sent?.path.split('?')[0]).toBe(`/api/sites/${SITE.id}/insights/post`);
    expect(askedAbout(engine).map((asked) => [...asked.entries()])).toEqual([[['path', written]]]);
  });

  /** Under everybody the check and the screen's own answer are one question, asked once. */
  it('asks once under everybody', async () => {
    const engine = engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await within(await highlights()).findByText('1,380');

    expect(askedAbout(engine)).toHaveLength(1);
  });
});

describe('the post’s all-time highlights', () => {
  /** The number somebody pressed on Insights is the number they land on. */
  it('shows the views Insights showed for the post', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const views = await within(await highlights()).findByText('1,380');

    expect(views.parentElement).toHaveTextContent('Views');
  });

  /** 1,389 views over the 606 days measured since it was published, whatever its months are called. */
  it('averages its views over the days measured since it was published', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const average = await within(await highlights()).findByText('2.3');

    expect(average.parentElement).toHaveTextContent('Average per day');
  });

  it('names its best month and how much it was read then', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const best = await within(await highlights()).findByText('April 2025');

    expect(best.parentElement).toHaveTextContent('Best month');
    expect(best.parentElement).toHaveTextContent('300 views');
  });

  it('calls a best month that is still running so far', async () => {
    engineWith({
      everybody: { ...RECORD, months: [...MONTHS.slice(0, -1), month('2026-11', 412, 7)] },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const best = await within(await highlights()).findByText('November 2026');

    expect(best.parentElement).toHaveTextContent('412 views so far');
  });

  it('says since when views are counted, where the post was published before counting began', async () => {
    engineWith({ everybody: PUBLISHED_EARLIER });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(await screen.findByText('Views since March 2025.')).toBeInTheDocument();
  });

  it('says nothing of it where every view since the post was published is counted', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await within(await highlights()).findByText('1,380');

    expect(screen.queryByText(/Views since/)).not.toBeInTheDocument();
  });

  it('names the months its figures leave out', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: MONTHS.map((one) =>
          one.month === '2026-02' ? { ...one, pageViews: null, daysMeasured: 0 } : one,
        ),
      },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(
      await screen.findByText("February 2026 wasn't counted and is left out of these figures."),
    ).toBeInTheDocument();
  });

  /** Nought a day is a figure; a month read most, where no month was read, is not. */
  it('names no best month where nothing was read in a month with a figure', async () => {
    engineWith({
      everybody: {
        ...NOBODY,
        months: NOBODY.months.map((one) =>
          one.month === '2026-02' ? { ...one, pageViews: null, daysMeasured: 0 } : one,
        ),
      },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await monthsTable();

    const place = await highlights();

    expect(within(place).getByText('Best month').parentElement).toHaveTextContent(/^Best month—$/);
    expect(within(place).getByText('Average per day').parentElement).toHaveTextContent(
      /^Average per day0$/,
    );
  });
});

describe('the post’s picture on its screen', () => {
  /** What it has come to, then how it was read over the period chosen, then month by month. */
  it('sets the picture between the highlights and the months, with the period in its own card', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await landed();

    const picture = screen.getByRole('heading', { name: 'Views over time', level: 2 });
    const card = picture.closest('section');

    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).getByRole('combobox', { name: 'Period' }),
    ).toBeInTheDocument();
    // The one card on the screen about a period: the others read the post's whole life.
    expect(screen.getAllByRole('combobox', { name: 'Period' })).toHaveLength(1);
    expect(follows(await highlights(), picture)).toBe(true);
    expect(follows(picture, screen.getByRole('heading', { name: 'Months and years' }))).toBe(true);
  });

  it.each([
    ['while the post is on its way', ['/insights/post']],
    ['before the website is known', ['/api/sites']],
  ])('holds the picture’s place %s, with no period to choose yet', async (_, holding) => {
    engineWith({ holding });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const picture = await screen.findByRole('heading', { name: 'Views over time', level: 2 });

    expect(screen.queryByRole('combobox', { name: 'Period' })).not.toBeInTheDocument();
    expect(follows(screen.getByRole('heading', { name: 'All-time highlights' }), picture)).toBe(
      true,
    );
    expect(follows(picture, screen.getByRole('heading', { name: 'Months and years' }))).toBe(true);
  });

  /** The period is the picture's alone, so choosing another asks for the picture and nothing else. */
  it('asks only for the picture again when another period is chosen, and leaves every other card as it was', async () => {
    const engine = engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const months = await monthsTable();
    const before = {
      highlights: (await highlights()).textContent,
      months: months.textContent,
      weeks: screen.getByRole('table', { name: WEEKS_CAPTION }).textContent,
    };

    await waitFor(() => expect(seriesAsked(engine)).not.toHaveLength(0));

    const first = seriesAsked(engine)[0]?.get('from');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Period' }), 'last-30-days');

    await waitFor(() =>
      expect(seriesAsked(engine).some((asked) => asked.get('from') !== first)).toBe(true),
    );
    expect(askedAbout(engine)).toHaveLength(1);
    expect((await highlights()).textContent).toBe(before.highlights);
    expect(screen.getByRole('table', { name: TOTAL_CAPTION }).textContent).toBe(before.months);
    expect(screen.getByRole('table', { name: WEEKS_CAPTION }).textContent).toBe(before.weeks);
  });
});

describe('the post’s months and years', () => {
  it('lays the post’s months out newest year first and totals each year', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((header) => header.textContent),
    ).toEqual(['2026', '2025']);
    expect(monthCell(table, '2025', 4)).toHaveTextContent('300');
    expect(monthCell(table, '2025', 4)).toHaveClass('bg-heat-5');
    expect(monthCell(table, '2025', 5)).toHaveTextContent(/^0$/);
    expect(monthCell(table, '2026', 11)).toHaveTextContent('33 so far');
    expect(yearTotal(table, '2026')).toBe('501');
    expect(yearTotal(table, '2025')).toBe('888');
  });

  it('switches the same table to an average per day', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    const averaged = await screen.findByRole('table', {
      name: 'Views per day in each month, counting only the days measured since it was published.',
    });

    expect(averaged).toBe(table);
    // 120 over the twenty days from the day it was published.
    expect(monthCell(averaged, '2025', 3)).toHaveTextContent(/^6$/);
    expect(yearTotal(averaged, '2025')).toBe('3');
  });

  it('calls the months before a dated post was published not yet published', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(monthCell(table, '2025', 1)).toHaveTextContent('Not yet published');
    expect(monthCell(table, '2025', 2)).toHaveTextContent('Not yet published');
    expect(screen.getByText('Before it was published')).toBeInTheDocument();
    expect(screen.queryByText('Before measuring began')).not.toBeInTheDocument();
  });

  it('calls the months before a post published before its views were counted not measured', async () => {
    engineWith({ everybody: PUBLISHED_EARLIER });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(monthCell(table, '2025', 2)).toHaveTextContent('Not measured');
    expect(screen.getByText('Before measuring began')).toBeInTheDocument();
  });

  it('calls the months before an undated post was first read months of no views', async () => {
    engineWith({
      everybody: { ...RECORD, post: { ...RECORD.post, published: null }, viewsFrom: '2025-01' },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(monthCell(table, '2025', 2)).toHaveTextContent('No views yet');
    expect(screen.getByText('Before its first view')).toBeInTheDocument();
    expect(screen.queryByText('Before measuring began')).not.toBeInTheDocument();
  });

  /** Nothing was counted before its views were, so nothing says whether anybody read it then. */
  it('calls the months before an undated post’s views were counted not measured, and those since months of no views', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        post: { ...RECORD.post, published: null },
        viewsFrom: '2026-03',
        months: MONTHS.filter((one) => one.month >= '2026-05'),
      },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(monthCell(table, '2026', 1)).toHaveTextContent('Not measured');
    expect(monthCell(table, '2026', 2)).toHaveTextContent('Not measured');
    expect(monthCell(table, '2026', 3)).toHaveTextContent('No views yet');
    expect(monthCell(table, '2026', 4)).toHaveTextContent('No views yet');
    expect(screen.getByText('Before measuring began')).toBeInTheDocument();
    expect(screen.getByText('Before its first view')).toBeInTheDocument();
  });

  it('captions an undated post’s average without its publishing date', async () => {
    engineWith({ everybody: { ...RECORD, post: { ...RECORD.post, published: null } } });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await monthsTable();
    await userEvent.click(screen.getByRole('radio', { name: 'Average per day' }));

    expect(
      await screen.findByRole('table', {
        name: 'Views per day in each month, counting only the days measured.',
      }),
    ).toBeInTheDocument();
  });

  /** Nought would say nobody read the post that month, where nobody counted it at all. */
  it('says in words that a month never counted for the post has no figure', async () => {
    engineWith({
      everybody: {
        ...RECORD,
        months: MONTHS.map((one) =>
          one.month === '2026-02' ? { ...one, pageViews: null, daysMeasured: 0 } : one,
        ),
      },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await monthsTable();

    expect(monthCell(table, '2026', 2)).toHaveTextContent('Not counted');
    expect(monthCell(table, '2026', 2).className).not.toContain('bg-heat');
    expect(yearTotal(table, '2026')).toBe('473');
  });

  it('lays each year out as a card of twelve tiles for a phone', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await monthsTable();

    const card = screen.getByRole('heading', { name: '2025', level: 3 }).closest('li');

    expect(card).not.toBeNull();

    const tiles = within(card as HTMLElement).getAllByRole('listitem');

    expect(tiles).toHaveLength(12);
    expect(tiles[0]).toHaveTextContent('Jan–Not yet published');
    expect(tiles[3]).toHaveTextContent('Apr300');
    expect(card).toHaveTextContent('Total 888');
  });
});

describe('the post’s recent weeks', () => {
  it('lays the recent weeks out newest first, a day to a column from Monday to Sunday', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();
    const weeks = within(table)
      .getAllByRole('rowheader')
      .map((header) => header.textContent?.replace(/\s+/g, ' '));

    expect(weeks).toHaveLength(13);
    expect(weeks[0]).toBe('Nov 2 – 8');
    expect(weeks[1]).toBe('Oct 26 – Nov 1');
    expect(weeks.at(-1)).toBe('Aug 10 – 16');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((column) => column.textContent),
    ).toEqual([
      'Week',
      'Mon',
      'Tue',
      'Wed',
      'Thu',
      'Fri',
      'Sat',
      'Sun',
      'Total',
      'Average per day',
      'Change',
    ]);
    expect(
      weekCells(table, 'Nov 2 – 8')
        .map((cell) => cell.textContent)
        .slice(0, 5),
    ).toEqual(['3', '5', '4', '6', '2']);
  });

  /** Today is still running, so it counts towards the week and not towards a day's average. */
  it('totals each week and averages it over the days that have finished', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(weekCell(table, 'Nov 2 – 8', TOTAL)).toHaveTextContent(/^27$/);
    expect(weekCell(table, 'Nov 2 – 8', PER_DAY)).toHaveTextContent(/^4$/);
    expect(weekCell(table, 'Oct 26 – Nov 1', TOTAL)).toHaveTextContent(/^42$/);
    expect(weekCell(table, 'Oct 26 – Nov 1', PER_DAY)).toHaveTextContent(/^6$/);
  });

  it('says how each week’s average moved against the week before, in words as well as colour', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();
    const moved = (week: string) => weekCell(table, week, MOVED);

    expect(moved('Nov 2 – 8')).toHaveTextContent('Down 33%');
    expect(within(moved('Nov 2 – 8')).getByText('Down 33%').parentElement).toHaveClass(
      'text-danger',
    );
    expect(moved('Oct 26 – Nov 1')).toHaveTextContent('Up 50%');
    expect(within(moved('Oct 26 – Nov 1')).getByText('Up 50%').parentElement).toHaveClass(
      'text-positive',
    );
    expect(moved('Oct 19 – 25')).toHaveTextContent('Level');
    expect(moved('Sep 21 – 27')).toHaveTextContent('Up from none');
  });

  /** Nought set against nought is no change at all, rather than nothing to compare with. */
  it('calls a week of nought after a week of nought level', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(weekCell(table, 'Sep 14 – 20', MOVED)).toHaveTextContent('Level');
    expect(weekCell(table, 'Sep 7 – 13', MOVED)).toHaveTextContent('Down 100%');
  });

  it('sets nothing against the oldest week', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const oldest = weekCell(await weeksTable(), 'Aug 10 – 16', MOVED);

    expect(oldest).toHaveTextContent('Nothing to compare with');
    expect(oldest).not.toHaveTextContent('%');
  });

  /** A post published last week is not shown a quarter of a year of noughts. */
  it('leaves out the weeks before the post’s first day', async () => {
    engineWith({ everybody: PUBLISHED_LAST_WEEK });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((header) => header.textContent?.replace(/\s+/g, ' ')),
    ).toEqual(['Nov 2 – 8', 'Oct 26 – Nov 1']);
    // The days of the week it was published in before it was say so, rather than nought.
    expect(weekCell(table, 'Oct 26 – Nov 1', 1)).toHaveTextContent(/^–Not yet published$/);
  });

  /** Thirty views over the five days from Wednesday 28 October, rather than over all seven. */
  it('averages the week the post was published in over the days since it was published', async () => {
    engineWith({ everybody: PUBLISHED_LAST_WEEK });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(weekCell(table, 'Oct 26 – Nov 1', PER_DAY)).toHaveTextContent(/^6$/);
    // Four a day over this week's five finished days, set against six.
    expect(weekCell(table, 'Nov 2 – 8', MOVED)).toHaveTextContent('Down 33%');
  });

  /** A day before a post was published is not a day nobody read it, so no nought stands for it. */
  it('says a day of this week before the post was published is that, in the table and on a phone', async () => {
    engineWith({ everybody: PUBLISHED_TUESDAY });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();
    const monday = weekCell(table, 'Nov 2 – 8', 1);

    expect(within(table).getAllByRole('rowheader')).toHaveLength(1);
    expect(monday).toHaveTextContent(/^–Not yet published$/);
    expect(within(monday).getByText('Not yet published')).toHaveClass('sr-only');
    expect(monday.className).not.toContain('bg-heat');
    expect(weekCell(table, 'Nov 2 – 8', 2)).toHaveTextContent(/^5$/);
    expect(weekCell(table, 'Nov 2 – 8', TOTAL)).toHaveTextContent(/^24$/);

    const week = screen.getByRole('heading', { name: saying('Nov 2 – 8'), level: 3 }).closest('li');
    const card = screen.getByRole('heading', { name: 'Recent weeks' }).closest('section');

    expect(week).not.toBeNull();
    expect(card).not.toBeNull();

    const [tile] = within(week as HTMLElement).getAllByRole('listitem');

    expect(tile).toHaveTextContent('Monday, November 2, 2026');
    expect(tile).toHaveTextContent(/–Not yet published$/);
    expect(tile?.className).not.toContain('bg-heat');
    expect(within(card as HTMLElement).getByText('Before it was published')).toBeInTheDocument();
    expect(
      within(card as HTMLElement).queryByText('Before measuring began'),
    ).not.toBeInTheDocument();
  });

  /** Every one of its recent days came after it was published, so there is no such day to name. */
  it('says nothing of days before publishing where the post began before the weeks did', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await weeksTable();

    const card = screen.getByRole('heading', { name: 'Recent weeks' }).closest('section');

    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).queryByText('Before it was published'),
    ).not.toBeInTheDocument();
    expect(within(card as HTMLElement).queryByText('Not yet published')).not.toBeInTheDocument();
  });

  it('marks today as so far and leaves the days after it blank', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();
    const today = weekCell(table, 'Nov 2 – 8', 6);
    const tomorrow = weekCell(table, 'Nov 2 – 8', 7);

    expect(today).toHaveTextContent('7 so far');
    expect(today).toHaveClass('outline-dashed');
    expect(tomorrow).toHaveTextContent('Not yet');
    expect(tomorrow.className).not.toContain('bg-heat');
    expect(screen.getByText('Today so far')).toBeInTheDocument();
  });

  /** Nought on a day nobody read the post is a different fact from a day nothing was measured on. */
  it('says in words that a day before measuring began has no figure', async () => {
    const measured = DAYS.map((day) => ({
      ...day,
      pageViews: day.day < '2026-10-29' ? null : day.pageViews,
    }));

    engineWith({
      everybody: { ...RECORD, firstDay: '2026-10-29', from: '2026-10-29', days: measured },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(within(table).getAllByRole('rowheader')).toHaveLength(2);
    expect(weekCell(table, 'Oct 26 – Nov 1', 1)).toHaveTextContent('Not measured');
    expect(weekCell(table, 'Oct 26 – Nov 1', 1).className).not.toContain('bg-heat');
    expect(weekCell(table, 'Oct 26 – Nov 1', 4)).toHaveTextContent(/^6$/);
    expect(screen.getAllByText('Before measuring began')).not.toHaveLength(0);
  });

  it('shades the busiest day of the weeks most strongly', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(weekCell(table, 'Oct 5 – 11', 3)).toHaveTextContent(/^12$/);
    expect(weekCell(table, 'Oct 5 – 11', 3)).toHaveClass('bg-heat-5');
    expect(weekCell(table, 'Oct 5 – 11', 1)).not.toHaveClass('bg-heat-5');
    expect(weekCell(table, 'Sep 7 – 13', 1)).toHaveClass('bg-heat-0');
  });

  it('writes the year beside each week where the weeks run across a new year', async () => {
    const newYear = '2027-01-06T09:30:00+00:00';

    engineWith({
      everybody: {
        ...RECORD,
        to: newYear,
        completeTo: newYear,
        from: '2026-12-21',
        // Two whole weeks from Monday 21 December, and the new year's first up to its Wednesday.
        days: daysFrom('2026-12-21', [4, 4, 4, 4, 4, 4, 4, 5, 5, 12, 5, 5, 5, 5, 2, 1, 3]),
      },
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const table = await weeksTable();

    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((header) => header.textContent?.replace(/\s+/g, ' ')),
    ).toEqual(['Jan 4 – 10, 2027', 'Dec 28, 2026 – Jan 3, 2027', 'Dec 21 – 27, 2026']);
  });

  /** A table scrolled sideways on a phone shows three days and hides the rest. */
  it('lays each week out as a card of seven tiles for a phone', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await weeksTable();

    const card = screen.getByRole('heading', { name: saying('Nov 2 – 8'), level: 3 }).closest('li');

    expect(card).not.toBeNull();

    const tiles = within(card as HTMLElement).getAllByRole('listitem');

    expect(tiles).toHaveLength(7);
    expect(tiles[0]).toHaveTextContent('Monday, November 2, 2026');
    expect(tiles[0]).toHaveTextContent(/3$/);
    expect(tiles[5]).toHaveTextContent('7 so far');
    expect(tiles[6]).toHaveTextContent('Not yet');
    expect(card).toHaveTextContent('Total 27');
    expect(card).toHaveTextContent('4 a day');
    expect(card).toHaveTextContent('Down 33% on the week before');
  });

  /** Every day already says in words what it is, and these words are about marks it cannot reach. */
  it('keeps the words of the legend from a screen reader', async () => {
    engineWith({});

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await weeksTable();

    for (const words of ['Today so far', 'This month so far', 'Before it was published']) {
      expect(screen.getByText(words).closest('[aria-hidden="true"]')).not.toBeNull();
    }
  });
});

describe('the screen about one post before it can be shown', () => {
  it('holds every card’s place, in its own words, while the post is on its way', async () => {
    engineWith({ holding: ['/insights/post'] });

    const { container } = renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const place = await highlights();

    expect(within(place).getByText('Views')).toBeInTheDocument();
    expect(within(place).getByText('Average per day')).toBeInTheDocument();
    expect(within(place).getByText('Best month')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Post insights', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Months and years' })).toBeInTheDocument();
    expect(screen.getAllByText(TOTAL_CAPTION)).not.toHaveLength(0);
    expect(screen.getByRole('heading', { name: 'Recent weeks' })).toBeInTheDocument();
    expect(screen.getAllByText(WEEKS_CAPTION)).not.toHaveLength(0);
    await waitFor(() => expect(container.querySelector('.animate-pulse')).not.toBeNull());
    // A figure on its way is nothing to read, so nothing about it reaches a screen reader as a table.
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  /**
   * A box too narrow for its table is a stop for the keyboard, and the stand-ins' go when the figures
   * land, taking the keyboard with them. What is asserted is that none could ever hold it.
   */
  it('keeps the keyboard out of the months and the weeks while they are on their way', async () => {
    engineWith({ holding: ['/insights/post'] });

    const { container } = renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await screen.findByRole('heading', { name: 'Recent weeks' });
    await waitFor(() => expect(container.querySelectorAll('.overflow-x-auto')).toHaveLength(2));

    for (const box of container.querySelectorAll('.overflow-x-auto')) {
      expect(box.closest('[inert]')).not.toBeNull();
    }
  });

  it('draws the same screen before the website is known', async () => {
    engineWith({ holding: ['/api/sites'] });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(
      await screen.findByRole('heading', { name: 'Post insights', level: 1 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /People only/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'All-time highlights' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Months and years' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Recent weeks' })).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  /** The switch is already there, so what it chooses has to be what the figures arrive under. */
  it('keeps the keyboard on how the months are read when the post lands', async () => {
    let release: () => void = () => {};
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });

    engineDoing(async (path) => {
      if (path.includes('/insights/post?')) {
        await released;

        return respondWith(200, RECORD);
      }

      return respondWith(200, [SITE]);
    });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    const held = await screen.findByRole('radio', { name: 'Average per day' });

    await userEvent.click(held);

    expect(held).toHaveFocus();

    act(() => release());

    await landed();

    expect(
      screen.getByRole('table', {
        name: 'Views per day in each month, counting only the days measured since it was published.',
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Average per day' })).toBe(held);
    expect(held).toHaveFocus();
  });

  it.each([
    ['names no post at all', undefined],
    ['names something no sitemap lists a post under', { path: 'hello-world' }],
  ])('says the post cannot be found where the address %s, and asks nothing', async (_, at) => {
    const engine = engineWith({});

    renderScreen(<PostInsights />, { searchParams: at });

    expect(
      await screen.findByRole('heading', { name: "We can't find this post" }),
    ).toBeInTheDocument();
    expect(screen.getByText("This link doesn't say which post to show.")).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Post insights', level: 1 })).toBeInTheDocument();
    expect(askedAbout(engine)).toEqual([]);
  });

  /** Whatever the address says is whatever somebody sent, and only a post the engine found is named. */
  it('says the post cannot be found where the sitemap does not list it, without repeating the address', async () => {
    engineWith({ unlisted: [SITE.id] });

    const { container } = renderScreen(<PostInsights />, {
      searchParams: { path: '/no-such-post/' },
    });

    expect(
      await screen.findByRole('heading', { name: "We can't find this post" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "example.com's sitemap doesn't list this post. It may have been taken off, or it may be on another website.",
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Back to Insights' })).toHaveLength(2);
    expect(container).not.toHaveTextContent('no-such-post');
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  it('says the post cannot be found once another website is chosen', async () => {
    engineWith({ sites: [SITE, SHOP], unlisted: [SHOP.id] });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    await within(await highlights()).findByText('1,380');

    window.localStorage.setItem('dewiride.chosen-site', SHOP.id);
    act(() => {
      window.dispatchEvent(new StorageEvent('storage', { key: 'dewiride.chosen-site' }));
    });

    expect(
      await screen.findByText(
        "shop.example.com's sitemap doesn't list this post. It may have been taken off, or it may be on another website.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('1,380')).not.toBeInTheDocument();
  });

  it('says what went wrong when the engine refuses', async () => {
    engineWith({ refusing: ['/insights/post'] });

    const { container } = renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Post insights', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    // Nothing goes on standing in for an answer that is not coming.
    expect(container.querySelector('.animate-pulse')).toBeNull();
  });

  it('puts one card in place of the figures where nobody has read the post', async () => {
    engineWith({ everybody: NOBODY });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(
      await screen.findByRole('heading', { name: 'Nobody has read this post yet' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Its views appear here, day by day and month by month, as soon as somebody reads it.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Hello, world', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  /** Views before its views were counted may have been many, so "yet" would say what is not known. */
  it('says nobody has read the post since its views were counted, where it was published before then', async () => {
    engineWith({ everybody: { ...NOBODY, post: { ...NOBODY.post, published: '2024-06-01' } } });

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST });

    expect(
      await screen.findByRole('heading', { name: 'Nobody has read this post since March 2025' }),
    ).toBeInTheDocument();
  });
});

describe('the screen about one post kept to people', () => {
  const PEOPLE_ONLY = { ...ABOUT_THE_POST, only: 'people' };

  it("asks for people's views and says so once", async () => {
    const engine = engineWith({ people: PEOPLE });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    const views = await within(await highlights()).findByText('512');

    expect(views.parentElement).toHaveTextContent('Views by people');
    expect(
      screen.getAllByText('Every figure counts only the visits judged to be people.'),
    ).toHaveLength(1);
    expect(askedAbout(engine).some((asked) => asked.get('only') === 'people')).toBe(true);
    expect(screen.getByRole('button', { name: /People only/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  /** Everybody's record is only what tells a post nobody read from one no person did. */
  it('asks nothing about everybody while the people’s record has views to show', async () => {
    const engine = engineWith({ people: PEOPLE });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    await within(await highlights()).findByText('512');

    expect(askedAbout(engine).map((asked) => asked.get('only'))).toEqual(['people']);
  });

  it('says every figure counts only people above their stand-ins too', async () => {
    engineWith({ holding: ['only=people'] });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    expect(
      await screen.findByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
    expect(within(await highlights()).getByText('Views by people')).toBeInTheDocument();
  });

  it('says once that the newest are still being judged where judging is two days behind', async () => {
    engineWith({ people: { ...PEOPLE, completeTo: TWO_DAYS_BEHIND } });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    expect(
      await screen.findByText(
        'Every figure counts only the visits judged to be people. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
  });

  it('offers everyone back where none of the post’s readers was a person', async () => {
    const engine = engineWith({ people: NOBODY });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    expect(await screen.findByRole('heading', { name: 'No people yet' })).toBeInTheDocument();
    expect(
      screen.getByText("None of this post's readers so far has been judged to be a person."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Every figure counts only/)).not.toBeInTheDocument();
    expect(askedAbout(engine).map((asked) => asked.get('only'))).toEqual(['people', null]);

    await userEvent.click(screen.getByRole('button', { name: 'Show everyone' }));

    expect(await within(await highlights()).findByText('1,380')).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  /** Nought is then only what was counted, and the table says which months were not. */
  it('does not call the people absent while months are left out of their figures', async () => {
    const engine = engineWith({
      people: {
        ...NOBODY,
        months: NOBODY.months.map((one) =>
          one.month === '2026-02' ? { ...one, pageViews: null, daysMeasured: 0 } : one,
        ),
      },
    });

    renderScreen(<PostInsights />, { searchParams: PEOPLE_ONLY });

    const table = await monthsTable();

    expect(monthCell(table, '2026', 2)).toHaveTextContent('Not counted');
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
    expect(askedAbout(engine).map((asked) => asked.get('only'))).toEqual(['people']);
  });

  /**
   * Written into an address that names the post, so that the link in the bar says what is on the
   * screen, and with no entry in the history, since nothing anybody pressed put it there.
   */
  it('writes the people chosen last into its address once its website is known', async () => {
    const watching = vi.fn();

    engineWith({ people: PEOPLE });
    window.localStorage.setItem('dewiride.population', 'people');

    renderScreen(<PostInsights />, { searchParams: ABOUT_THE_POST, watchingAddress: watching });

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].searchParams.get('only')).toBe('people');
    expect(watching.mock.calls[0]?.[0].searchParams.get('path')).toBe(PATH);
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });
});

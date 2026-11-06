import type { QueryClient } from '@tanstack/react-query';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Dashboard } from '@/components/dashboard/dashboard';
import { spanInstants, todayIn } from '@/lib/analytics/period';
import { allTimeKey, overviewKey, sitesKey, trafficKey } from '@/lib/queries/keys';
import { drawn } from '@/test/drawing';
import { engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * The drawing itself needs a canvas, which this document does not have. Everything around it —
 * the legend, the caption, and the table the same figures are published in — is ordinary markup
 * and is exercised for real. The drawing is checked by looking at it in a browser; what a press
 * on it would do is kept where a test can press it.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.localStorage.clear();
  drawn.pick = undefined;
  drawn.picks.clear();
  drawn.options.clear();
});

const SITE = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const SECOND_SITE = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df99',
  domain: 'shop.example.com',
  displayName: 'The Shop',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';

function totals(pageViews: number, visitors: number, events: number) {
  return { from: FROM, to: TO, pageViews, visitors, events };
}

function series(metric: string, values: readonly number[], completeTo = TO) {
  return {
    from: FROM,
    to: TO,
    metric,
    granularity: 'day',
    completeTo,
    points: values.map((value, day) => ({
      bucketStart: `2026-08-${String(11 + day).padStart(2, '0')}T00:00:00+00:00`,
      value,
    })),
  };
}

const VIEWS = [40, 55, 30, 70, 65, 90, 84];
const VISITORS = [12, 18, 9, 21, 20, 27, 25];

/** The days the figures above cover, as the engine writes them. */
const BUCKETS = VIEWS.map(
  (_, day) => `2026-08-${String(11 + day).padStart(2, '0')}T00:00:00+00:00`,
);

/** A week whose visits have not been judged yet, which is where a new website starts. */
const NOTHING_JUDGED = {
  from: FROM,
  to: TO,
  granularity: 'day',
  completeTo: TO,
  buckets: [],
  groups: [],
};

/**
 * The same week, judged.
 *
 * A category on either side of the colour that means machinery, so that a band standing for both
 * of them can be told apart from a label that names either.
 */
const JUDGED = {
  from: FROM,
  to: TO,
  granularity: 'day',
  completeTo: TO,
  buckets: BUCKETS,
  groups: [
    {
      category: 'likely-human',
      sessions: [3, 5, 2, 6, 4, 8, 7],
      pageViews: [9, 14, 5, 18, 11, 24, 21],
    },
    {
      category: 'known-ai-crawler',
      sessions: [1, 0, 2, 1, 3, 1, 0],
      pageViews: [1, 0, 4, 1, 3, 1, 0],
    },
  ],
};

/** The same week judged, with its newest day still being judged. */
const STILL_JUDGING = { ...JUDGED, completeTo: BUCKETS[6] ?? TO };

/** A week none of whose visits has been judged yet, with its newest day still being judged. */
const STILL_COMING = { ...NOTHING_JUDGED, buckets: BUCKETS, completeTo: BUCKETS[6] ?? TO };

/** The same week before judging has reached any of it, which is where an engine stopped leaves it. */
const NOT_YET_REACHED = { ...NOTHING_JUDGED, buckets: BUCKETS, completeTo: FROM };

/** The same week judged six hours into its first day and no further, so not one day has settled. */
const STOPPED_IN_THE_FIRST_DAY = {
  ...NOTHING_JUDGED,
  buckets: BUCKETS,
  completeTo: '2026-08-11T06:00:00+00:00',
};

/** A week judged to the end in which nothing at all was judged, its traffic all begun before it. */
const SETTLED_EMPTY = { ...NOTHING_JUDGED, buckets: BUCKETS, completeTo: TO };

/** How recently a window has to end to be the period on screen rather than the one before it. */
const RECENTLY = 86_400_000;

/**
 * Whether a question is about the period being looked at rather than the one it is measured
 * against.
 *
 * Told apart by where the window ends. The period on screen runs up to about now; the one it is
 * compared with ended when that one began, which is at least a period ago.
 */
function current(path: string): boolean {
  const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
  const ends = Date.parse(asked.get('to') ?? '');

  return !Number.isNaN(ends) && ends > Date.now() - RECENTLY;
}

/** What a bucketed answer says of the whole period, as the breakdown reports it. */
interface JudgedWeek {
  readonly groups: readonly {
    readonly category: string;
    readonly sessions: readonly number[];
    readonly pageViews: readonly number[];
  }[];
}

/** The breakdown of a week, derived from the same judged visits the picture is drawn from. */
function breakdownOf(judged: JudgedWeek) {
  const groups = judged.groups.map((group) => ({
    category: group.category,
    strength: 'moderate',
    sessions: group.sessions.reduce((total, one) => total + one, 0),
    pageViews: group.pageViews.reduce((total, one) => total + one, 0),
  }));

  return {
    from: FROM,
    to: TO,
    sessions: groups.reduce((total, group) => total + group.sessions, 0),
    pageViews: groups.reduce((total, group) => total + group.pageViews, 0),
    groups,
  };
}

/**
 * Honest empty answers to every list on the screen, by the address each is asked at, so that a
 * state which replaces the lists can be told from a screen of failure notices.
 */
const NOTHING_LISTED: Readonly<Record<string, Readonly<Record<string, unknown>>>> = {
  '/engagement/pages': {
    ranking: 'attention',
    totalPages: 0,
    longestMedianEngagedMs: 0,
    pages: [],
  },
  '/engagement': {
    readings: 0,
    measured: 0,
    medianEngagedMs: 0,
    interacted: 0,
    depths: { top: 0, quarter: 0, half: 0, whole: 0 },
  },
  '/pages': { pageViews: 0, totalPaths: 0, mostPageViews: 0, pages: [] },
  '/locations': { grouping: 'country', visitors: 0, totalPlaces: 0, mostVisitors: 0, places: [] },
  '/sources': { grouping: 'site', visitors: 0, totalSources: 0, mostVisitors: 0, sources: [] },
  '/devices': { visitors: 0, devices: [] },
  '/software': { grouping: 'browser', visitors: 0, totalNames: 0, mostVisitors: 0, names: [] },
  '/actions': { grouping: 'control', presses: 0, totalControls: 0, mostPresses: 0, controls: [] },
};

/**
 * Answers every question the screen asks, in whichever order they arrive.
 *
 * The breakdown is derived from the judged picture rather than answered on its own, so "watched"
 * and "nothing judged" mean one thing for both questions. A question about people is answered
 * with the people's totals and, for the picture of how much, with a last bucket still filling.
 */
function engineWith(
  sites: unknown,
  overview: unknown,
  judged: JudgedWeek = NOTHING_JUDGED,
  earlier: unknown = overview,
  people: unknown = overview,
  refused: readonly string[] = [],
) {
  return engineDoing(async (path) => {
    if (refused.some((address) => path.includes(address))) {
      return respondWith(500, { status: 500, title: 'The engine is having trouble.' });
    }

    // Named first: every address all time is asked at begins with one asked below as well.
    if (path.includes('/all-time')) {
      return respondWith(200, measuredToday(path));
    }

    if (path.includes('/server-keys')) {
      return respondWith(200, []);
    }

    if (path.includes('/settings')) {
      return respondWith(200, {
        displayName: 'Example',
        timeZoneId: 'Etc/UTC',
        captureClicks: true,
      });
    }

    // Named before the breakdown, whose address it begins with.
    if (path.includes('/traffic/series')) {
      return respondWith(200, judged);
    }

    if (path.includes('/traffic')) {
      return respondWith(200, breakdownOf(judged));
    }

    // Named before the visit list, which its address begins with.
    if (path.includes('/visits/totals')) {
      return respondWith(200, { from: FROM, to: TO, visits: 0, singlePageVisits: 0, pageViews: 0 });
    }

    if (path.includes('/visits/pages')) {
      return respondWith(200, {
        from: FROM,
        to: TO,
        position: 'entry',
        totalVisits: 0,
        totalPaths: 0,
        mostVisits: 0,
        pages: [],
      });
    }

    if (path.includes('/visits')) {
      return respondWith(200, { from: FROM, to: TO, visits: [] });
    }

    if (path.includes('/series')) {
      const completeTo = path.includes('only=people') ? (BUCKETS[6] ?? TO) : TO;

      return respondWith(
        200,
        path.includes('metric=visitors')
          ? series('visitors', VISITORS, completeTo)
          : series('pageviews', VIEWS, completeTo),
      );
    }

    if (path.includes('/overview')) {
      if (path.includes('only=people')) {
        return respondWith(200, people);
      }

      return respondWith(200, current(path) ? overview : earlier);
    }

    const listed = Object.keys(NOTHING_LISTED).find((address) => path.includes(address));

    if (listed !== undefined) {
      return respondWith(200, { from: FROM, to: TO, ...NOTHING_LISTED[listed] });
    }

    return respondWith(200, sites);
  });
}

/**
 * All time for a website first measured today, which is what every test says when it says nothing
 * about all time: one month, the one under way, and no years to offer from any period.
 */
function measuredToday(path: string) {
  const now = new Date().toISOString();
  const firstDay = todayIn(SITE.timeZoneId, new Date());
  const opens = spanInstants({ first: firstDay, last: firstDay }, SITE.timeZoneId).first;
  const month = new Date(Date.parse(`${firstDay.slice(0, 7)}-01T00:00:00Z`) - KOLKATA_AHEAD);

  if (path.includes('/all-time/traffic/series')) {
    return {
      from: month.toISOString(),
      to: now,
      granularity: 'month',
      completeTo: now,
      buckets: [month.toISOString()],
      groups: [],
      missing: [],
      unjudged: [],
    };
  }

  if (path.includes('/all-time/series')) {
    return {
      from: month.toISOString(),
      to: now,
      metric: partOf(path, 'metric'),
      granularity: 'month',
      completeTo: now,
      points: [{ bucketStart: month.toISOString(), value: 0 }],
      missing: [],
      unjudged: [],
    };
  }

  return {
    from: opens.toISOString(),
    to: now,
    firstDay,
    detailFrom: opens.toISOString(),
    pageViews: 464,
    visitors: 132,
    completeTo: now,
    missing: [],
    unjudged: [],
  };
}

/** How far ahead of UTC the website's clock in Kolkata always is. */
const KOLKATA_AHEAD = 5.5 * 3_600_000;

function busy() {
  return engineWith([SITE], totals(464, 132, 900));
}

/** The same website, with a week of its visits already judged. */
function watched() {
  return engineWith([SITE], totals(464, 132, 900), JUDGED);
}

/**
 * The website, answering every question at once except the figures the period is measured
 * against, which wait until the test lets them go.
 *
 * @param earlier The figures of the period before, once they are let go.
 */
function periodBeforeHeld(earlier: unknown) {
  engineWith([SITE], totals(464, 132, 900), NOTHING_JUDGED, earlier);

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;
  let release: () => void = () => {};
  let answered: () => void = () => {};
  const released = new Promise<void>((resolve) => {
    release = resolve;
  });
  const answeredNow = new Promise<void>((resolve) => {
    answered = resolve;
  });

  engineDoing(async (path, init) => {
    if (!path.includes('/overview')) {
      return answering(path, init);
    }

    if (current(path)) {
      const response = await answering(path, init);

      answered();

      return response;
    }

    await released;

    return answering(path, init);
  });

  return { answeredNow, release: () => release() };
}

/** What the engine was told about one part of the period, read back off the first question asked. */
function asked(sent: readonly Sent[], part: string): string | null {
  const first = sent.find((one) => one.path.includes('?'));

  return new URLSearchParams(first?.path.slice(first.path.indexOf('?') + 1)).get(part);
}

/** Whether a question at an address was asked about exactly this window. */
function askedFor(sent: readonly Sent[], part: string, from: string, to: string): boolean {
  return sent
    .filter(({ path }) => path.includes(part))
    .map(({ path }) => new URLSearchParams(path.slice(path.indexOf('?') + 1)))
    .some((asking) => asking.get('from') === from && asking.get('to') === to);
}

describe('the dashboard', () => {
  it('names the website and the address it measures', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('heading', { name: 'My Blog' })).toBeInTheDocument();
    expect(screen.getByText('example.com')).toBeInTheDocument();
  });

  it('shows the headline numbers, including the one it works out itself', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByText('464')).toBeInTheDocument();
    expect(screen.getByText('132')).toBeInTheDocument();
    expect(screen.getByText('3.5')).toBeInTheDocument();
  });

  /**
   * A count of daily visitors read as a count of people is worse than no count at all, so the
   * caveat travels with the number rather than sitting behind a hint.
   */
  it('says beside the visitor count exactly what it counts', async () => {
    busy();

    renderScreen(<Dashboard />);

    const count = await screen.findByText('132');

    expect(count.parentElement).toHaveTextContent('Someone who returns tomorrow counts again.');
  });

  it('has nothing to divide by when nobody came, and says so rather than guessing', async () => {
    engineWith([SITE], totals(0, 0, 0));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  /**
   * A stand-in promises a figure on its way. Figures that could not be had are not on their way, so
   * the cards settle on the mark for no figure beneath the notice that says why, and take their
   * numbers once asking again is answered.
   */
  it('settles the cards on no figure when their figures are refused, until asking again is answered', async () => {
    engineWith(
      [SITE],
      totals(464, 132, 900),
      NOTHING_JUDGED,
      totals(464, 132, 900),
      totals(464, 132, 900),
      ['/overview'],
    );

    const { cache } = renderScreen(<Dashboard />);

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();

    const cards = screen.getByRole('heading', { name: 'Page views' }).closest('.grid');

    expect(cards).not.toBeNull();

    const grid = cards as HTMLElement;

    await waitFor(() => expect(within(grid).getAllByText('—')).toHaveLength(3));
    expect(grid.querySelector('.animate-pulse')).toBeNull();
    expect(within(grid).getByRole('heading', { name: 'Daily visitors' })).toBeInTheDocument();
    expect(within(grid).getByRole('heading', { name: 'Pages per visitor' })).toBeInTheDocument();
    expect(
      within(grid).getByText('Someone who returns tomorrow counts again.'),
    ).toBeInTheDocument();

    busy();
    await act(() => cache.invalidateQueries());

    expect(await within(grid).findByText('464')).toBeInTheDocument();
    expect(within(grid).queryByText('—')).not.toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  /**
   * A website with nothing on it yet is almost always a website whose owner has not put the code
   * on their pages. Sending them elsewhere to look for it is how a first evening with a new
   * product ends.
   */
  it('offers the tracking code from the screen a website shows before anybody has been', async () => {
    engineWith([SITE], totals(0, 0, 0));

    renderScreen(<Dashboard />);

    await userEvent.click(await screen.findByRole('button', { name: 'Get your tracking code' }));

    expect(await screen.findByRole('dialog')).toHaveTextContent(SITE.id);
  });

  /**
   * The tracking code counts browsers. Whatever is asked for a page and never renders it is only
   * ever seen by the site's own server, so the way to let that be reported has to be reachable
   * from the same place.
   */
  it('offers the keys a website’s own server reports with', async () => {
    busy();

    renderScreen(<Dashboard />);

    await userEvent.click(await screen.findByRole('button', { name: 'Server keys' }));

    const panel = await screen.findByRole('dialog');

    expect(panel).toHaveTextContent('Crawlers and AI assistants');

    await userEvent.click(screen.getByRole('button', { name: 'Close' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  /**
   * The question this product exists to answer is the one the screen opens on, without anybody
   * having to press for it. How much traffic there was is a press away, never the other way round.
   */
  it('opens on who the traffic was, and publishes those figures as a table', async () => {
    watched();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('img', { name: /Who and what visited/ })).toBeInTheDocument();

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getByRole('columnheader', { name: 'People' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Machinery' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'All visits' })).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(BUCKETS.length + 1);
  });

  it('draws how much was read instead, for anybody who asks for it', async () => {
    watched();

    renderScreen(<Dashboard />);

    await userEvent.click(await screen.findByRole('radio', { name: 'How much' }));

    expect(
      await screen.findByRole('img', { name: /Page views and visitors for/ }),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(VIEWS.length + 1);
  });

  /** A period nothing has been judged in is a state of its own, not an empty drawing. */
  it('says so plainly when nothing in the period has been judged yet', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByText('No visits have been judged in this period yet.'),
    ).toBeInTheDocument();
  });

  /**
   * The whole point of the product is on this screen rather than behind a link. A website whose
   * traffic has not been judged yet says so on the same page as its totals.
   */
  it('carries what the traffic was judged to be, beneath the numbers and the drawing', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
  });

  it('says which place a day is counted in, without printing an identifier', async () => {
    watched();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByText('Visits that have finished, by day in Kolkata.'),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'How much' }));

    expect(
      await screen.findByText('Days run midnight to midnight in Kolkata.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Asia\/Kolkata/)).not.toBeInTheDocument();
  });

  it('opens on the last week, and offers every other period beside it', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('last-7-days');
    expect(screen.getByRole('option', { name: 'Yesterday' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Last month' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Choose dates…' })).toBeInTheDocument();
  });

  /**
   * A single day drawn as one daily column is a graph with one point on it. How finely a period
   * is cut is settled by how long it is rather than by what was asked for, so choosing a short
   * one has to change the question the engine is asked.
   */
  it('asks for a short period an hour at a time', async () => {
    const engine = busy();

    renderScreen(<Dashboard />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Period' }), 'today');

    await waitFor(() =>
      expect(engine.all().some(({ path }) => path.includes('granularity=hour'))).toBe(true),
    );
  });

  /**
   * A link is only worth sending if it opens on what its sender was looking at, and that is
   * settled before the screen has drawn anything.
   */
  it('opens on the period the address names rather than on the usual one', async () => {
    busy();

    renderScreen(<Dashboard />, { searchParams: '?period=yesterday' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
  });

  /**
   * Two days after the stretch the address names, in Kolkata: both of them over, and both still
   * kept. Left on the real clock, the stretch would one day be older than the days kept, and the
   * screen would rightly pull it forward.
   */
  it('asks the engine about the days the address names, in the website’s own clock', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-08-20T09:37:12Z'));

    try {
      const engine = busy();

      renderScreen(<Dashboard />, { searchParams: '?period=2026-08-17..2026-08-18' });

      await waitFor(() => expect(asked(engine.all(), 'from')).toBe('2026-08-16T18:30:00.000Z'));
      expect(asked(engine.all(), 'to')).toBe('2026-08-18T18:30:00.000Z');
    } finally {
      vi.useRealTimers();
    }
  });

  /**
   * An address is typed, edited and forwarded by people, so most of what can arrive in one is not
   * a period at all. Every one of those has to leave somebody on a working screen.
   */
  it('opens on the usual period when the address names something that is not one', async () => {
    busy();

    renderScreen(<Dashboard />, { searchParams: '?period=whenever' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('last-7-days');
  });

  /**
   * An account with a second website used to be an account that could only ever see its first
   * one, which reads as traffic that was never collected rather than as a screen looking
   * elsewhere.
   */
  it('offers no way to change website when there is only the one', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('heading', { name: 'My Blog' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Website' })).not.toBeInTheDocument();
  });

  it('opens on the website last looked at rather than the first on the account', async () => {
    window.localStorage.setItem('dewiride.chosen-site', SECOND_SITE.id);
    engineWith([SITE, SECOND_SITE], totals(464, 132, 900));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('shop.example.com')).toBeInTheDocument();
  });

  it('falls back to the first website when the one last looked at has gone', async () => {
    window.localStorage.setItem('dewiride.chosen-site', '01a013fa-49d6-77be-b65d-20ec86e9df00');
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByText('example.com')).toBeInTheDocument();
  });

  it('says there is nothing to show when the account has no website', async () => {
    engineWith([], totals(0, 0, 0));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('No website yet')).toBeInTheDocument();
  });

  it('reports an engine that cannot be reached rather than an empty page', async () => {
    engineStopped();

    renderScreen(<Dashboard />);

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('the period before', () => {
  /**
   * A number on its own says how much. A number beside the one before it says whether anything is
   * happening, which is what somebody opens a dashboard to find out.
   */
  it('says which way every headline number moved, and against what', async () => {
    engineWith([SITE], totals(464, 132, 900), NOTHING_JUDGED, totals(400, 120, 800));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('16% more')).toBeInTheDocument();
    expect(screen.getByText('10% more')).toBeInTheDocument();
    expect(screen.getByText('5% more')).toBeInTheDocument();
    expect(screen.getAllByText(/than the 7 days before/)).toHaveLength(3);
  });

  /**
   * Four hundred page views after none is neither four thousand per cent nor infinitely many.
   * A percentage taken against nothing would look like a measurement.
   */
  it('shows no percentage where the period before held nothing', async () => {
    engineWith([SITE], totals(464, 132, 900), NOTHING_JUDGED, totals(0, 0, 0));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('464')).toBeInTheDocument();
    expect(screen.getAllByText('Up from none')).toHaveLength(3);
    expect(screen.queryByText(/% more/)).not.toBeInTheDocument();
  });

  /**
   * A number and the one it is measured against are two answers. Drawn as each arrived, every card
   * would grow a line when the second came in and move the picture somebody is using below the
   * cards; held until both are in, each card is drawn once, at its full height.
   */
  it('draws each number only once the one it is measured against is in too', async () => {
    const { answeredNow, release } = periodBeforeHeld(totals(400, 120, 800));

    renderScreen(<Dashboard />);

    await act(async () => {
      await answeredNow;
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(screen.queryByText('464')).not.toBeInTheDocument();

    release();

    expect(await screen.findByText('464')).toBeInTheDocument();
    expect(screen.getByText('16% more')).toBeInTheDocument();
  });

  it('draws it behind the picture for anybody who asks, and publishes its figures too', async () => {
    watched();

    renderScreen(<Dashboard />);

    await userEvent.click(
      await screen.findByRole('button', { name: 'Compare with the period before' }),
    );

    expect(
      await screen.findByRole('columnheader', { name: 'All visits before' }),
    ).toBeInTheDocument();
  });
});

describe('what the overview remembers', () => {
  /**
   * The stretch of days somebody reads their website over is a habit rather than a decision made
   * afresh each morning, so the one chosen last is the one the screen opens on next.
   */
  it('remembers the period once chosen', async () => {
    busy();

    renderScreen(<Dashboard />);

    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Period' }), 'today');

    expect(window.localStorage.getItem('dewiride.period')).toBe('today');
  });

  it('opens on the period last chosen when the address says nothing', async () => {
    window.localStorage.setItem('dewiride.period', 'yesterday');
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
  });

  /** A link names what its sender was looking at, and the reader's own habit gives way to it. */
  it("lets a link's period win over the remembered one", async () => {
    window.localStorage.setItem('dewiride.period', 'yesterday');
    busy();

    renderScreen(<Dashboard />, { searchParams: '?period=today' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('today');
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the period there, so it is written in over the entry somebody is already on.
   */
  it('writes the remembered period into the address without adding to the history', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.period', 'yesterday');
    busy();

    renderScreen(<Dashboard />, { watchingAddress: watching });

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('period=yesterday');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  it('opens on the view last read', async () => {
    window.localStorage.setItem('dewiride.chart-view', 'activity');
    watched();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByRole('img', { name: /Page views and visitors for/ }),
    ).toBeInTheDocument();
  });

  it('draws only people when that was what somebody last asked for', async () => {
    window.localStorage.setItem('dewiride.population', 'people');
    watched();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByRole('img', { name: /Visits judged to be people on/ }),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getByRole('columnheader', { name: 'People' })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'All visits' })).not.toBeInTheDocument();
  });

  it('remembers the people once chosen', async () => {
    watched();

    renderScreen(<Dashboard />);

    await userEvent.click(await screen.findByRole('button', { name: /People only/ }));

    await waitFor(() => expect(window.localStorage.getItem('dewiride.population')).toBe('people'));
  });
});

describe('the whole screen kept to people', () => {
  /** The people's own totals, a share of everybody's. */
  const PEOPLE = totals(120, 40, 250);

  /** Every question about a period's activity that the screen asks, by its address. */
  const ACTIVITY = [
    '/overview?',
    '/series?',
    '/pages?',
    '/locations?',
    '/sources?',
    '/devices?',
    '/actions?',
    '/engagement?',
    '/visits/totals?',
    '/visits/pages?',
  ];

  /** The screen, kept to people, with everything it asks for answered. */
  function keptToPeople(at = '?only=people') {
    const engine = engineWith([SITE], totals(464, 132, 900), JUDGED, totals(464, 132, 900), PEOPLE);

    renderScreen(<Dashboard />, { searchParams: at });

    return engine;
  }

  /**
   * The picture of how much is the cards' own arithmetic over the people's reports, so it is
   * asked of the same reports rather than read off the judged answer.
   */
  it('asks for page views by people rather than judged visits when only people are wanted', async () => {
    const engine = keptToPeople('?show=activity&only=people');

    await screen.findByRole('img', { name: /Page views and visitors among people on/ });

    expect(
      engine
        .all()
        .some(({ path }) => /\/series\?metric=/.test(path) && path.includes('only=people')),
    ).toBe(true);
    expect(engine.all().some(({ path }) => path.includes('/traffic/series'))).toBe(false);
  });

  it('asks every panel about the same people', async () => {
    const engine = keptToPeople('?only=people&show=activity');

    await screen.findAllByText('Page views by people');
    await waitFor(() => {
      for (const address of ACTIVITY) {
        expect(engine.all().some(({ path }) => path.includes(address))).toBe(true);
      }
    });

    // Everybody's own totals are the one question asked without the people, and once.
    const check = engine
      .all()
      .filter(({ path }) => path.includes('/overview?') && !path.includes('only='));

    expect(check).toHaveLength(1);

    for (const address of ACTIVITY) {
      const sent = engine
        .all()
        .filter(({ path }) => path.includes(address) && !check.some((one) => one.path === path));

      expect(sent.every(({ path }) => path.includes('only=people'))).toBe(true);
    }

    const verdicts = engine.all().filter(({ path }) => /\/traffic\?/.test(path));

    expect(verdicts.length).toBeGreaterThan(0);
    expect(verdicts.every(({ path }) => !path.includes('only='))).toBe(true);
  });

  /**
   * Everybody is asked about as well, once, for the period on screen: it is what tells a website
   * nobody has been to from a period in which nobody was judged a person.
   */
  it('still checks everybody, quietly', async () => {
    const engine = keptToPeople();

    await screen.findByText('Page views by people');

    const everybody = engine
      .all()
      .filter(({ path }) => path.includes('/overview?') && !path.includes('only='))
      .filter(({ path }) => current(path));

    expect(everybody).toHaveLength(1);
  });

  it('labels the cards with what they count', async () => {
    keptToPeople();

    expect(await screen.findByText('Page views by people')).toBeInTheDocument();
    expect(screen.getByText('Daily visitors judged to be people')).toBeInTheDocument();
    expect(screen.getByText('Pages per person')).toBeInTheDocument();
    expect(screen.getByText('Someone who returns tomorrow counts again.')).toBeInTheDocument();
  });

  it('says once, above the figures, whose they are', async () => {
    keptToPeople();

    expect(
      await screen.findByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
  });

  it('says nothing about whose the figures are when they are everybody’s', async () => {
    watched();

    renderScreen(<Dashboard />);

    await screen.findByText('Page views');

    expect(
      screen.queryByText('Every figure counts only the visits judged to be people.'),
    ).not.toBeInTheDocument();
  });

  it('washes the newest figures the engine has not finished judging', async () => {
    keptToPeople('?show=activity&only=people');

    expect(
      await screen.findByText(
        'Days run midnight to midnight in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getAllByText('still being judged')).toHaveLength(1);
  });

  /**
   * One state in place of the picture and every list, because eight cards each saying nothing is
   * eight ways of saying one thing.
   */
  it('replaces the picture and the lists with one state when none of the people were counted', async () => {
    engineWith([SITE], totals(464, 132, 900), JUDGED, totals(464, 132, 900), totals(0, 0, 0));

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('No people in this period')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show everyone' })).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText('No pages read yet')).not.toBeInTheDocument();
  });

  /**
   * Nothing judged yet and nobody judged a person are different facts, and a website whose
   * verdicts are still coming has not been found to have no readers.
   */
  it('says nothing has been judged rather than that nobody was a person', async () => {
    engineWith([SITE], totals(464, 132, 900), STILL_COMING, totals(464, 132, 900), totals(0, 0, 0));

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(
      screen.getByText('People appear here once their visits have finished and been judged.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Look at each visit/ })).not.toBeInTheDocument();
  });

  /**
   * A period judged to the end has its answer, even where nothing in it was judged: its traffic
   * may all belong to visits that began before it.
   */
  it('says a period judged to the end that judged nothing has no people in it', async () => {
    engineWith(
      [SITE],
      totals(464, 132, 900),
      SETTLED_EMPTY,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('No people in this period')).toBeInTheDocument();
    expect(screen.queryByText('Nothing judged yet')).not.toBeInTheDocument();
  });

  /**
   * Without the picture there is no telling whether the period has settled, so the screen says
   * what went wrong rather than reaching an answer it could not check.
   */
  it('says what went wrong rather than an answer when the picture could not be read', async () => {
    engineWith([SITE], totals(464, 132, 900), JUDGED, totals(464, 132, 900), totals(0, 0, 0), [
      '/traffic/series',
    ]);

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.queryByText('No people in this period')).not.toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  /**
   * A period judging has not finished is never reported as one with no people in it: the people
   * may be among the visits still to be judged.
   */
  it('says there are no people yet while the period is still being judged', async () => {
    engineWith(
      [SITE],
      totals(464, 132, 900),
      STILL_JUDGING,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(
      screen.getByText(
        "So far none of this period's visits has been judged to be a person, and the rest are still being judged.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('No people in this period')).not.toBeInTheDocument();
  });

  /**
   * While the engine is stopped or still working through a backlog, judging may not have reached
   * the period at all, and the first people are not half an hour away.
   */
  it('says the whole period is still being judged when judging has reached none of it', async () => {
    engineWith(
      [SITE],
      totals(464, 132, 900),
      NOT_YET_REACHED,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText('This whole period is still being judged.')).toBeInTheDocument();
    expect(screen.queryByText(/once their visits have finished/)).not.toBeInTheDocument();
  });

  /** Judged a few hours into its first day and no further, not one day of the period has settled. */
  it('says the whole period is still being judged when judging stopped inside its first day', async () => {
    engineWith(
      [SITE],
      totals(464, 132, 900),
      STOPPED_IN_THE_FIRST_DAY,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText('This whole period is still being judged.')).toBeInTheDocument();
    expect(screen.queryByText(/once their visits have finished/)).not.toBeInTheDocument();
  });

  it('offers everyone back from that state', async () => {
    const watching = vi.fn();
    const engine = engineWith(
      [SITE],
      totals(464, 132, 900),
      JUDGED,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people', watchingAddress: watching });

    await userEvent.click(await screen.findByRole('button', { name: 'Show everyone' }));

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls.at(-1)?.[0].queryString).not.toContain('only=');
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('push');
    expect(await screen.findByRole('img', { name: /Who and what visited/ })).toBeInTheDocument();
    expect(engine.all().some(({ path }) => path.includes('/pages?'))).toBe(true);
  });

  it('still waits for the first visit when nobody at all has been', async () => {
    engineWith([SITE], totals(0, 0, 0), NOTHING_JUDGED, totals(0, 0, 0), totals(0, 0, 0));

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.queryByText('No people in this period')).not.toBeInTheDocument();
  });

  it('starts every list again when the population changes', async () => {
    const engine = keptToPeople('?only=people');

    await screen.findByText('Page views by people');
    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    await screen.findByText('Page views');

    const pages = engine.all().filter(({ path }) => path.includes('/pages?'));

    expect(pages.at(-1)?.path).toContain('offset=0');
    expect(pages.at(-1)?.path).not.toContain('only=');
  });
});

describe('a day pressed on the picture', () => {
  /** Every question the screen asks about the period, by its address. */
  const ABOUT_THE_PERIOD = [
    '/overview?',
    '/traffic/series?',
    '/traffic?',
    '/pages?',
    '/locations?',
    '/sources?',
    '/devices?',
    '/engagement?',
    '/actions?',
    '/visits/totals?',
    '/visits/pages?',
  ];

  /**
   * Mid-afternoon on the seventeenth in Kolkata, the last day of the week the engine answers with,
   * so the sixteenth is yesterday there and the seventeenth today.
   */
  const NOW = new Date('2026-08-17T09:37:12Z');

  /**
   * Whether a pressed day has a name depends on what day it is where the website is, so the clock
   * is stood still on the week the engine answers with. Only the clock: the timers beneath it keep
   * running, since pressing things and waiting for answers need them.
   */
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * The drawing is a stand-in here, so the day is pressed from the row of the table — the way in
   * that every reader has. The day is the twelfth where the website is, which begins the evening
   * before in the engine's clock.
   */
  it('narrows the whole screen to that day, and leaves the way back in the history', async () => {
    const watching = vi.fn();
    const engine = watched();
    const focusing = vi.spyOn(HTMLSelectElement.prototype, 'focus');

    renderScreen(<Dashboard />, { watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 12, look at this day' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({
          queryString: '?period=2026-08-12..2026-08-12',
          options: expect.objectContaining({ history: 'push' }),
        }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('chosen');
    // The row that was pressed has gone with the week, so the reader is handed to the control
    // that names the day, brought into view, and hears what changed.
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();
    expect(focusing).toHaveBeenCalledWith({ preventScroll: false });
    expect(await screen.findAllByText(/the day before/)).toHaveLength(3);

    for (const address of ABOUT_THE_PERIOD) {
      await waitFor(() =>
        expect(
          askedFor(engine.all(), address, '2026-08-11T18:30:00.000Z', '2026-08-12T18:30:00.000Z'),
        ).toBe(true),
      );
    }

    expect(engine.all().some(({ path }) => path.includes('granularity=hour'))).toBe(true);
  });

  /**
   * Nothing on the picture can hold a reading position, so a press on it hands the control the
   * focus without moving the page — a tap on a phone stays where the reader was looking.
   */
  it('narrows the screen from the picture itself, without moving the page', async () => {
    const watching = vi.fn();
    const focusing = vi.spyOn(HTMLSelectElement.prototype, 'focus');

    watched();
    renderScreen(<Dashboard />, { watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    drawn.picks.get('Who and what visited My Blog over the chosen period.')?.(1);

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({ queryString: '?period=2026-08-12..2026-08-12' }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();
    expect(focusing).toHaveBeenCalledWith({ preventScroll: true });
  });

  it('offers no day to press on a period that is already one', async () => {
    busy();

    renderScreen(<Dashboard />, { searchParams: '?period=yesterday' });

    await userEvent.click(await screen.findByRole('radio', { name: 'How much' }));
    await screen.findByRole('img', { name: /Page views and visitors for/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(screen.getAllByRole('rowheader').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /look at this day/ })).not.toBeInTheDocument();
  });

  /**
   * The list behind the control already has a name for the day before today, and a reader who
   * pressed it is told that name rather than that they chose a stretch of their own.
   */
  it('names a pressed day that is yesterday where the website is as Yesterday', async () => {
    const watching = vi.fn();
    const engine = watched();

    renderScreen(<Dashboard />, { watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 16, look at this day' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({
          queryString: '?period=yesterday',
          options: expect.objectContaining({ history: 'push' }),
        }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();
    expect(await screen.findAllByText(/the day before/)).toHaveLength(3);

    for (const address of ABOUT_THE_PERIOD) {
      await waitFor(() =>
        expect(
          askedFor(engine.all(), address, '2026-08-15T18:30:00.000Z', '2026-08-16T18:30:00.000Z'),
        ).toBe(true),
      );
    }
  });

  /**
   * Half an hour past midnight in Kolkata it is still the evening before where these tests run, so
   * the sixteenth is yesterday for the website and today for the reader. The website's calendar
   * is the one that names it.
   */
  it('names a pressed day by the website’s calendar rather than the reader’s', async () => {
    vi.setSystemTime(new Date('2026-08-16T19:00:00Z'));

    const watching = vi.fn();

    watched();
    renderScreen(<Dashboard />, { watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 16, look at this day' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({ queryString: '?period=yesterday' }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
  });

  /**
   * Today is still running, so it is asked about up to the top of the hour and set beside the same
   * part of yesterday — which is what the cards then say they were measured against.
   */
  it('names a pressed day that is today where the website is as Today, measured against yesterday', async () => {
    const watching = vi.fn();
    const engine = watched();

    renderScreen(<Dashboard />, { watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(screen.getByRole('button', { name: 'Aug 17, look at this day' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({ queryString: '?period=today' }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('today');
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();
    // The engine answers both windows alike, so every card reads "No change from yesterday".
    expect(await screen.findAllByText(/from yesterday$/)).toHaveLength(3);
    expect(screen.queryByText(/the day before/)).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        askedFor(
          engine.all(),
          '/overview?',
          '2026-08-16T18:30:00.000Z',
          '2026-08-17T10:00:00.000Z',
        ),
      ).toBe(true),
    );
    expect(
      askedFor(engine.all(), '/overview?', '2026-08-15T18:30:00.000Z', '2026-08-16T10:00:00.000Z'),
    ).toBe(true);
  });

  /**
   * A day is named when somebody chooses it and never when an address is read. A link names the
   * days its sender put in it, and today's date sent on is that date, not the reader's today.
   */
  it("opens a link to today's date as that date rather than as Today", async () => {
    busy();

    renderScreen(<Dashboard />, { searchParams: '?period=2026-08-17..2026-08-17' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('chosen');
    expect(screen.getByRole('option', { name: 'Your dates' })).toBeInTheDocument();
    expect(await screen.findAllByText(/the day before/)).toHaveLength(3);
  });
});

/**
 * The weeks of the last thirty days on the seventeenth of August, each named by the moment its
 * Monday begins in Kolkata. The first began on the thirteenth of July, before the period did, and
 * the period reaches only its Sunday; the last is the Monday the screen is looked at on.
 */
const WEEK_STARTS = [
  '2026-07-12T18:30:00+00:00',
  '2026-07-19T18:30:00+00:00',
  '2026-07-26T18:30:00+00:00',
  '2026-08-02T18:30:00+00:00',
  '2026-08-09T18:30:00+00:00',
  '2026-08-16T18:30:00+00:00',
];

/**
 * The months of the last ninety days on the seventeenth of August, each named by the moment its
 * first day begins in Kolkata. The period reaches May from the twentieth and August to the
 * seventeenth, and the whole of June and July.
 */
const MONTH_STARTS = [
  '2026-04-30T18:30:00+00:00',
  '2026-05-31T18:30:00+00:00',
  '2026-06-30T18:30:00+00:00',
  '2026-07-31T18:30:00+00:00',
];

/** The buckets the engine answers a picture with, by how the picture was cut. */
const CUT_STARTS: Readonly<Record<string, readonly string[]>> = {
  week: WEEK_STARTS,
  month: MONTH_STARTS,
};

/** What one part of a question asked, read off its address. */
function partOf(path: string, part: string): string | null {
  return new URLSearchParams(path.slice(path.indexOf('?') + 1)).get(part);
}

/**
 * The watched website, answering a picture cut by the week or by the month with buckets cut that
 * way over whatever window it was asked about, as the engine does. Everything else is answered as
 * the watched website answers it.
 */
function cutByTheCalendar() {
  watched();

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;

  return engineDoing(async (path, init) => {
    const granularity = partOf(path, 'granularity') ?? '';
    const starts = CUT_STARTS[granularity];

    if (starts === undefined) {
      return answering(path, init);
    }

    const window = { from: partOf(path, 'from'), to: partOf(path, 'to') };
    const counted = starts.map((_, at) => 10 * (at + 1));

    if (path.includes('/traffic/series')) {
      return respondWith(200, {
        ...window,
        granularity,
        completeTo: window.to,
        buckets: starts,
        groups: [{ category: 'likely-human', sessions: counted, pageViews: counted }],
      });
    }

    return respondWith(200, {
      ...window,
      metric: partOf(path, 'metric'),
      granularity,
      completeTo: window.to,
      points: starts.map((bucketStart, at) => ({ bucketStart, value: counted[at] ?? 0 })),
    });
  });
}

/** Whether the picture was asked for, cut the given way. */
function drawnBy(sent: readonly Sent[], granularity: string): boolean {
  return sent.some(
    ({ path }) => path.includes('/traffic/series?') && partOf(path, 'granularity') === granularity,
  );
}

describe('a picture cut by the week or the month', () => {
  /**
   * Mid-afternoon on Monday the seventeenth of August in Kolkata. Which days a week or a month
   * covers, and what a pressed month is called, depend on what day it is where the website is, so
   * the clock is stood still — only the clock, since pressing things needs the timers beneath it.
   */
  const NOW = new Date('2026-08-17T09:37:12Z');

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** The list of ways the picture can be cut, as it is reached without seeing the card. */
  function ways() {
    return screen.findByRole('combobox', { name: 'Show the chart by' });
  }

  it('offers weeks and months on a long enough period and asks the engine for them', async () => {
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, { searchParams: '?period=last-90-days' });

    const list = await ways();

    expect(
      within(list)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['By day', 'By week', 'By month']);

    await userEvent.selectOptions(list, 'week');
    await waitFor(() => expect(drawnBy(engine.all(), 'week')).toBe(true));

    await userEvent.selectOptions(list, 'month');
    await waitFor(() => expect(drawnBy(engine.all(), 'month')).toBe(true));

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(await screen.findByRole('columnheader', { name: 'Month' })).toBeInTheDocument();
    expect(
      screen.getAllByRole('rowheader').map((row) => row.textContent?.replace(/\s+/g, ' ')),
    ).toStrictEqual(['May 20 – 31', 'Jun', 'Jul', 'Aug 1 – 17']);
  });

  /** Somebody who reads their quarters by the week finds them that way next time. */
  it('remembers how the chart was cut', async () => {
    cutByTheCalendar();

    renderScreen(<Dashboard />, { searchParams: '?period=last-90-days' });

    await userEvent.selectOptions(await ways(), 'week');

    await waitFor(() => expect(window.localStorage.getItem('dewiride.chart-by')).toBe('week'));
  });

  /** The choice is set aside rather than forgotten, so a longer period has it back. */
  it('falls back to days on a period too short for weeks, and keeps the choice', async () => {
    window.localStorage.setItem('dewiride.chart-by', 'week');

    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />);

    expect(await ways()).toHaveValue('day');
    await waitFor(() => expect(drawnBy(engine.all(), 'day')).toBe(true));
    expect(drawnBy(engine.all(), 'week')).toBe(false);
    expect(window.localStorage.getItem('dewiride.chart-by')).toBe('week');
  });

  it('opens on the cut the address names', async () => {
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, { searchParams: '?period=last-90-days&by=month' });

    expect(await ways()).toHaveValue('month');
    await waitFor(() => expect(drawnBy(engine.all(), 'month')).toBe(true));
  });

  /** A period's own way is what an address saying nothing already means, so it is never written. */
  it("leaves the address plain when somebody goes back to the period's own way", async () => {
    const watching = vi.fn();

    cutByTheCalendar();
    renderScreen(<Dashboard />, {
      searchParams: '?period=last-90-days&by=week',
      watchingAddress: watching,
    });

    await userEvent.selectOptions(await ways(), 'day');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls.at(-1)?.[0].queryString).not.toContain('by=');
    expect(window.localStorage.getItem('dewiride.chart-by')).toBeNull();
  });

  /**
   * The last seven days are too short for weeks, and the way to weeks is in the list itself rather
   * than in a note nobody can read without a pointer: choosing it moves to the shortest period that
   * has weeks to show and keeps the choice. The reader stays on the list — on some systems the arrow
   * keys choose as they go, and a list that sent them elsewhere on the first key could never be read
   * to its end — and hears which period the screen moved to.
   */
  it('offers weeks from a short period by moving to the last 30 days, without a pointer', async () => {
    const watching = vi.fn();
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, { watchingAddress: watching });

    const list = await ways();
    const moving = within(list).getByRole('option', { name: 'By week · Last 30 days' });

    expect(moving).not.toHaveAttribute('title');

    list.focus();

    const focusing = vi.spyOn(HTMLSelectElement.prototype, 'focus');

    fireEvent.change(list, { target: { value: 'week' } });

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('last-30-days'),
    );
    expect(list).toHaveFocus();
    expect(focusing).not.toHaveBeenCalled();
    expect(await screen.findByText('Period changed to Last 30 days.')).toHaveAttribute(
      'aria-live',
      'polite',
    );
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toHaveValue('week'),
    );

    const written = watching.mock.calls.at(-1)?.[0];

    expect(written?.queryString).toContain('period=last-30-days');
    expect(written?.queryString).toContain('by=week');
    expect(written?.options.history).toBe('push');
    await waitFor(() => expect(drawnBy(engine.all(), 'week')).toBe(true));
  });

  it('narrows the whole screen to the days of a pressed week', async () => {
    const watching = vi.fn();
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, {
      searchParams: '?period=last-30-days&by=week',
      watchingAddress: watching,
    });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(
      await screen.findByRole('button', { name: /^Jul 27\s–\sAug 2, look at these days$/ }),
    );

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({
          queryString: expect.stringContaining('period=2026-07-27..2026-08-02'),
        }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('chosen');
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveFocus();

    for (const address of ['/overview?', '/traffic/series?', '/pages?']) {
      await waitFor(() =>
        expect(
          askedFor(engine.all(), address, '2026-07-26T18:30:00.000Z', '2026-08-02T18:30:00.000Z'),
        ).toBe(true),
      );
    }

    // Seven days are drawn a day at a time, and the weeks come back with a longer period.
    expect(drawnBy(engine.all(), 'day')).toBe(true);
    expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toHaveValue('day');
  });

  /** A month the list already has a name for arrives under that name, as a pressed day does. */
  it('names a pressed month that is last month as Last month', async () => {
    const watching = vi.fn();

    cutByTheCalendar();
    renderScreen(<Dashboard />, {
      searchParams: '?period=last-90-days&by=month',
      watchingAddress: watching,
    });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(await screen.findByRole('button', { name: 'Jul, look at these days' }));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({ queryString: expect.stringContaining('period=last-month') }),
      ),
    );
    expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('last-month');
  });

  /**
   * Thirty days before the last thirty begin on a Friday, and the dashed weeks would fall two days
   * out from the ones they are drawn beside. Thirty-five days before is a Sunday again — while the
   * cards are still measured against the thirty days before, and each says which days it means.
   */
  it('asks for the earlier weeks lined up week for week', async () => {
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, { searchParams: '?period=last-30-days&by=week&against=before' });

    await waitFor(() =>
      expect(
        askedFor(
          engine.all(),
          '/traffic/series?',
          '2026-06-13T18:30:00.000Z',
          '2026-07-13T10:00:00.000Z',
        ),
      ).toBe(true),
    );
    await waitFor(() =>
      expect(
        askedFor(
          engine.all(),
          '/overview?',
          '2026-06-18T18:30:00.000Z',
          '2026-07-18T10:00:00.000Z',
        ),
      ).toBe(true),
    );
    expect(
      await screen.findByText(
        /The dashed line is Jun 14\s–\sJul 13, 2026, lined up week for week\.$/,
      ),
    ).toBeInTheDocument();
  });

  /**
   * The period is worked out once, at the moment it was chosen, and so is the stretch its dashed
   * lines are drawn from. Cut by the week later that evening, after midnight has passed in Kolkata,
   * the picture still shows the days it was showing, and the earlier weeks still line up with them
   * rather than with a day that has begun since.
   */
  it('keeps the earlier weeks lined up with the picture when it is cut another way later', async () => {
    const engine = cutByTheCalendar();

    renderScreen(<Dashboard />, { searchParams: '?period=last-30-days&against=before' });
    await screen.findByRole('img', { name: /Who and what visited/ });

    vi.setSystemTime(new Date('2026-08-17T19:00:00Z'));
    await userEvent.selectOptions(await ways(), 'week');

    await waitFor(() =>
      expect(
        askedFor(
          engine.all(),
          '/traffic/series?',
          '2026-06-13T18:30:00.000Z',
          '2026-07-13T10:00:00.000Z',
        ),
      ).toBe(true),
    );
    expect(
      await screen.findByText(
        /The dashed line is Jun 14\s–\sJul 13, 2026, lined up week for week\.$/,
      ),
    ).toBeInTheDocument();
  });
});

/**
 * Reports are deleted a year after they arrive, so the same days last year are always partly gone.
 * Set beside what is left of them, this year would read as growth from almost nothing.
 */
describe('a period whose earlier days are no longer kept', () => {
  /** Mid-afternoon on the seventeenth of August in Kolkata. */
  const NOW = new Date('2026-08-17T09:37:12Z');

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("hides the comparison when last year's same days are no longer kept", async () => {
    const engine = engineWith([SITE], totals(464, 132, 900), JUDGED, totals(0, 0, 0));

    renderScreen(<Dashboard />, { searchParams: '?period=this-year' });

    expect(await screen.findByText('464')).toBeInTheDocument();
    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(screen.queryByText(/the same days last year/)).not.toBeInTheDocument();
    expect(screen.queryByText('Up from none')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Compare with the period before' }),
    ).not.toBeInTheDocument();
    expect(
      engine
        .all()
        .filter(({ path }) => path.includes('/overview?'))
        .every(({ path }) => current(path)),
    ).toBe(true);
  });
});

/**
 * Every month from the one a day falls in to the one under way where the website is, each written
 * as the engine names a month: `2025-03`.
 *
 * @param firstDay The first day, written `2025-03-19`.
 * @param timeZone The website's zone, which says which month is under way.
 */
function monthsSince(firstDay: string, timeZone: string): readonly string[] {
  const [fromYear = 1970, fromMonth = 1] = firstDay.split('-').map(Number);
  const [toYear = 1970, toMonth = 1] = todayIn(timeZone, new Date()).split('-').map(Number);
  const count = (toYear - fromYear) * 12 + toMonth - fromMonth + 1;

  return Array.from({ length: count }, (_, at) =>
    new Date(Date.UTC(fromYear, fromMonth - 1 + at, 1)).toISOString().slice(0, 7),
  );
}

/** The moment a month begins where the website is, as the engine names a bucket by it. */
function startOf(month: string, timeZone: string): string {
  return spanInstants({ first: `${month}-01`, last: `${month}-01` }, timeZone).first.toISOString();
}

/** What all time holds for a website in a test. */
interface Recorded {
  /**
   * The record's first day — the first visit where that is known, otherwise the first of its first
   * month — or nothing for a website nobody has visited.
   */
  readonly firstDay?: string | null;
  /** The months that were never counted. */
  readonly missing?: readonly string[];
  /**
   * The months kept before their visits were all judged, which only the people's figures and who
   * came leave out.
   */
  readonly unjudged?: readonly string[];
  /**
   * The people's totals, where a test needs them other than as their months add up. Nought keeps
   * the people out of who came as well, whose visits are then all of something else.
   */
  readonly people?: { readonly pageViews: number; readonly visitors: number };
  /** Whether any of its visits was ever judged, which a test that says nothing assumes. */
  readonly judged?: boolean;
  /** How far every answer counted from verdicts is complete: to now, unless a test says otherwise. */
  readonly judgedTo?: string;
  /** The zone the website is in as each answer is given, which its months are cut in. */
  readonly zone?: () => string;
  /** The website answering everything that is not about all time: the watched one by default. */
  readonly base?: () => unknown;
}

/** What one month of all time came to, by its place in the record, for each answer. */
const PER_MONTH = {
  pageViews: 120,
  visitors: 40,
  peoplePageViews: 36,
  peopleVisitors: 12,
  visits: 30,
} as const;

/**
 * The website, answering every question about all time from its record as the engine does — every
 * month from the record's first to the one under way — and everything else as its base answers it.
 */
function measuredSince(record: Recorded = {}) {
  (record.base ?? watched)();

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;

  return engineDoing(async (path, init) => {
    if (!path.includes('/all-time')) {
      return answering(path, init);
    }

    return respondWith(200, recorded(path, record));
  });
}

/**
 * The answer to one question about all time, as the engine gives it: a bucket for every month from
 * the first day's to the one under way, or for every year they fall in, each named by the moment it
 * begins where the website is, with the months it leaves out and why. A month never counted is
 * nothing in every answer. A month kept before its visits were all judged is nothing in who came and
 * in the people's figures, which rest on verdicts, and stands in everybody's. A year adds up the
 * months of it that have figures — nothing only where none has.
 */
function recorded(
  path: string,
  {
    firstDay = '2025-03-19',
    missing = [],
    unjudged = [],
    people,
    judged = true,
    judgedTo,
    zone = () => SITE.timeZoneId,
  }: Recorded,
) {
  const now = new Date().toISOString();
  const settled = judgedTo ?? now;
  const timeZone = zone();
  const forPeople = partOf(path, 'only') === 'people';
  const judgedPicture = path.includes('/all-time/traffic/series');
  // Who came and the people's figures rest on verdicts, so they leave out the months kept before
  // judging had finished as well; everybody's figures name none of those.
  const unsettled = judgedPicture || forPeople ? unjudged : [];
  const leftOut = [...missing, ...unsettled];
  const months = firstDay === null ? [] : monthsSince(firstDay, timeZone);
  const byYear = partOf(path, 'granularity') === 'year';
  const buckets = byYear ? [...new Set(months.map((month) => month.slice(0, 4)))] : months;
  const starts = buckets.map((bucket) => startOf(byYear ? `${bucket}-01` : bucket, timeZone));
  const each = (perMonth: number) =>
    months.map((month, at) => (leftOut.includes(month) ? null : perMonth * (at + 1)));
  const cut = (values: readonly (number | null)[]) =>
    byYear
      ? buckets.map((year) => {
          const known = values.filter(
            (value, at): value is number => value !== null && months[at]?.startsWith(year) === true,
          );

          return known.length === 0 ? null : known.reduce((total, one) => total + one, 0);
        })
      : values;
  const shape = { from: starts[0] ?? now, to: now, granularity: byYear ? 'year' : 'month' };

  if (judgedPicture) {
    return {
      ...shape,
      completeTo: settled,
      buckets: starts,
      groups:
        judged && firstDay !== null
          ? [
              {
                category: people?.pageViews === 0 ? 'suspected-ai-crawler' : 'likely-human',
                sessions: cut(each(PER_MONTH.visits)),
                pageViews: cut(each(PER_MONTH.pageViews)),
              },
            ]
          : [],
      missing,
      unjudged: unsettled,
    };
  }

  const views = each(forPeople ? PER_MONTH.peoplePageViews : PER_MONTH.pageViews);
  const visitors = each(forPeople ? PER_MONTH.peopleVisitors : PER_MONTH.visitors);
  const completeTo = forPeople ? settled : now;

  if (path.includes('/all-time/series')) {
    const values = cut(partOf(path, 'metric') === 'visitors' ? visitors : views);

    return {
      ...shape,
      metric: partOf(path, 'metric'),
      completeTo,
      points: starts.map((bucketStart, at) => ({ bucketStart, value: values[at] ?? null })),
      missing,
      unjudged: unsettled,
    };
  }

  const total = (values: readonly (number | null)[]) =>
    values.reduce<number>((sum, one) => sum + (one ?? 0), 0);
  const counted =
    forPeople && people ? people : { pageViews: total(views), visitors: total(visitors) };

  return {
    from: starts[0] ?? null,
    to: now,
    firstDay,
    detailFrom: '2025-08-18T18:30:00.000Z',
    ...counted,
    completeTo,
    missing,
    unjudged: unsettled,
  };
}

/** The address a question was asked at, without what it asked. */
function addressOf(path: string): string {
  return path.split('?')[0] ?? path;
}

/** Whether a question was everybody's totals across all time. */
function everybodysRecord(path: string): boolean {
  return addressOf(path).endsWith('/all-time') && partOf(path, 'only') === null;
}

/**
 * The website as measuredSince answers it, with one of its answers held back until the test lets it
 * go: everybody's totals across all time, which all time's picture waits for, unless another is named.
 *
 * @param holding Whether a question is the one held back.
 */
function recordHeld(record: Recorded = {}, holding: (path: string) => boolean = everybodysRecord) {
  measuredSince(record);

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;
  let release: () => void = () => {};
  const released = new Promise<void>((resolve) => {
    release = resolve;
  });

  const engine = engineDoing(async (path, init) => {
    if (holding(path)) {
      await released;
    }

    return answering(path, init);
  });

  return { engine, release: () => release() };
}

/**
 * The website as measuredSince answers it, refusing whichever questions the test names.
 *
 * @param refusing Whether a question is one to refuse.
 */
function recordRefusing(refusing: (path: string) => boolean, record: Recorded = {}) {
  measuredSince(record);

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;

  return engineDoing(async (path, init) =>
    refusing(path)
      ? respondWith(500, { status: 500, title: 'The engine is having trouble.' })
      : answering(path, init),
  );
}

/**
 * All time is the website's whole record, from its first month kept, or still held, to now, from
 * what is kept of each month. Its figures have nothing before them, and its lists cover the last
 * twelve months, which is as long as the visits themselves are kept.
 */
describe('all time', () => {
  /** Mid-afternoon on Monday the seventeenth of August in Kolkata. */
  const NOW = new Date('2026-08-17T09:37:12Z');

  /** Half an hour before now, which is as far as the engine has judged visits that have ended. */
  const HALF_AN_HOUR_AGO = '2026-08-17T09:07:12.000Z';

  /** Where the lists begin: the first day still kept, the nineteenth of August 2025 in Kolkata. */
  const LISTS_FROM = '2025-08-18T18:30:00.000Z';

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** The name each row of the table is headed with, with what is marked beneath it. */
  function rowheaders(): readonly (string | undefined)[] {
    return screen.getAllByRole('rowheader').map((row) => row.textContent?.replace(/\s+/g, ' '));
  }

  /** What a list offers, in order. */
  function options(list: HTMLElement): readonly (string | null)[] {
    return within(list)
      .getAllByRole('option')
      .map((option) => option.textContent);
  }

  /** What kind of mark each series of a drawing is drawn with, by what the drawing announces. */
  function drawnTypes(label: string): readonly unknown[] | undefined {
    const series = drawn.options.get(label)?.series as readonly { type: unknown }[] | undefined;

    return series?.map((one) => one.type);
  }

  /** Waits until everybody's record has been answered, and the screen told of it. */
  async function recordIn(cache: QueryClient) {
    await waitFor(() =>
      expect(cache.getQueryState(allTimeKey(SITE.id, SITE.timeZoneId, 'everybody'))?.status).toBe(
        'success',
      ),
    );
    // The cache tells the screen on a timer of its own, a step after the answer is in.
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  }

  it('reads the cards from the record, and compares them with nothing', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('20,520')).toBeInTheDocument();
    expect(screen.getByText('6,840')).toBeInTheDocument();
    expect(screen.queryByText('464')).not.toBeInTheDocument();
    expect(screen.queryByText(/ more$| less$|No change|Up from none/)).not.toBeInTheDocument();

    // The period's own figures are asked about only over the days the lists cover, to learn whether
    // the lists have anything in them.
    await waitFor(() =>
      expect(engine.all().some(({ path }) => path.includes('/overview?'))).toBe(true),
    );
    expect(
      engine
        .all()
        .filter(({ path }) => path.includes('/overview?'))
        .every(({ path }) => partOf(path, 'from') === LISTS_FROM),
    ).toBe(true);
  });

  it('says since when all time runs, beside the period', async () => {
    measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('all-time');
    expect(await screen.findByText('Since Mar 19, 2025')).toBeInTheDocument();
  });

  it('draws by the month, and by the year once asked', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    // The ways of cutting it are the record's, so they are read once the record is in.
    expect(await screen.findByText('Since Mar 19, 2025')).toBeInTheDocument();

    const list = await screen.findByRole('combobox', { name: 'Show the chart by' });

    expect(options(list)).toStrictEqual(['By month', 'By year']);
    await waitFor(() => expect(askedAllTimeBy(engine.all(), 'month')).toBe(true));

    await userEvent.selectOptions(list, 'year');
    await waitFor(() => expect(askedAllTimeBy(engine.all(), 'year')).toBe(true));

    await userEvent.click(screen.getByText('Show these figures as a table'));

    expect(await screen.findByRole('columnheader', { name: 'Year' })).toBeInTheDocument();
    expect(rowheaders()).toStrictEqual(['Mar 19 – Dec 31, 2025', 'Jan 1 – Aug 17, 2026']);
  });

  /** A record that lies within one year has only months to show, and a list of one is no choice. */
  it('offers no choice of how to cut it while the record lies within one year', async () => {
    const engine = measuredSince({ firstDay: '2026-02-03' });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('Since Feb 3, 2026')).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: /Who and what visited/ })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).not.toBeInTheDocument();
    expect(
      screen.getByText(/^Visits that have finished, by month in Kolkata\./),
    ).toBeInTheDocument();
    expect(askedAllTimeBy(engine.all(), 'year')).toBe(false);
  });

  /**
   * How all time is cut, whether its months carry their years and where its first month begins are
   * all settled by the record, so nothing of the picture is asked for or drawn before it is in — not
   * even by a way of cutting brought along from another website.
   */
  it('waits for the record before asking for its picture or drawing it', async () => {
    const { engine, release } = recordHeld({ firstDay: '2026-02-03' });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&by=year' });

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('all-time');
    await waitFor(() => expect(engine.all().some(({ path }) => everybodysRecord(path))).toBe(true));
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(engine.all().some(({ path }) => /\/all-time\/(series|traffic)/.test(path))).toBe(false);
    expect(screen.queryByRole('option', { name: 'By year' })).not.toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /Who and what visited/ })).not.toBeInTheDocument();

    release();

    expect(await screen.findByRole('img', { name: /Who and what visited/ })).toBeInTheDocument();
    expect(askedAllTimeBy(engine.all(), 'month')).toBe(true);
    expect(askedAllTimeBy(engine.all(), 'year')).toBe(false);
    expect(rowheaders()[0]).toBe('Feb 3 – 28');
  });

  /** Without the record there is no telling how to cut the picture, so it says what went wrong. */
  it('says what went wrong when the record cannot be read, kept to people too', async () => {
    recordRefusing(everybodysRecord);

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findAllByText('Something went wrong')).toHaveLength(2);
    expect(screen.queryByRole('img', { name: /Who and what visited/ })).not.toBeInTheDocument();
  });

  it('says what went wrong when the cards cannot be read, kept to people or not', async () => {
    recordRefusing(
      (path) => addressOf(path).endsWith('/all-time') && partOf(path, 'only') === 'people',
    );

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.queryByText('6,156')).not.toBeInTheDocument();
    expect(screen.queryByText('20,520')).not.toBeInTheDocument();

    // Under all time as under any period, a stand-in promises a figure that is not coming.
    const cards = screen.getByRole('heading', { name: 'Page views by people' }).closest('.grid');

    expect(cards).not.toBeNull();
    await waitFor(() => expect(within(cards as HTMLElement).getAllByText('—')).toHaveLength(3));
    expect((cards as HTMLElement).querySelector('.animate-pulse')).toBeNull();
  });

  /**
   * A stand-in promises a figure on its way, and the people's figures are: only everybody's record was
   * refused, which the notice says. The cards take the mark for no figure only once their own answer
   * has been refused too.
   */
  it('keeps the cards standing in while the people’s figures are on their way, where only everybody’s record was refused', async () => {
    const { release } = recordHeld(
      {},
      (path) => addressOf(path).endsWith('/all-time') && partOf(path, 'only') === 'people',
    );
    const answering = globalThis.fetch as unknown as (
      path: string,
      init: RequestInit,
    ) => Promise<Response>;

    engineDoing(async (path, init) =>
      everybodysRecord(path)
        ? respondWith(500, { status: 500, title: 'The engine is having trouble.' })
        : answering(path, init),
    );

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findAllByText('Something went wrong')).not.toHaveLength(0);

    const cards = screen
      .getByRole('heading', { name: 'Page views by people' })
      .closest('.grid') as HTMLElement;

    expect(cards.querySelector('.animate-pulse')).not.toBeNull();
    expect(within(cards).queryByText('—')).not.toBeInTheDocument();

    release();

    expect(await within(cards).findByText('6,156')).toBeInTheDocument();
    expect(cards.querySelector('.animate-pulse')).toBeNull();
  });

  it('says the lists cover the last 12 months, and asks every list about them', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(
      await screen.findByText('The lists below cover the last 12 months.'),
    ).toBeInTheDocument();

    for (const address of ['/pages?', '/sources?', '/locations?', '/visits/pages?', '/traffic?']) {
      await waitFor(() =>
        expect(askedFor(engine.all(), address, LISTS_FROM, '2026-08-17T10:00:00.000Z')).toBe(true),
      );
    }
  });

  /** A record no longer than the lists has its figures and its lists over the same days. */
  it('says nothing about the lists where the record reaches no further back than they do', async () => {
    measuredSince({ firstDay: '2026-02-03' });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('Since Feb 3, 2026')).toBeInTheDocument();
    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(screen.queryByText('The lists below cover the last 12 months.')).not.toBeInTheDocument();
  });

  /**
   * A website quiet for the last twelve months still has the figures of the months before them.
   * Eight lists each saying nothing has happened yet would say what the cards above contradict.
   */
  it('says once that nothing reached the website in the months the lists cover', async () => {
    measuredSince({ base: () => engineWith([SITE], totals(0, 0, 0)) });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('Nothing in the last 12 months')).toBeInTheDocument();
    expect(
      screen.getByText(
        'The lists cover the last 12 months, and nothing has reached example.com in that time.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('20,520')).toBeInTheDocument();

    for (const empty of [
      'No pages read yet',
      'Nothing has sent visitors yet',
      'No readers placed yet',
      'No devices seen yet',
      'Nothing judged yet',
      'The lists below cover the last 12 months.',
    ]) {
      expect(screen.queryByText(empty)).not.toBeInTheDocument();
    }

    await userEvent.click(screen.getByRole('button', { name: 'Get your tracking code' }));

    expect(await screen.findByRole('dialog')).toHaveTextContent(SITE.id);
  });

  /** Nothing at all reached the website, which is so of the people as much as of everybody. */
  it('says nothing reached it in those months where the screen is kept to people', async () => {
    measuredSince({ base: () => engineWith([SITE], totals(0, 0, 0)) });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('Nothing in the last 12 months')).toBeInTheDocument();
    expect(screen.getByText('6,156')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Get your tracking code' })).toBeInTheDocument();
    expect(screen.queryByText(/judged to be a person\.$/)).not.toBeInTheDocument();
  });

  /**
   * The twelve months run to now, so they are always partly still being judged: visits that came,
   * none of the judged ones a person, are no people so far — never no people.
   */
  it('says no people yet in those months where visits came and none judged was a person', async () => {
    measuredSince({
      base: () => engineWith([SITE], totals(464, 132, 900), JUDGED, undefined, totals(0, 0, 0)),
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(
      screen.getByText(
        'The lists cover the last 12 months, and so far none of the visits to example.com in that time has been judged to be a person.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show everyone' })).toBeInTheDocument();
    expect(screen.queryByText('Nothing in the last 12 months')).not.toBeInTheDocument();
    expect(screen.queryByText(/^No people in/)).not.toBeInTheDocument();
  });

  it('says nothing was judged yet in those months where visits came and none was judged', async () => {
    measuredSince({
      base: () =>
        engineWith([SITE], totals(464, 132, 900), NOTHING_JUDGED, undefined, totals(0, 0, 0)),
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(
      await screen.findByText(
        'The lists cover the last 12 months, and none of the visits to example.com in that time has been judged yet.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show everyone' })).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  /**
   * A website never measured and one with no people at all each have a state of their own in place
   * of the lists, so twelve quiet months are not said before the record has said which it is — or
   * one state would be swapped for another a moment later.
   */
  it('says nothing about quiet lists before the record is in', async () => {
    const { release } = recordHeld({
      firstDay: null,
      base: () => engineWith([SITE], totals(0, 0, 0)),
    });
    const lists = { from: LISTS_FROM, to: '2026-08-17T10:00:00.000Z' };

    const { cache } = renderScreen(<Dashboard />, {
      searchParams: '?period=all-time&only=people',
    });

    for (const population of ['everybody', 'people'] as const) {
      await waitFor(() =>
        expect(cache.getQueryState(overviewKey(SITE.id, lists, population))?.status).toBe(
          'success',
        ),
      );
    }
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(screen.queryByText(/last 12 months/)).not.toBeInTheDocument();

    release();

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.queryByText(/last 12 months/)).not.toBeInTheDocument();
  });

  /**
   * Without everybody's figures there is no telling twelve months with nothing in them from twelve
   * months with no people, so the screen says what went wrong rather than waiting for ever.
   */
  it('says what went wrong where whether the lists hold anything cannot be had', async () => {
    recordRefusing((path) => path.includes('/overview?') && partOf(path, 'only') === null, {
      base: () => engineWith([SITE], totals(464, 132, 900), JUDGED, undefined, totals(0, 0, 0)),
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    const notice = await screen.findByText('Something went wrong');
    const picture = await screen.findByRole('img', { name: /Visits judged to be people/ });

    expect(screen.getByText('6,156')).toBeInTheDocument();
    expect(screen.queryByText(/last 12 months/)).not.toBeInTheDocument();
    // Said where the lists would stand, below the picture, and nowhere above the cards, which were
    // read: beside them it would read as their failure.
    expect(screen.getAllByText('Something went wrong')).toHaveLength(1);
    expect(picture.compareDocumentPosition(notice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  /**
   * Whether the lists' months could be read is said only once the screen knows what stands where the
   * lists go: a website nobody has visited gives that place up, and a refusal shown there for the
   * moment before would be taken back.
   */
  it('says nothing went wrong before a website never measured is known to have nothing to list', async () => {
    const { release } = recordHeld({
      firstDay: null,
      base: () => engineWith([SITE], totals(0, 0, 0)),
    });
    const answering = globalThis.fetch as unknown as (
      path: string,
      init: RequestInit,
    ) => Promise<Response>;

    engineDoing(async (path, init) =>
      path.includes('/overview?') && partOf(path, 'only') === null
        ? respondWith(500, { status: 500, title: 'The engine is having trouble.' })
        : answering(path, init),
    );

    const { cache } = renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await waitFor(() =>
      expect(
        cache.getQueryState(
          overviewKey(SITE.id, { from: LISTS_FROM, to: '2026-08-17T10:00:00.000Z' }, 'everybody'),
        )?.status,
      ).toBe('error'),
    );
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();

    release();

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  it('says nothing went wrong before a screen kept to people is known to have none', async () => {
    const { release } = recordHeld(
      {
        people: { pageViews: 0, visitors: 0 },
        judgedTo: HALF_AN_HOUR_AGO,
        base: () => engineWith([SITE], totals(464, 132, 900), JUDGED, undefined, totals(0, 0, 0)),
      },
      (path) => addressOf(path).endsWith('/all-time') && partOf(path, 'only') === 'people',
    );
    const answering = globalThis.fetch as unknown as (
      path: string,
      init: RequestInit,
    ) => Promise<Response>;

    engineDoing(async (path, init) =>
      path.includes('/overview?') && partOf(path, 'only') === 'people'
        ? respondWith(500, { status: 500, title: 'The engine is having trouble.' })
        : answering(path, init),
    );

    const { cache } = renderScreen(<Dashboard />, {
      searchParams: '?period=all-time&only=people',
    });

    await waitFor(() =>
      expect(
        cache.getQueryState(
          overviewKey(SITE.id, { from: LISTS_FROM, to: '2026-08-17T10:00:00.000Z' }, 'people'),
        )?.status,
      ).toBe('error'),
    );
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();

    release();

    expect(
      await screen.findByText(
        "So far none of this period's visits has been judged to be a person, and the rest are still being judged.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  /** Nothing on the screen rests on the lists' twelve months where a screen stands in for them. */
  it('says nothing went wrong where a website never measured has nothing to list', async () => {
    const engine = recordRefusing(
      (path) => path.includes('/overview?') && partOf(path, 'only') === null,
      { firstDay: null, base: () => engineWith([SITE], totals(0, 0, 0)) },
    );

    const { cache } = renderScreen(<Dashboard />, {
      searchParams: '?period=all-time&only=people',
    });

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        cache.getQueryState(
          overviewKey(SITE.id, { from: LISTS_FROM, to: '2026-08-17T10:00:00.000Z' }, 'everybody'),
        )?.status,
      ).toBe('error'),
    );
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(engine.all().some(({ path }) => path.includes('/overview?'))).toBe(true);
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  /**
   * The lists are set out only once the screen's own figures over their twelve months are in, so a
   * screen that turns out to have no people at all never has them ask a year's question it reads
   * none of.
   */
  it('asks the lists nothing on a cold load where no people at all stand in for them', async () => {
    const engine = measuredSince({
      people: { pageViews: 0, visitors: 0 },
      judgedTo: HALF_AN_HOUR_AGO,
      base: () => engineWith([SITE], totals(464, 132, 900), JUDGED, undefined, totals(0, 0, 0)),
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(
      await screen.findByText(
        "So far none of this period's visits has been judged to be a person, and the rest are still being judged.",
      ),
    ).toBeInTheDocument();
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(
      engine
        .all()
        .filter(({ path }) => partOf(path, 'from') === LISTS_FROM)
        .map(({ path }) => addressOf(path).split('/').slice(4).join('/')),
    ).toStrictEqual(['overview', 'overview']);
  });

  /**
   * No people at all stands in for the lists, and reads every month of all time instead, so nothing
   * on the screen holds the lists' twelve months open to be asked about again.
   */
  it('stops asking about the lists’ twelve months where no people at all stand in for them', async () => {
    const engine = measuredSince({
      people: { pageViews: 0, visitors: 0 },
      judgedTo: HALF_AN_HOUR_AGO,
      base: () => engineWith([SITE], totals(464, 132, 900), JUDGED, undefined, totals(0, 0, 0)),
    });
    const twelveMonths = trafficKey(SITE.id, { from: LISTS_FROM, to: '2026-08-17T10:00:00.000Z' });
    const breakdowns = () =>
      engine.all().filter(({ path }) => addressOf(path).endsWith('/traffic')).length;

    const { cache } = renderScreen(<Dashboard />, {
      searchParams: '?period=all-time&only=people',
    });

    expect(
      await screen.findByText(
        "So far none of this period's visits has been judged to be a person, and the rest are still being judged.",
      ),
    ).toBeInTheDocument();
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    const asked = breakdowns();

    // Whatever is still on the screen asking about them is asked again when they go out of date.
    await act(() => cache.invalidateQueries({ queryKey: twelveMonths }));
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(breakdowns()).toBe(asked);
  });

  /** A month held as its totals alone has no days to narrow the screen to. */
  it('offers only the months still held day by day to be pressed', async () => {
    measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));

    const pressable = screen
      .getAllByRole('button', { name: /look at these days$/ })
      .map((row) => row.textContent?.replace(/\s+/g, ' '));

    expect(pressable[0]).toBe('Sep 2025');
    expect(pressable.at(-1)).toBe('Aug 1 – 17, 2026');
    expect(pressable).not.toContain('Aug 2025');
    expect(screen.getByRole('rowheader', { name: 'Aug 2025' })).toBeInTheDocument();
  });

  it('narrows the whole screen to the days of a pressed month', async () => {
    const watching = vi.fn();

    measuredSince();
    renderScreen(<Dashboard />, { searchParams: '?period=all-time', watchingAddress: watching });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await userEvent.click(screen.getByText('Show these figures as a table'));
    await userEvent.click(
      await screen.findByRole('button', { name: 'Jul 2026, look at these days' }),
    );

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({ queryString: expect.stringContaining('period=last-month') }),
      ),
    );
  });

  it('offers no comparison', async () => {
    measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      screen.queryByRole('button', { name: 'Compare with the period before' }),
    ).not.toBeInTheDocument();
  });

  /** A month nobody counted in time is not a month nobody came in, and the caption says which. */
  it('shows a month never counted as not counted, and names it as left out', async () => {
    measuredSince({ missing: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      await screen.findByText(/June 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not counted Not counted' }),
    ).toBeInTheDocument();
  });

  /**
   * A month kept before its visits were all judged keeps its figures, but has no verdicts to say who
   * came: who came says so itself, even where the cards leave nothing out.
   */
  it('says who came leaves out a month whose visits were never all judged', async () => {
    measuredSince({ unjudged: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      await screen.findByText(/June 2025 wasn.t fully judged and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not fully judged Not fully judged' }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/wasn.t counted/)).not.toBeInTheDocument();
    expect(screen.queryByText('Not counted')).not.toBeInTheDocument();
  });

  /** Never counted, and counted but not fully judged, are two facts, and each is named as itself. */
  it('names a month never counted apart from one not fully judged', async () => {
    measuredSince({ missing: ['2025-06'], unjudged: ['2025-07'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      await screen.findByText(
        /June 2025 wasn.t counted and is left out of these figures\. July 2025 wasn.t fully judged and is left out of these figures\.$/,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not counted Not counted' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jul 2025 Not fully judged Not fully judged' }),
    ).toBeInTheDocument();
  });

  /**
   * Each answer says why it leaves each month out. A month that stopped being countable between the
   * record and the picture was never counted in the picture, and the picture says so, whatever the
   * record read a moment before said of it.
   */
  it('says why a month is left out from the picture’s own answer, never from the record’s', async () => {
    measuredSince({ missing: ['2025-10', '2025-11'] });

    const answering = globalThis.fetch as unknown as (
      path: string,
      init: RequestInit,
    ) => Promise<Response>;

    engineDoing(async (path, init) => {
      const answer = await answering(path, init);

      return everybodysRecord(path)
        ? respondWith(200, { ...(await answer.json()), missing: ['2025-10'] })
        : answer;
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity' });

    await screen.findByRole('img', { name: /Page views and visitors for/ });

    expect(
      await screen.findByText(
        /October 2025 and November 2025 weren.t counted and are left out of these figures\.$/,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Nov 2025 Not counted Not counted' }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/fully judged/)).not.toBeInTheDocument();
  });

  /** How much was read names a month never counted from its own answers. */
  it('says how much was read leaves out a month never counted, by the month', async () => {
    measuredSince({ missing: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity' });

    await screen.findByRole('img', { name: /Page views and visitors for/ });

    expect(
      await screen.findByText(/June 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not counted Not counted' }),
    ).toBeInTheDocument();
  });

  /**
   * A year adds up the months of it that were counted, so its figure stands with no mark of its own:
   * the caption names the month, and the year's row says it leaves it out.
   */
  it('marks the year that leaves out a month never counted', async () => {
    measuredSince({ missing: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity&by=year' });

    await screen.findByRole('img', { name: /Page views and visitors for/ });

    expect(
      await screen.findByText(/June 2025 wasn.t counted and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(screen.queryByText('Not counted')).not.toBeInTheDocument();
    expect(rowheaders()).toStrictEqual([
      'Mar 19 – Dec 31, 2025leaves out June',
      'Jan 1 – Aug 17, 2026',
    ]);
  });

  /** The people's figures leave out a month kept before its visits were all judged. */
  it('says the people’s figures leave out a month whose visits were never all judged', async () => {
    measuredSince({ unjudged: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity&only=people' });

    await screen.findByRole('img', { name: /Page views and visitors among people/ });

    expect(
      await screen.findByText(/June 2025 wasn.t fully judged and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not fully judged Not fully judged' }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/wasn.t counted/)).not.toBeInTheDocument();
  });

  /** The picture says which months it leaves out from its own answer, never from the cards'. */
  it('names a month the people’s figures leave out where the cards cannot be read', async () => {
    recordRefusing(
      (path) => addressOf(path).endsWith('/all-time') && partOf(path, 'only') === 'people',
      { unjudged: ['2025-06'] },
    );

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity&only=people' });

    await screen.findByRole('img', { name: /Page views and visitors among people/ });

    expect(
      await screen.findByText(/June 2025 wasn.t fully judged and is left out of these figures\.$/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('row', { name: 'Jun 2025 Not fully judged Not fully judged' }),
    ).toBeInTheDocument();
    expect(screen.queryByText('Not counted')).not.toBeInTheDocument();
  });

  /** Everybody's figures for that month stand, and how much was read never borrows who came's. */
  it('leaves everybody’s figures for such a month standing', async () => {
    measuredSince({ unjudged: ['2025-06'] });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&show=activity' });

    expect(await screen.findByText('20,520')).toBeInTheDocument();
    await screen.findByRole('img', { name: /Page views and visitors for/ });

    expect(screen.queryByText(/left out of these figures/)).not.toBeInTheDocument();
  });

  it('reads the cards kept to people from the people’s record', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('6,156')).toBeInTheDocument();
    expect(screen.getByText('2,052')).toBeInTheDocument();
    expect(screen.queryByText('20,520')).not.toBeInTheDocument();
    expect(
      engine
        .all()
        .some(
          ({ path }) => addressOf(path).endsWith('/all-time') && partOf(path, 'only') === 'people',
        ),
    ).toBe(true);
  });

  it('draws how much the people read from the people’s months', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people&show=activity' });

    await screen.findByRole('img', { name: /Page views and visitors among people/ });

    const series = engine.all().filter(({ path }) => path.includes('/all-time/series?'));

    expect(series.length).toBeGreaterThan(0);
    expect(series.every(({ path }) => partOf(path, 'only') === 'people')).toBe(true);
    expect(engine.all().some(({ path }) => path.includes('/all-time/traffic/series'))).toBe(false);
    expect(screen.getByRole('row', { name: /^Mar 19\s–\s31, 2025 36 12$/ })).toBeInTheDocument();
  });

  /**
   * Under all time the month under way is always still being judged, so a record whose judged
   * visits held no people has none yet — never none at all.
   */
  it('says there are no people yet where every visit judged was something else', async () => {
    measuredSince({ people: { pageViews: 0, visitors: 0 }, judgedTo: HALF_AN_HOUR_AGO });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(
      screen.getByText(
        "So far none of this period's visits has been judged to be a person, and the rest are still being judged.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('No people in this period')).not.toBeInTheDocument();
  });

  /** All time's older months were judged, so the last twelve judging nothing is not "nothing judged". */
  it('reads whether anything was judged from every month of all time, not from the lists’ year', async () => {
    measuredSince({
      people: { pageViews: 0, visitors: 0 },
      judgedTo: HALF_AN_HOUR_AGO,
      base: busy,
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(screen.queryByText('Nothing judged yet')).not.toBeInTheDocument();
  });

  it('says nothing has been judged where all time has judged nothing at all', async () => {
    measuredSince({
      firstDay: '2026-08-03',
      people: { pageViews: 0, visitors: 0 },
      judged: false,
      judgedTo: '2026-07-31T18:30:00.000Z',
      base: busy,
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText('This whole period is still being judged.')).toBeInTheDocument();
  });

  /**
   * Nought among the people is only what was counted while a month of theirs never was, so no
   * people among the months judged names the month that was not.
   */
  it('never calls the people absent while months are left out of their figures', async () => {
    measuredSince({
      people: { pageViews: 0, visitors: 0 },
      unjudged: ['2025-06'],
      judgedTo: HALF_AN_HOUR_AGO,
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&only=people' });

    expect(
      await screen.findByText(
        "No visits in this period were judged to be people. The newest are still being judged. June 2025 wasn't fully judged and is left out of these figures.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('No people yet')).not.toBeInTheDocument();
  });

  /**
   * A first month is drawn as columns and nothing else, so no way of drawing it is offered — not
   * even while its picture is on its way, which would be a switch shown and then taken back.
   */
  it('never offers a way of drawing a first month while its picture is on its way', async () => {
    const { release } = recordHeld({ firstDay: '2026-08-03', judgedTo: HALF_AN_HOUR_AGO }, (path) =>
      path.includes('/all-time/traffic/series'),
    );

    const { cache } = renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await recordIn(cache);

    expect(screen.getByRole('radiogroup', { name: 'What the chart shows' })).toBeInTheDocument();
    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
    ).not.toBeInTheDocument();

    release();

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
    ).not.toBeInTheDocument();
  });

  it('offers the ways of drawing a long record while its picture is on its way', async () => {
    const { release } = recordHeld({}, (path) => path.includes('/all-time/traffic/series'));

    const { cache } = renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await recordIn(cache);

    expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBeInTheDocument();

    release();

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBeInTheDocument();
  });

  /**
   * Until the record says how many months there are, the ways of drawing them are neither offered
   * nor missing: their place is held, so for the usual record the header does not move when they
   * are offered in it.
   */
  it('holds the places of the ways of drawing and cutting until the record is in, and offers them there', async () => {
    const { release } = recordHeld();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    const place = await screen.findByRole('radiogroup', {
      name: 'How the chart is drawn',
      hidden: true,
    });
    const list = screen.getByRole('combobox', { name: 'Show the chart by', hidden: true });

    expect(screen.queryByRole('radiogroup', { name: 'How the chart is drawn' })).toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).toBeNull();
    expect(place.closest('[inert]')).not.toBeNull();
    expect(list.closest('[inert]')).not.toBeNull();

    release();

    await waitFor(() =>
      expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBe(place),
    );
    expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toBe(list);
    expect(place.closest('[inert]')).toBeNull();
    expect(list.closest('[inert]')).toBeNull();
  });

  it('gives up the held places for a first month, having offered nothing in them', async () => {
    const { release } = recordHeld({ firstDay: '2026-08-03', judgedTo: HALF_AN_HOUR_AGO });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('radiogroup', { name: 'How the chart is drawn', hidden: true });

    expect(screen.queryByRole('radiogroup', { name: 'How the chart is drawn' })).toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by' })).toBeNull();

    release();

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by', hidden: true })).toBeNull();
  });

  /**
   * A record across a new year offers the list in its held place, but drawn in two buckets it has
   * no shape for a line or an area, so the switch's place is given up: December into January, by
   * the month.
   */
  it('offers the list in its place and gives up the switch’s for a record from December into January', async () => {
    vi.setSystemTime(new Date('2027-01-05T09:00:00Z'));

    const { release } = recordHeld({
      firstDay: '2026-12-10',
      judgedTo: '2027-01-05T08:30:00.000Z',
    });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    const list = await screen.findByRole('combobox', { name: 'Show the chart by', hidden: true });

    expect(
      screen.getByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeInTheDocument();

    release();

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toBe(list),
    );
    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeNull();
  });

  /** The same for a record of two years drawn by the year, which is two buckets as well. */
  it('offers the list in its place and gives up the switch’s for a record of two years by the year', async () => {
    const { release } = recordHeld();

    renderScreen(<Dashboard />, { searchParams: '?period=all-time&by=year' });

    const list = await screen.findByRole('combobox', { name: 'Show the chart by', hidden: true });

    expect(
      screen.getByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeInTheDocument();

    release();

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toBe(list),
    );
    expect(screen.getByRole('combobox', { name: 'Show the chart by' })).toHaveValue('year');
    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeNull();
  });

  /** A picture that could not be had is not on its way, and holds no place for what it would offer. */
  it('holds no place for ways of drawing or cutting a picture that could not be had', async () => {
    recordRefusing(everybodysRecord);

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findAllByText('Something went wrong')).not.toHaveLength(0);
    expect(screen.getByRole('radiogroup', { name: 'What the chart shows' })).toBeInTheDocument();
    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn', hidden: true }),
    ).toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Show the chart by', hidden: true })).toBeNull();
  });

  /** Chosen from today, the ways of drawing keep their place while all time's record arrives. */
  it('keeps the place of the ways of drawing when all time is chosen from today', async () => {
    const { release } = recordHeld();

    renderScreen(<Dashboard />, { searchParams: '?period=today' });

    const offered = await screen.findByRole('radiogroup', { name: 'How the chart is drawn' });

    fireEvent.change(screen.getByRole('combobox', { name: 'Period' }), {
      target: { value: 'all-time' },
    });

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('all-time'),
    );
    expect(screen.queryByRole('radiogroup', { name: 'How the chart is drawn' })).toBeNull();
    expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn', hidden: true })).toBe(
      offered,
    );

    release();

    await waitFor(() =>
      expect(screen.getByRole('radiogroup', { name: 'How the chart is drawn' })).toBe(offered),
    );
  });

  /**
   * Reaching for the period is when what the next one needs is asked for: the record, which all
   * time is drawn from and the chooser decides its offer of all time on.
   */
  it('asks for the record once somebody reaches for the period, and not before', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=today' });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await screen.findByText('464');

    expect(engine.all().some(({ path }) => everybodysRecord(path))).toBe(false);

    fireEvent.pointerDown(screen.getByRole('combobox', { name: 'Period' }));

    await waitFor(() => expect(engine.all().some(({ path }) => everybodysRecord(path))).toBe(true));
  });

  /** The offer of all time is the record's to decide, whatever was read before the chooser opened. */
  it('offers all time for older days from the chooser on a period that never asked for the record', async () => {
    measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=today' });

    await screen.findByText('464');

    fireEvent.change(screen.getByRole('combobox', { name: 'Period' }), {
      target: { value: 'choose' },
    });

    const chooser = await screen.findByRole('dialog');

    fireEvent.change(within(chooser).getByLabelText('First day'), {
      target: { value: '2025-06-01' },
    });

    expect(
      await within(chooser).findByText(
        'Visits are kept for 12 months. Pick days on or after Aug 19, 2025, or choose All time.',
      ),
    ).toBeInTheDocument();
  });

  /**
   * Counted from its visits until its month is kept, a new website's first visits are there at
   * once: one month, drawn as a column, and only its newest visits still being judged.
   */
  it('shows the visits of a website first measured this month', async () => {
    measuredSince({ firstDay: '2026-08-03', judgedTo: HALF_AN_HOUR_AGO });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('120')).toBeInTheDocument();
    expect(screen.getByText('40')).toBeInTheDocument();
    expect(screen.queryByText('Waiting for your first visit')).not.toBeInTheDocument();

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(drawnTypes('Who and what visited My Blog over the chosen period.')).toStrictEqual([
      'bar',
    ]);
    expect(
      screen.queryByRole('radiogroup', { name: 'How the chart is drawn' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Visits that have finished, by month in Kolkata. The newest are still being judged.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/drawn fainter/)).not.toBeInTheDocument();
    expect(rowheaders()).toStrictEqual(['Aug 3 – 17still being judged']);
  });

  /**
   * Nothing of the website held is all the screen can know: a website never measured, or one
   * measured only before anything was kept and since gone.
   */
  it('waits for the first visit only when nothing of the website is held', async () => {
    measuredSince({ firstDay: null });

    renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.queryByText('Since the first visit')).not.toBeInTheDocument();
  });

  /**
   * The engine cuts all time's months where the website is when it answers. Moved to another zone,
   * the website has its months asked for again, rather than read from an answer cut in the old one.
   */
  it('asks for all time again when the website moves to another zone', async () => {
    let zone = SITE.timeZoneId;

    measuredSince({ zone: () => zone });

    const answering = globalThis.fetch as unknown as (
      path: string,
      init: RequestInit,
    ) => Promise<Response>;
    const engine = engineDoing(async (path, init) =>
      addressOf(path) === '/api/sites'
        ? respondWith(200, [{ ...SITE, timeZoneId: zone }])
        : answering(path, init),
    );
    const pictures = () =>
      engine.all().filter(({ path }) => path.includes('/all-time/traffic/series')).length;

    const { cache } = renderScreen(<Dashboard />, { searchParams: '?period=all-time' });

    await screen.findByRole('img', { name: /Who and what visited/ });

    expect(rowheaders()).toContain('Nov 2025');

    const asked = pictures();

    zone = 'Etc/UTC';
    await act(() => cache.invalidateQueries({ queryKey: sitesKey, exact: true }));

    await waitFor(() => expect(pictures()).toBeGreaterThan(asked));
    await waitFor(() => expect(rowheaders()).toContain('Nov 2025'));
    expect(rowheaders().filter((row) => row?.includes('Oct 31'))).toStrictEqual([]);
  });

  /** Nothing on a period drawn an hour at a time reads where the record begins. */
  it('asks nothing about all time on a period with no ways to cut its picture', async () => {
    const engine = measuredSince();

    renderScreen(<Dashboard />, { searchParams: '?period=today' });

    await screen.findByRole('img', { name: /Who and what visited/ });
    await screen.findByText('464');

    expect(engine.all().some(({ path }) => path.includes('/all-time'))).toBe(false);
  });

  /**
   * Years are all time's alone. A shorter period lists them only where all time has more than one
   * to show, and choosing them moves the screen there, as choosing months does from a week.
   */
  it('lists years from a shorter period where all time has them, and moves there for them', async () => {
    const watching = vi.fn();

    measuredSince();

    const { cache } = renderScreen(<Dashboard />, {
      searchParams: '?period=last-90-days',
      watchingAddress: watching,
    });
    const list = await screen.findByRole('combobox', { name: 'Show the chart by' });

    await recordIn(cache);

    expect(within(list).getByRole('option', { name: 'By year · All time' })).toBeInTheDocument();

    fireEvent.change(list, { target: { value: 'year' } });

    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Period' })).toHaveValue('all-time'),
    );
    expect(await screen.findByText('Period changed to All time.')).toHaveAttribute(
      'aria-live',
      'polite',
    );

    const written = watching.mock.calls.at(-1)?.[0];

    expect(written?.queryString).toContain('period=all-time');
    expect(written?.queryString).toContain('by=year');
  });

  it('lists no years from a shorter period while the record lies within one year', async () => {
    measuredSince({ firstDay: '2026-02-03' });

    const { cache } = renderScreen(<Dashboard />, { searchParams: '?period=last-90-days' });
    const list = await screen.findByRole('combobox', { name: 'Show the chart by' });

    await recordIn(cache);

    expect(options(list)).toStrictEqual(['By day', 'By week', 'By month']);
  });
});

/** Whether all time's picture of who came was asked for, cut the given way. */
function askedAllTimeBy(sent: readonly Sent[], granularity: string): boolean {
  return sent.some(
    ({ path }) =>
      path.includes('/all-time/traffic/series?') && partOf(path, 'granularity') === granularity,
  );
}

/**
 * The busy website with recording what people click switched off, switched on by whatever the
 * settings panel saves. Every other question is answered as the busy website answers it.
 */
function recordingSwitchedOff() {
  busy();

  const answering = globalThis.fetch as unknown as (
    path: string,
    init: RequestInit,
  ) => Promise<Response>;
  let captureClicks = false;

  return engineDoing(async (path, init) => {
    if (!path.includes('/settings')) {
      return answering(path, init);
    }

    if (init.method === 'PUT') {
      const saved = JSON.parse(String(init.body)) as { readonly captureClicks?: boolean };

      captureClicks = saved.captureClicks ?? captureClicks;
    }

    return respondWith(200, { displayName: 'Example', timeZoneId: 'Etc/UTC', captureClicks });
  });
}

describe('the settings panel opened from the clicks card', () => {
  /**
   * Switching recording on there changes the card behind the panel, and the card's own way in goes
   * with it. Closing the panel then puts focus on the card's heading, where the reader was and
   * which now says what changed, rather than on the page or on a control at the top of it.
   */
  it('puts focus back on the clicks card once the card’s own way in has gone', async () => {
    recordingSwitchedOff();

    renderScreen(<Dashboard />);

    await userEvent.click(await screen.findByRole('button', { name: 'Open settings' }));

    const panel = screen.getByRole('dialog', { name: 'Settings' });

    await userEvent.click(
      await within(panel).findByRole('switch', { name: /Record what people click/ }),
    );
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Open settings' })).not.toBeInTheDocument(),
    );
    await userEvent.click(within(panel).getByRole('button', { name: 'Close' }));

    expect(screen.getByRole('heading', { name: 'No clicks yet' })).toHaveFocus();
  });
});

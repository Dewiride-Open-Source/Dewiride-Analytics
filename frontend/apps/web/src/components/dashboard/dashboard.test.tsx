import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Dashboard } from '@/components/dashboard/dashboard';
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
) {
  return engineDoing(async (path) => {
    if (path.includes('/server-keys')) {
      return respondWith(200, []);
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

function busy() {
  return engineWith([SITE], totals(464, 132, 900));
}

/** The same website, with a week of its visits already judged. */
function watched() {
  return engineWith([SITE], totals(464, 132, 900), JUDGED);
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

    expect(
      await screen.findByText('Someone who returns tomorrow counts again.'),
    ).toBeInTheDocument();
  });

  it('has nothing to divide by when nobody came, and says so rather than guessing', async () => {
    engineWith([SITE], totals(0, 0, 0));

    renderScreen(<Dashboard />);

    expect(await screen.findByText('Waiting for your first visit')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
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

  it('asks the engine about the days the address names, in the website’s own clock', async () => {
    const engine = busy();

    renderScreen(<Dashboard />, { searchParams: '?period=2026-08-17..2026-08-18' });

    await waitFor(() => expect(asked(engine.all(), 'from')).toBe('2026-08-16T18:30:00.000Z'));
    expect(asked(engine.all(), 'to')).toBe('2026-08-18T18:30:00.000Z');
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
    engineWith(
      [SITE],
      totals(464, 132, 900),
      NOTHING_JUDGED,
      totals(464, 132, 900),
      totals(0, 0, 0),
    );

    renderScreen(<Dashboard />, { searchParams: '?only=people' });

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText(/the first people appear/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Look at each visit/ })).not.toBeInTheDocument();
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
});

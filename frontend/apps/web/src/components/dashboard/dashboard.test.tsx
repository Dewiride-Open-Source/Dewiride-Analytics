import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Dashboard } from '@/components/dashboard/dashboard';
import { engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * The drawing itself needs a canvas, which this document does not have. Everything around it —
 * the legend, the caption, and the table the same figures are published in — is ordinary markup
 * and is exercised for real. The drawing is checked by looking at it in a browser.
 */
vi.mock('@/components/charts/chart', () => ({
  Chart: ({ label }: { readonly label: string }) => <div role="img" aria-label={label} />,
}));

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

function series(metric: string, values: readonly number[]) {
  return {
    from: FROM,
    to: TO,
    metric,
    granularity: 'day',
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

/** Answers every question the screen asks, in whichever order they arrive. */
function engineWith(
  sites: unknown,
  overview: unknown,
  judged: unknown = NOTHING_JUDGED,
  earlier: unknown = overview,
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
      return respondWith(200, { from: FROM, to: TO, sessions: 0, pageViews: 0, groups: [] });
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
      return respondWith(
        200,
        path.includes('metric=visitors')
          ? series('visitors', VISITORS)
          : series('pageviews', VIEWS),
      );
    }

    if (path.includes('/overview')) {
      return respondWith(200, current(path) ? overview : earlier);
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

import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Dashboard } from '@/components/dashboard/dashboard';
import { engineDoing, engineStopped, respondWith } from '@/test/engine';
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

/** Answers every question the screen asks, in whichever order they arrive. */
function engineWith(sites: unknown, overview: unknown) {
  return engineDoing(async (path) => {
    if (path.includes('/server-keys')) {
      return respondWith(200, []);
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

    return respondWith(200, path.includes('/overview') ? overview : sites);
  });
}

function busy() {
  return engineWith([SITE], totals(464, 132, 900));
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

  it('draws the period and publishes the same figures as a table', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByRole('img', { name: /Daily page views and daily visitors/ }),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByText('Show these figures as a table'));

    const table = screen.getByRole('table');

    expect(table).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(VIEWS.length + 1);
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
    busy();

    renderScreen(<Dashboard />);

    expect(
      await screen.findByText('Days run midnight to midnight in Kolkata.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Asia\/Kolkata/)).not.toBeInTheDocument();
  });

  it('offers both periods and marks the one being shown', async () => {
    busy();

    renderScreen(<Dashboard />);

    expect(await screen.findByRole('radio', { name: '7 days' })).toHaveAttribute(
      'aria-checked',
      'true',
    );

    await userEvent.click(screen.getByRole('radio', { name: '30 days' }));

    expect(screen.getByRole('radio', { name: '30 days' })).toHaveAttribute('aria-checked', 'true');
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

  it('lets somebody move between websites when the account has several', async () => {
    engineWith([SITE, SECOND_SITE], totals(464, 132, 900));

    renderScreen(<Dashboard />);

    const picker = await screen.findByRole('combobox', { name: 'Website' });

    expect(picker).toHaveValue(SITE.id);
    expect(screen.getByText('example.com')).toBeInTheDocument();

    await userEvent.selectOptions(picker, SECOND_SITE.id);

    expect(await screen.findByText('shop.example.com')).toBeInTheDocument();
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

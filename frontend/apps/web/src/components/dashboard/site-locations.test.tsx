import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteLocations } from '@/components/dashboard/site-locations';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

const COUNTRIES = [
  { place: 'IN', countryCode: 'IN', visitors: 24, pageViews: 61 },
  { place: 'GB', countryCode: 'GB', visitors: 9, pageViews: 20 },
  { place: '', countryCode: '', visitors: 4, pageViews: 7 },
];

const TOWNS = [
  { place: 'Pune', countryCode: 'IN', visitors: 12, pageViews: 30 },
  { place: 'Cambridge', countryCode: 'GB', visitors: 5, pageViews: 11 },
  { place: '', countryCode: 'IN', visitors: 3, pageViews: 6 },
];

/** One website with more places than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  place: `A${String.fromCharCode(65 + rank)}`,
  countryCode: `A${String.fromCharCode(65 + rank)}`,
  visitors: 50 - rank,
  pageViews: 100 - rank,
}));

/** The period held more readers than the rows listed, as it does on any real site. */
function groupingIn(path: string): string {
  if (path.includes('grouping=network')) {
    return 'network';
  }

  return path.includes('grouping=town') ? 'town' : 'country';
}

function engineWith(places: readonly unknown[], visitors = 40, totalPlaces = places.length) {
  return engineDoing(async (path) =>
    respondWith(200, {
      from: FROM,
      to: TO,
      grouping: groupingIn(path),
      visitors,
      totalPlaces,
      mostVisitors: 24,
      places,
    }),
  );
}

/** Answers countries and towns from two lists, and slices whichever was asked for. */
function engineWithBoth(countries: readonly unknown[], towns: readonly unknown[]): Engine {
  return engineDoing(async (path) => {
    const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
    const grouping = asked.get('grouping') ?? 'country';
    const offset = Number(asked.get('offset') ?? 0);
    const limit = Number(asked.get('limit') ?? 10);
    const all = grouping === 'town' ? towns : countries;

    return respondWith(200, {
      from: FROM,
      to: TO,
      grouping,
      visitors: 500,
      totalPlaces: all.length,
      mostVisitors: 50,
      places: all.slice(offset, offset + limit),
    });
  });
}

function show() {
  return renderScreen(<SiteLocations siteId={SITE_ID} window={WINDOW} />);
}

describe('where a website’s readers are', () => {
  it('writes a country code out as a country', async () => {
    engineWith(COUNTRIES);

    show();

    expect(await screen.findByText('India')).toBeInTheDocument();
    expect(screen.getByText('United Kingdom')).toBeInTheDocument();
  });

  /** The same word for the same population as every other card on the screen. */
  it('says how many visitors were in each place, and how much they read', async () => {
    engineWith(COUNTRIES);

    show();

    expect(await screen.findByText(/24 visitors/)).toBeInTheDocument();
    expect(screen.getByText(/61 views/)).toBeInTheDocument();
  });

  /**
   * A slice is one screenful of a longer list, so a share taken against the rows shown would put
   * the busiest country of a widely-read site at several times the share it has.
   */
  it('takes a share against the whole period rather than against the rows shown', async () => {
    engineWith(COUNTRIES, 40);

    show();

    expect(await screen.findByText('60%')).toBeInTheDocument();
    expect(screen.getByText('23%')).toBeInTheDocument();
    expect(screen.getByText('10%')).toBeInTheDocument();
  });

  it('writes a share too small to round to a whole per cent with a decimal', async () => {
    engineWith([{ place: 'NZ', countryCode: 'NZ', visitors: 2, pageViews: 3 }], 462);

    show();

    expect(await screen.findByText('0.4%')).toBeInTheDocument();
  });

  /**
   * An installation whose proxy does not pass its visitors' addresses through resolves nothing at
   * all. Hiding those rows would leave it looking as though it had barely any readers.
   */
  it('shows the readers it could not place rather than leaving them out', async () => {
    engineWith(COUNTRIES);

    show();

    expect(await screen.findByText("Somewhere we couldn't place")).toBeInTheDocument();
  });

  it('says a single visitor is one rather than a plural', async () => {
    engineWith([{ place: 'IE', countryCode: 'IE', visitors: 1, pageViews: 1 }], 1);

    show();

    expect(await screen.findByText('1 visitor')).toBeInTheDocument();
  });

  /**
   * A site whose visitors almost all resolve to nowhere is nearly always a site whose engine
   * never sees their address. Reporting that as a finding about the readers, rather than as a
   * setting somebody can change, would send them looking in the wrong place.
   */
  it('explains itself when almost nobody could be placed', async () => {
    engineWith(
      [
        { place: '', countryCode: '', visitors: 132, pageViews: 380 },
        { place: 'IN', countryCode: 'IN', visitors: 2, pageViews: 5 },
      ],
      134,
    );

    show();

    expect(
      await screen.findByText(/isn't passing the visitor's address along/),
    ).toBeInTheDocument();
  });

  it('says nothing of the sort when most visitors were placed', async () => {
    engineWith(COUNTRIES);

    show();

    await screen.findByText('India');

    expect(screen.queryByText(/isn't passing the visitor's address along/)).not.toBeInTheDocument();
  });

  /**
   * The data behind these rows is published under a licence whose one condition is a link back
   * from any page showing results from it.
   */
  it('credits where the places came from, and links back to it', async () => {
    engineWith(COUNTRIES);

    show();

    const credit = await screen.findByRole('link', { name: 'DB-IP' });

    expect(credit).toHaveAttribute('href', 'https://db-ip.com');
  });

  /**
   * A town is a guess, and a screen that does not say so is claiming more than it knows. It is
   * said where towns are on screen and nowhere else: a caveat about something a reader is not
   * looking at is noise.
   */
  it('says plainly that a town is an estimate, where towns are what is shown', async () => {
    engineWithBoth(COUNTRIES, TOWNS);

    show();

    await screen.findByText('India');
    expect(screen.queryByText(/Towns are an estimate/)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Towns' }));

    expect(await screen.findByText(/Towns are an estimate/)).toBeInTheDocument();
  });

  it('explains itself rather than showing an empty box when nobody has visited', async () => {
    engineWith([], 0, 0);

    show();

    expect(await screen.findByText('No readers placed yet')).toBeInTheDocument();
  });

  it('says so plainly when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('reading the list by town instead', () => {
  it('swaps countries for towns when asked', async () => {
    engineWithBoth(COUNTRIES, TOWNS);

    show();

    expect(await screen.findByText('India')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Towns' }));

    expect(await screen.findByText('Pune')).toBeInTheDocument();
    expect(screen.getByText('Cambridge')).toBeInTheDocument();
  });

  /** A great many town names belong to more than one country. */
  it('names the country beside the town', async () => {
    engineWithBoth(COUNTRIES, TOWNS);

    show();

    await screen.findByText('India');
    await userEvent.click(screen.getByRole('radio', { name: 'Towns' }));

    expect(await screen.findByText(', United Kingdom')).toBeInTheDocument();
  });

  it('says where readers were when only their country could be placed', async () => {
    engineWithBoth(COUNTRIES, TOWNS);

    show();

    await screen.findByText('India');
    await userEvent.click(screen.getByRole('radio', { name: 'Towns' }));

    expect(await screen.findByText('Elsewhere in India')).toBeInTheDocument();
  });

  /** A position in one list means nothing in the other. */
  it('returns to the top of the list when the two are swapped', async () => {
    engineWithBoth(MANY, TOWNS);

    show();

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Towns' }));

    expect(await screen.findByText('Pune')).toBeInTheDocument();
    expect(screen.queryByText('11–20 of 23')).not.toBeInTheDocument();
  });
});

describe('moving through the whole list of places', () => {
  it('says where in the list the reader is', async () => {
    engineWithBoth(MANY, TOWNS);

    show();

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();
  });

  it('brings back the next places, and then the ones after those', async () => {
    engineWithBoth(MANY, TOWNS);

    show();

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('21–23 of 23')).toBeInTheDocument();
  });

  it('offers no way back from the beginning, and none onward from the end', async () => {
    engineWithBoth(MANY, TOWNS);

    show();

    await screen.findByText('1–10 of 23');
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('11–20 of 23');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('21–23 of 23');

    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /previous/i })).toBeEnabled();
  });

  it('shows no way through a list that already fits on one screen', async () => {
    engineWith(COUNTRIES);

    show();

    await screen.findByText('India');

    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /previous/i })).not.toBeInTheDocument();
  });

  it('asks the engine once for each slice and remembers the answer', async () => {
    const engine = engineWithBoth(MANY, TOWNS);

    show();

    await screen.findByText('1–10 of 23');

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('11–20 of 23');

    await userEvent.click(screen.getByRole('button', { name: /previous/i }));
    await screen.findByText('1–10 of 23');

    expect(engine.count).toBe(2);
  });
});

describe('the networks visits arrived over', () => {
  /** One company's datacentre, and two household networks. */
  const NETWORKS = [
    { place: 'Alibaba Cloud', countryCode: '', visitors: 99, pageViews: 109 },
    { place: 'Reliance Jio Infocomm Limited', countryCode: '', visitors: 3, pageViews: 8 },
    { place: '', countryCode: '', visitors: 2, pageViews: 4 },
  ];

  async function showingNetworks() {
    engineWith(NETWORKS, 104);
    renderScreen(<SiteLocations siteId={SITE_ID} window={WINDOW} />);

    await screen.findByRole('radio', { name: 'Networks' });
    await userEvent.click(screen.getByRole('radio', { name: 'Networks' }));
  }

  /**
   * The view that answers the question countries hide: a rented server reports the country it is
   * racked in, so a hundred of them read as an audience there.
   */
  it('names the network each visit came over', async () => {
    await showingNetworks();

    expect(await screen.findByText('Alibaba Cloud')).toBeInTheDocument();
  });

  it('says what a network is, since it is not a place', async () => {
    await showingNetworks();

    expect(
      await screen.findByText(/one company.s datacentre are usually one program/),
    ).toBeInTheDocument();
  });

  it('says so plainly when a network could not be established', async () => {
    await showingNetworks();

    expect(await screen.findByText('Network not known')).toBeInTheDocument();
  });

  /** A network is not a place, so a row must not be dressed up with a country beside it. */
  it('never puts a country beside a network', async () => {
    await showingNetworks();

    await screen.findByText('Alibaba Cloud');

    expect(screen.queryByText(/, India/)).not.toBeInTheDocument();
  });

  /** The routing data carries its own licence, and the place data's credit does not cover it. */
  it('credits the routing data rather than the place data', async () => {
    await showingNetworks();

    expect(await screen.findByRole('link', { name: 'iptoasn.com' })).toHaveAttribute(
      'href',
      'https://iptoasn.com',
    );
    expect(screen.queryByRole('link', { name: 'DB-IP' })).not.toBeInTheDocument();
  });
});

import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteSources } from '@/components/dashboard/site-sources';
import type { Population } from '@/lib/analytics/people-only';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { ringParts } from '@/test/drawing';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for the drawing surface, so that what the ring would be told to draw can be read as
 * an object rather than looked for among pixels on a canvas.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

/** The view the card opens on: five kinds, one of which is arrivals that named nowhere. */
const KINDS = [
  { source: '', site: '', visitors: 402, pageViews: 913 },
  { source: 'search', site: '', visitors: 271, pageViews: 640 },
  { source: 'link', site: '', visitors: 96, pageViews: 210 },
  { source: 'social', site: '', visitors: 44, pageViews: 78 },
  { source: 'assistant', site: '', visitors: 12, pageViews: 25 },
];

const SITES = [
  { source: 'Google', site: 'Google', visitors: 271, pageViews: 640 },
  { source: '', site: '', visitors: 402, pageViews: 913 },
  { source: 'news.ycombinator.com', site: 'news.ycombinator.com', visitors: 96, pageViews: 210 },
  { source: 'DuckDuckGo', site: 'DuckDuckGo', visitors: 44, pageViews: 78 },
];

const PAGES = [
  {
    source: 'news.ycombinator.com/item',
    site: 'news.ycombinator.com',
    visitors: 20,
    pageViews: 50,
  },
  { source: 'lobste.rs/s/analytics', site: 'lobste.rs', visitors: 5, pageViews: 11 },
];

/** One website with more sources than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  source: `sender-${String.fromCharCode(65 + rank)}.test`,
  site: `sender-${String.fromCharCode(65 + rank)}.test`,
  visitors: 50 - rank,
  pageViews: 100 - rank,
}));

function groupingIn(path: string): string {
  return new URLSearchParams(path.slice(path.indexOf('?') + 1)).get('grouping') ?? 'kind';
}

/**
 * One list, answered whichever way it is asked for.
 *
 * The period sent more visitors than the rows listed, as it does on any real site.
 */
function engineWith(sources: readonly unknown[], visitors = 40, totalSources = sources.length) {
  return engineDoing(async (path) =>
    respondWith(200, {
      from: FROM,
      to: TO,
      grouping: groupingIn(path),
      visitors,
      totalSources,
      mostVisitors: 402,
      sources,
    }),
  );
}

/** Answers each of the three views from its own list, and slices whichever was asked for. */
function engineWithAll(
  kinds: readonly unknown[],
  sites: readonly unknown[],
  pages: readonly unknown[],
): Engine {
  return engineDoing(async (path) => {
    const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
    const grouping = asked.get('grouping') ?? 'kind';
    const offset = Number(asked.get('offset') ?? 0);
    const limit = Number(asked.get('limit') ?? 10);
    const all = grouping === 'page' ? pages : grouping === 'site' ? sites : kinds;

    return respondWith(200, {
      from: FROM,
      to: TO,
      grouping,
      visitors: 500,
      totalSources: all.length,
      mostVisitors: 402,
      sources: all.slice(offset, offset + limit),
    });
  });
}

function show(population: Population = 'everybody') {
  return renderScreen(<SiteSources siteId={SITE_ID} window={WINDOW} population={population} />);
}

/** Renders and switches to a named view, the way somebody reading the card would. */
async function showing(view: 'Sites' | 'Pages') {
  show();

  await screen.findByRole('radio', { name: view });
  await userEvent.click(screen.getByRole('radio', { name: view }));
}

describe('how a website’s visitors found it', () => {
  /** Kept to people, every question the card asks is asked of them and of nobody else. */
  it('asks about the people when the card is kept to them', async () => {
    const engine = engineWith(KINDS, 825);

    show('people');

    await screen.findByText('Search engines');

    expect(engine.all().every((sent) => sent.path.includes('only=people'))).toBe(true);
  });
  /**
   * The question a list of website addresses cannot answer: how much of an audience search
   * brings, without the reader already knowing which of the names are search engines.
   */
  it('opens on the overall shape rather than on a list of addresses', async () => {
    engineWith(KINDS, 825);

    show();

    expect(await screen.findByText('Search engines')).toBeInTheDocument();
    expect(screen.getByText('Social networks')).toBeInTheDocument();
    expect(screen.getByText('Links from other sites')).toBeInTheDocument();
  });

  /**
   * Kept apart from search rather than folded into it. Somebody who arrives having been told
   * about a page did not read a list of results and choose it.
   */
  it('counts arrivals from an assistant as their own kind', async () => {
    engineWith(KINDS, 825);

    show();

    expect(await screen.findByText('AI assistants')).toBeInTheDocument();
  });

  it('says how many visitors each kind sent, and how much they read', async () => {
    engineWith(KINDS, 825);

    show();

    expect(await screen.findByText(/271 visitors/)).toBeInTheDocument();
    expect(screen.getByText(/640 views/)).toBeInTheDocument();
  });

  /**
   * The engine's word for a kind is a wire format. Showing it raw would put a developer's
   * vocabulary in front of a reader, and would not translate.
   */
  /**
   * Four ways in and the visitors who named none, every visitor in exactly one of them — so the
   * five account for the whole the card states rather than heading a longer list.
   */
  it('draws the ways in as parts of a single whole', async () => {
    engineWith(KINDS, 825);

    show();

    await screen.findByText('Search engines');

    expect(ringParts().map((part) => part.name)).toStrictEqual([
      'Came straight here',
      'Search engines',
      'Links from other sites',
      'Social networks',
      'AI assistants',
    ]);
  });

  it('announces what the drawing shows, since a ring tells a screen reader nothing', async () => {
    engineWith(KINDS, 825);

    show();

    expect(
      await screen.findByRole('img', { name: 'The share of visitors arriving each way.' }),
    ).toBeInTheDocument();
  });

  /**
   * The same fallback the words already make, made in the colours as well: naming it as anything
   * more particular would be a claim nothing supports.
   */
  it('gives a kind it has no word for the name and the colour of a plain link', async () => {
    engineWith([{ source: 'newsletter', site: '', visitors: 5, pageViews: 9 }], 5);

    show();

    expect(await screen.findByText('Links from other sites')).toBeInTheDocument();
    expect(ringParts()[0]?.name).toBe('Links from other sites');
  });

  it('never shows the engine’s own word for a kind', async () => {
    engineWith(KINDS, 825);

    show();

    await screen.findByText('Search engines');

    expect(screen.queryByText('search')).not.toBeInTheDocument();
    expect(screen.queryByText('assistant')).not.toBeInTheDocument();
  });
});

describe('where a website’s visitors come from', () => {
  it('names the sites that sent them', async () => {
    engineWithAll(KINDS, SITES, PAGES);

    await showing('Sites');

    expect(await screen.findByText('Google')).toBeInTheDocument();
    expect(screen.getByText('news.ycombinator.com')).toBeInTheDocument();
  });

  /**
   * A slice is one screenful of a longer list, so a share taken against the rows shown would put
   * the busiest source of a widely-linked site at several times the share it has.
   */
  /**
   * A ring says these are the parts of one thing, which is false of one screenful of a list that
   * runs on past it.
   */
  it('draws no ring beside a list that is only part of a longer one', async () => {
    engineWithAll(KINDS, SITES, PAGES);

    await showing('Sites');

    expect(await screen.findByText('Google')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('takes a share against the whole period rather than against the rows shown', async () => {
    engineWith(KINDS, 825);

    show();

    expect(await screen.findByText('49%')).toBeInTheDocument();
    expect(screen.getByText('33%')).toBeInTheDocument();
  });

  it('writes a share too small to round to a whole per cent with a decimal', async () => {
    engineWith([{ source: 'lobste.rs', site: 'lobste.rs', visitors: 2, pageViews: 3 }], 462);

    await showing('Sites');

    expect(await screen.findByText('0.4%')).toBeInTheDocument();
  });

  /**
   * Typing an address in, opening a bookmark and following a link from an application all look
   * the same here, and together they are usually the largest row on the list. Leaving them out
   * would take every share on the screen against a total that excluded most of the audience.
   */
  it('shows the visitors who arrived naming nowhere rather than leaving them out', async () => {
    engineWith(KINDS, 825);

    show();

    expect(await screen.findByText('Came straight here')).toBeInTheDocument();
  });

  it('says a single visitor is one rather than a plural', async () => {
    engineWith([{ source: 'lobste.rs', site: 'lobste.rs', visitors: 1, pageViews: 1 }], 1);

    await showing('Sites');

    expect(await screen.findByText('1 visitor')).toBeInTheDocument();
  });

  /**
   * A site nearly all of whose visitors arrive naming nowhere reads as a site nothing links to,
   * which is almost never what it means.
   */
  it('explains itself when almost everybody came straight here', async () => {
    engineWith(
      [
        { source: '', site: '', visitors: 132, pageViews: 380 },
        { source: 'lobste.rs', site: 'lobste.rs', visitors: 2, pageViews: 5 },
      ],
      134,
    );

    show();

    expect(await screen.findByText(/typed in, bookmarked/)).toBeInTheDocument();
  });

  it('says nothing of the sort when most visitors were sent by somebody', async () => {
    engineWith(KINDS, 825);

    show();

    await screen.findByText('Search engines');

    expect(screen.queryByText(/typed in, bookmarked/)).not.toBeInTheDocument();
  });

  /**
   * The address is written by whoever visited the site. A clickable one would put a stranger's
   * destination a mis-click away from somebody reading their own numbers.
   */
  it('never makes a source clickable', async () => {
    engineWithAll(KINDS, SITES, PAGES);

    await showing('Sites');

    await screen.findByText('Google');

    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('explains itself rather than showing an empty box when nothing has sent anybody', async () => {
    engineWith([], 0, 0);

    show();

    expect(await screen.findByText('Nothing has sent visitors yet')).toBeInTheDocument();
  });

  it('says so plainly when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('reading the list by sending page instead', () => {
  it('shows the pages the links were on when asked', async () => {
    engineWithAll(KINDS, SITES, PAGES);

    await showing('Pages');

    expect(await screen.findByText('/item')).toBeInTheDocument();
    expect(screen.getByText('/s/analytics')).toBeInTheDocument();
  });

  /** A position in one list means nothing in another. */
  it('returns to the top of the list when the views are swapped', async () => {
    engineWithAll(KINDS, MANY, PAGES);

    await showing('Sites');

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Pages' }));

    expect(await screen.findByText('/item')).toBeInTheDocument();
    expect(screen.queryByText('11–20 of 23')).not.toBeInTheDocument();
  });
});

describe('moving through the whole list of sources', () => {
  it('says where in the list the reader is', async () => {
    engineWithAll(KINDS, MANY, PAGES);

    await showing('Sites');

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();
  });

  it('brings back the next sources, and then the ones after those', async () => {
    engineWithAll(KINDS, MANY, PAGES);

    await showing('Sites');

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('21–23 of 23')).toBeInTheDocument();
  });

  /** The overall view is five rows and has nothing to page through. */
  it('shows no way through a list that already fits', async () => {
    engineWith(KINDS, 825);

    show();

    await screen.findByText('Search engines');

    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
  });
});

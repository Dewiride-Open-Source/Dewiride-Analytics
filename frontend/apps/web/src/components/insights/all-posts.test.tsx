import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AllPosts } from '@/components/insights/all-posts';
import type { InsightsPost, PostsPage, Site, SitemapStatus } from '@/lib/api/schemas';
import { sitemapStatusKey } from '@/lib/queries/keys';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

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

/** A sitemap read to the end, listing forty-two posts. */
const READ: SitemapStatus = {
  address: 'https://example.com/sitemap.xml',
  state: 'read',
  problem: null,
  readAt: '2026-11-07T08:00:00+00:00',
  succeededAt: '2026-11-07T08:00:00+00:00',
  posts: 42,
  dated: 42,
  reading: 0,
  turnedAway: 0,
};

const A_DAY = 86_400_000;

/**
 * One of the website's posts: the first published today, Saturday 7 November 2026, and each after it
 * a day earlier, every one read ten times more than the one published after it.
 */
function post(number: number, changes: Partial<InsightsPost> = {}): InsightsPost {
  return {
    path: `/post-${number}/`,
    address: `https://example.com/post-${number}/`,
    title: `Post number ${number}`,
    published: new Date(Date.parse('2026-11-07T00:00:00Z') - (number - 1) * A_DAY)
      .toISOString()
      .slice(0, 10),
    pageViews: 10 * number,
    ...changes,
  };
}

/** Every post the sitemap lists: the newest is the least read, and the oldest the most. */
function postsUpTo(count: number): readonly InsightsPost[] {
  return Array.from({ length: count }, (_, at) => post(at + 1));
}

const POSTS = postsUpTo(42);

/** What the engine answers one question about the posts with, from every post the sitemap lists. */
function pageFrom(listed: readonly InsightsPost[], asked: URLSearchParams): PostsPage {
  const search = (asked.get('search') ?? '').toLowerCase();
  const matching = listed.filter(
    ({ title, address }) =>
      (title ?? '').toLowerCase().includes(search) || address.toLowerCase().includes(search),
  );
  const ordered = [...matching].sort((one, other) =>
    asked.get('order') === 'newest'
      ? (other.published ?? '').localeCompare(one.published ?? '')
      : other.pageViews - one.pageViews,
  );
  const offset = Number(asked.get('offset'));

  return {
    posts: listed.length,
    matching: matching.length,
    viewsFrom: matching.length === 0 ? null : '2025-11',
    mostPageViews: Math.max(0, ...matching.map(({ pageViews }) => pageViews)),
    shown: ordered.slice(offset, offset + Number(asked.get('limit'))),
  };
}

/** A second website the same account can see, with a sitemap of its own. */
const NOTES: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df79',
  domain: 'notes.example.com',
  displayName: 'My Notes',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

interface Engine {
  readonly site?: Site;
  /** Every website the account can see, the first of them chosen unless the browser says another. */
  readonly sites?: readonly Site[];
  /**
   * How reading the sitemap stands, or what says so at the moment it is asked; or that the engine
   * refuses to say, or never does.
   */
  readonly sitemap?: SitemapStatus | (() => SitemapStatus) | 'refused' | 'held';
  /** Every post the sitemap lists, with everybody's views. */
  readonly everybody?: readonly InsightsPost[];
  /** The same posts, with the views of people. */
  readonly people?: readonly InsightsPost[];
  /** The posts of the other websites, by the website. */
  readonly elsewhere?: Readonly<Record<string, readonly InsightsPost[]>>;
  /** Which questions about the posts it never answers. */
  readonly holding?: (asked: URLSearchParams) => boolean;
  /** Whether it refuses every question about the posts. */
  readonly refusing?: boolean;
  /** Whether it never says which websites the account can see. */
  readonly sitesHeld?: boolean;
}

/** What the engine answers with where it is having trouble. */
const TROUBLE = { status: 500, title: 'The engine is having trouble.' };

/** An answer that never comes. */
function never(): Promise<Response> {
  return new Promise<Response>(() => {});
}

/** Where a question was asked, read back the way the engine reads it. */
function askedIn(path: string): URLSearchParams {
  return new URLSearchParams(path.slice(path.indexOf('?') + 1));
}

/** The website a question was asked about. */
function siteIn(path: string): string {
  return path.split('/')[3] ?? '';
}

/** How the engine answers about the sitemap, or what it says of its address. */
function sitemapOf(standing: Required<Engine>['sitemap']): SitemapStatus | 'refused' | 'held' {
  return typeof standing === 'function' ? standing() : standing;
}

function engineWith({
  site = SITE,
  sites = [site],
  sitemap: standing = READ,
  everybody = POSTS,
  people = everybody,
  elsewhere = {},
  holding = () => false,
  refusing = false,
  sitesHeld = false,
}: Engine = {}) {
  return engineDoing(async (path) => {
    const sitemap = sitemapOf(standing);

    if (path.endsWith('/sitemap')) {
      if (sitemap === 'held') {
        return never();
      }

      return sitemap === 'refused' ? respondWith(500, TROUBLE) : respondWith(200, sitemap);
    }

    if (path.includes('/posts?')) {
      const asked = askedIn(path);
      const listed =
        elsewhere[siteIn(path)] ?? (asked.get('only') === 'people' ? people : everybody);

      if (holding(asked)) {
        return never();
      }

      return refusing ? respondWith(500, TROUBLE) : respondWith(200, pageFrom(listed, asked));
    }

    if (path.endsWith('/settings')) {
      return respondWith(200, {
        displayName: site.displayName,
        timeZoneId: site.timeZoneId,
        captureClicks: true,
        sitemapAddress: typeof sitemap === 'string' ? READ.address : sitemap.address,
      });
    }

    return sitesHeld ? never() : respondWith(200, sites);
  });
}

/** Every question put to the engine about the posts, read back the way the engine reads them. */
function postsAsked(engine: ReturnType<typeof engineWith>, site = SITE): URLSearchParams[] {
  return engine
    .all()
    .map(({ path }) => path)
    .filter((path) => path.includes('/posts?') && siteIn(path) === site.id)
    .map(askedIn);
}

/** The line under the rows saying which of them are on the screen. */
function pagingLine(): HTMLElement {
  const paging = screen.getByRole('navigation', { name: 'Pages of posts' });
  const line = paging.querySelector('p');

  if (line === null) {
    throw new Error('The list has no line saying which posts are on the screen.');
  }

  return line;
}

/** Chooses another of the account's websites, as the bar across the top of the screen does. */
function choose(site: Site): void {
  window.localStorage.setItem('dewiride.chosen-site', site.id);
  act(() => {
    window.dispatchEvent(new StorageEvent('storage', { key: 'dewiride.chosen-site' }));
  });
}

/** What each question about the posts searched for, or nothing where it searched for nothing. */
function searchesAsked(engine: ReturnType<typeof engineWith>): (string | null)[] {
  return postsAsked(engine).map((asked) => asked.get('search'));
}

/** The rows of the list, once there are any on the screen. */
async function rows(): Promise<HTMLElement[]> {
  return within(await screen.findByRole('list')).getAllByRole('listitem');
}

/** The bar drawn behind a row, as a share of the whole width of the row. */
function barOf(row: HTMLElement): number {
  const bar = row.querySelector<HTMLElement>(':scope > span[aria-hidden]');

  if (bar === null) {
    throw new Error('The row has no bar.');
  }

  return Number.parseFloat(bar.style.width);
}

/** The box the posts are searched from. */
function searchBox(): HTMLElement {
  return screen.getByRole('searchbox', { name: 'Search your posts' });
}

describe('the list of every post', () => {
  it('lists the posts the most read first, each with how much it was read', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />);

    const listed = await rows();

    expect(listed).toHaveLength(25);
    expect(listed[0]).toHaveTextContent('Post number 42');
    expect(listed[0]).toHaveTextContent('420 views');
    expect(listed[0]).toHaveTextContent('Sep 27, 2026');
    expect(listed[24]).toHaveTextContent('Post number 18');
    expect(listed[24]).toHaveTextContent('180 views');
    expect(screen.getByRole('heading', { name: '42 posts', level: 2 })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Most read' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    expect([...(postsAsked(engine)[0]?.entries() ?? [])]).toStrictEqual([
      ['limit', '25'],
      ['offset', '0'],
    ]);
  });

  it('names its website and leads back to Insights, carrying who the screen is kept to', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'only=people' });

    expect(screen.getByRole('heading', { name: 'All posts', level: 1 })).toBeInTheDocument();
    expect(
      await screen.findByText('Every post on My Blog, and how often each one has been read.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to Insights' })).toHaveAttribute(
      'href',
      '/app/insights?only=people',
    );
  });

  /**
   * A title on a phone is a line of small words, so the whole row is the way to the post's page, with
   * the mark that opens the post itself drawn above it.
   */
  it('opens each post’s own page from anywhere on its row, and the post itself in a tab of its own', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'only=people' });

    const [first] = await rows();
    const title = within(first as HTMLElement).getByRole('link', { name: 'Post number 42' });
    const open = within(first as HTMLElement).getByRole('link', {
      name: 'Open Post number 42 in a new tab',
    });

    expect(title).toHaveAttribute(
      'href',
      '/app/insights/post?path=%2Fpost-42%2F&list=&only=people',
    );
    expect(title).toHaveClass('after:absolute', 'after:inset-0');
    expect(first).toHaveClass('relative');
    expect(open).toHaveAttribute('target', '_blank');
    expect(open).toHaveClass('relative', 'z-10');
  });

  /** The post's page leads back to the page of the list it was opened from, whatever it holds. */
  it('names the page of the list each post is opened from, so the post’s page leads back to it', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'search=number&order=newest&page=2&only=people' });

    const [first] = await rows();
    const opened = new URL(
      within(first as HTMLElement)
        .getByRole('link', { name: 'Post number 26' })
        .getAttribute('href') ?? '',
      'https://dashboard.example',
    );

    expect(opened.pathname).toBe('/app/insights/post');
    expect([...opened.searchParams.entries()]).toStrictEqual([
      ['path', '/post-26/'],
      ['list', 'search=number&order=newest&page=2'],
      ['only', 'people'],
    ]);
  });

  it('writes a title in its own direction, and names a post by its address where its page gave none', async () => {
    engineWith({
      everybody: [post(2, { title: 'مرحبا بالعالم' }), post(1, { title: null }), ...POSTS.slice(2)],
    });

    renderScreen(<AllPosts />, { searchParams: 'order=newest' });

    const [first, second] = await rows();

    expect(within(first as HTMLElement).getByText('/post-1/')).toHaveAttribute('dir', 'ltr');
    expect(within(second as HTMLElement).getByText('مرحبا بالعالم')).toHaveAttribute('dir', 'auto');
  });

  /** A bar means the same thing on every page, so the busiest post on a later page is no full bar. */
  it('draws every bar against the most read of all the posts that match, not the first on the page', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'page=2' });

    const [first] = await rows();

    expect(first).toHaveTextContent('Post number 17');
    expect(barOf(first as HTMLElement)).toBeCloseTo((100 * 170) / 420, 5);
  });

  it('asks for the newest first once somebody chooses it, and starts again from the first page', async () => {
    const engine = engineWith({});
    const watching = vi.fn();

    renderScreen(<AllPosts />, { searchParams: 'page=2', watchingAddress: watching });

    await screen.findByRole('link', { name: 'Post number 17' });

    await userEvent.click(screen.getByRole('radio', { name: 'Newest' }));

    expect(await screen.findByRole('link', { name: 'Post number 1' })).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('Post number 1');
    expect(postsAsked(engine).at(-1)?.get('order')).toBe('newest');
    expect(postsAsked(engine).at(-1)?.get('offset')).toBe('0');

    const written = watching.mock.calls.at(-1)?.[0];

    expect(written?.searchParams.get('order')).toBe('newest');
    expect(written?.searchParams.has('page')).toBe(false);
    expect(written?.options.history).toBe('push');
  });

  it('keeps the page where somebody presses the order already chosen', async () => {
    const engine = engineWith({});
    const watching = vi.fn();

    renderScreen(<AllPosts />, { searchParams: 'page=2', watchingAddress: watching });

    await screen.findByRole('link', { name: 'Post number 17' });

    await userEvent.click(screen.getByRole('radio', { name: 'Most read' }));

    expect(screen.getByRole('link', { name: 'Post number 17' })).toBeInTheDocument();
    expect(postsAsked(engine).map((asked) => asked.get('offset'))).toStrictEqual(['25']);
    expect(watching).not.toHaveBeenCalled();
  });

  it('lets somebody choose how many posts a page holds, and starts again from the first page', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'page=2' });

    await screen.findByText('26–42 of 42');

    await userEvent.selectOptions(screen.getByLabelText('Show'), '50');

    expect(await screen.findByText('1–42 of 42')).toBeInTheDocument();
    expect(postsAsked(engine).at(-1)?.get('limit')).toBe('50');
    expect(postsAsked(engine).at(-1)?.get('offset')).toBe('0');
    expect(await rows()).toHaveLength(42);
    expect(screen.getByRole('heading', { name: '42 posts', level: 2 })).toHaveFocus();
  });

  /**
   * The choice is put out of reach while the larger page is read, and the keyboard would go with it
   * to nowhere; the list starts again at its first page, so its top is where the keyboard goes.
   */
  it('brings the keyboard to the top of the list once somebody chooses how many a page holds', async () => {
    engineWith({ holding: (asked) => asked.get('limit') === '50' });

    renderScreen(<AllPosts />, { searchParams: 'page=2' });

    await screen.findByText('26–42 of 42');

    await userEvent.selectOptions(screen.getByLabelText('Show'), '50');

    expect(screen.getByLabelText('Show')).toBeDisabled();
    expect(screen.getByRole('heading', { name: '42 posts', level: 2 })).toHaveFocus();
  });

  /** Which website is shown is chosen in the bar across the top, and is not in the address. */
  it('starts again from the first page once another website is chosen, keeping what was searched for', async () => {
    const engine = engineWith({
      sites: [SITE, NOTES],
      elsewhere: {
        [NOTES.id]: postsUpTo(30).map((one) => ({ ...one, title: `Note ${one.title}` })),
      },
    });
    const watching = vi.fn();

    const { cache } = renderScreen(<AllPosts />, {
      searchParams: 'search=number&page=2',
      watchingAddress: watching,
    });

    await screen.findByText('26–42 of 42');

    // Already known, as it is for a website looked at before: its posts can be asked for at once.
    cache.setQueryData(sitemapStatusKey(NOTES.id), READ);
    choose(NOTES);

    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('Note Post number 30');
    expect(screen.getByRole('heading', { name: '30 posts match your search' })).toBeVisible();
    expect(searchBox()).toHaveValue('number');
    expect(postsAsked(engine, NOTES).map((asked) => asked.get('offset'))).toStrictEqual(['0']);

    await waitFor(() =>
      expect(watching.mock.calls.at(-1)?.[0].searchParams.has('page')).toBe(false),
    );
    expect(watching.mock.calls.at(-1)?.[0].searchParams.get('search')).toBe('number');
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('replace');
  });

  it('sets the thousands apart under the list, as its heading does', async () => {
    engineWith({ everybody: postsUpTo(1234) });

    renderScreen(<AllPosts />);

    expect(
      await screen.findByRole('heading', { name: '1,234 posts', level: 2 }),
    ).toBeInTheDocument();
    expect(pagingLine()).toHaveTextContent('1–25 of 1,234');
    expect(screen.getByRole('status')).toHaveTextContent('1,234 posts, showing 1 to 25');
  });

  /** Every control is pressed with a thumb on a phone, so none is smaller than one there. */
  it('makes every control large enough to press on a phone', async () => {
    engineWith({ everybody: postsUpTo(120) });

    renderScreen(<AllPosts />, { searchParams: 'page=3' });

    await rows();

    for (const order of ['Most read', 'Newest']) {
      expect(screen.getByRole('radio', { name: order })).toHaveClass('min-h-10');
    }

    for (const step of ['Previous', 'Next']) {
      expect(screen.getByRole('button', { name: step })).toHaveClass('size-10', 'shrink-0');
    }

    expect(screen.getByRole('button', { name: 'Page 3' })).toHaveClass(
      'h-10',
      'min-w-10',
      'shrink-0',
    );
    expect(screen.getByLabelText('Show')).toHaveClass('h-10');
    expect(screen.getByRole('button', { name: /People only/ })).toHaveClass('h-10');
  });

  /**
   * With every page large enough to press, a phone has room for the current page and both ends, and
   * a row still too wide for a screen carries on beneath rather than squeezing its numbers.
   */
  it('offers a phone the page it is on and both ends of the list, and lets the row wrap', async () => {
    engineWith({ everybody: postsUpTo(250) });

    renderScreen(<AllPosts />, { searchParams: 'page=5' });

    await rows();

    for (const page of [1, 5, 10]) {
      expect(screen.getByRole('button', { name: `Page ${page}` })).not.toHaveClass('max-sm:hidden');
    }

    for (const page of [4, 6]) {
      expect(screen.getByRole('button', { name: `Page ${page}` })).toHaveClass('max-sm:hidden');
    }

    expect(screen.getByRole('button', { name: 'Page 5' }).parentElement).toHaveClass('flex-wrap');
  });

  it('opens on the search, the order, the page and the size its address names', async () => {
    const engine = engineWith({ everybody: postsUpTo(120) });

    renderScreen(<AllPosts />, { searchParams: 'search=post&order=newest&page=2&size=50' });

    expect(await screen.findByText('51–100 of 120')).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('Post number 51');
    expect(searchBox()).toHaveValue('post');
    expect(screen.getByRole('radio', { name: 'Newest' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByLabelText('Show')).toHaveValue('50');
    expect([...(postsAsked(engine)[0]?.entries() ?? [])]).toStrictEqual([
      ['search', 'post'],
      ['order', 'newest'],
      ['limit', '50'],
      ['offset', '50'],
    ]);
  });

  /** An address is written by whoever sent the link, and anything it says that is not a page opens the first. */
  it.each(['0', '-1', '2.5', 'two', '1e3'])(
    'opens on the first page where the address names page %j',
    async (page) => {
      const engine = engineWith({});

      renderScreen(<AllPosts />, { searchParams: { page } });

      await rows();

      expect(postsAsked(engine).map((asked) => asked.get('offset'))).toStrictEqual(['0']);
    },
  );
});

describe('searching the list of every post', () => {
  it('asks for what was typed once typing stops, rather than at every letter', async () => {
    const engine = engineWith({});
    // A moment between letters, as a person types: far less than typing has to stop for.
    const typing = userEvent.setup({ delay: 20 });

    renderScreen(<AllPosts />);

    await rows();
    await typing.type(searchBox(), 'number 4');

    expect(searchesAsked(engine)).toStrictEqual([null]);

    await waitFor(() => expect(searchesAsked(engine)).toStrictEqual([null, 'number 4']), {
      timeout: 3000,
    });
    expect(await screen.findByRole('heading', { name: '4 posts match your search' })).toBeVisible();
  });

  it('asks at once where somebody presses Enter', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();
    await typing.type(searchBox(), 'number 4{Enter}');

    expect(searchesAsked(engine)).toStrictEqual([null, 'number 4']);
  });

  /** What is typed is read as words, so the list is never asked about the spaces around them. */
  it('searches for the words typed, without the spaces around and between them', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();
    await typing.type(searchBox(), '   number    4   {Enter}');

    expect(searchesAsked(engine).at(-1)).toBe('number 4');
    expect(searchBox()).toHaveValue('   number    4   ');
  });

  /** A search changes as the words go down, so it is written over the entry it was typed on. */
  it('writes what was searched for into the address over the entry it was typed on, from the first page', async () => {
    engineWith({});
    const watching = vi.fn();
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />, { searchParams: 'page=2', watchingAddress: watching });

    await screen.findByRole('link', { name: 'Post number 17' });
    await typing.type(searchBox(), 'number 4{Enter}');

    await waitFor(() =>
      expect(watching.mock.calls.at(-1)?.[0].searchParams.get('search')).toBe('number 4'),
    );
    expect(watching.mock.calls.at(-1)?.[0].searchParams.has('page')).toBe(false);
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('replace');
    expect(await rows()).toHaveLength(4);
  });

  it('clears the search at once', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />, { searchParams: 'search=number+4' });

    expect(await rows()).toHaveLength(4);

    await typing.clear(searchBox());

    expect(searchesAsked(engine)).toStrictEqual(['number 4', null]);
    expect(await screen.findByRole('heading', { name: '42 posts' })).toBeInTheDocument();
  });

  it('says how many posts match, and tells anybody listening', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'search=number+4' });

    expect(
      await screen.findByRole('heading', { name: '4 posts match your search', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('4 posts match your search');
  });

  it('says no post matches, and offers to clear the search', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'search=nothing+like+it' });

    expect(
      await screen.findByRole('heading', { name: 'No posts match your search' }),
    ).toBeInTheDocument();
    expect(screen.getByText("Try fewer words, or part of a post's address.")).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('No posts match your search');
    expect(screen.queryByRole('list')).not.toBeInTheDocument();

    const card = screen.getByRole('heading', { name: 'No posts match your search' }).parentElement;
    const clear = within(card as HTMLElement).getByRole('button', { name: 'Clear search' });

    // Large enough to press with a thumb on a phone.
    expect(clear).toHaveClass('h-10');

    await userEvent.click(clear);

    expect(await rows()).toHaveLength(25);
    expect(searchBox()).toHaveValue('');
    expect(searchBox()).toHaveFocus();
    expect(searchesAsked(engine).at(-1)).toBeNull();
  });

  /**
   * The mark a browser draws in a search box is its own, in colours none of the product's, and too
   * small to press with a thumb; the box carries one of the product's own in its place.
   */
  it('empties the box at once from the mark inside it, and keeps the keyboard in the box', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();

    const search = screen.getByRole('search');

    expect(within(search).queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument();
    expect(searchBox()).toHaveClass('[&::-webkit-search-cancel-button]:appearance-none');

    await typing.type(searchBox(), 'number 4{Enter}');

    expect(await rows()).toHaveLength(4);

    const clear = within(search).getByRole('button', { name: 'Clear search' });

    expect(clear).toHaveClass('size-10');

    await typing.click(clear);

    expect(searchBox()).toHaveValue('');
    expect(searchBox()).toHaveFocus();
    // The whole list was read when the screen opened, and is what the box asks for again.
    expect(searchesAsked(engine)).toStrictEqual([null, 'number 4']);
    expect(await rows()).toHaveLength(25);
    expect(within(search).queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument();
  });

  it('empties a box nothing has been searched for yet without asking anything', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();
    await typing.type(searchBox(), 'num');
    await typing.click(
      within(screen.getByRole('search')).getByRole('button', { name: 'Clear search' }),
    );

    expect(searchBox()).toHaveValue('');
    expect(searchBox()).toHaveFocus();

    // Longer than typing has to stop for, so a search left waiting would have been asked by now.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
    });

    expect(searchesAsked(engine)).toStrictEqual([null]);
  });

  /** The box stops where the engine stops reading, rather than letting somebody type a refusal. */
  it('stops a search at the longest the engine reads', async () => {
    engineWith({});

    renderScreen(<AllPosts />);

    await rows();

    expect(searchBox()).toHaveAttribute('maxlength', '100');
  });
});

describe('moving through the list of every post', () => {
  it('moves a page at a time, writes the page into the address and brings the reader to the top of it', async () => {
    const engine = engineWith({});
    const watching = vi.fn();

    renderScreen(<AllPosts />, { watchingAddress: watching });

    await screen.findByText('1–25 of 42');

    await userEvent.click(screen.getByRole('button', { name: 'Page 2' }));

    expect(screen.getByRole('heading', { name: '42 posts' })).toHaveFocus();
    expect(await screen.findByRole('link', { name: 'Post number 17' })).toBeInTheDocument();
    expect(await screen.findByText('26–42 of 42')).toBeInTheDocument();
    expect(postsAsked(engine).at(-1)?.get('offset')).toBe('25');

    await waitFor(() => expect(watching.mock.calls.at(-1)?.[0].searchParams.get('page')).toBe('2'));
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('push');

    await userEvent.click(screen.getByRole('button', { name: 'Previous' }));

    expect(await screen.findByRole('link', { name: 'Post number 42' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '42 posts' })).toHaveFocus();
  });

  it('keeps the page it shows while the next one is read, and says it is busy', async () => {
    engineWith({ holding: (asked) => asked.get('offset') === '25' });

    renderScreen(<AllPosts />);

    await screen.findByText('1–25 of 42');

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    const list = screen.getByRole('list');

    expect(within(list).getAllByRole('listitem')[0]).toHaveTextContent('Post number 42');
    expect(list).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Page 1' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Page 1' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(pagingLine()).toHaveTextContent('1–25 of 42');
    expect(screen.getByRole('status')).toHaveTextContent('42 posts, showing 1 to 25');
  });

  /** Nothing is said aloud about a list still standing in for the one asked for. */
  it('says nothing of a search until its answer lands', async () => {
    engineWith({ holding: (asked) => asked.has('search') });
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();
    await typing.type(searchBox(), 'number 4{Enter}');

    expect(screen.getByRole('status')).toHaveTextContent('42 posts');
    expect(screen.getByRole('heading', { name: '42 posts' })).toBeInTheDocument();
    expect(screen.getByRole('list')).toHaveAttribute('aria-busy', 'true');
  });

  /**
   * The rows on the screen are still the second page of every post, so that is what the line under
   * them says, and the page marked as the one somebody is on, until the search's own answer lands.
   */
  it('describes the page on the screen, not the search on its way, while the search is read', async () => {
    engineWith({ holding: (asked) => asked.has('search') });
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />, { searchParams: 'page=2' });

    await screen.findByText('26–42 of 42');
    await typing.type(searchBox(), 'number 4{Enter}');

    expect(screen.getByRole('list')).toHaveAttribute('aria-busy', 'true');
    expect(pagingLine()).toHaveTextContent('26–42 of 42');
    expect(screen.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Page 1' })).not.toHaveAttribute('aria-current');
    expect(screen.getByRole('status')).toHaveTextContent('42 posts, showing 26 to 42');
  });

  /** Two lines read out as one answer lands say it twice; the line under the rows is for the eye. */
  it('tells anybody listening about each answer once, with which of the posts the page shows', async () => {
    engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />, { searchParams: 'page=2' });

    await screen.findByText('26–42 of 42');

    expect(screen.getByRole('status')).toHaveTextContent(/^42 posts, showing 26 to 42$/);
    expect(pagingLine()).not.toHaveAttribute('aria-live');

    await typing.type(searchBox(), 'number 4{Enter}');

    expect(await rows()).toHaveLength(4);
    // One page holds every post that matches, so which of them it shows goes without saying.
    expect(screen.getByRole('status')).toHaveTextContent(/^4 posts match your search$/);
    expect(pagingLine()).not.toHaveAttribute('aria-live');
  });

  it('says a page past the end has nothing on it, and offers the first page', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'page=9' });

    expect(
      await screen.findByRole('heading', { name: 'Nothing on this page' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Your list of posts ends before this page.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '42 posts', level: 2 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to the first page' })).toHaveClass('h-10');

    await userEvent.click(screen.getByRole('button', { name: 'Go to the first page' }));

    expect(await screen.findByRole('link', { name: 'Post number 42' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '42 posts' })).toHaveFocus();
    expect(postsAsked(engine).at(-1)?.get('offset')).toBe('0');
  });
});

describe('the list of every post kept to people', () => {
  const PEOPLE = POSTS.map((one) => ({ ...one, pageViews: one.pageViews / 10 }));

  it('asks for the views of people on a screen kept to people, says so once, and starts again from the first page when that changes', async () => {
    const engine = engineWith({ people: PEOPLE });

    renderScreen(<AllPosts />, { searchParams: 'only=people&page=2' });

    expect(await screen.findByRole('link', { name: 'Post number 17' })).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('17 views');
    expect(
      screen.getAllByText('Every figure counts only the visits judged to be people.'),
    ).toHaveLength(1);
    expect(postsAsked(engine).at(-1)?.get('only')).toBe('people');

    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(await screen.findByRole('link', { name: 'Post number 42' })).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('420 views');
    expect(postsAsked(engine).at(-1)?.has('only')).toBe(false);
    expect(postsAsked(engine).at(-1)?.get('offset')).toBe('0');
    expect(
      screen.queryByText('Every figure counts only the visits judged to be people.'),
    ).not.toBeInTheDocument();
  });

  /** Another population's figures are never shown while the people's are on their way. */
  it('never shows everybody’s views while the people’s are on their way', async () => {
    engineWith({ people: PEOPLE, holding: (asked) => asked.has('only') });

    renderScreen(<AllPosts />);

    await screen.findByRole('link', { name: 'Post number 42' });

    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(screen.queryByText('420 views')).not.toBeInTheDocument();
  });
});

describe('what qualifies the list of every post', () => {
  it('says since when views are counted, and how many pages are still to read', async () => {
    engineWith({ sitemap: { ...READ, reading: 12 } });

    renderScreen(<AllPosts />);

    await rows();

    expect(screen.getByText('Views since November 2025. 12 pages still to read.')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Check the address' })).not.toBeInTheDocument();
  });

  it('says part of the sitemap could not be read, above the posts it did find', async () => {
    engineWith({ sitemap: { ...READ, state: 'partly-read', problem: 'too-slow', reading: 12 } });

    renderScreen(<AllPosts />);

    expect(
      await screen.findByText(
        "Views since November 2025. Part of your sitemap couldn't be read, so some posts may be missing. 12 pages still to read.",
      ),
    ).toBeInTheDocument();
    expect(await rows()).toHaveLength(25);

    expect(screen.getByRole('button', { name: 'Check the address' })).toHaveClass('h-10');

    await userEvent.click(screen.getByRole('button', { name: 'Check the address' }));

    expect(
      await within(await screen.findByRole('dialog')).findByRole('textbox', {
        name: 'Sitemap address',
      }),
    ).toBeInTheDocument();
  });

  it('offers nothing to press to somebody who may only read the numbers', async () => {
    engineWith({
      site: { ...SITE, role: 'viewer' },
      sitemap: { ...READ, state: 'failed', problem: 'not-found' },
    });

    renderScreen(<AllPosts />);

    expect(
      await screen.findByText(
        "Views since November 2025. We couldn't read your sitemap the last time we tried, so your newest posts may be missing.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Check the address' })).not.toBeInTheDocument();
  });
});

describe('the list of every post before any can be shown', () => {
  /** The heading is drawn once, and only filled in as the website arrives. */
  it('draws its heading and the way back before the website is known, and stands in for the rest', async () => {
    engineWith({ sitesHeld: true });

    const { container } = renderScreen(<AllPosts />, { searchParams: 'only=people' });

    expect(screen.getByRole('heading', { name: 'All posts', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to Insights' })).toHaveAttribute(
      'href',
      '/app/insights?only=people',
    );
    expect(
      screen.getByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
    expect(container.querySelector('header .animate-pulse')).not.toBeNull();
    expect(container.querySelector('header ~ .animate-pulse')).not.toBeNull();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('stands in for the list while how the sitemap stands is on its way', async () => {
    const engine = engineWith({ sitemap: 'held' });

    const { container } = renderScreen(<AllPosts />, { searchParams: 'only=people' });

    expect(
      await screen.findByText('Every post on My Blog, and how often each one has been read.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Every figure counts only the visits judged to be people.'),
    ).toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).not.toBeNull();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument();
    expect(postsAsked(engine)).toHaveLength(0);
  });

  it('says what went wrong where how the sitemap stands could not be asked about', async () => {
    const engine = engineWith({ sitemap: 'refused' });

    const { container } = renderScreen(<AllPosts />);

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'All posts', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    // Nothing goes on standing in for an answer that is not coming.
    expect(container.querySelector('.animate-pulse')).toBeNull();
    expect(postsAsked(engine)).toHaveLength(0);
  });

  it.each([
    ['no sitemap', { address: null, state: 'none' }, 'See your posts here', 'Add your sitemap'],
    [
      'reading switched off',
      { state: 'off' },
      'Reading sitemaps is switched off on this server',
      null,
    ],
    ['a sitemap still to be read', { state: 'waiting' }, 'Reading your sitemap…', null],
    [
      'a sitemap that could not be read',
      { state: 'failed', problem: 'not-found' },
      "We couldn't read your sitemap",
      'Check the address',
    ],
  ] as const)(
    'stands the same card in place of the list as Insights does where the sitemap lists no posts, for %s',
    async (_, reading, title, action) => {
      engineWith({ sitemap: { ...READ, posts: 0, dated: 0, ...reading } });

      renderScreen(<AllPosts />);

      expect(await screen.findByRole('heading', { name: title })).toBeInTheDocument();
      expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();

      if (action === null) {
        expect(screen.queryByRole('button', { name: /sitemap|address/ })).not.toBeInTheDocument();

        return;
      }

      await userEvent.click(screen.getByRole('button', { name: action }));

      const box = await within(await screen.findByRole('dialog')).findByRole('textbox', {
        name: 'Sitemap address',
      });

      await waitFor(() => expect(box).toHaveFocus());
    },
  );

  it('asks nothing about posts a sitemap does not list', async () => {
    const engine = engineWith({ sitemap: { ...READ, posts: 0, dated: 0 } });

    renderScreen(<AllPosts />);

    await screen.findByRole('heading', { name: 'No posts found' });

    expect(postsAsked(engine)).toHaveLength(0);
  });

  /** A sitemap read again since the screen last heard can list none of its posts any more. */
  it('says the sitemap lists no posts where it has stopped listing them since', async () => {
    engineWith({ everybody: [] });

    renderScreen(<AllPosts />);

    expect(await screen.findByRole('heading', { name: 'No posts found' })).toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('asks for the list again once every page has been read', async () => {
    let standing: SitemapStatus = { ...READ, reading: 12 };
    const engine = engineWith({ sitemap: () => standing });

    const { cache } = renderScreen(<AllPosts />);

    await rows();

    expect(postsAsked(engine)).toHaveLength(1);

    standing = { ...READ, reading: 0 };

    await act(async () => {
      await cache.invalidateQueries({ queryKey: sitemapStatusKey(SITE.id), exact: true });
    });

    await waitFor(() => expect(postsAsked(engine)).toHaveLength(2));
    expect(await rows()).toHaveLength(25);
  });

  it('says what went wrong where the list could not be read', async () => {
    engineWith({ refusing: true });

    const { container } = renderScreen(<AllPosts />);

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(searchBox()).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    // Nothing goes on standing in for an answer that is not coming.
    expect(container.querySelector('.animate-pulse')).toBeNull();
  });

  it('stands in for the list while it is on its way', async () => {
    engineWith({ holding: () => true });

    const { container } = renderScreen(<AllPosts />);

    expect(await screen.findByRole('searchbox', { name: 'Search your posts' })).toBeEnabled();
    expect(container.querySelector('.animate-pulse')).not.toBeNull();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });
});

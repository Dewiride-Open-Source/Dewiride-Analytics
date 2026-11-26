import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AllPosts } from '@/components/insights/all-posts';
import type {
  AllPages,
  InsightsPost,
  ListedPage,
  PostsPage,
  Site,
  SitemapStatus,
} from '@/lib/api/schemas';
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

/** A sitemap read to the end, listing sixty posts. */
const READ: SitemapStatus = {
  address: 'https://example.com/sitemap.xml',
  state: 'read',
  problem: null,
  readAt: '2026-11-07T08:00:00+00:00',
  succeededAt: '2026-11-07T08:00:00+00:00',
  posts: 60,
  dated: 60,
  reading: 0,
  turnedAway: 0,
};

/** Every post the sitemap lists, the most read first. */
const POSTS: readonly InsightsPost[] = Array.from({ length: 60 }, (_, at) => ({
  path: `/post-${at + 1}/`,
  address: `https://example.com/post-${at + 1}/`,
  title: `Post number ${at + 1}`,
  published: '2026-11-01',
  pageViews: 600 - at,
}));

/**
 * One of the pages the website was viewed at, each viewed ten times less than the one before it:
 * every third of them a post on the blog, and the rest the website's documentation.
 */
function page(number: number): ListedPage {
  return {
    path: number % 3 === 0 ? `/blog/post-${number}/` : `/docs/page-${number}/`,
    pageViews: 10 * (76 - number),
  };
}

/** Every page the website was viewed at, the most viewed first. */
const PAGES: readonly ListedPage[] = Array.from({ length: 75 }, (_, at) => page(at + 1));

/**
 * Every page of a website viewed at more pages than the list reaches, the most viewed first, each
 * viewed once less than the one before.
 */
const MANY: readonly ListedPage[] = Array.from({ length: 23_456 }, (_, at) => ({
  path: `/docs/page-${at + 1}/`,
  pageViews: 30_000 - at,
}));

/** How far down the pages that match the engine lists, the most viewed first. */
const LISTED_MOST = 10_000;

/** What the engine answers one question about the pages with, from every page it holds. */
function pagesFrom(
  listed: readonly ListedPage[],
  asked: URLSearchParams,
  leftOut: boolean,
): AllPages {
  const words = (asked.get('search') ?? '').toLowerCase().split(' ').filter(Boolean);
  const matching = listed.filter(({ path }) =>
    words.every((word) => decodeURI(path).toLowerCase().includes(word)),
  );
  const offset = Number(asked.get('offset'));

  return {
    matching: matching.length,
    viewsFrom: matching.length === 0 ? null : '2025-11',
    leftOut,
    mostPageViews: Math.max(0, ...matching.map(({ pageViews }) => pageViews)),
    shown: matching.slice(offset, Math.min(offset + Number(asked.get('limit')), LISTED_MOST)),
  };
}

/** What the engine answers one question about the posts with. */
function postsFrom(asked: URLSearchParams): PostsPage {
  const search = (asked.get('search') ?? '').toLowerCase();
  const matching = POSTS.filter(({ title }) => (title ?? '').toLowerCase().includes(search));
  const offset = Number(asked.get('offset'));

  return {
    posts: POSTS.length,
    matching: matching.length,
    viewsFrom: matching.length === 0 ? null : '2025-11',
    mostPageViews: Math.max(0, ...matching.map(({ pageViews }) => pageViews)),
    shown: matching.slice(offset, offset + Number(asked.get('limit'))),
  };
}

interface Engine {
  /** Every page the website was viewed at, with everybody's views. */
  readonly everybody?: readonly ListedPage[];
  /** The pages people viewed, with their views. */
  readonly people?: readonly ListedPage[];
  /** Whether a month kept fewer of everybody's pages, and of the people's, than were viewed in it. */
  readonly leftOut?: { readonly everybody: boolean; readonly people: boolean };
  /** Which questions about the pages it never answers. */
  readonly holding?: (asked: URLSearchParams) => boolean;
  /** Whether it refuses every question about the pages. */
  readonly refusing?: boolean;
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

function engineWith({
  everybody = PAGES,
  people = everybody,
  leftOut = { everybody: false, people: false },
  holding = () => false,
  refusing = false,
}: Engine = {}) {
  return engineDoing(async (path) => {
    if (path.endsWith('/sitemap')) {
      return respondWith(200, READ);
    }

    if (path.includes('/insights/pages?')) {
      const asked = askedIn(path);
      const ofPeople = asked.get('only') === 'people';

      if (holding(asked)) {
        return never();
      }

      return refusing
        ? respondWith(500, TROUBLE)
        : respondWith(
            200,
            pagesFrom(
              ofPeople ? people : everybody,
              asked,
              ofPeople ? leftOut.people : leftOut.everybody,
            ),
          );
    }

    if (path.includes('/posts?')) {
      return respondWith(200, postsFrom(askedIn(path)));
    }

    return respondWith(200, [SITE]);
  });
}

/** Every question put to the engine about the pages, read back the way the engine reads them. */
function pagesAsked(engine: ReturnType<typeof engineWith>): URLSearchParams[] {
  return engine
    .all()
    .map(({ path }) => path)
    .filter((path) => path.includes('/insights/pages?'))
    .map(askedIn);
}

/** Whether anything was asked about the sitemap or the posts. */
function postsOrSitemapAsked(engine: ReturnType<typeof engineWith>): boolean {
  return engine.all().some(({ path }) => path.endsWith('/sitemap') || path.includes('/posts?'));
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

/** The box the pages are searched from. */
function searchBox(): HTMLElement {
  return screen.getByRole('searchbox', { name: 'Search your pages' });
}

describe('the list of every page', () => {
  it('switches to every page from the heading and back, writing the choice into the address', async () => {
    const engine = engineWith({});
    const watching = vi.fn();

    renderScreen(<AllPosts />, { searchParams: 'page=2&size=50', watchingAddress: watching });

    await screen.findByText('51–60 of 60');
    await userEvent.click(screen.getByRole('radio', { name: 'Pages' }));

    expect(await screen.findByRole('heading', { name: 'All pages', level: 1 })).toBeInTheDocument();
    expect(
      screen.getByText('Every page on My Blog, and how often each one has been viewed.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Pages' })).toHaveAttribute('aria-checked', 'true');
    expect(await screen.findByText('1–50 of 75')).toBeInTheDocument();
    expect(pagesAsked(engine).map((asked) => [...asked.entries()])).toStrictEqual([
      [
        ['limit', '50'],
        ['offset', '0'],
      ],
    ]);

    const written = watching.mock.calls.at(-1)?.[0];

    expect(written?.searchParams.get('of')).toBe('pages');
    expect(written?.searchParams.get('size')).toBe('50');
    expect(written?.searchParams.has('page')).toBe(false);
    expect(written?.options.history).toBe('push');

    await userEvent.click(screen.getByRole('radio', { name: 'Posts' }));

    expect(screen.getByRole('heading', { name: 'All posts', level: 1 })).toBeInTheDocument();
    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument();
    expect(watching.mock.calls.at(-1)?.[0].searchParams.has('of')).toBe(false);
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('push');
  });

  /** The box is one box whichever list is shown, so nothing typed into it is lost on the way. */
  it('keeps the search box, and what was typed, across the switch', async () => {
    const engine = engineWith({});
    const typing = userEvent.setup({ delay: null });

    renderScreen(<AllPosts />);

    await rows();

    const box = screen.getByRole('searchbox', { name: 'Search your posts' });

    await typing.type(box, 'blog');
    await typing.click(screen.getByRole('radio', { name: 'Pages' }));

    expect(searchBox()).toBe(box);
    expect(searchBox()).toHaveValue('blog');
    expect(searchBox()).toHaveAttribute('placeholder', 'Search by address');
    expect(
      await screen.findByRole('heading', { name: '25 pages match your search', level: 2 }),
    ).toBeInTheDocument();
    expect(pagesAsked(engine).at(-1)?.get('search')).toBe('blog');

    await typing.click(screen.getByRole('radio', { name: 'Posts' }));

    expect(screen.getByRole('searchbox', { name: 'Search your posts' })).toBe(box);
    expect(box).toHaveValue('blog');
  });

  it('lists every page the most viewed first, as text read left to right, never a link', async () => {
    const engine = engineWith({
      everybody: [
        { path: '/caf%C3%A9/', pageViews: 90 },
        { path: '/%D9%85%D8%B1%D8%AD%D8%A8%D8%A7/', pageViews: 60 },
        { path: '/invoice%E2%80%AEfdp.exe', pageViews: 30 },
      ],
    });

    renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    const listed = await rows();

    expect(listed.map((row) => row.textContent)).toStrictEqual([
      '/café/90 views',
      '/مرحبا/60 views',
      '/invoice%E2%80%AEfdp.exe30 views',
    ]);

    for (const row of listed) {
      const address = within(row).getByText(/^\//);

      expect(address.tagName).toBe('BDI');
      expect(address).toHaveAttribute('dir', 'ltr');
      expect(address).toHaveAttribute('title', address.textContent);
      expect(address).toHaveClass('line-clamp-2');
    }

    expect(within(screen.getByRole('list')).queryAllByRole('link')).toHaveLength(0);
    expect(screen.getByRole('heading', { name: '3 pages', level: 2 })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^3 pages$/);
    expect(postsOrSitemapAsked(engine)).toBe(false);
  });

  /** A bar means the same thing on every page, so the busiest page on a later page is no full bar. */
  it('draws every bar against the most viewed page that matches', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'of=pages&page=2' });

    const [first] = await rows();

    expect(first).toHaveTextContent('/docs/page-26/');
    expect(barOf(first as HTMLElement)).toBeCloseTo((100 * 500) / 750, 5);
  });

  /** A page has no day it was published on, so it is only ever listed the most viewed first. */
  it('offers no order', async () => {
    engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    await rows();

    expect(screen.queryByRole('radiogroup', { name: 'Order your posts' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('radio').map((radio) => radio.textContent)).toStrictEqual([
      'Posts',
      'Pages',
    ]);
  });

  it('moves a page at a time, writes the page into the address and brings the reader to the top of it', async () => {
    const engine = engineWith({});
    const watching = vi.fn();

    renderScreen(<AllPosts />, { searchParams: 'of=pages', watchingAddress: watching });

    await screen.findByText('1–25 of 75');

    expect(screen.getByRole('status')).toHaveTextContent(/^75 pages, showing 1 to 25$/);
    expect(screen.getByRole('navigation', { name: 'Pages of the list' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Page 2' }));

    expect(screen.getByRole('heading', { name: '75 pages', level: 2 })).toHaveFocus();
    expect(await screen.findByText('26–50 of 75')).toBeInTheDocument();
    expect((await rows())[0]).toHaveTextContent('/docs/page-26/');
    expect(pagesAsked(engine).at(-1)?.get('offset')).toBe('25');

    await waitFor(() => expect(watching.mock.calls.at(-1)?.[0].searchParams.get('page')).toBe('2'));
    expect(watching.mock.calls.at(-1)?.[0].searchParams.get('of')).toBe('pages');
    expect(watching.mock.calls.at(-1)?.[0].options.history).toBe('push');
  });
});

describe('what qualifies the list of every page', () => {
  it('says since when views are counted, and that a page few visitors opened may show fewer views only where a month left pages out', async () => {
    engineWith({ leftOut: { everybody: true, people: false } });

    renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    expect(
      await screen.findByText(
        'Views since November 2025. A page few visitors opened in an earlier month may show fewer views than it had.',
      ),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(await screen.findByText('Views since November 2025.')).toBeInTheDocument();
    expect(screen.queryByText(/few visitors/)).not.toBeInTheDocument();
  });
});

/** More pages match than the list reaches, and the heading still counts every one of them. */
describe('the list of every page where more pages match than it reaches', () => {
  it('pages through the 10,000 most viewed and no further, and says how to find the rest', async () => {
    engineWith({ everybody: MANY });

    renderScreen(<AllPosts />, { searchParams: 'of=pages&page=400' });

    expect(await screen.findByText('9,976–10,000 of 10,000')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '23,456 pages', level: 2 })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Views since November 2025. Only the 10,000 most viewed are listed. Search to find any other page.',
      ),
    ).toBeInTheDocument();

    const listed = await rows();

    expect(listed).toHaveLength(25);
    expect(listed.at(-1)).toHaveTextContent('/docs/page-10000/');
    expect(screen.getByRole('button', { name: 'Page 400' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(screen.queryByRole('button', { name: 'Page 401' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent(/^23,456 pages, showing 9,976 to 10,000$/);
  });

  it('says a page past the 10,000 most viewed has nothing on it, keeping the count and the way to the rest', async () => {
    const engine = engineWith({ everybody: MANY });

    renderScreen(<AllPosts />, { searchParams: 'of=pages&page=401' });

    expect(
      await screen.findByRole('heading', { name: 'Nothing on this page', level: 3 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '23,456 pages', level: 2 })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Views since November 2025. Only the 10,000 most viewed are listed. Search to find any other page.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^23,456 pages$/);
    expect(pagesAsked(engine).at(-1)?.get('offset')).toBe('10000');
  });

  it('says to narrow the search where a search itself matches more than the list reaches', async () => {
    engineWith({ everybody: MANY });

    renderScreen(<AllPosts />, { searchParams: 'of=pages&search=docs' });

    expect(
      await screen.findByRole('heading', { name: '23,456 pages match your search', level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Views since November 2025. Only the 10,000 most viewed are listed. Add a word to narrow the search.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Search to find any other page/)).not.toBeInTheDocument();
  });

  it('says nothing about how far the list reaches where every page that matches is on it', async () => {
    engineWith({ everybody: MANY.slice(0, LISTED_MOST) });

    renderScreen(<AllPosts />, { searchParams: 'of=pages&page=400' });

    expect(await screen.findByText('9,976–10,000 of 10,000')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '10,000 pages', level: 2 })).toBeInTheDocument();
    expect(screen.getByText('Views since November 2025.')).toBeInTheDocument();
    expect(screen.queryByText(/most viewed are listed/)).not.toBeInTheDocument();
  });
});

describe('the list of every page where none can be shown', () => {
  it('says nothing has been viewed yet and offers the tracking code', async () => {
    engineWith({ everybody: [] });

    renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    expect(
      await screen.findByRole('heading', { name: 'No pages viewed yet', level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Once example.com has its first visit, each page that's opened appears here with how often it was viewed.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('No pages viewed yet');
    expect(searchBox()).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Get your tracking code' }));

    expect(
      within(await screen.findByRole('dialog')).getByRole('heading', {
        name: 'Your tracking code',
      }),
    ).toBeInTheDocument();
  });

  it('says no people yet and offers everyone back', async () => {
    const engine = engineWith({ people: [] });

    renderScreen(<AllPosts />, { searchParams: 'of=pages&only=people' });

    expect(
      await screen.findByRole('heading', { name: 'No people yet', level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('None of the visits to your pages so far has been judged to be a person.'),
    ).toBeInTheDocument();

    const everyone = screen.getByRole('button', { name: 'Show everyone' });

    expect(everyone).toHaveClass('h-10');

    await userEvent.click(everyone);

    expect(await rows()).toHaveLength(25);
    expect(pagesAsked(engine).at(-1)?.has('only')).toBe(false);
    expect(screen.getByRole('button', { name: /People only/ })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  /**
   * Pages were viewed, in months that keep only the pages more than one visitor opened, so neither
   * "nothing viewed yet" nor the tracking code is the answer; for people, everybody's list may be.
   */
  it('says there are no pages to list yet where earlier months kept none, offering everyone back to people', async () => {
    engineWith({ everybody: [], people: [], leftOut: { everybody: true, people: true } });

    renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    expect(
      await screen.findByRole('heading', { name: 'No pages to list yet', level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Earlier months keep only pages more than one visitor opened, and none has been viewed since.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^No pages to list yet$/);
    expect(
      screen.queryByRole('button', { name: 'Get your tracking code' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show everyone' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'No pages viewed yet' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    const everyone = await screen.findByRole('button', { name: 'Show everyone' });

    expect(
      screen.getByRole('heading', { name: 'No pages to list yet', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'No people yet' })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^No pages to list yet$/);

    await userEvent.click(everyone);

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Show everyone' })).not.toBeInTheDocument(),
    );
    expect(
      screen.getByRole('heading', { name: 'No pages to list yet', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /People only/ })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('says no page matches and offers to clear the search', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'of=pages&search=nothing+like+it' });

    expect(
      await screen.findByRole('heading', { name: 'No pages match your search' }),
    ).toBeInTheDocument();
    expect(screen.getByText("Try fewer words, or part of a page's address.")).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('No pages match your search');
    expect(screen.queryByRole('list')).not.toBeInTheDocument();

    const card = screen.getByRole('heading', { name: 'No pages match your search' }).parentElement;

    await userEvent.click(
      within(card as HTMLElement).getByRole('button', { name: 'Clear search' }),
    );

    expect(await rows()).toHaveLength(25);
    expect(searchBox()).toHaveValue('');
    expect(searchBox()).toHaveFocus();
    expect(pagesAsked(engine).at(-1)?.has('search')).toBe(false);
  });

  it('says a page past the end has nothing on it, keeping the list’s heading', async () => {
    const engine = engineWith({});

    renderScreen(<AllPosts />, { searchParams: 'of=pages&page=9' });

    expect(
      await screen.findByRole('heading', { name: 'Nothing on this page', level: 3 }),
    ).toBeInTheDocument();
    expect(screen.getByText('Your list of pages ends before this page.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '75 pages', level: 2 })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^75 pages$/);

    await userEvent.click(screen.getByRole('button', { name: 'Go to the first page' }));

    expect(await rows()).toHaveLength(25);
    expect(screen.getByRole('heading', { name: '75 pages', level: 2 })).toHaveFocus();
    expect(pagesAsked(engine).at(-1)?.get('offset')).toBe('0');
  });

  it('stands in for the list while its first page is on its way', async () => {
    engineWith({ holding: () => true });

    const { container } = renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    expect(await screen.findByRole('searchbox', { name: 'Search your pages' })).toBeEnabled();
    expect(container.querySelector('.animate-pulse')).not.toBeNull();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });

  /** The answer on its way may well hold pages, and offering the tracking code then is wrong. */
  it.each([
    ['everybody', 'of=pages', { everybody: [] }, 'No pages viewed yet'],
    ['people', 'of=pages&only=people', { people: [] }, 'No people yet'],
  ] as const)(
    'never says the list is empty while the next answer is on its way: %s',
    async (_, address, empty, title) => {
      engineWith({ ...empty, holding: (asked) => asked.has('search') });
      const typing = userEvent.setup({ delay: null });

      const { container } = renderScreen(<AllPosts />, { searchParams: address });

      expect(await screen.findByRole('heading', { name: title, level: 2 })).toBeInTheDocument();

      await typing.type(searchBox(), 'docs{Enter}');

      expect(screen.queryByRole('heading', { name: title })).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Get your tracking code' }),
      ).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Show everyone' })).not.toBeInTheDocument();
      expect(container.querySelector('.animate-pulse')).not.toBeNull();
      expect(screen.getByRole('status')).toBeEmptyDOMElement();
    },
  );

  it('says what went wrong where the list could not be read', async () => {
    engineWith({ refusing: true });

    const { container } = renderScreen(<AllPosts />, { searchParams: 'of=pages' });

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(searchBox()).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    // Nothing goes on standing in for an answer that is not coming.
    expect(container.querySelector('.animate-pulse')).toBeNull();
  });
});

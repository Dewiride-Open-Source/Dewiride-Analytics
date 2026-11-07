import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Insights } from '@/components/insights/insights';
import type {
  InsightsPost,
  InsightsPosts,
  Insights as InsightsAnswer,
  Site,
  SitemapStatus,
} from '@/lib/api/schemas';
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

const TO = '2026-11-07T09:30:00+00:00';

/** A record from November 2025 to the month running now. */
const RECORD: InsightsAnswer = {
  firstDay: '2025-11-19',
  to: TO,
  completeTo: TO,
  pageViews: 4321,
  visitors: 1234,
  bestDay: { day: '2026-09-16', pageViews: 412 },
  weekdays: [10, 20, 40, 10, 10, 5, 5],
  busiestWeekdays: [2],
  hours: Array.from({ length: 24 }, (_, hour) => (hour === 14 ? 50 : 0)),
  busiestHours: [14],
  months: [
    { month: '2025-11', pageViews: 120, daysMeasured: 12 },
    { month: '2026-11', pageViews: 90, daysMeasured: 7 },
  ],
  missing: [],
  unjudged: [],
};

/** A sitemap read to the end, listing forty-two posts, forty of them dated. */
const READ: SitemapStatus = {
  address: 'https://example.com/sitemap.xml',
  state: 'read',
  problem: null,
  readAt: '2026-11-07T08:00:00+00:00',
  succeededAt: '2026-11-07T08:00:00+00:00',
  posts: 42,
  dated: 40,
  reading: 0,
  turnedAway: 0,
};

/** One of the website's posts, published on a day of November or of October before it. */
function post(number: number, changes: Partial<InsightsPost> = {}): InsightsPost {
  const day = number <= 7 ? `2026-11-0${8 - number}` : `2026-10-${30 - number}`;

  return {
    path: `/post-${number}/`,
    address: `https://example.com/post-${number}/`,
    title: `Post number ${number}`,
    published: day,
    pageViews: 10 * number,
    ...changes,
  };
}

const LATEST = Array.from({ length: 10 }, (_, place) => post(place + 1));

const BEST = post(20, { title: 'The one everybody read', published: '2026-03-03', pageViews: 900 });

const POSTS: InsightsPosts = {
  posts: 42,
  dated: 40,
  viewsFrom: '2025-11',
  latest: LATEST,
  mostRead: BEST,
  calendarFrom: '2025-12-01',
  calendarTo: '2026-11-07',
  calendar: [
    { day: '2026-03-03', posts: 1 },
    { day: '2026-11-03', posts: 2 },
  ],
};

interface Engine {
  readonly site?: Site;
  /** How reading the sitemap stands, or what says so at the moment it is asked. */
  readonly sitemap?: SitemapStatus | (() => SitemapStatus);
  readonly everybody?: InsightsPosts;
  readonly people?: InsightsPosts;
}

function engineWith({
  site = SITE,
  sitemap: standing = READ,
  everybody = POSTS,
  people = everybody,
}: Engine) {
  return engineDoing(async (path) => {
    const sitemap = typeof standing === 'function' ? standing() : standing;

    if (path.endsWith('/sitemap')) {
      return respondWith(200, sitemap);
    }

    if (path.includes('/insights/posts')) {
      return respondWith(200, path.includes('only=people') ? people : everybody);
    }

    if (path.includes('/insights')) {
      return respondWith(200, RECORD);
    }

    if (path.endsWith('/settings')) {
      return respondWith(200, {
        displayName: site.displayName,
        timeZoneId: site.timeZoneId,
        captureClicks: true,
        sitemapAddress: sitemap.address,
      });
    }

    return respondWith(200, [site]);
  });
}

/** The card a heading names, once it is on the screen. */
async function card(name: string): Promise<HTMLElement> {
  const heading = await screen.findByRole('heading', { name });
  const holding = heading.closest('section');

  if (holding === null) {
    throw new Error(`${name} is not on a card.`);
  }

  return holding;
}

/** The newest posts, a row to each, once they are on the screen. */
async function latestRows(): Promise<HTMLElement[]> {
  const latest = await card('Latest posts');

  return within(await within(latest).findByRole('list')).getAllByRole('listitem');
}

describe('the posts on the insights screen', () => {
  it('counts the posts the sitemap lists among the highlights', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const highlights = await screen.findByRole('region', { name: 'All-time highlights' });
    const count = await within(highlights).findByText('42');

    expect(count.parentElement).toHaveTextContent('Posts');
    expect(count.parentElement).toHaveTextContent('Posts your sitemap lists.');
  });

  /** A post is a page on the website, not a visit to it, so it is the same number either way. */
  it('counts the same posts on a screen kept to people', async () => {
    engineWith({});

    renderScreen(<Insights />, { searchParams: 'only=people' });

    const highlights = await screen.findByRole('region', { name: 'All-time highlights' });

    expect(await within(highlights).findByText('42')).toBeInTheDocument();
  });

  it('lists the ten newest posts, each with how much it was read', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const rows = await latestRows();

    expect(rows).toHaveLength(10);
    expect(rows[0]).toHaveTextContent('Post number 1');
    expect(rows[0]).toHaveTextContent('Nov 7, 2026');
    expect(rows[0]).toHaveTextContent('10 views');
    expect(rows[9]).toHaveTextContent('Post number 10');
    expect(rows[9]).toHaveTextContent('100 views');
  });

  it('opens a post in a tab of its own, passing nothing on about where it was opened from', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const [first] = await latestRows();
    const link = within(first as HTMLElement).getByRole('link', {
      name: 'Post number 1 (opens in a new tab)',
    });

    expect(link).toHaveAttribute('href', 'https://example.com/post-1/');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')?.split(' ')).toEqual(
      expect.arrayContaining(['noopener', 'noreferrer']),
    );
  });

  it('writes a title in its own direction, and the address it is read at left to right', async () => {
    engineWith({ everybody: { ...POSTS, latest: [post(1, { title: 'مرحبا بالعالم' })] } });

    renderScreen(<Insights />);

    const [first] = await latestRows();

    expect(within(first as HTMLElement).getByText('مرحبا بالعالم')).toHaveAttribute('dir', 'auto');
    expect(within(first as HTMLElement).getByText('/post-1/')).toHaveAttribute('dir', 'ltr');
  });

  it('names a post by its address where its page gave no title', async () => {
    engineWith({ everybody: { ...POSTS, latest: [post(1, { title: null })] } });

    renderScreen(<Insights />);

    const [first] = await latestRows();
    const link = within(first as HTMLElement).getByRole('link');

    expect(link).toHaveAccessibleName('/post-1/ (opens in a new tab)');
    expect(within(link).getByText('/post-1/')).toHaveAttribute('dir', 'ltr');
  });

  /** An address from somebody's sitemap that would run something on this screen is never a link. */
  it.each([
    'javascript:alert(document.cookie)',
    'data:text/html,<script>alert(1)</script>',
    '/post-1/',
  ])('leaves a post whose address is %j as text', async (address) => {
    engineWith({
      everybody: { ...POSTS, latest: [post(1, { address })], mostRead: post(1, { address }) },
    });

    renderScreen(<Insights />);

    const [first] = await latestRows();

    expect(within(first as HTMLElement).getByText('Post number 1')).toBeInTheDocument();
    expect(within(first as HTMLElement).queryByRole('link')).not.toBeInTheDocument();
    expect(within(await card('Most popular post')).queryByRole('link')).not.toBeInTheDocument();
  });

  it('names the post read most of all, with how much it was read', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const best = await card('Most popular post');

    expect(
      await within(best).findByRole('link', {
        name: 'The one everybody read (opens in a new tab)',
      }),
    ).toHaveAttribute('href', 'https://example.com/post-20/');
    expect(best).toHaveTextContent('Mar 3, 2026');
    expect(best).toHaveTextContent('900 views');
    expect(best).not.toHaveTextContent('%');
  });

  it('says so where none of the posts has been read', async () => {
    engineWith({ everybody: { ...POSTS, mostRead: null } });

    renderScreen(<Insights />);

    expect(await screen.findByText('None of your posts has been read yet.')).toBeInTheDocument();
  });

  it('draws when the website posts over the last twelve months', async () => {
    engineWith({});

    renderScreen(<Insights />);

    const activity = await card('Posting activity');

    expect(within(activity).getAllByRole('grid')).toHaveLength(12);
    expect(activity).toHaveTextContent('3 posts on 2 days in the last 12 months.');
  });

  it('asks for the views of people on a screen kept to people', async () => {
    const engine = engineWith({ people: { ...POSTS, latest: [post(1, { pageViews: 3 })] } });

    renderScreen(<Insights />, { searchParams: 'only=people' });

    const [first] = await latestRows();

    expect(first).toHaveTextContent('3 views');
    expect(engine.all().some(({ path }) => path.endsWith('/insights/posts?only=people'))).toBe(
      true,
    );
  });

  it('asks nothing about posts a sitemap does not list', async () => {
    const engine = engineWith({ sitemap: { ...READ, posts: 0, dated: 0 } });

    renderScreen(<Insights />);

    await screen.findByRole('heading', { name: 'No posts found' });

    expect(engine.all().some(({ path }) => path.includes('/insights/posts'))).toBe(false);
  });

  it('says since when views are counted, where that is later than the record begins', async () => {
    engineWith({ everybody: { ...POSTS, viewsFrom: '2026-08' } });

    renderScreen(<Insights />);

    expect(await screen.findByText('Views since August 2026.')).toBeInTheDocument();
  });

  it('says nothing of it where views are counted from the record’s first month', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await latestRows();

    expect(screen.queryByText(/Views since/)).not.toBeInTheDocument();
  });

  it('says under the newest and the calendar how many posts have no publishing date', async () => {
    engineWith({});

    renderScreen(<Insights />);

    await latestRows();

    expect(
      screen.getAllByText("2 posts have no publishing dates, so they aren't shown here."),
    ).toHaveLength(2);
  });

  it('puts one card in place of the newest and the calendar where no post is dated', async () => {
    engineWith({ everybody: { ...POSTS, dated: 0, latest: [], calendar: [] } });

    renderScreen(<Insights />);

    expect(
      await screen.findByRole('heading', { name: 'None of your posts says when it was published' }),
    ).toBeInTheDocument();
    expect(await card('Most popular post')).toHaveTextContent('The one everybody read');
    expect(screen.queryByRole('heading', { name: 'Latest posts' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Posting activity' })).not.toBeInTheDocument();
  });
});

/** A page says whether it is a post, and when it was published, only once it has been read. */
describe('the posts on the insights screen while their pages are read', () => {
  it('says they are still being read, rather than that none says when it was published', async () => {
    engineWith({
      sitemap: { ...READ, reading: 12 },
      everybody: { ...POSTS, dated: 0, latest: [], calendar: [] },
    });

    renderScreen(<Insights />);

    expect(await screen.findByRole('heading', { name: 'Reading your posts…' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Your newest posts and how often you post appear here as their pages are read.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText('None of your posts says when it was published'),
    ).not.toBeInTheDocument();
  });

  it('says nothing of posts with no publishing date until every page has been read', async () => {
    engineWith({ sitemap: { ...READ, reading: 12 } });

    renderScreen(<Insights />);

    await latestRows();

    expect(screen.getByText('12 pages still to read.')).toBeInTheDocument();
    expect(screen.queryByText(/no publishing dates/)).not.toBeInTheDocument();
  });

  it('asks for the posts again once every page has been read', async () => {
    let standing: SitemapStatus = { ...READ, reading: 12 };
    const engine = engineWith({ sitemap: () => standing });
    const asked = () => engine.all().filter(({ path }) => path.endsWith('/insights/posts'));

    const { cache } = renderScreen(<Insights />);

    await latestRows();

    expect(asked()).toHaveLength(1);

    standing = { ...READ, reading: 0 };

    await act(async () => {
      await cache.invalidateQueries({ queryKey: sitemapStatusKey(SITE.id), exact: true });
    });

    await waitFor(() => expect(asked()).toHaveLength(2));
    expect(await latestRows()).toHaveLength(10);
  });
});

describe('the posts on the insights screen before any can be shown', () => {
  it.each(['owner', 'editor'] as const)(
    'asks the %s to add the website’s sitemap, and opens the box for it',
    async (role) => {
      engineWith({
        site: { ...SITE, role },
        sitemap: { ...READ, address: null, state: 'none', posts: 0, dated: 0 },
      });

      renderScreen(<Insights />);

      expect(
        await screen.findByRole('heading', { name: 'See your posts here' }),
      ).toBeInTheDocument();
      expect(
        screen.getByText(
          "Add your website's sitemap and your posts appear here: how many there are, the newest, and how much each is read.",
        ),
      ).toBeInTheDocument();
      expect(screen.getByText('No sitemap yet')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Add your sitemap' }));

      const box = await within(await screen.findByRole('dialog')).findByRole('textbox', {
        name: 'Sitemap address',
      });

      await waitFor(() => expect(box).toHaveFocus());
    },
  );

  it('tells somebody who may only read the numbers who can add one, and offers nothing to press', async () => {
    engineWith({
      site: { ...SITE, role: 'viewer' },
      sitemap: { ...READ, address: null, state: 'none', posts: 0, dated: 0 },
    });

    renderScreen(<Insights />);

    expect(
      await screen.findByText('Somebody who manages example.com can add its sitemap in Settings.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add your sitemap' })).not.toBeInTheDocument();
  });

  it.each([
    [
      'a sitemap still to be read',
      { state: 'waiting' },
      'Reading your sitemap…',
      "Your posts appear here once they're found.",
    ],
    [
      'pages still to be read',
      { state: 'read', reading: 12 },
      'Reading your posts…',
      '12 pages still to read.',
    ],
    [
      'reading switched off',
      { state: 'off' },
      'Reading sitemaps is switched off on this server',
      'Whoever runs this server can switch it on.',
    ],
  ] as const)('says so while there are no posts yet for %s', async (_, reading, title, body) => {
    engineWith({ sitemap: { ...READ, posts: 0, dated: 0, ...reading } });

    renderScreen(<Insights />);

    expect(await screen.findByRole('heading', { name: title })).toBeInTheDocument();
    expect(screen.getByText(body)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Check the address' })).not.toBeInTheDocument();
  });

  it.each([
    [
      'a sitemap that could not be read',
      { state: 'failed', problem: 'not-found' },
      "We couldn't read your sitemap",
      "There's nothing at that address. Sitemaps usually end in sitemap.xml, sitemap_index.xml or wp-sitemap.xml.",
    ],
    [
      'a sitemap that could not be read, with pages still to read',
      { state: 'failed', problem: 'unreachable', reading: 12 },
      "We couldn't read your sitemap",
      "Your website didn't answer. Check that the address opens in a browser.",
    ],
    [
      'a sitemap read only in part, with pages still to read',
      { state: 'partly-read', problem: 'too-slow', reading: 12 },
      "Part of your sitemap couldn't be read",
      'Your website answered too slowly for all of it to be read. 12 pages still to read.',
    ],
    [
      'a sitemap read to the end with no posts in it',
      { state: 'read' },
      'No posts found',
      'If you have some, check that this is the sitemap that lists them.',
    ],
  ] as const)(
    'says what to do about %s, and opens the box for it',
    async (_, reading, title, body) => {
      engineWith({ sitemap: { ...READ, posts: 0, dated: 0, ...reading } });

      renderScreen(<Insights />);

      expect(await screen.findByRole('heading', { name: title })).toBeInTheDocument();
      expect(screen.getByText(body)).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Check the address' }));

      expect(
        await within(await screen.findByRole('dialog')).findByRole('textbox', {
          name: 'Sitemap address',
        }),
      ).toBeInTheDocument();
    },
  );

  it('says part of the sitemap could not be read, above the posts it did find', async () => {
    engineWith({ sitemap: { ...READ, state: 'partly-read', problem: 'too-slow', reading: 12 } });

    renderScreen(<Insights />);

    expect(
      await screen.findByText(
        "Part of your sitemap couldn't be read, so some posts may be missing. 12 pages still to read.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check the address' })).toBeInTheDocument();
    expect(await latestRows()).toHaveLength(10);
  });

  /** The posts shown are the ones an earlier reading found, so the newest may not be among them. */
  it.each([
    ['an editor', 'editor', 1],
    ['somebody who may only read the numbers', 'viewer', 0],
  ] as const)(
    'tells %s, above the posts found before, that the sitemap could not be read again',
    async (_, role, buttons) => {
      engineWith({
        site: { ...SITE, role },
        sitemap: { ...READ, state: 'failed', problem: 'not-found' },
      });

      renderScreen(<Insights />);

      expect(
        await screen.findByText(
          "We couldn't read your sitemap the last time we tried, so your newest posts may be missing.",
        ),
      ).toBeInTheDocument();
      expect(await latestRows()).toHaveLength(10);
      expect(screen.queryAllByRole('button', { name: 'Check the address' })).toHaveLength(buttons);

      const highlights = screen.getByRole('region', { name: 'All-time highlights' });

      expect(within(highlights).getByText('42').parentElement).toHaveTextContent(
        "Couldn't read your sitemap",
      );
    },
  );
});

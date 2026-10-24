import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { OnUrlUpdateFunction } from 'nuqs/adapters/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Journeys } from '@/components/journeys/journeys';
import { DETAIL_DIMENSIONS } from '@/lib/analytics/journeys';
import { engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';
import messages from '../../../messages/en.json';

/** What the filters are called, read from the catalogue rather than written out again here. */
const FILTERS = messages.journeys.filters;

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

const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';

const GROUPS = [
  { category: 'likely-human', strength: 'moderate', sessions: 6, pageViews: 18 },
  { category: 'suspected-ai-crawler', strength: 'strong', sessions: 3, pageViews: 96 },
  { category: 'security-scanner', strength: 'strong', sessions: 1, pageViews: 6 },
];

const READER = {
  id: 'visit-reader',
  startedAt: '2026-08-17T09:14:00+00:00',
  endedAt: '2026-08-17T09:21:00+00:00',
  pageCount: 3,
  surfaces: ['browser-tracker', 'no-script-pixel'],
  category: 'likely-human',
  strength: 'moderate',
  ruleset: '3.0',
  supporting: [
    { code: 'browser.script_executed', direction: 'toward-human', weight: 35, values: {} },
    {
      code: 'engagement.read_time',
      direction: 'toward-human',
      weight: 60,
      values: { seconds: '212' },
    },
  ],
  contradicting: [
    {
      code: 'retrieval.breadth',
      direction: 'toward-automation',
      weight: 40,
      values: { pageCount: '3' },
    },
  ],
  context: {
    source: 'Google',
    kind: 'search',
    countryCode: 'IN',
    town: 'Jaipur',
    network: 'Reliance Jio Infocomm Limited',
    device: 'phone',
    browser: 'Firefox',
    system: 'Android',
  },
  entryPath: '/posts/hello',
};

const CRAWLER = {
  id: 'visit-crawler',
  startedAt: '2026-08-17T04:02:00+00:00',
  endedAt: '2026-08-17T04:05:00+00:00',
  pageCount: 64,
  surfaces: ['nextjs-middleware', 'aspnetcore-middleware'],
  category: 'suspected-ai-crawler',
  strength: 'strong',
  ruleset: '3.0',
  supporting: [
    {
      code: 'identity.declared_crawler',
      direction: 'toward-automation',
      weight: 80,
      values: { operator: 'OpenAI', token: 'GPTBot', purpose: 'ai-training' },
    },
    { code: 'identity.unverified_claim', direction: 'neutral', weight: 0, values: {} },
  ],
  contradicting: [],
  context: {
    source: '',
    kind: 'direct',
    countryCode: 'US',
    town: '',
    network: 'Amazon Web Services',
    device: 'unknown',
    browser: '',
    system: '',
  },
  entryPath: '/robots.txt',
};

/** A crawler whose operator vouched for the address, seen only by the website's own server. */
const VERIFIED = {
  id: 'visit-verified',
  startedAt: '2026-08-17T06:40:00+00:00',
  endedAt: '2026-08-17T06:41:00+00:00',
  pageCount: 12,
  surfaces: ['wordpress-plugin'],
  category: 'known-search-crawler',
  strength: 'verified',
  ruleset: '9.0',
  supporting: [
    {
      code: 'identity.declared_crawler',
      direction: 'toward-automation',
      weight: 70,
      values: { operator: 'Google', token: 'Googlebot', purpose: 'search-index' },
    },
    {
      code: 'identity.confirmed_crawler',
      direction: 'toward-automation',
      weight: 100,
      values: { operator: 'Google', purpose: 'search-index' },
    },
  ],
  contradicting: [],
  context: {
    source: '',
    kind: 'direct',
    countryCode: '',
    town: '',
    network: '',
    device: 'unknown',
    browser: '',
    system: '',
  },
  entryPath: '/sitemap.xml',
};

/** The pages one opened visit turns out to have gone through. */
const JOURNEY = [
  {
    at: '2026-08-17T09:14:00+00:00',
    path: '/posts/hello',
    statusCode: 200,
    engagedMs: 74_000,
    depthPercent: 82,
    press: null,
  },
];

/** What a visit says about its visitor when nothing about them was established. */
const NOTHING_KNOWN = {
  source: '',
  kind: 'direct',
  countryCode: '',
  town: '',
  network: '',
  device: 'unknown',
  browser: '',
  system: '',
};

/**
 * What the period turns out to have held, which is what the filters offer.
 *
 * Written the way the engine reports it — the empty value is a value, meaning nothing about that
 * visit could be established, and the two vocabularies it names arrive by their own spellings
 * rather than as words anybody would say.
 */
const HELD = {
  from: FROM,
  to: TO,
  devices: [
    { value: 'phone', visits: 6 },
    { value: 'desktop', visits: 3 },
  ],
  sourceKinds: [
    { value: 'search', visits: 5 },
    { value: 'direct', visits: 4 },
  ],
  browsers: [
    { value: 'Firefox', visits: 6 },
    { value: '', visits: 1 },
  ],
  systems: [{ value: 'Android', visits: 6 }],
  countries: [
    { value: 'IN', visits: 6 },
    { value: 'FR', visits: 3 },
  ],
  towns: [{ value: 'Jaipur', visits: 6 }],
  networks: [{ value: 'Reliance Jio Infocomm Limited', visits: 6 }],
  sources: [
    { value: 'Google', visits: 5 },
    { value: '', visits: 4 },
  ],
  entryPages: [
    { value: '/pricing', visits: 6 },
    { value: '/blog/caf%C3%A9', visits: 2 },
    { value: '', visits: 1 },
  ],
};

/**
 * Answers every question the screen asks, in whichever order they arrive.
 *
 * A journey and what a period held are both asked for under the visit list's own address and have
 * to be recognised first, or the list's answer would be handed back for them. What the reader
 * narrowed to is applied here rather than ignored, because narrowing is a question for the engine
 * and a test that let the screen filter its own rows would be testing something the product does
 * not do.
 */
function engineWith(
  groups: readonly unknown[],
  visits: readonly unknown[],
  journeys: { asked: number } = { asked: 0 },
) {
  return engineDoing(async (path) => {
    if (path.includes('/journey')) {
      journeys.asked += 1;

      return respondWith(200, { visit: 'visit-reader', context: NOTHING_KNOWN, steps: JOURNEY });
    }

    if (path.includes('/visits/facets')) {
      return respondWith(200, HELD);
    }

    if (path.includes('/visits')) {
      return respondWith(200, sliceOf(path, visits));
    }

    if (path.includes('/traffic')) {
      return respondWith(200, {
        from: FROM,
        to: TO,
        sessions: groups.length === 0 ? 0 : 10,
        pageViews: 120,
        groups,
      });
    }

    return respondWith(200, [SITE]);
  });
}

/**
 * A period with more visits in it than one page holds.
 *
 * Each is given a different number of pages, which is the one thing a row prints that tells two
 * visits apart on screen, so a test can say which of them it is looking at.
 */
function manyVisits(count: number): readonly unknown[] {
  return Array.from({ length: count }, (_, index) => ({
    ...READER,
    id: `visit-${index + 1}`,
    pageCount: index + 1,
  }));
}

/** Answers the visit list the way the engine does: the page asked for, narrowed as asked. */
function sliceOf(path: string, visits: readonly unknown[]) {
  const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
  const offset = Number(asked.get('offset') ?? 0);
  const limit = Number(asked.get('limit') ?? visits.length);
  const categories = asked.getAll('category');
  const leastPages = Number(asked.get('minPages') ?? 0);

  const matching = (visits as { category: string; pageCount: number }[]).filter(
    (visit) =>
      (categories.length === 0 || categories.includes(visit.category)) &&
      visit.pageCount >= leastPages,
  );

  return {
    from: FROM,
    to: TO,
    totalVisits: matching.length,
    visits: matching.slice(offset, offset + limit),
  };
}

/** Every address the visit list was asked for, in order. */
function listed(sent: readonly Sent[]): string[] {
  return sent.map((one) => one.path).filter((path) => /\/visits\?/.test(path));
}

/** Every time the engine was asked what the period held. */
function askedWhatIsHere(sent: readonly Sent[]): string[] {
  return sent.map((one) => one.path).filter((path) => path.includes('/visits/facets'));
}

/** The values of one narrowing the visit list was last asked for, read back as they were sent. */
function lastAskedFor(sent: readonly Sent[], narrowing: string): string[] {
  const asked = listed(sent).at(-1) ?? '';

  return new URLSearchParams(asked.slice(asked.indexOf('?') + 1)).getAll(narrowing);
}

/**
 * The shut row of one visit, found by its page count, which is the one thing every row says.
 *
 * What the row says before it is opened is the point of the row, so a test about it looks inside
 * the row alone: the same name or town found in the evidence beneath would pass for the wrong
 * reason.
 */
async function rowSaying(pages: string): Promise<HTMLElement> {
  const row = (await screen.findByText(pages)).closest('summary');

  expect(row).not.toBeNull();

  return row as HTMLElement;
}

/** Opens one of the controls that narrows the list by something about the visit. */
async function open(filter: string) {
  await userEvent.click(screen.getByRole('button', { name: filter }));
}

function show(at?: string, watchingAddress?: OnUrlUpdateFunction) {
  return renderScreen(<Journeys />, { searchParams: at, watchingAddress });
}

describe('the user journey screen', () => {
  it('names itself and the website it is about', async () => {
    engineWith(GROUPS, [READER]);

    show();

    expect(await screen.findByRole('heading', { name: 'User journey' })).toBeInTheDocument();
    expect(screen.getByText(/visited My Blog/)).toBeInTheDocument();
  });

  it('lists the newest visits with what generated them and how big they were', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    expect(await screen.findByText('3 pages')).toBeInTheDocument();
    expect(screen.getByText('64 pages')).toBeInTheDocument();
  });

  /**
   * Somebody arrives here from the numbers on the overview, and the two are two questions about
   * the same days. A screen that started again on the usual period would answer a question nobody
   * asked and look like the wrong answer to the one they did.
   */
  it('opens on the period the address names rather than on the usual one', async () => {
    engineWith(GROUPS, [READER]);

    show('?period=yesterday');

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
  });

  it('says nothing has been judged rather than showing an empty list', async () => {
    engineWith([], []);

    show();

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
  });

  it('says something a reader can act on when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });

  /**
   * The totals and the list are both refused by an engine that cannot be reached. Saying so twice
   * reads as two problems, and a list's shape pulsing under the notice promises an answer that is
   * not coming.
   */
  it('says once that the engine cannot be reached, and stops waiting for the list', async () => {
    engineStopped();

    const { container } = show();

    await screen.findByText("Can't reach Dewiride Analytics");

    await waitFor(() => {
      expect(container.querySelectorAll('.animate-pulse')).toHaveLength(0);
    });
    expect(screen.getAllByText("Can't reach Dewiride Analytics")).toHaveLength(1);
  });

  it('opens a visit to show what was seen, in sentences with the figures filled in', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));

    expect(screen.getByText('A real browser ran your tracking code.')).toBeInTheDocument();
    expect(
      screen.getByText('Your pages were open in front of somebody for about 4 minutes.'),
    ).toBeInTheDocument();
  });

  it('keeps the evidence that pointed the other way instead of hiding it', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));

    expect(screen.getByText('Pointing the other way')).toBeInTheDocument();
    expect(screen.getByText('It worked through 3 pages in a single visit.')).toBeInTheDocument();
  });

  it('reads the strongest observation first', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));

    const seen = screen.getByRole('heading', { name: 'What we saw' }).parentElement;
    const sentences = within(seen as HTMLElement)
      .getAllByRole('listitem')
      .map((item) => item.textContent);

    expect(sentences[0]).toContain('about 4 minutes');
  });

  it('says a crawler name is what the visitor claimed rather than who it was', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show();

    await userEvent.click(await screen.findByText('64 pages'));

    expect(
      screen.getByText('It called itself GPTBot, a crawler OpenAI uses to train AI models.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Any visitor can claim that name/)).toBeInTheDocument();
  });

  it('names two reporters that mean the same thing to the reader only once', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show();

    await userEvent.click(await screen.findByText('64 pages'));

    expect(screen.getByText('Seen by your own server')).toBeInTheDocument();
  });

  /**
   * The concrete half of a verdict. A conclusion somebody can check for themselves is worth more
   * than one they have to take on trust, which is the whole reason a visit opens at all.
   */
  it('shows the pages a visit went through once it is opened', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));

    expect(await screen.findByText('/posts/hello')).toBeInTheDocument();
    expect(screen.getByText('1m 14s')).toBeInTheDocument();
  });

  it('asks for no journey until somebody opens a visit', async () => {
    const journeys = { asked: 0 };
    engineWith(GROUPS, [READER, CRAWLER], journeys);

    show();

    await screen.findByText('3 pages');

    expect(journeys.asked).toBe(0);
  });

  /**
   * The name is on the row only once the company behind it has vouched for the address. A name
   * any visitor can claim stays inside the opened row, beside the sentence saying it was claimed.
   */
  it('names a crawler on the row once the name has been confirmed', async () => {
    engineWith(GROUPS, [VERIFIED]);

    show();

    expect(within(await rowSaying('12 pages')).getByText('Googlebot')).toBeInTheDocument();
  });

  it('shows no name for a crawler that only claimed one', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show();

    await screen.findByText('64 pages');

    expect(screen.queryByText('GPTBot')).not.toBeInTheDocument();
  });

  it('says on the row where each visit came from', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    expect(within(await rowSaying('3 pages')).getByText('From Google')).toBeInTheDocument();
    expect(within(await rowSaying('64 pages')).getByText('Came straight here')).toBeInTheDocument();
  });

  /**
   * Which page a visit landed on is said beside what the visit was, on the line a reader scans,
   * in the words of the narrowing that finds the visits that landed there.
   */
  it('says on the first line which page a visit landed on', async () => {
    engineWith(GROUPS, [READER]);

    show();

    const row = within(await rowSaying('3 pages'));

    expect(row.getByText('A person').parentElement).toHaveTextContent('Landed on /posts/hello');
  });

  it('writes the page a visit landed on the way somebody would read it', async () => {
    engineWith(GROUPS, [{ ...READER, entryPath: '/blog/caf%C3%A9' }]);

    show();

    expect(
      within(await rowSaying('3 pages')).getByText('Landed on /blog/café'),
    ).toBeInTheDocument();
  });

  /**
   * The address is written by whoever asked for the page, which includes anybody hoping to make a
   * row say something else. It reaches the screen as text, in the order it was asked for.
   */
  it('shows a page somebody wrote to be something else as nothing but text', async () => {
    engineWith(GROUPS, [
      { ...READER, entryPath: '/%3Cimg%20src=x%20onerror=alert(1)%3E' },
      { ...CRAWLER, entryPath: '/invoice%E2%80%AEfdp.exe' },
    ]);

    show();

    const written = await rowSaying('3 pages');

    expect(
      within(written).getByText('Landed on /<img src=x onerror=alert(1)>'),
    ).toBeInTheDocument();
    expect(written.querySelector('img')).toBeNull();
    expect(
      within(await rowSaying('64 pages')).getByText('Landed on /invoice%E2%80%AEfdp.exe'),
    ).toBeInTheDocument();
  });

  /**
   * A visit whose activity has aged out has no page recorded against it, and says nothing about
   * one rather than naming the front page, which it never opened.
   */
  it('says nothing about where a visit landed where no page was recorded', async () => {
    engineWith(GROUPS, [{ ...VERIFIED, entryPath: '' }]);

    show();

    const row = within(await rowSaying('12 pages'));

    expect(row.getByText('Googlebot')).toBeInTheDocument();
    expect(row.queryByText(/Landed on/)).not.toBeInTheDocument();
  });

  /**
   * A visit whose activity has aged out holds neither the page it landed on nor anything that
   * named a sender. It named nowhere only because nothing of its arrival is left to name anything,
   * so it is not said to have come straight here — on the row or in the panel opened from it.
   */
  it('says nothing about how a visit arrived where nothing of its arrival is on record', async () => {
    engineWith(GROUPS, [{ ...VERIFIED, entryPath: '' }]);

    show();

    const summary = await rowSaying('12 pages');

    expect(within(summary).queryByText('Came straight here')).not.toBeInTheDocument();

    await userEvent.click(screen.getByText('12 pages'));

    // The filter above the list is also called "Came from", so the panel is asked on its own.
    const panel = within(summary.closest('details') as HTMLElement);

    await panel.findByText('Nothing in this visit said where it was or what it was read on.');

    expect(panel.queryByText('Came from')).not.toBeInTheDocument();
    expect(panel.queryByText('Came straight here')).not.toBeInTheDocument();
  });

  /** The first line is the row's heading, so it stays when the row is opened. */
  it('keeps the page a visit landed on while the row is open', async () => {
    engineWith(GROUPS, [READER]);

    show();

    const row = within(await rowSaying('3 pages'));

    await userEvent.click(screen.getByText('3 pages'));

    expect(row.queryByText('Jaipur, India')).not.toBeInTheDocument();
    expect(row.getByText('Landed on /posts/hello')).toBeInTheDocument();
  });

  it('says roughly where a visit was and what it was read on, without opening it', async () => {
    engineWith(GROUPS, [READER]);

    show();

    const row = within(await rowSaying('3 pages'));

    expect(row.getByText('Jaipur, India')).toBeInTheDocument();
    expect(row.getByText('Via Reliance Jio Infocomm Limited')).toBeInTheDocument();
    expect(row.getByText('Firefox on Android')).toBeInTheDocument();
  });

  /**
   * The row's line is only for a shut row. Opening it lays the same facts out in full an inch
   * below, and a screen that says "Jaipur, India" twice in two lines reads as a mistake.
   */
  it('puts the facts away while the row is open and brings them back when it is shut', async () => {
    engineWith(GROUPS, [READER]);

    show();

    const row = within(await rowSaying('3 pages'));

    expect(row.getByText('Jaipur, India')).toBeInTheDocument();

    await userEvent.click(screen.getByText('3 pages'));

    expect(row.queryByText('Jaipur, India')).not.toBeInTheDocument();

    await userEvent.click(screen.getByText('3 pages'));

    expect(row.getByText('Jaipur, India')).toBeInTheDocument();
  });

  /**
   * A visit nothing placed or named still says how it arrived where the page it landed on is on
   * record, because a visit seen arriving that named nowhere came straight here — and says nothing
   * else rather than a line of absences.
   */
  it('says only that a visit came straight here where nothing else was established', async () => {
    engineWith(GROUPS, [VERIFIED]);

    show();

    const row = within(await rowSaying('12 pages'));

    expect(row.getByText('Came straight here')).toBeInTheDocument();
    expect(row.queryByText(/Via /)).not.toBeInTheDocument();
    expect(row.queryByText('Not known')).not.toBeInTheDocument();
  });

  /**
   * Each licence asks for a link back wherever its results appear. The panel that offers the
   * countries and networks a period held is one such place and the foot of a list naming them is
   * another, so each is credited twice on a screen showing both.
   */
  it('credits the data behind the rows', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');

    expect(screen.getAllByRole('link', { name: 'DB-IP' })).toHaveLength(2);
    expect(screen.getAllByRole('link', { name: 'iptoasn.com' })).toHaveLength(2);
  });

  it('credits nothing under a list nothing was placed in', async () => {
    engineWith(GROUPS, [VERIFIED]);

    show();

    await screen.findByText('12 pages');

    expect(screen.getAllByRole('link', { name: 'DB-IP' })).toHaveLength(1);
    expect(screen.getAllByRole('link', { name: 'iptoasn.com' })).toHaveLength(1);
  });

  it('shows nothing the engine calls by a name of its own', async () => {
    engineWith(GROUPS, [READER, CRAWLER, VERIFIED]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));
    await userEvent.click(await screen.findByText('64 pages'));
    await userEvent.click(await screen.findByText('12 pages'));

    const shown = document.body.textContent ?? '';

    for (const spelling of [
      'suspected-ai-crawler',
      'likely-human',
      'security-scanner',
      'known-search-crawler',
      'identity.declared_crawler',
      'identity.confirmed_crawler',
      'search-index',
      'browser.script_executed',
      'toward-automation',
      'nextjs-middleware',
      'browser-tracker',
    ]) {
      expect(shown).not.toContain(spelling);
    }
  });
});

describe('working through a long list', () => {
  it('reaches any page of the list rather than only the next one', async () => {
    engineWith(GROUPS, manyVisits(80));

    show();

    expect(await screen.findByText('1–25 of 80')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Page 4' }));

    expect(await screen.findByText('76–80 of 80')).toBeInTheDocument();
    expect(screen.getByText('80 pages')).toBeInTheDocument();
  });

  it('marks the page being looked at', async () => {
    engineWith(GROUPS, manyVisits(80));

    show();

    await screen.findByText('1–25 of 80');

    expect(screen.getByRole('button', { name: 'Page 1' })).toHaveAttribute('aria-current', 'page');
  });

  it('steps one page at a time as well', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    await screen.findByText('1–25 of 30');

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
  });

  it('offers no way back from the first page', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    await screen.findByText('1–25 of 30');

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
  });

  /**
   * The count says what the period holds, not what one page of it does. A list reporting the
   * length of its own slice would tell somebody with a thousand visits that they had twenty-five.
   */
  it('says how many visits the period holds rather than how many are on screen', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument();
  });

  it('shows no page numbers for a list that already fits', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    await screen.findByText('3 pages');

    expect(screen.queryByRole('button', { name: 'Page 1' })).not.toBeInTheDocument();
  });

  /**
   * Turning three pages of results into one is the fastest way to stop paging altogether, so the
   * choice is offered even where there is only one page.
   */
  it('lets somebody decide how many to show at once, and starts them again at the top', async () => {
    const engine = engineWith(GROUPS, manyVisits(80));

    show();

    await screen.findByText('1–25 of 80');
    await userEvent.click(screen.getByRole('button', { name: 'Page 3' }));
    await screen.findByText('51–75 of 80');

    await userEvent.selectOptions(screen.getByLabelText('Show'), '50');

    expect(await screen.findByText('1–50 of 80')).toBeInTheDocument();
    expect(listed(engine.all()).at(-1)).toContain('limit=50');
  });

  /**
   * A new period is a new list. Left where they were, somebody would land on page three of a list
   * that may now be one page long, which is a screen with nothing on it.
   */
  it('starts the list again at the top when the period changes', async () => {
    const engine = engineWith(GROUPS, manyVisits(80));

    show();

    await screen.findByText('1–25 of 80');
    await userEvent.click(screen.getByRole('button', { name: 'Page 3' }));
    await screen.findByText('51–75 of 80');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Period' }), 'yesterday');

    expect(await screen.findByText('1–25 of 80')).toBeInTheDocument();
    expect(listed(engine.all()).at(-1)).toContain('offset=0');
  });
});

describe('narrowing the list down', () => {
  it('offers the conclusions this period actually reached, with how many there were', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    expect(await screen.findByRole('button', { name: /A person 6/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Says it's an AI crawler 3/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Fake traffic/ })).not.toBeInTheDocument();
  });

  it('asks the engine for the narrowed list rather than filtering what came back', async () => {
    const engine = engineWith(GROUPS, [READER, CRAWLER]);

    show();

    await userEvent.click(await screen.findByRole('button', { name: /A person 6/ }));

    await screen.findByText('3 pages');

    expect(listed(engine.all()).at(-1)).toContain('category=likely-human');
    expect(screen.queryByText('64 pages')).not.toBeInTheDocument();
  });

  it('takes a conclusion off again when it is pressed a second time', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    const person = await screen.findByRole('button', { name: /A person 6/ });

    await userEvent.click(person);
    expect(person).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(person);
    expect(person).toHaveAttribute('aria-pressed', 'false');
    expect(await screen.findByText('64 pages')).toBeInTheDocument();
  });

  it('asks for a floor under the evidence rather than one band of it', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('How sure we are');
    await userEvent.click(await screen.findByRole('radio', { name: 'Some signs or stronger' }));

    await waitFor(() => expect(listed(engine.all()).at(-1)).toContain('strength=moderate'));
  });

  it('asks for visits that reached a page', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Pages read');
    await userEvent.click(await screen.findByRole('radio', { name: 'More than one' }));

    await waitFor(() => expect(listed(engine.all()).at(-1)).toContain('minPages=2'));
  });

  /**
   * A period narrowed to nothing is not a website with no traffic, and saying so would be telling
   * somebody their measurement had stopped working.
   */
  it('says why the list is empty and offers the one press that fills it again', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show();

    await userEvent.click(await screen.findByRole('button', { name: /A person 6/ }));

    expect(await screen.findByText('No visits like that')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show every visit' }));

    expect(await screen.findByText('64 pages')).toBeInTheDocument();
  });

  it('puts somebody back at the start of the list when they narrow it', async () => {
    engineWith(GROUPS, manyVisits(80));

    show();

    await screen.findByText('1–25 of 80');
    await userEvent.click(screen.getByRole('button', { name: 'Page 3' }));
    await screen.findByText('51–75 of 80');

    await open('Pages read');
    await userEvent.click(await screen.findByRole('radio', { name: 'One or more' }));

    expect(await screen.findByText(/^1–25 of/)).toBeInTheDocument();
  });

  it('offers nothing to clear until something has been narrowed', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');

    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /A person 6/ }));

    expect(await screen.findByRole('button', { name: 'Clear' })).toBeInTheDocument();
  });

  /**
   * The figures beside every choice count the whole period rather than what is left of it once
   * something has been narrowed away. Two conclusions are alternatives rather than conditions
   * piled up, so picking one leaves every figure in that control still true of what pressing the
   * next one would give.
   */
  it('keeps the figures while one control is the only thing narrowing the list', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await userEvent.click(await screen.findByRole('button', { name: /A person 6/ }));

    expect(
      await screen.findByRole('button', { name: /Says it's an AI crawler 3/ }),
    ).toBeInTheDocument();
  });
});

describe('narrowing by what a visit actually was', () => {
  /**
   * Working out what a period held means reading its whole activity a second time, which is the
   * same work a narrowed list pays for. Most people open this screen to read the list, so it is
   * asked for when somebody reaches for one of these controls and not before.
   */
  it('asks what the period held only once somebody reaches for it', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');

    expect(askedWhatIsHere(engine.all())).toHaveLength(0);

    await open('Country');

    expect(await screen.findByRole('checkbox', { name: 'India 6' })).toBeInTheDocument();
    expect(askedWhatIsHere(engine.all())).toHaveLength(1);
  });

  it('offers only what this period held, with how many visits held it', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');

    expect(await screen.findByRole('checkbox', { name: 'India 6' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'France 3' })).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Germany/ })).not.toBeInTheDocument();
  });

  /** A stored code is not a place. Nobody asks to see the visits from IN. */
  it('writes a country out in the reader’s own language rather than as its code', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');

    await screen.findByRole('checkbox', { name: 'India 6' });

    expect(document.body.textContent ?? '').not.toContain('IN 6');
  });

  it('names a kind of device in words rather than in the engine’s own spelling', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Device');

    expect(await screen.findByRole('checkbox', { name: 'Phones 6' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Computers 3' })).toBeInTheDocument();
    expect(document.body.textContent ?? '').not.toContain('desktop');
  });

  it('asks the engine to narrow by something about the visit itself', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));

    await waitFor(() => expect(listed(engine.all()).at(-1)).toContain('country=IN'));
  });

  /**
   * A visit nothing could be established about is a real answer and a common one — an install
   * behind a proxy that passes no address on resolves nothing at all — so it is a choice of its
   * own rather than a blank row, and asking for it is a different question from asking for
   * everything.
   */
  it('offers the visits nothing could be established about as a choice of its own', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Browser');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Not known 1' }));

    await waitFor(() => expect(listed(engine.all()).at(-1)).toMatch(/[?&]browser=(&|$)/));
  });

  /**
   * A page is offered the way every row writes it, and asked for exactly as it was recorded,
   * because the recorded value is what the engine narrows by.
   */
  it('offers the pages visits landed on as somebody would read them, and asks for them as recorded', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Landed on');
    await userEvent.click(await screen.findByRole('checkbox', { name: '/blog/café 2' }));

    await waitFor(() =>
      expect(lastAskedFor(engine.all(), 'entryPage')).toEqual(['/blog/caf%C3%A9']),
    );

    const pill = await screen.findByRole('button', { name: 'Remove Landed on /blog/café' });

    expect(within(pill).getByText('/blog/café')).toHaveAttribute('title', '/blog/café');
  });

  /**
   * An address reads left to right even where a part of it is written right to left. Left to set
   * its own direction, a page whose first part is in Arabic would be drawn back to front in the
   * list of choices and in the pill it leaves, and read differently from the row it found. Every
   * other value keeps setting its own.
   */
  it('draws the pages visits landed on left to right, in the choices and in the pill', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Landed on');

    const choice = await screen.findByRole('checkbox', { name: '/blog/café 2' });
    const label = choice.closest('label') as HTMLElement;

    expect(within(label).getByText('/blog/café')).toHaveAttribute('dir', 'ltr');

    await userEvent.click(choice);

    const pill = await screen.findByRole('button', { name: 'Remove Landed on /blog/café' });

    expect(within(pill).getByText('/blog/café')).toHaveAttribute('dir', 'ltr');
  });

  it('leaves every value that is not an address to set its own direction', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Came from');

    const choice = await screen.findByRole('checkbox', { name: 'Google 5' });
    const label = choice.closest('label') as HTMLElement;

    expect(within(label).getByText('Google')).not.toHaveAttribute('dir');
  });

  /**
   * The visits with no page recorded are named by a sentence of the catalogue, not by an address,
   * so that choice and its pill run the way the reader's language does.
   */
  it('leaves the choice of visits with no page recorded to run as its words do', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Landed on');

    const choice = await screen.findByRole('checkbox', { name: 'No page recorded 1' });
    const label = choice.closest('label') as HTMLElement;

    expect(within(label).getByText('No page recorded')).not.toHaveAttribute('dir');

    await userEvent.click(choice);

    const pill = await screen.findByRole('button', { name: 'Remove Landed on No page recorded' });

    expect(within(pill).getByText('No page recorded')).not.toHaveAttribute('dir');
  });

  /**
   * A visit with no page recorded is a choice of its own, called what it is. Written as "/", it
   * would offer the visits that never opened a page as visits to the front page.
   */
  it('offers the visits with no page recorded by name rather than as the front page', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Landed on');

    expect(await screen.findByRole('checkbox', { name: 'No page recorded 1' })).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: '/ 1' })).not.toBeInTheDocument();
  });

  /**
   * A view arrived at through several menus has to be undoable without going back through them,
   * which is what the row underneath the controls is for.
   */
  it('shows everything picked, and takes one off again in a single press', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));

    await userEvent.click(await screen.findByRole('button', { name: 'Remove Country India' }));

    expect(screen.queryByRole('button', { name: 'Remove Country India' })).not.toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument(),
    );
  });

  it('says on the control itself how much of it is being asked for', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'France 3' }));

    expect(await screen.findByRole('button', { name: 'Country 2 chosen' })).toBeInTheDocument();
  });

  /**
   * Once something else is narrowing the list, a figure counted over the whole period is true of
   * the period and false of what is on screen underneath it, so it goes.
   */
  it('drops the figures once something else is narrowing the list', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));

    // The conclusions lose theirs, because a country now stands between them and the list.
    expect(await screen.findByRole('button', { name: 'A person' })).toBeInTheDocument();

    // The countries keep theirs, because picking a second one is an alternative to the first
    // rather than a condition on top of it.
    expect(screen.getByRole('checkbox', { name: 'France 3' })).toBeInTheDocument();
  });

  /**
   * Driven off the list of things a visit can be narrowed by rather than off a list written out
   * here, so a tenth added to the model and forgotten on the screen fails rather than shipping as
   * a narrowing nothing offers. It is the counterpart of the guard on the engine's own side.
   */
  it.each([...DETAIL_DIMENSIONS])('offers a control for narrowing by %s', async (dimension) => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');

    expect(screen.getByRole('button', { name: FILTERS.of[dimension] })).toBeInTheDocument();
  });

  it('offers the two floors beside them', async () => {
    engineWith(GROUPS, [READER]);

    show();

    await screen.findByText('3 pages');

    expect(screen.getByRole('button', { name: FILTERS.strength })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: FILTERS.pages })).toBeInTheDocument();
  });
});

/**
 * The point of the whole panel. Somebody who has worked a period down to the visits they were
 * looking for has answered a question, and the answer is worth keeping: it survives a reload, it
 * can be bookmarked, and it can be sent to somebody who then sees what the sender saw.
 */
describe('a narrowed list as a link', () => {
  it('opens narrowed to what the address names rather than on the whole period', async () => {
    const engine = engineWith(GROUPS, [READER, CRAWLER]);

    show('?category=likely-human');

    expect(await screen.findByText('3 pages')).toBeInTheDocument();
    expect(screen.queryByText('64 pages')).not.toBeInTheDocument();
    expect(listed(engine.all()).at(-1)).toContain('category=likely-human');
  });

  it('carries the days and the narrowing in the one link', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show('?period=yesterday&category=likely-human');

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
    expect(listed(engine.all()).at(-1)).toContain('category=likely-human');
  });

  /**
   * Somebody who arrives on a link did not narrow anything themselves, so the screen has to show
   * them what it was narrowed to — otherwise they are looking at a short list with no way of
   * telling why, and nothing to press to see the rest.
   */
  it('shows what a link narrowed to, and offers to undo it', async () => {
    engineWith(GROUPS, [READER]);

    show('?country=IN&strength=moderate');

    expect(await screen.findByRole('button', { name: 'Remove Country India' })).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Remove How sure we are Some signs or stronger' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument();
  });

  it('puts what a reader narrows to into the address as they narrow it', async () => {
    const written = vi.fn();
    engineWith(GROUPS, [READER]);

    show(undefined, written);

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls.at(-1)?.[0].queryString).toContain('country=IN');
  });

  /**
   * An address is typed, edited and forwarded by people. Every one of those has to leave somebody
   * on a working screen rather than on a refusal, because there is nothing they could do about it
   * from where they are standing.
   */
  it('opens the whole period when a link names something that is not a narrowing', async () => {
    const engine = engineWith(GROUPS, [READER, CRAWLER]);

    show('?category=marvellous&strength=verified');

    expect(await screen.findByText('64 pages')).toBeInTheDocument();
    expect(listed(engine.all()).at(-1)).not.toContain('category=');
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
  });
});

describe('the list kept to people', () => {
  /**
   * Kept to people, the list is asked for by their verdict over whatever else was narrowed to. A
   * conclusion the address carried in is set aside rather than combined with, because a screen
   * kept to people has already answered what generated the visits.
   */
  it('asks for the people alone while the screen is kept to them, whatever the address says', async () => {
    const engine = engineWith(GROUPS, [READER, CRAWLER]);

    show('?only=people&category=security-scanner');

    expect(await screen.findByText('3 pages')).toBeInTheDocument();

    const asked = listed(engine.all()).at(-1) ?? '';

    expect(asked).toContain('category=likely-human');
    expect(asked).not.toContain('security-scanner');
  });

  it('forgets a conclusion the address named while the screen is kept to people', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show('?only=people&category=security-scanner');

    await screen.findByText('3 pages');

    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
    expect(screen.queryByText('No visits like that')).not.toBeInTheDocument();
  });

  it('offers no conclusions to pick while the screen is kept to people', async () => {
    engineWith(GROUPS, [READER]);

    show('?only=people');

    await screen.findByText('3 pages');

    expect(screen.queryByRole('button', { name: /A person 6/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Country' })).toBeInTheDocument();
  });

  it('still narrows the people by something about the visit', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show('?only=people');

    await screen.findByText('3 pages');
    await open('Country');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'India 6' }));

    await waitFor(() => {
      const asked = listed(engine.all()).at(-1) ?? '';

      expect(asked).toContain('category=likely-human');
      expect(asked).toContain('country=IN');
    });
  });

  /**
   * The screen was kept to people on purpose, so the way out of a list they are absent from is
   * the choice that kept it.
   */
  it('offers everyone back when none of the people match', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show('?only=people');

    expect(await screen.findByText('No visits like that')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show every visit' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show everyone' }));

    expect(await screen.findByText('64 pages')).toBeInTheDocument();
  });

  it('offers both ways out when the people were narrowed to nothing', async () => {
    engineWith(GROUPS, [CRAWLER]);

    show('?only=people&country=FR');

    expect(await screen.findByText('No visits like that')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show every visit' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show everyone' })).toBeInTheDocument();
  });

  /**
   * Nothing judged yet and no people matching are different facts, and a website whose verdicts
   * are still coming has not been found to have no readers.
   */
  it('says nothing has been judged rather than that no people match', async () => {
    engineWith([], []);

    show('?only=people');

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.queryByText('No visits like that')).not.toBeInTheDocument();
  });

  it('says whose visits it lists', async () => {
    engineWith(GROUPS, [READER]);

    show('?only=people');

    expect(
      await screen.findByText(/The visits to My Blog judged to be people, newest first/),
    ).toBeInTheDocument();
  });

  it('starts the list again at the top when the population changes', async () => {
    const engine = engineWith(GROUPS, manyVisits(80));

    show();

    await screen.findByText('1–25 of 80');
    await userEvent.click(screen.getByRole('button', { name: 'Page 3' }));
    await screen.findByText('51–75 of 80');

    await userEvent.click(screen.getByRole('button', { name: /People only/ }));

    expect(await screen.findByText(/^1–25 of/)).toBeInTheDocument();
    expect(listed(engine.all()).at(-1)).toContain('offset=0');
  });

  it('carries the people in the link it opened from', async () => {
    engineWith(GROUPS, [READER]);

    show('?period=yesterday&only=people');

    expect(await screen.findByRole('combobox', { name: 'Period' })).toHaveValue('yesterday');
    expect(screen.getByRole('button', { name: /People only/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });
});

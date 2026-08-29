import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SiteLive } from '@/components/live/site-live';
import type { Live, LiveVisitor, Site } from '@/lib/api/schemas';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

/**
 * A screen that asks the same question over and over.
 *
 * Almost everything worth testing here happens between one answer and the next, so the beat is
 * driven rather than waited for: the clock is a fake one that still lets the library's own waiting
 * work, and each test says what the engine answers on each beat.
 */

/** Stands in for the drawing surface, which has no canvas to draw on in this document. */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

/** How often the screen asks again, which the tests advance the clock past. */
const BEAT = 10_000;

const AT = '2026-08-29T10:30:00.000Z';
const FROM = '2026-08-29T10:00:00.000Z';

function visitor(overrides: Partial<LiveVisitor> = {}): LiveVisitor {
  return {
    visitor: 'a1',
    firstSeen: '2026-08-29T10:20:00.000Z',
    lastSeen: '2026-08-29T10:29:40.000Z',
    pageCount: 3,
    currentPath: '/writing/how-we-measure',
    category: null,
    strength: null,
    ruleset: null,
    supporting: [],
    contradicting: [],
    operator: '',
    network: 0,
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
    ...overrides,
  };
}

function reading(visitors: readonly LiveVisitor[], overrides: Partial<Live> = {}): Live {
  return {
    at: AT,
    from: FROM,
    visitorsSeen: visitors.length,
    visitors: [...visitors],
    minutes: [],
    pages: [],
    ...overrides,
  };
}

/** What the engine answers, one entry per beat, staying on the last once they run out. */
function engineReading(answers: readonly (Live | 'refused')[]) {
  let beat = 0;

  return engineDoing(async (path) => {
    if (path.includes('/trail')) {
      return respondWith(200, { visitor: 'a1', at: AT, steps: [] });
    }

    const answer = answers[Math.min(beat, answers.length - 1)];

    beat += 1;

    return answer === 'refused'
      ? respondWith(500, { title: 'The engine is having trouble.' })
      : respondWith(200, answer);
  });
}

/** How many times the screen has asked what is happening now. */
function readings(engine: ReturnType<typeof engineDoing>): number {
  return engine.all().filter((sent) => !sent.path.includes('/trail')).length;
}

/** How many times it has asked what one visitor has been doing. */
function trails(engine: ReturnType<typeof engineDoing>): number {
  return engine.all().filter((sent) => sent.path.includes('/trail')).length;
}

beforeEach(() => {
  // The library's own waiting still runs in real time; only the screen's beat is under the test's
  // control. Without that the two fight and every wait in the file becomes a timeout.
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function press() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
}

/**
 * The card listing everybody.
 *
 * A category is named twice on this screen — once in the summary above and once on the row it
 * belongs to — so a test asking about a row has to say which of the two it means.
 */
function list(): HTMLElement {
  return screen.getByRole('heading', { name: "Who's here" }).closest('section') as HTMLElement;
}

/** The card carrying the figure and what can be named among the visitors behind it. */
function headline(): HTMLElement {
  return screen.getByRole('heading', { name: 'Visitors' }).closest('section') as HTMLElement;
}

/** Lets the screen's next beat fall, and waits for whatever it brings. */
async function beat(times = 1) {
  await vi.advanceTimersByTimeAsync(BEAT * times);
}

describe('who is here now', () => {
  it('counts everybody the half hour holds', async () => {
    engineReading([reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2' })])]);

    renderScreen(<SiteLive site={SITE} />);

    const count = await screen.findByText('Visitors');

    expect(count.parentElement).toHaveTextContent('2');
  });

  /**
   * The figure is the whole reason somebody opened the screen, so it is the one thing on it that
   * announces itself to anybody reading rather than looking — and it announces the label with it,
   * so what is read out is "Visitors, two" rather than a bare number.
   */
  it('announces the figure with the word for what it counts', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    const label = await screen.findByText('Visitors');
    const region = label.parentElement;

    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveAttribute('aria-atomic', 'true');
  });

  it('is the only part of the screen that announces itself', async () => {
    engineReading([reading([visitor()])]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    expect(container.querySelectorAll('[aria-live]')).toHaveLength(1);
  });

  it('names what it can and counts the rest', async () => {
    engineReading([
      reading([
        visitor({ visitor: 'a1', category: 'known-search-crawler', strength: 'verified' }),
        visitor({ visitor: 'a2' }),
        visitor({ visitor: 'a3' }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('2 visitors are still being watched')).toBeInTheDocument();
    expect(screen.getByText('Judged once their visit finishes.')).toBeInTheDocument();
  });

  /**
   * A visitor is named only where the naming rests on something they cannot take back. Everybody
   * else carries a marker saying so, rather than a category borrowed to fill the column.
   */
  it('says a visitor is still being watched rather than guessing at one', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Still watching')).toBeInTheDocument();
    expect(screen.queryByText('Too little to go on')).not.toBeInTheDocument();
  });

  it('says what a visitor is where that has been settled', async () => {
    engineReading([reading([visitor({ category: 'security-scanner', strength: 'strong' })])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByRole('heading', { name: "Who's here" });

    expect(within(list()).getByText('Probing for a way in')).toBeInTheDocument();
    expect(within(list()).getByText('strong signs')).toBeInTheDocument();
  });

  /**
   * The same category is named twice on purpose: once in the summary, which is what somebody
   * glances at, and once on the row it belongs to, which is what they open.
   */
  it('names a category in the summary as well as on the row', async () => {
    engineReading([reading([visitor({ category: 'security-scanner', strength: 'strong' })])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByRole('heading', { name: 'Visitors' });

    expect(within(headline()).getByText('Probing for a way in')).toBeInTheDocument();
  });

  it('describes a page somebody is on now in the present, and one they left in the past', async () => {
    engineReading([
      reading([
        visitor({ visitor: 'a1', lastSeen: '2026-08-29T10:29:00.000Z', currentPath: '/now' }),
        visitor({ visitor: 'a2', lastSeen: '2026-08-29T10:08:00.000Z', currentPath: '/then' }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Reading /now')).toBeInTheDocument();
    expect(screen.getByText('Last on /then')).toBeInTheDocument();
  });

  it('says how long ago each of them was last heard from', async () => {
    engineReading([
      reading([
        visitor({ visitor: 'a1', lastSeen: '2026-08-29T10:29:50.000Z' }),
        visitor({ visitor: 'a2', lastSeen: '2026-08-29T10:26:00.000Z' }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Just now')).toBeInTheDocument();
    expect(screen.getByText('4 min ago')).toBeInTheDocument();
  });

  it('admits when the list is shorter than the count beside it', async () => {
    engineReading([
      reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2', currentPath: '/other' })], {
        visitorsSeen: 214,
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Showing the 2 most recently active.')).toBeInTheDocument();
  });

  it('says nothing about a list that carries everybody', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    expect(screen.queryByText(/most recently active are listed/)).not.toBeInTheDocument();
  });

  /**
   * The engine puts the most recently active first. The browser draws that order and never sorts
   * again: a list rearranging itself under somebody's hand every ten seconds would be unreadable
   * however correct each rearrangement was.
   */
  it('never moves a visitor past another one', async () => {
    engineReading([
      reading([
        visitor({ visitor: 'a1', currentPath: '/first' }),
        visitor({ visitor: 'a2', currentPath: '/second' }),
        visitor({ visitor: 'a3', currentPath: '/third' }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /first');

    const listed = [...list().querySelectorAll('details > summary bdi')].map(
      (row) => row.textContent,
    );

    expect(listed).toEqual(['Reading /first', 'Reading /second', 'Reading /third']);
  });
});

describe('what the half hour looked like', () => {
  /** One reading feeds four cards, and each of them reads a different part of it. */
  it('draws the minutes and lists the pages from the same reading the rows came from', async () => {
    engineReading([
      reading([visitor()], {
        minutes: [
          { start: '2026-08-29T10:28:00.000Z', pageViews: 4 },
          { start: '2026-08-29T10:29:00.000Z', pageViews: 6 },
          { start: '2026-08-29T10:30:00.000Z', pageViews: 1 },
        ],
        pages: [{ path: '/writing/how-we-measure', pageViews: 9, visitors: 5 }],
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(
      await screen.findByRole('img', {
        name: 'Pages read on My Blog each minute over the last half hour.',
      }),
    ).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: "What's being read" })).toBeInTheDocument();
    expect(screen.getByText('5 visitors')).toBeInTheDocument();
  });

  it('says so plainly when the half hour has nothing in it to draw', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    expect(
      await screen.findByText('Nothing has been read in the last half hour.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Once somebody opens a page, it appears here.')).toBeInTheDocument();
  });
});

describe('a half hour with nobody in it', () => {
  it('says so, and offers the one thing that would change it', async () => {
    engineReading([reading([])]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Nobody is here right now')).toBeInTheDocument();
    expect(
      screen.getByText('The moment somebody opens a page on example.com, they appear here.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: "See today's traffic" })).toBeInTheDocument();
  });

  it('says nobody has done anything lately when everybody has moved on', async () => {
    engineReading([reading([visitor({ lastSeen: '2026-08-29T10:09:00.000Z' })])]);

    renderScreen(<SiteLive site={SITE} />);

    expect(
      await screen.findByText('Nobody has done anything in the last few minutes.'),
    ).toBeInTheDocument();
  });
});

describe('asking again', () => {
  it('asks again on its own while somebody is watching', async () => {
    const engine = engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    const asked = readings(engine);

    await beat(2);

    await waitFor(() => expect(readings(engine)).toBeGreaterThan(asked));
  });

  it('stops asking when the reader holds the screen still, and starts again when they let go', async () => {
    const engine = engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await press().click(await screen.findByRole('button', { name: 'Pause' }));

    const held = readings(engine);

    await beat(3);

    expect(readings(engine)).toBe(held);

    await press().click(screen.getByRole('button', { name: 'Resume' }));
    await beat(2);

    await waitFor(() => expect(readings(engine)).toBeGreaterThan(held));
  });

  it('says which moment the screen is holding while it is held', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await press().click(await screen.findByRole('button', { name: 'Pause' }));

    expect(screen.getByText(/^Paused at 11:30:00/)).toBeInTheDocument();
  });

  /**
   * A red panel appearing and vanishing every ten seconds is worse than the fault it reports, so a
   * refused beat leaves the last reading exactly where it was and says so in one quiet line.
   */
  it('keeps the last reading on screen when a later one is refused, and says so quietly', async () => {
    engineReading([reading([visitor({ currentPath: '/still-here' })]), 'refused']);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /still-here');

    await beat(2);

    expect(await screen.findByText(/^Out of touch — showing 11:30:00/)).toBeInTheDocument();
    expect(screen.getByText('Reading /still-here')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('fills the screen with the reason when the very first reading is refused', async () => {
    engineReading(['refused']);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('Visitors')).not.toBeInTheDocument();
  });
});

describe('a visitor somebody has opened', () => {
  it('asks for nothing until the row is opened', async () => {
    const engine = engineReading([
      reading([
        visitor({ visitor: 'a1', currentPath: '/first' }),
        visitor({ visitor: 'a2', currentPath: '/second' }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /first');

    expect(trails(engine)).toBe(0);

    await press().click(screen.getByText('Reading /first'));

    await waitFor(() => expect(trails(engine)).toBe(1));
  });

  /**
   * A row vanishing from under a reader part-way through it is the one failure this screen can
   * actually cause. An opened row outlives its visitor and says what happened to them.
   */
  it('keeps a visitor on screen after they have gone, and says they have', async () => {
    engineReading([
      reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2', currentPath: '/other' })]),
      reading([visitor({ visitor: 'a2', currentPath: '/other' })]),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(container.querySelector('details summary')!);

    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();
    expect(screen.getByText('Last on /writing/how-we-measure')).toBeInTheDocument();
  });

  it('lets a visitor who has gone go once the reader closes their row', async () => {
    engineReading([
      reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2', currentPath: '/other' })]),
      reading([visitor({ visitor: 'a2', currentPath: '/other' })]),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    const opened = container.querySelector('details summary')!;

    await press().click(opened);
    await beat(2);

    await screen.findByText('Gone');

    await press().click(container.querySelector('details summary')!);
    await beat(2);

    await waitFor(() =>
      expect(screen.queryByText('Reading /writing/how-we-measure')).not.toBeInTheDocument(),
    );
  });

  it('shows what the verdict rested on where there is one', async () => {
    engineReading([
      reading([
        visitor({
          category: 'known-search-crawler',
          strength: 'verified',
          supporting: [
            {
              code: 'identity.confirmed_crawler',
              direction: 'toward-automation',
              weight: 90,
              values: { operator: 'Google' },
            },
          ],
        }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByRole('heading', { name: "Who's here" });

    await press().click(within(list()).getByText('A search engine'));

    expect(await screen.findByRole('heading', { name: 'What we saw' })).toBeInTheDocument();
  });

  it('shows no evidence for a visitor nothing has been said about', async () => {
    engineReading([reading([visitor()])]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Still watching');

    await press().click(container.querySelector('details summary')!);

    await screen.findByRole('heading', { name: 'What happened in this visit' });

    expect(screen.queryByRole('heading', { name: 'What we saw' })).not.toBeInTheDocument();
  });
});

describe('crediting the data behind a row', () => {
  it('links back where a visitor has been placed', async () => {
    engineReading([
      reading([visitor({ context: { ...visitor().context, countryCode: 'GB', town: 'Leeds' } })]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Still watching');

    expect(screen.getAllByRole('link', { name: 'DB-IP' }).length).toBeGreaterThan(0);
  });

  it('links back where a network has been named', async () => {
    engineReading([
      reading([visitor({ context: { ...visitor().context, network: 'Example Networks' } })]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Still watching');

    expect(screen.getByRole('link', { name: 'iptoasn.com' })).toBeInTheDocument();
  });

  it('credits neither where nothing was placed and no network named', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Still watching');

    expect(screen.queryByRole('link', { name: 'DB-IP' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'iptoasn.com' })).not.toBeInTheDocument();
  });
});

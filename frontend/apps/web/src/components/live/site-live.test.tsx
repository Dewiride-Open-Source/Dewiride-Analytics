import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SiteLive } from '@/components/live/site-live';
import type { Live, LiveArrivals, LiveVisitor, Site } from '@/lib/api/schemas';
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
    seenArriving: true,
    ...overrides,
  };
}

/** Nobody sent by any kind of site. */
const NOBODY_SENT: LiveArrivals = { search: 0, assistant: 0, social: 0, link: 0 };

/**
 * A reading carrying the visitors given, counting how each of them arrived as an answer carrying
 * everybody would. A test of a reading cut short says how many each kind of site sent in all.
 */
function reading(visitors: readonly LiveVisitor[], overrides: Partial<Live> = {}): Live {
  const arrivals = { ...NOBODY_SENT };

  for (const one of visitors) {
    if (one.context.kind !== 'direct') {
      arrivals[one.context.kind] += 1;
    }
  }

  return {
    at: AT,
    from: FROM,
    visitorsSeen: visitors.length,
    arrivals,
    visitors: [...visitors],
    minutes: [],
    pages: [],
    ...overrides,
  };
}

/**
 * What the engine answers, one entry per beat, staying on the last once they run out.
 *
 * A trail is answered over whatever instant it was asked about, as the engine answers it, so a
 * test can read back which reading a row was asked under.
 */
function engineReading(answers: readonly (Live | 'refused')[]) {
  let beat = 0;

  return engineDoing(async (path) => {
    if (path.includes('/trail')) {
      return respondWith(200, { visitor: 'a1', at: instantAsked(path) ?? AT, steps: [] });
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

/** Every time it has asked what one visitor has been doing, in the order it asked. */
function trailsAsked(engine: ReturnType<typeof engineDoing>): string[] {
  return engine
    .all()
    .map((sent) => sent.path)
    .filter((path) => path.includes('/trail'));
}

/** How many times it has asked what one visitor has been doing. */
function trails(engine: ReturnType<typeof engineDoing>): number {
  return trailsAsked(engine).length;
}

/** The instant a trail was asked over, read back from the question. */
function instantAsked(path: string): string | null {
  return new URLSearchParams(path.slice(path.indexOf('?') + 1)).get('at');
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

/**
 * The sentence at the foot of the list, set apart from the same words said once to anybody listening
 * when what the list is kept to changes.
 */
function footSaying(words: string): HTMLElement {
  const said = within(list())
    .getAllByText(words)
    .filter((one) => one.getAttribute('role') !== 'status');

  expect(said).toHaveLength(1);

  return said[0] as HTMLElement;
}

/** The card carrying the figure and what can be named among the visitors behind it. */
function headline(): HTMLElement {
  return screen.getByRole('heading', { name: 'Visitors' }).closest('section') as HTMLElement;
}

/** The row of the visitor reading one page: the part of it on the list, open or shut. */
function rowOf(path: string): HTMLElement {
  return screen.getByText(`Reading ${path}`).closest('summary') as HTMLElement;
}

/**
 * The first line of that row: the marker an unnamed visitor wears, and everything said beside it.
 *
 * A row says several things on several lines, and what the first line says is what a reader
 * scanning the list reads — so a test about the first line looks at that line alone.
 */
function firstLineOf(path: string): HTMLElement {
  return within(rowOf(path)).getByText('Still watching').parentElement as HTMLElement;
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

  it('counts every page read in the half hour on the same card, the minute running now among them', async () => {
    engineReading([
      reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2' })], {
        minutes: [
          { start: '2026-08-29T10:28:00.000Z', pageViews: 1_200, sentBy: NOBODY_SENT },
          { start: '2026-08-29T10:29:00.000Z', pageViews: 0, sentBy: NOBODY_SENT },
          { start: '2026-08-29T10:30:00.000Z', pageViews: 7, sentBy: NOBODY_SENT },
        ],
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    const views = within(headline()).getByText('Page views');

    expect(views.parentElement).toHaveTextContent('1,207');
    expect(views.closest('[aria-live]')).toBeNull();
  });

  it('counts no pages read where the half hour holds none', async () => {
    engineReading([reading([visitor()])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    expect(within(headline()).getByText('Page views').parentElement).toHaveTextContent(
      /^Page views0$/,
    );
  });

  /**
   * Neither the chips nor the counts on them speak up: a busy half hour would read itself aloud. The
   * list says what it shows only once what it is kept to changes, so on a reading it says nothing.
   */
  it('is the only part of the screen that announces itself on a reading', async () => {
    engineReading([
      reading([
        visitor({ visitor: 'a1', category: 'security-scanner', strength: 'strong' }),
        visitor({
          visitor: 'a2',
          currentPath: '/searched',
          context: { ...visitor().context, source: 'Google', kind: 'search' },
        }),
      ]),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Visitors');

    expect(within(headline()).getAllByRole('button')).toHaveLength(5);
    expect(container.querySelectorAll('[aria-live]')).toHaveLength(1);
    expect(within(list()).getByRole('status')).toBeEmptyDOMElement();
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

    expect(
      within(headline()).getByRole('button', { name: 'Probing for a way in 1' }),
    ).toBeInTheDocument();
    expect(within(list()).getByText('Probing for a way in')).toBeInTheDocument();
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

    expect(within(list()).queryByText(/most recently active/)).not.toBeInTheDocument();
  });

  /**
   * The list is cut short at a hundred and the count is not. The visitors it had no room for are
   * still being watched, and the headline has to count them from the whole rather than the list —
   * a sweep of two hundred would otherwise be reported as one still being watched.
   */
  it('counts everybody the list could not carry among those still being watched', async () => {
    engineReading([
      reading(
        [
          visitor({ visitor: 'a1', category: 'security-scanner', strength: 'strong' }),
          visitor({ visitor: 'a2', currentPath: '/other' }),
        ],
        { visitorsSeen: 214 },
      ),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('213 visitors are still being watched')).toBeInTheDocument();
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

    const listed = within(list())
      .getAllByText(/^Reading /)
      .map((row) => row.textContent);

    expect(listed).toEqual(['Reading /first', 'Reading /second', 'Reading /third']);
  });
});

describe('what the half hour looked like', () => {
  /** One reading feeds four cards, and each of them reads a different part of it. */
  it('draws the minutes and lists the pages from the same reading the rows came from', async () => {
    engineReading([
      reading([visitor()], {
        minutes: [
          { start: '2026-08-29T10:28:00.000Z', pageViews: 4, sentBy: NOBODY_SENT },
          { start: '2026-08-29T10:29:00.000Z', pageViews: 6, sentBy: NOBODY_SENT },
          { start: '2026-08-29T10:30:00.000Z', pageViews: 1, sentBy: NOBODY_SENT },
        ],
        pages: [{ path: '/writing/how-we-measure', visitors: 1 }],
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    expect(
      await screen.findByRole('img', {
        name: 'Pages read on My Blog each minute over the last half hour.',
      }),
    ).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: "What's being read" })).toBeInTheDocument();
    expect(screen.getByText('1 visitor')).toBeInTheDocument();
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

  /**
   * The count is the whole and the list is what fitted, so it is the count that decides whether
   * the half hour is empty: a reading counting three and carrying nobody is not an empty one.
   */
  it('says nobody is here only when the count says so', async () => {
    engineReading([reading([]), reading([], { visitorsSeen: 3 })]);

    renderScreen(<SiteLive site={SITE} />);

    expect(await screen.findByText('Nobody is here right now')).toBeInTheDocument();

    await beat(2);

    const count = await screen.findByText('Visitors');

    expect(count.parentElement).toHaveTextContent('3');
    expect(screen.queryByText('Nobody is here right now')).not.toBeInTheDocument();
  });

  /**
   * A row somebody is holding open outlives the reading that emptied the half hour. The screen
   * stays, with the row saying its visitor has gone, rather than closing over the reader.
   */
  it('keeps the screen for a row somebody is holding open after everybody has gone', async () => {
    engineReading([reading([visitor()]), reading([])]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(container.querySelector('details summary')!);

    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();
    expect(screen.queryByText('Nobody is here right now')).not.toBeInTheDocument();
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
   * The pages counted on the row and the pages listed under it have to be one reading of one
   * window, so the trail is asked over the instant of the reading the row was drawn from.
   */
  it('asks for the trail over the minutes of the reading it sits under', async () => {
    const engine = engineReading([reading([visitor()])]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(container.querySelector('details summary')!);

    await waitFor(() => expect(trails(engine)).toBe(1));

    expect(trailsAsked(engine)[0]).toContain(`at=${encodeURIComponent(AT)}`);
  });

  /**
   * The trail has no beat of its own. Each reading's instant renames the question, so a row still
   * here is asked about under every new reading and never in between.
   */
  it('asks again with each new reading, and not on a clock of its own', async () => {
    const later = '2026-08-29T10:30:10.000Z';
    const renewed = engineReading([reading([visitor()]), reading([visitor()], { at: later })]);

    const first = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(first.container.querySelector('details summary')!);

    await waitFor(() => expect(trails(renewed)).toBe(1));

    await beat(2);

    await waitFor(() => expect(trails(renewed)).toBe(2));
    expect(instantAsked(trailsAsked(renewed)[1] ?? '')).toBe(later);

    first.unmount();

    const unchanged = engineReading([reading([visitor()])]);

    const second = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(second.container.querySelector('details summary')!);

    await waitFor(() => expect(trails(unchanged)).toBe(1));

    await beat(3);

    expect(trails(unchanged)).toBe(1);
  });

  /**
   * A row held after its visitor has gone keeps the instant it was last seen in, so its trail is
   * never asked for again and stays exactly as it stood.
   */
  it("keeps a held row's trail as it was once the visitor has gone", async () => {
    const engine = engineReading([
      reading([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2', currentPath: '/other' })]),
      reading([visitor({ visitor: 'a2', currentPath: '/other' })], {
        at: '2026-08-29T10:30:10.000Z',
      }),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(container.querySelector('details summary')!);

    await waitFor(() => expect(trails(engine)).toBe(1));

    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();
    expect(trails(engine)).toBe(1);
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

    await press().click(within(list()).getByText("A search engine's crawler"));

    expect(await screen.findByRole('heading', { name: 'What we saw' })).toBeInTheDocument();
  });

  /**
   * A crawler whose owner vouched for the address is named on the row, by the name people know
   * it by, while it is still crawling — the same way the journeys list names it once the visit
   * has finished.
   */
  it('names a crawler on the row once its owner has vouched for it', async () => {
    engineReading([
      reading([
        visitor({
          category: 'known-search-crawler',
          strength: 'verified',
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
              weight: 90,
              values: { operator: 'Google', purpose: 'search-index' },
            },
          ],
        }),
      ]),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByRole('heading', { name: "Who's here" });

    expect(
      within(container.querySelector('details summary')!).getByText('Googlebot'),
    ).toBeInTheDocument();
  });

  /**
   * How somebody arrived is said once, beside what they were judged to be, so nobody has to open a
   * row to learn it: the site that sent them, that nothing did, or — where nothing did and the half
   * hour began after they arrived — that they arrived earlier, which is all it knows.
   */
  it('says on the first line where a visitor came from', async () => {
    engineReading([
      reading([
        visitor({
          visitor: 'a1',
          currentPath: '/searched',
          context: { ...visitor().context, source: 'Google', kind: 'search' },
        }),
        visitor({ visitor: 'a2', currentPath: '/typed' }),
        visitor({
          visitor: 'a3',
          currentPath: '/linked',
          context: { ...visitor().context, source: 'forum.example.net', kind: 'link' },
        }),
        visitor({ visitor: 'a4', currentPath: '/already-reading', seenArriving: false }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    expect(firstLineOf('/searched')).toHaveTextContent('From Google');
    expect(within(rowOf('/searched')).getAllByText(/Google/)).toHaveLength(1);
    expect(firstLineOf('/typed')).toHaveTextContent('Came straight here');
    expect(firstLineOf('/linked')).toHaveTextContent('From forum.example.net');
    expect(firstLineOf('/already-reading')).toHaveTextContent('Arrived earlier');
  });

  /**
   * The first line is the row's heading, so it stays when the row is opened. The panel under it
   * names the site on its own, under a label that already says where from.
   */
  it('keeps saying where a visitor came from once their row is open', async () => {
    engineReading([
      reading([
        visitor({
          context: {
            ...visitor().context,
            source: 'Google',
            kind: 'search',
            countryCode: 'GB',
            town: 'Leeds',
          },
        }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(rowOf('/writing/how-we-measure'));

    // The line under the page is put away once the row is open, and the first line is not.
    await waitFor(() =>
      expect(
        within(rowOf('/writing/how-we-measure')).queryByText('Leeds, United Kingdom'),
      ).not.toBeInTheDocument(),
    );

    const opened = rowOf('/writing/how-we-measure').closest('details') as HTMLElement;

    expect(opened).toHaveAttribute('open');
    expect(firstLineOf('/writing/how-we-measure')).toHaveTextContent('From Google');
    expect(within(opened).getByText('Came from')).toBeInTheDocument();
    expect(within(opened).getByText('Google')).toBeInTheDocument();
  });

  /**
   * Arriving earlier is no answer to where from, so the panel under such a row leaves "Came from"
   * out like any other fact nothing answered, and goes on to say what was established.
   */
  it('leaves where a visitor came from out of the panel when nobody saw them arrive', async () => {
    engineReading([
      reading([
        visitor({
          seenArriving: false,
          context: { ...visitor().context, browser: 'Chrome', system: 'Android' },
        }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(rowOf('/writing/how-we-measure'));

    await waitFor(() =>
      expect(
        within(rowOf('/writing/how-we-measure')).queryByText('Chrome on Android'),
      ).not.toBeInTheDocument(),
    );

    const opened = rowOf('/writing/how-we-measure').closest('details') as HTMLElement;

    expect(opened).toHaveAttribute('open');
    expect(firstLineOf('/writing/how-we-measure')).toHaveTextContent('Arrived earlier');
    expect(within(opened).queryByText('Came from')).not.toBeInTheDocument();
    expect(within(opened).getByText('Read on')).toBeInTheDocument();
    expect(within(opened).getByText('Chrome on Android')).toBeInTheDocument();
  });

  /**
   * Where nobody saw the visitor arrive and nothing else was established either, the panel says so
   * in one sentence, with no list of nothing above it.
   */
  it('says once that nothing was observed about a visitor nobody saw arrive', async () => {
    engineReading([reading([visitor({ seenArriving: false })])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    await press().click(rowOf('/writing/how-we-measure'));

    const opened = rowOf('/writing/how-we-measure').closest('details') as HTMLElement;

    expect(
      await within(opened).findByText(
        'Nothing in this visit said where it was or what it was read on.',
      ),
    ).toBeInTheDocument();
    expect(opened.querySelector('dl')).toBeEmptyDOMElement();
  });

  /**
   * Under the page, a live row says roughly where and on what, and leaves whose network they came
   * over to the panel under it: a row still being watched is read for the two facts that tell a
   * person from a rented server at a glance.
   */
  it('says where a visitor roughly is and what they are reading on, but not whose network', async () => {
    engineReading([
      reading([
        visitor({
          context: {
            source: 'Google',
            kind: 'search',
            countryCode: 'GB',
            town: 'Leeds',
            network: 'Example Networks',
            device: 'phone',
            browser: 'Chrome',
            system: 'Android',
          },
        }),
      ]),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /writing/how-we-measure');

    const row = within(rowOf('/writing/how-we-measure'));

    expect(row.getByText('Leeds, United Kingdom')).toBeInTheDocument();
    expect(row.getByText('Chrome on Android')).toBeInTheDocument();
    expect(row.queryByText(/Example Networks/)).not.toBeInTheDocument();
  });

  it('names nothing on the row of a visitor that only claimed a name', async () => {
    engineReading([
      reading([
        visitor({
          category: 'suspected-ai-crawler',
          strength: 'moderate',
          supporting: [
            {
              code: 'identity.declared_crawler',
              direction: 'toward-automation',
              weight: 70,
              values: { operator: 'OpenAI', token: 'GPTBot', purpose: 'ai-training' },
            },
            { code: 'identity.unverified_claim', direction: 'neutral', weight: 0, values: {} },
          ],
        }),
      ]),
    ]);

    const { container } = renderScreen(<SiteLive site={SITE} />);

    await screen.findByRole('heading', { name: "Who's here" });

    expect(
      within(container.querySelector('details summary')!).queryByText('GPTBot'),
    ).not.toBeInTheDocument();
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

describe('keeping the list to one kind of visitor', () => {
  /** Sent by a search engine. */
  const SEARCHED = visitor({
    visitor: 'a1',
    currentPath: '/searched',
    context: { ...visitor().context, source: 'Google', kind: 'search' },
  });

  /** Nothing named a sender for them. */
  const TYPED = visitor({ visitor: 'a2', currentPath: '/typed' });

  /** Recognised as probing for a way in, and sent by a link on another site. */
  const SCANNING = visitor({
    visitor: 'a3',
    currentPath: '/.env',
    category: 'security-scanner',
    strength: 'strong',
    context: { ...visitor().context, source: 'forum.example.net', kind: 'link' },
  });

  /** Recognised as a search engine's crawler. */
  const CRAWLING = visitor({
    visitor: 'a4',
    currentPath: '/sitemap.xml',
    category: 'known-search-crawler',
    strength: 'verified',
  });

  /** One chip on the card at the top, found by everything it says. */
  function chip(name: string): HTMLElement {
    return within(headline()).getByRole('button', { name });
  }

  /** The pill beside the heading of the list, which shows everybody again. */
  function pill(choice: string): HTMLElement {
    return within(list()).getByRole('button', { name: `Show everyone, not only ${choice}` });
  }

  /** The page line of every row on the list, in the order the rows are drawn. */
  function listed(): string[] {
    return within(list())
      .queryAllByText(/^(Reading|Last on) \//)
      .map((row) => row.textContent ?? '');
  }

  /**
   * The figure on each chip counts everybody each kind of site sent, as the visitor count counts
   * everybody: a list cut short at a hundred would otherwise make a sweep from one place read as a
   * trickle.
   */
  it('counts how visitors arrived over the whole half hour rather than the rows listed', async () => {
    engineReading([
      reading([SEARCHED, TYPED], {
        visitorsSeen: 214,
        arrivals: { search: 120, assistant: 3, social: 0, link: 7 },
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    const arrived = await screen.findByRole('group', { name: 'How they arrived' });

    expect(
      within(arrived)
        .getAllByRole('button')
        .map((one) => one.textContent),
    ).toEqual([
      'From a search engine 120',
      'From an AI assistant 3',
      'From a social network 0',
      'From a link on another site 7',
    ]);
  });

  it('keeps the list to the visitors a search engine sent once its chip is pressed', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));

    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'true');
    expect(listed()).toEqual(['Reading /searched']);
    expect(pill('From a search engine')).toHaveTextContent('Only From a search engine');
  });

  it('keeps the list to one recognised kind of visitor once its chip is pressed', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /.env');

    await press().click(chip('Probing for a way in 1'));

    expect(chip('Probing for a way in 1')).toHaveAttribute('aria-pressed', 'true');
    expect(listed()).toEqual(['Reading /.env']);
    expect(pill('Probing for a way in')).toBeInTheDocument();
  });

  it('keeps the list to one chip at a time', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));
    await press().click(chip('Probing for a way in 1'));

    expect(chip('Probing for a way in 1')).toHaveAttribute('aria-pressed', 'true');
    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'false');
    expect(listed()).toEqual(['Reading /.env']);
  });

  it('shows everybody again when the pressed chip is pressed again', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));
    await press().click(chip('From a search engine 1'));

    expect(listed()).toEqual(['Reading /searched', 'Reading /typed', 'Reading /.env']);
    expect(
      within(list()).queryByRole('button', { name: /^Show everyone/ }),
    ).not.toBeInTheDocument();
  });

  /** The pill goes the moment it is pressed, so the keyboard is handed on rather than dropped. */
  it('shows everybody again from the pill, and hands the keyboard to the heading of the list', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));
    await press().click(pill('From a search engine'));

    expect(listed()).toEqual(['Reading /searched', 'Reading /typed', 'Reading /.env']);
    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('heading', { name: "Who's here" })).toHaveFocus();
  });

  it('keeps the minutes to the visitors the chip names, and the figures and pages to everybody', async () => {
    engineReading([
      reading([SEARCHED, TYPED, SCANNING], {
        minutes: [
          {
            start: '2026-08-29T10:29:00.000Z',
            pageViews: 6,
            sentBy: { ...NOBODY_SENT, search: 2 },
          },
          {
            start: '2026-08-29T10:30:00.000Z',
            pageViews: 3,
            sentBy: { ...NOBODY_SENT, search: 1 },
          },
        ],
        pages: [
          { path: '/searched', visitors: 1 },
          { path: '/typed', visitors: 1 },
          { path: '/.env', visitors: 1 },
        ],
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));

    const pages = screen
      .getByRole('heading', { name: "What's being read" })
      .closest('section') as HTMLElement;

    expect(screen.getByText('Visitors').parentElement).toHaveTextContent('3');
    expect(within(headline()).getByText('Page views').parentElement).toHaveTextContent('9');
    expect(within(pages).getByText('/typed')).toBeInTheDocument();
    expect(within(pages).getByText('/.env')).toBeInTheDocument();
    await press().click(screen.getByText('Show these figures as a table'));

    const minutes = await screen.findByRole('table');

    expect(
      within(minutes)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['2', '1']);
    expect(
      within(minutes).getByText('Pages read by visitors from a search engine'),
    ).toBeInTheDocument();
  });

  it('admits a kept list is the most recently active when there are more of them', async () => {
    engineReading([
      reading(
        [
          SEARCHED,
          visitor({ visitor: 'a5', currentPath: '/searched-too', context: SEARCHED.context }),
          TYPED,
        ],
        { visitorsSeen: 214, arrivals: { ...NOBODY_SENT, search: 40 } },
      ),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 40'));

    expect(listed()).toEqual(['Reading /searched', 'Reading /searched-too']);
    expect(footSaying('Showing the 2 most recently active.')).toBeInTheDocument();
  });

  it('says nobody like that is here when nobody arrived that way', async () => {
    engineReading([reading([SEARCHED, TYPED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a social network 0'));

    expect(listed()).toEqual([]);
    expect(footSaying('Nobody like that is here right now.')).toBeInTheDocument();
  });

  /**
   * A sweep can put every visitor one kind of site sent further back than the hundred most recently
   * active, and saying nobody like that is here would then be false.
   */
  it('says when those visitors are further back than the list reaches', async () => {
    engineReading([
      reading([SEARCHED, TYPED], {
        visitorsSeen: 214,
        arrivals: { ...NOBODY_SENT, search: 1, assistant: 6 },
      }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From an AI assistant 6'));

    expect(listed()).toEqual([]);
    expect(footSaying('None of the 2 most recently active.')).toBeInTheDocument();
    expect(within(list()).queryByText(/^Nobody like that/)).not.toBeInTheDocument();
  });

  it('opens kept to what a link names', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />, { searchParams: '?sourceKind=search' });

    await screen.findByText('Reading /searched');

    expect(listed()).toEqual(['Reading /searched']);
    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'true');
  });

  /**
   * On anything narrower than a wide screen the chips stand three cards above the list, so a press
   * would otherwise change something nobody can see — and leave the keyboard on a chip scrolled off
   * the screen.
   */
  it('brings the list into sight once a chip is pressed far above it, and the keyboard with it', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    const heading = screen.getByRole('heading', { name: "Who's here" });

    vi.spyOn(heading, 'getBoundingClientRect').mockReturnValue({
      top: 1800,
      bottom: 1824,
    } as DOMRect);
    const scrolled = vi.spyOn(heading, 'scrollIntoView');

    await press().click(chip('From a search engine 1'));

    expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
    expect(heading).toHaveFocus();
  });

  it('leaves the page and the keyboard where they are when the list is already in sight', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    const scrolled = vi.spyOn(
      screen.getByRole('heading', { name: "Who's here" }),
      'scrollIntoView',
    );

    await press().click(chip('From a search engine 1'));

    expect(scrolled).not.toHaveBeenCalled();
    expect(chip('From a search engine 1')).toHaveFocus();
  });

  /** Showing everybody again from the chip leaves the reader at the chips they were using. */
  it('moves nothing when the pressed chip is let go', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />, { searchParams: '?sourceKind=search' });

    await screen.findByText('Reading /searched');

    const heading = screen.getByRole('heading', { name: "Who's here" });

    vi.spyOn(heading, 'getBoundingClientRect').mockReturnValue({
      top: 1800,
      bottom: 1824,
    } as DOMRect);
    const scrolled = vi.spyOn(heading, 'scrollIntoView');

    await press().click(chip('From a search engine 1'));

    expect(listed()).toEqual(['Reading /searched', 'Reading /typed', 'Reading /.env']);
    expect(scrolled).not.toHaveBeenCalled();
    expect(chip('From a search engine 1')).toHaveFocus();
  });

  /** One chip at a time is the rule, and an address cannot press two. */
  it('keeps the list to the kind of visitor where a link names a way of arriving as well', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />, {
      searchParams: '?category=security-scanner&sourceKind=search',
    });

    await screen.findByText('Reading /.env');

    expect(listed()).toEqual(['Reading /.env']);
    expect(chip('Probing for a way in 1')).toHaveAttribute('aria-pressed', 'true');
    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'false');
  });

  /** Spelled as the User journey spells it, so the link means one thing on either screen. */
  it('writes the choice into the address over the entry somebody is on, and leaves it plain again', async () => {
    const watching = vi.fn();

    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />, { watchingAddress: watching });

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));

    await waitFor(() =>
      expect(watching).toHaveBeenCalledWith(
        expect.objectContaining({
          queryString: '?sourceKind=search',
          options: expect.objectContaining({ history: 'replace' }),
        }),
      ),
    );

    await press().click(chip('Probing for a way in 1'));

    await waitFor(() =>
      expect(watching.mock.calls.at(-1)?.[0].queryString).toBe('?category=security-scanner'),
    );

    await press().click(chip('Probing for a way in 1'));

    await waitFor(() => expect(watching.mock.calls.at(-1)?.[0].queryString).toBe(''));
  });

  /**
   * An address is written by whoever sent the link. One naming something this screen never keeps
   * the list to — a person among them, since nobody is recognised as one mid-visit — is read as
   * everybody rather than as a list of nobody under a chip at nought.
   */
  it.each([
    '?sourceKind=carrier-pigeon',
    '?sourceKind=direct',
    '?category=likely-human',
    '?category=unknown',
    '?category=marvellous',
  ])('reads %s as everybody', async (address) => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />, { searchParams: address });

    await screen.findByText('Reading /searched');

    const chips = within(headline()).getAllByRole('button');

    expect(listed()).toHaveLength(3);
    expect(chips.map((one) => one.textContent)).toEqual([
      'From a search engine 1',
      'From an AI assistant 0',
      'From a social network 0',
      'From a link on another site 1',
      'Probing for a way in 1',
    ]);
    expect(chips.every((one) => one.getAttribute('aria-pressed') === 'false')).toBe(true);
    expect(
      within(list()).queryByRole('button', { name: /^Show everyone/ }),
    ).not.toBeInTheDocument();
  });

  /**
   * A reading in which the last of them left keeps the pressed chip where it was, at nought, so the
   * way back to everybody stays under the hand that pressed it. Let go, it stays while the keyboard
   * is on it, and goes once the keyboard moves on.
   */
  it('keeps a recognised kind pressed at nought once its last visitor has gone, and under the keyboard once it is let go', async () => {
    engineReading([reading([SCANNING, CRAWLING, TYPED]), reading([CRAWLING, TYPED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /.env');

    await press().click(chip('Probing for a way in 1'));
    await beat(2);

    const atNought = await within(headline()).findByRole('button', {
      name: 'Probing for a way in 0',
    });

    expect(atNought).toHaveAttribute('aria-pressed', 'true');
    expect(footSaying('Nobody like that is here right now.')).toBeInTheDocument();

    await press().click(atNought);

    expect(atNought).toHaveFocus();
    expect(atNought).toHaveAttribute('aria-pressed', 'false');
    expect(listed()).toEqual(['Reading /sitemap.xml', 'Reading /typed']);

    await press().tab();

    expect(
      within(headline()).queryByRole('button', { name: /^Probing for a way in/ }),
    ).not.toBeInTheDocument();
  });

  /** A reading never pulls a chip out from under the keyboard, pressed or not. */
  it('keeps a recognised kind the keyboard is on once its last visitor has gone, until the keyboard moves on', async () => {
    engineReading([reading([SCANNING, TYPED]), reading([TYPED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /.env');

    act(() => chip('Probing for a way in 1').focus());
    await beat(2);

    const atNought = await within(headline()).findByRole('button', {
      name: 'Probing for a way in 0',
    });

    expect(atNought).toHaveFocus();
    expect(atNought).toHaveAttribute('aria-pressed', 'false');

    await press().tab();

    expect(screen.queryByRole('heading', { name: 'Recognised right now' })).not.toBeInTheDocument();
  });

  /**
   * The four ways of arriving are always there, so they go first: beneath the recognised row, which
   * comes and goes with the crawlers in the half hour, they would jump under a reader's pointer.
   */
  it('draws the ways of arriving above what has been recognised', async () => {
    engineReading([reading([SCANNING, SEARCHED])]);

    renderScreen(<SiteLive site={SITE} />);

    const arrived = await screen.findByRole('heading', { name: 'How they arrived' });
    const recognised = screen.getByRole('heading', { name: 'Recognised right now' });

    expect(
      arrived.compareDocumentPosition(recognised) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  /** The chip that shows everybody again stays for as long as the list is kept to something. */
  it('keeps the chips while the list is kept to something, even once nobody is left in the half hour', async () => {
    engineReading([
      reading([SEARCHED, TYPED]),
      reading([], { visitorsSeen: 0, arrivals: NOBODY_SENT }),
    ]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(rowOf('/searched'));
    await press().click(chip('From a search engine 1'));
    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();
    expect(chip('From a search engine 0')).toHaveAttribute('aria-pressed', 'true');
  });

  /**
   * A chip says only that it is pressed, so what the list now shows is said to anybody listening —
   * once, when the choice changes, and never again on a reading.
   */
  it('says what the list now shows once a chip is pressed, and nothing more on a reading', async () => {
    engineReading([reading([SEARCHED, TYPED, SCANNING])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From a search engine 1'));

    const said = within(list()).getByRole('status');

    expect(said).toHaveTextContent('Showing 1 visitor.');

    said.textContent = '';
    await beat(2);

    expect(said).toBeEmptyDOMElement();
  });

  it('says when nobody like that is here, and when everybody is shown again', async () => {
    engineReading([reading([SEARCHED, TYPED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(chip('From an AI assistant 0'));

    expect(within(list()).getByRole('status')).toHaveTextContent(
      'Nobody like that is here right now.',
    );

    await press().click(chip('From an AI assistant 0'));

    expect(within(list()).getByRole('status')).toHaveTextContent('Showing everyone.');
  });

  it('says nothing when it opens kept to what a link names', async () => {
    engineReading([reading([SEARCHED, TYPED])]);

    renderScreen(<SiteLive site={SITE} />, { searchParams: '?sourceKind=search' });

    await screen.findByText('Reading /searched');

    expect(within(list()).getByRole('status')).toBeEmptyDOMElement();
  });

  /** What a list was kept to is a question asked of this visit to the screen, never a habit. */
  it('forgets what the list was kept to once the screen is opened afresh', async () => {
    engineReading([reading([SEARCHED, TYPED])]);

    const first = renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');
    await press().click(chip('From a search engine 1'));
    first.unmount();

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /typed');

    expect(listed()).toEqual(['Reading /searched', 'Reading /typed']);
    expect(chip('From a search engine 1')).toHaveAttribute('aria-pressed', 'false');
  });

  /**
   * A row somebody is holding open after its visitor left is hidden while the list is kept to
   * something it is not, and comes back, still open, once everybody is shown again.
   */
  it('hides a row held open while it is not one of those the list is kept to, and keeps it', async () => {
    engineReading([reading([SEARCHED, TYPED]), reading([SEARCHED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /typed');

    await press().click(rowOf('/typed'));
    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();

    await press().click(chip('From a search engine 1'));

    expect(listed()).toEqual(['Reading /searched']);

    await press().click(chip('From a search engine 1'));

    expect(listed()).toEqual(['Reading /searched', 'Last on /typed']);
    expect(screen.getByText('Last on /typed').closest('details')).toHaveAttribute('open');
  });

  it('keeps a row held open after its visitor left while it is one of those the list is kept to', async () => {
    engineReading([reading([SEARCHED, TYPED]), reading([TYPED])]);

    renderScreen(<SiteLive site={SITE} />);

    await screen.findByText('Reading /searched');

    await press().click(rowOf('/searched'));
    await press().click(chip('From a search engine 1'));
    await beat(2);

    expect(await screen.findByText('Gone')).toBeInTheDocument();
    expect(listed()).toEqual(['Last on /searched']);
    expect(footSaying('Nobody like that is here right now.')).toBeInTheDocument();
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

import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Journeys } from '@/components/journeys/journeys';
import { engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

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
  isProvisional: false,
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
};

const CRAWLER = {
  id: 'visit-crawler',
  startedAt: '2026-08-17T04:02:00+00:00',
  endedAt: '2026-08-17T04:05:00+00:00',
  pageCount: 64,
  surfaces: ['nextjs-middleware', 'aspnetcore-middleware'],
  category: 'suspected-ai-crawler',
  strength: 'strong',
  isProvisional: false,
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
 * Answers every question the screen asks, in whichever order they arrive.
 *
 * A journey is asked for under the same address as the visit list and has to be recognised first,
 * or the list's own answer would be handed back for it. What the reader narrowed to is applied
 * here rather than ignored, because narrowing is a question for the engine and a test that let the
 * screen filter its own rows would be testing something the product does not do.
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

function show() {
  return renderScreen(<Journeys />);
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

  it('shows nothing the engine calls by a name of its own', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    await userEvent.click(await screen.findByText('3 pages'));
    await userEvent.click(await screen.findByText('64 pages'));

    const shown = document.body.textContent ?? '';

    for (const spelling of [
      'suspected-ai-crawler',
      'likely-human',
      'security-scanner',
      'identity.declared_crawler',
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

    await userEvent.selectOptions(
      await screen.findByLabelText('How sure we are'),
      'Some signs or stronger',
    );

    await screen.findByText('3 pages');

    expect(listed(engine.all()).at(-1)).toContain('strength=moderate');
  });

  it('asks for visits that reached a page', async () => {
    const engine = engineWith(GROUPS, [READER]);

    show();

    await userEvent.selectOptions(await screen.findByLabelText('Pages read'), 'More than one');

    await screen.findByText('3 pages');

    expect(listed(engine.all()).at(-1)).toContain('minPages=2');
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

    await userEvent.selectOptions(screen.getByLabelText('Pages read'), 'One or more');

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
});

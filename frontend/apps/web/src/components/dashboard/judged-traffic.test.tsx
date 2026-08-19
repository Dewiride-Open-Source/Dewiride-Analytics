import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JudgedTraffic } from '@/components/dashboard/judged-traffic';
import type { Site } from '@/lib/api/schemas';
import { engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'My Blog',
  timeZoneId: 'Asia/Kolkata',
  role: 'owner',
};

const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

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
  ruleset: '1.0',
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
  ruleset: '1.0',
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

/**
 * Answers every question the section asks, in whichever order they arrive.
 *
 * A journey is asked for under the same address as the visit list and has to be recognised first,
 * or the list's own answer would be handed back for it.
 */
function engineWith(
  groups: readonly unknown[],
  visits: readonly unknown[],
  journeys: { asked: number } = { asked: 0 },
) {
  const sessions = groups.length === 0 ? 0 : 10;

  return engineDoing(async (path) => {
    if (path.includes('/journey')) {
      journeys.asked += 1;

      return respondWith(200, { visit: 'visit-reader', steps: JOURNEY });
    }

    return respondWith(
      200,
      path.includes('/visits')
        ? sliceOf(path, visits)
        : { from: FROM, to: TO, sessions, pageViews: 120, groups },
    );
  });
}

/**
 * A period with more visits in it than one screenful holds.
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

/**
 * Answers the visit list the way the engine does: the slice that was asked for, and the whole
 * period's count beside it rather than the length of the slice.
 */
function sliceOf(path: string, visits: readonly unknown[]) {
  const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
  const offset = Number(asked.get('offset') ?? 0);
  const limit = Number(asked.get('limit') ?? visits.length);

  return {
    from: FROM,
    to: TO,
    totalVisits: visits.length,
    visits: visits.slice(offset, offset + limit),
  };
}

function show() {
  return renderScreen(<JudgedTraffic site={SITE} window={WINDOW} />);
}

describe('the breakdown of who is visiting', () => {
  it('names every group in words rather than in the spelling the engine uses', async () => {
    engineWith(GROUPS, []);

    show();

    expect(await screen.findByText('A person')).toBeInTheDocument();
    expect(screen.getByText("Says it's an AI crawler")).toBeInTheDocument();
    expect(screen.getByText('Probing for a way in')).toBeInTheDocument();
  });

  it('says how many visits each group is, and what share of the whole', async () => {
    engineWith(GROUPS, []);

    show();

    expect(await screen.findByText(/6 visits/)).toBeInTheDocument();
    expect(screen.getByText('60%')).toBeInTheDocument();
    expect(screen.getByText('30%')).toBeInTheDocument();
  });

  it('reports how much weight stood behind a group as words, never as a number', async () => {
    engineWith(GROUPS, []);

    show();

    expect(await screen.findAllByText('strong signs')).toHaveLength(2);
    expect(screen.getByText('some signs')).toBeInTheDocument();
  });

  it('says plainly that a visit still under way has not been counted', async () => {
    engineWith(GROUPS, []);

    show();

    expect(
      await screen.findByText("Visits still under way aren't counted here yet."),
    ).toBeInTheDocument();
  });

  it('explains itself rather than showing an empty box before anything is judged', async () => {
    engineWith([], []);

    show();

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText(/about half an hour after they end/)).toBeInTheDocument();
    expect(screen.queryByText('Visits')).not.toBeInTheDocument();
  });

  it('says something a reader can act on when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('one visit and the case behind it', () => {
  it('lists the newest visits with what generated them and how big they were', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    expect(await screen.findByText('Visits')).toBeInTheDocument();
    expect(screen.getByText('3 pages')).toBeInTheDocument();
    expect(screen.getByText('64 pages')).toBeInTheDocument();
  });

  /**
   * Every visit carries its whole case, so the list is read a screenful at a time. What must not
   * happen is the list quietly stopping: a verdict nobody can reach is a verdict nobody can
   * question, which is the one thing this product exists to allow.
   */
  it('reaches the visits behind the first screenful', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument();
    expect(screen.getByText('1 page')).toBeInTheDocument();
    expect(screen.queryByText('30 pages')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
    expect(screen.getByText('30 pages')).toBeInTheDocument();
    expect(screen.queryByText('1 page')).not.toBeInTheDocument();
  });

  /**
   * The count says what the period holds, not what one screenful does. A list that reported the
   * length of its own slice would tell somebody with a thousand visits that they had twenty-five.
   */
  it('says how many visits the period holds rather than how many are on screen', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    expect(await screen.findByText(/^30 visits in this period/)).toBeInTheDocument();
  });

  it('offers no way back from the first screenful', async () => {
    engineWith(GROUPS, manyVisits(30));

    show();

    await screen.findByText('1–25 of 30');

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
  });

  /**
   * A period that fits on one screen has nothing to move through, and controls that could never do
   * anything are clutter on every quiet website.
   */
  it('shows no way through a list that already fits', async () => {
    engineWith(GROUPS, [READER, CRAWLER]);

    show();

    await screen.findByText('Visits');

    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
  });

  it('opens to show what was seen, in sentences with the figures filled in', async () => {
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

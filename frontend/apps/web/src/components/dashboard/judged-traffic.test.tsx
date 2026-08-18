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

/** Answers both questions the section asks, in whichever order they arrive. */
function engineWith(groups: readonly unknown[], visits: readonly unknown[]) {
  const sessions = groups.length === 0 ? 0 : 10;

  return engineDoing(async (path) =>
    respondWith(
      200,
      path.includes('/visits')
        ? { from: FROM, to: TO, visits }
        : { from: FROM, to: TO, sessions, pageViews: 120, groups },
    ),
  );
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
    expect(screen.queryByText('Recent visits')).not.toBeInTheDocument();
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

    expect(await screen.findByText('Recent visits')).toBeInTheDocument();
    expect(screen.getByText('3 pages')).toBeInTheDocument();
    expect(screen.getByText('64 pages')).toBeInTheDocument();
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

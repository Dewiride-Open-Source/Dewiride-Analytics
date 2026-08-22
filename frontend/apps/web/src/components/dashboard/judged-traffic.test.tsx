import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JudgedTraffic } from '@/components/dashboard/judged-traffic';
import type { Site } from '@/lib/api/schemas';
import { engineAnswering, engineStopped } from '@/test/engine';
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

function engineWith(groups: readonly unknown[]) {
  return engineAnswering(200, {
    from: FROM,
    to: TO,
    sessions: groups.length === 0 ? 0 : 10,
    pageViews: 120,
    groups,
  });
}

function show() {
  return renderScreen(<JudgedTraffic site={SITE} window={WINDOW} />);
}

describe('the breakdown of who is visiting', () => {
  it('names every group in words rather than in the spelling the engine uses', async () => {
    engineWith(GROUPS);

    show();

    expect(await screen.findByText('A person')).toBeInTheDocument();
    expect(screen.getByText("Says it's an AI crawler")).toBeInTheDocument();
    expect(screen.getByText('Probing for a way in')).toBeInTheDocument();
  });

  it('says how many visits each group is, and what share of the whole', async () => {
    engineWith(GROUPS);

    show();

    expect(await screen.findByText(/6 visits/)).toBeInTheDocument();
    expect(screen.getByText('60%')).toBeInTheDocument();
    expect(screen.getByText('30%')).toBeInTheDocument();
  });

  it('reports how much weight stood behind a group as words, never as a number', async () => {
    engineWith(GROUPS);

    show();

    expect(await screen.findAllByText('strong signs')).toHaveLength(2);
    expect(screen.getByText('some signs')).toBeInTheDocument();
  });

  it('says plainly that a visit still under way has not been counted', async () => {
    engineWith(GROUPS);

    show();

    expect(
      await screen.findByText("Visits still under way aren't counted here yet."),
    ).toBeInTheDocument();
  });

  /**
   * The summary is what somebody glances at; the visits behind it are what they work through. The
   * way between the two has to be on the screen, or the second one may as well not exist.
   */
  it('offers the way through to the visits behind the summary', async () => {
    engineWith(GROUPS);

    show();

    expect(await screen.findByRole('link', { name: /Look at each visit/ })).toHaveAttribute(
      'href',
      '/app/journeys',
    );
  });

  it('explains itself rather than showing an empty box before anything is judged', async () => {
    engineWith([]);

    show();

    expect(await screen.findByText('Nothing judged yet')).toBeInTheDocument();
    expect(screen.getByText(/about half an hour after they end/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('says something a reader can act on when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

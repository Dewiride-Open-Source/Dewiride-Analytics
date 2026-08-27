import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JudgedTraffic } from '@/components/dashboard/judged-traffic';
import type { Site } from '@/lib/api/schemas';
import { engineAnswering, engineStopped } from '@/test/engine';
import { PALETTE, ringParts } from '@/test/drawing';
import { renderScreen } from '@/test/harness';

/**
 * Stands in for the drawing surface, so that what the ring would be told to draw can be read as
 * an object rather than looked for among pixels on a canvas.
 */
vi.mock('@/components/charts/chart', async () => ({ ...(await import('@/test/drawing')) }));

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

function show(at?: string) {
  return renderScreen(<JudgedTraffic site={SITE} window={WINDOW} />, { searchParams: at });
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

    // Twice each: once in the summary above the list and once on the row itself, which agree here
    // only because this period happens to hold one category per tone.
    expect(screen.getAllByText('60%')).toHaveLength(2);
    expect(screen.getAllByText('30%')).toHaveLength(2);
  });

  /**
   * Four tones on the ring and fourteen categories in the list, on purpose: the ring settles what
   * somebody glances at the card to settle, and the words below it keep every category exact.
   */
  it('divides the period four ways while the list still names every category', async () => {
    engineWith(GROUPS);

    show();

    await screen.findByText('A person');

    expect(ringParts()).toStrictEqual([
      { name: 'People', value: 6, itemStyle: { color: PALETTE.tones.people } },
      { name: 'Machinery', value: 3, itemStyle: { color: PALETTE.tones.automation } },
      { name: 'Unwanted', value: 1, itemStyle: { color: PALETTE.tones.unwanted } },
    ]);
  });

  it('names each part of the ring and how much of the period it came to', async () => {
    engineWith(GROUPS);

    show();

    expect(await screen.findByText('People')).toBeInTheDocument();
    expect(screen.getByText('Machinery')).toBeInTheDocument();
    expect(screen.getByText('Unwanted')).toBeInTheDocument();
  });

  it('announces what the drawing shows, since a ring tells a screen reader nothing', async () => {
    engineWith(GROUPS);

    show();

    expect(
      await screen.findByRole('img', {
        name: 'How the judged visits divide between people, machinery and everything else.',
      }),
    ).toBeInTheDocument();
  });

  /**
   * Two categories that share a colour never share a name. On the ring they are one arc called
   * what the colour means, and in the list they are two rows called what they are.
   */
  it('never names a company on a ring drawn from what a colour means', async () => {
    engineWith([
      { category: 'known-ai-crawler', strength: 'verified', sessions: 4, pageViews: 12 },
      { category: 'suspected-ai-crawler', strength: 'weak', sessions: 2, pageViews: 5 },
    ]);

    show();

    await screen.findByText('An AI crawler');

    expect(ringParts()).toStrictEqual([
      { name: 'Machinery', value: 6, itemStyle: { color: PALETTE.tones.automation } },
    ]);
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

  /**
   * The summary and the visits behind it are two questions about the same days, so the way between
   * them carries the days. Sent to the usual period, somebody looking at a single Tuesday would
   * arrive at a fortnight and read it as the wrong answer rather than as the wrong question.
   */
  it('takes the period being looked at through with it', async () => {
    engineWith(GROUPS);

    show('?period=2026-08-01..2026-08-14');

    expect(await screen.findByRole('link', { name: /Look at each visit/ })).toHaveAttribute(
      'href',
      '/app/journeys?period=2026-08-01..2026-08-14',
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

import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteFlow } from '@/components/dashboard/site-flow';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

const TOTALS = { visits: 80, singlePageVisits: 20, pageViews: 208 };

const ENTRIES = [
  { path: '/posts/hello', visits: 44 },
  { path: '/pricing', visits: 24 },
];

const EXITS = [
  { path: '/contact', visits: 50 },
  { path: '/posts/hello', visits: 30 },
];

/** One website with more doorways than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  path: `/post-${rank}`,
  visits: 40 - rank,
}));

/** Answers both questions the card asks: the period's visits, and where they began or ended. */
function engineWith(
  totals: Record<string, unknown> = TOTALS,
  entries: readonly { path: string; visits: number }[] = ENTRIES,
  exits: readonly { path: string; visits: number }[] = EXITS,
): Engine {
  return engineDoing(async (path) => {
    if (path.includes('/visits/pages')) {
      const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
      const position = asked.get('position') ?? 'entry';
      const offset = Number(asked.get('offset') ?? 0);
      const limit = Number(asked.get('limit') ?? 10);
      const rows = position === 'exit' ? exits : entries;

      return respondWith(200, {
        from: FROM,
        to: TO,
        position,
        totalVisits: 80,
        totalPaths: rows.length,
        mostVisits: rows[0]?.visits ?? 0,
        pages: rows.slice(offset, offset + limit),
      });
    }

    return respondWith(200, { from: FROM, to: TO, ...totals });
  });
}

function show() {
  renderScreen(<SiteFlow siteId={SITE_ID} window={WINDOW} />);
}

describe('how people move through a website', () => {
  it('says how many pages a visit takes on average', async () => {
    engineWith();

    show();

    // 208 pages across 80 visits.
    expect(await screen.findByText('2.6')).toBeInTheDocument();
    expect(screen.getByText('Pages per visit')).toBeInTheDocument();
  });

  /**
   * The headline row above this card carries pages per visitor, which differs from this by a
   * fraction. Without a word saying which is which the pair reads as one number printed twice.
   */
  it('says which of the two per-page figures on the screen this one is', async () => {
    engineWith();

    show();

    expect(await screen.findByText('Counted per sitting, not per person.')).toBeInTheDocument();
  });

  it('says how many visits read one page and no others, as a share of the visits', async () => {
    engineWith();

    show();

    // Twenty of eighty, taken against the visits rather than against the pages.
    expect(await screen.findByText('25%')).toBeInTheDocument();
    expect(screen.getByText('Read only one page')).toBeInTheDocument();
  });

  it('counts only the visits that have finished, and says so', async () => {
    engineWith();

    show();

    expect(await screen.findByText('80 visits that have finished')).toBeInTheDocument();
  });

  it('opens on where visits began', async () => {
    engineWith();

    show();

    expect(await screen.findByText('/posts/hello')).toBeInTheDocument();
    expect(screen.getByText('44 visits')).toBeInTheDocument();
  });

  it('shows where visits ended when that is asked for', async () => {
    engineWith();

    show();

    await screen.findByText('/posts/hello');
    await userEvent.click(screen.getByRole('radio', { name: 'Left from' }));

    expect(await screen.findByText('/contact')).toBeInTheDocument();
    expect(screen.getByText('50 visits')).toBeInTheDocument();
  });

  it('takes every share against the visits the whole period held', async () => {
    engineWith();

    show();

    // Forty-four doorways of eighty visits, not of the two rows on screen.
    expect(await screen.findByText('55%')).toBeInTheDocument();
  });

  it('steps through the doorways a screenful at a time', async () => {
    engineWith(TOTALS, MANY, MANY);

    show();

    expect(await screen.findByText('/post-0')).toBeInTheDocument();
    expect(screen.queryByText('/post-10')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(await screen.findByText('/post-10')).toBeInTheDocument();
    expect(screen.getByText('11–20 of 23')).toBeInTheDocument();
  });

  it('starts the list again when the reader switches ends', async () => {
    engineWith(TOTALS, MANY, MANY);

    show();

    await screen.findByText('/post-0');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('/post-10');

    await userEvent.click(screen.getByRole('radio', { name: 'Left from' }));

    expect(await screen.findByText('/post-0')).toBeInTheDocument();
  });

  it('shows a designed screen rather than an empty card before anybody has visited', async () => {
    engineWith({ visits: 0, singlePageVisits: 0, pageViews: 0 }, [], []);

    show();

    expect(await screen.findByText('No finished visits yet')).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'Started on' })).not.toBeInTheDocument();
  });

  it('says the numbers could not be read rather than showing none', async () => {
    engineStopped();

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  /**
   * An address is written by whoever asked for it. It reaches the screen as text and nothing else,
   * so it is never a link and never markup.
   */
  it('never turns a visited address into something that can be followed', async () => {
    engineWith(TOTALS, [{ path: '/<img src=x onerror=alert(1)>', visits: 3 }], EXITS);

    show();

    expect(await screen.findByText('/<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});

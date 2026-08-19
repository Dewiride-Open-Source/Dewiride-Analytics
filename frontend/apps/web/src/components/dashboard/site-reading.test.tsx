import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteReading } from '@/components/dashboard/site-reading';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

const READING = {
  readings: 40,
  measured: 24,
  medianEngagedMs: 72_000,
  interacted: 6,
  depths: { top: 12, quarter: 5, half: 4, whole: 3 },
};

const PAGES = [
  {
    path: '/posts/hello',
    readings: 9,
    medianEngagedMs: 141_000,
    medianDepthPercent: 82,
    interacted: 4,
  },
  { path: '/pricing', readings: 6, medianEngagedMs: 41_000, medianDepthPercent: 30, interacted: 2 },
];

/** One website with more pages than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  path: `/post-${rank}`,
  readings: 5,
  medianEngagedMs: 100_000 - rank * 1000,
  medianDepthPercent: 90 - rank,
  interacted: 1,
}));

/** Answers both questions the card asks: the period overall, and page by page. */
function engineWith(
  reading: Record<string, unknown> = READING,
  pages: readonly unknown[] = PAGES,
): Engine {
  return engineDoing(async (path) => {
    if (path.includes('/engagement/pages')) {
      const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
      const offset = Number(asked.get('offset') ?? 0);
      const limit = Number(asked.get('limit') ?? 10);

      return respondWith(200, {
        from: FROM,
        to: TO,
        ranking: asked.get('ranking') ?? 'attention',
        totalPages: pages.length,
        longestMedianEngagedMs: 141_000,
        pages: pages.slice(offset, offset + limit),
      });
    }

    return respondWith(200, { from: FROM, to: TO, ...reading });
  });
}

function show(onShowCode = vi.fn()) {
  renderScreen(<SiteReading siteId={SITE_ID} window={WINDOW} onShowCode={onShowCode} />);

  return onShowCode;
}

describe('how a website’s pages were read', () => {
  it('says how long a typical reader stayed, in words rather than milliseconds', async () => {
    engineWith();

    show();

    expect(await screen.findByText('1m 12s')).toBeInTheDocument();
  });

  it('writes a short read as seconds alone', async () => {
    engineWith({ ...READING, medianEngagedMs: 9000 });

    show();

    expect(await screen.findByText('9s')).toBeInTheDocument();
  });

  it('says what share of readers did something on the page', async () => {
    engineWith();

    show();

    await screen.findByText('1m 12s');

    expect(screen.getByText('25%')).toBeInTheDocument();
  });

  /**
   * Every figure on the card is taken over the readings a browser could measure, so a share
   * against everything that reached the site would understate every one of them.
   */
  it('takes each share against what could be measured rather than against all the traffic', async () => {
    engineWith();

    show();

    await screen.findByText('1m 12s');

    // Twelve of the twenty-four measured readings, not twelve of the forty that arrived.
    expect(screen.getByText('50%')).toBeInTheDocument();
  });

  it('names how far down readers got in words a reader would use', async () => {
    engineWith();

    show();

    expect(await screen.findByText('Just the top')).toBeInTheDocument();
    expect(screen.getByText('Nearly all the way')).toBeInTheDocument();
  });

  /**
   * Only the browser tracker sees any of this, so how much of the period it could be taken from
   * is stated rather than left for somebody to assume.
   */
  it('says how much of the period could be measured at all', async () => {
    engineWith();

    show();

    expect(await screen.findByText(/Measured on 24 of 40 page reads/)).toBeInTheDocument();
  });

  it('says so plainly when every reading could be measured', async () => {
    engineWith({ ...READING, readings: 24 });

    show();

    expect(await screen.findByText(/Measured on all 24 page reads/)).toBeInTheDocument();
  });

  /**
   * The distinction the whole product rests on. Traffic nobody was watching read must never be
   * drawn as an audience that did nothing.
   */
  it('explains itself rather than showing noughts when nothing could be measured', async () => {
    engineWith({ ...READING, measured: 0, interacted: 0, medianEngagedMs: 0 });

    show();

    expect(await screen.findByText('Nothing here could be measured')).toBeInTheDocument();
    expect(screen.queryByText('0s')).not.toBeInTheDocument();
  });

  it('offers the tracking code from that state, which is the one thing that would change it', async () => {
    engineWith({ ...READING, measured: 0, interacted: 0, medianEngagedMs: 0 });

    const onShowCode = show();

    await userEvent.click(await screen.findByRole('button', { name: 'Get your tracking code' }));

    expect(onShowCode).toHaveBeenCalledOnce();
  });

  it('explains itself rather than showing an empty box when nobody has visited', async () => {
    engineWith({ ...READING, readings: 0, measured: 0 });

    show();

    expect(await screen.findByText('No pages read yet')).toBeInTheDocument();
  });

  it('says so plainly when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });

  /** A card opened on the summary has no reason to fetch a page list nobody has asked for. */
  it('asks nothing about individual pages until somebody looks at them', async () => {
    const engine = engineWith();

    show();

    await screen.findByText('1m 12s');

    expect(engine.all().every((sent) => !sent.path.includes('/engagement/pages'))).toBe(true);
  });
});

describe('reading the same period page by page', () => {
  it('swaps the summary for the pages that held attention longest', async () => {
    engineWith();

    show();

    await screen.findByText('Just the top');

    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    expect(await screen.findByText('/posts/hello')).toBeInTheDocument();
    expect(screen.getByText('/pricing')).toBeInTheDocument();
    expect(screen.queryByText('Just the top')).not.toBeInTheDocument();
  });

  /** A page held four minutes once is a different fact from one that did it four hundred times. */
  it('says how many readings each page was measured on', async () => {
    engineWith();

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    expect(await screen.findByText(/9 reads/)).toBeInTheDocument();
  });

  it('carries both figures on every row, whichever one the list was ordered by', async () => {
    engineWith();

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    expect(await screen.findByText('2m 21s')).toBeInTheDocument();
    expect(screen.getByText(/82% down/)).toBeInTheDocument();
  });

  it('asks the engine to order by how far down readers got when asked to', async () => {
    const engine = engineWith();

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'How far' }));

    await screen.findByText('/posts/hello');

    expect(engine.all().some((sent) => sent.path.includes('ranking=depth'))).toBe(true);
  });

  /** A position in one ordering means nothing in the other. */
  it('returns to the top of the list when the orderings are swapped', async () => {
    engineWith(READING, MANY);

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'How far' }));

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();
  });

  it('keeps the same coverage across all three views', async () => {
    engineWith();

    show();

    expect(await screen.findByText(/Measured on 24 of 40 page reads/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));
    await screen.findByText('/posts/hello');

    expect(screen.getByText(/Measured on 24 of 40 page reads/)).toBeInTheDocument();
  });
});

describe('moving through a long list of pages', () => {
  it('brings back the next pages, and then the ones after those', async () => {
    engineWith(READING, MANY);

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('21–23 of 23')).toBeInTheDocument();
  });

  it('offers no way back from the beginning, and none onward from the end', async () => {
    engineWith(READING, MANY);

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    await screen.findByText('1–10 of 23');
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('11–20 of 23');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('21–23 of 23');

    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /previous/i })).toBeEnabled();
  });

  it('shows no way through a list that already fits on one screen', async () => {
    engineWith();

    show();

    await screen.findByText('Just the top');
    await userEvent.click(screen.getByRole('radio', { name: 'Time' }));

    await screen.findByText('/posts/hello');

    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
  });
});

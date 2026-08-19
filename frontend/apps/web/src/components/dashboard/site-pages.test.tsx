import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SitePages } from '@/components/dashboard/site-pages';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

const PAGES = [
  { path: '/', pageViews: 21, visitors: 8 },
  { path: '/integrations', pageViews: 5, visitors: 3 },
  { path: '/legal/privacy', pageViews: 2, visitors: 2 },
];

/** One website with more addresses than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  path: `/page-${String(rank + 1).padStart(2, '0')}`,
  pageViews: 50 - rank,
  visitors: 10,
}));

/** The period held more traffic than the rows listed, as it does on any real site. */
function engineWith(pages: readonly unknown[], pageViews = 40, totalPaths = pages.length) {
  return engineDoing(async () =>
    respondWith(200, {
      from: FROM,
      to: TO,
      pageViews,
      totalPaths,
      mostPageViews: 21,
      pages,
    }),
  );
}

/** Answers each slice from one long list, the way the engine does. */
function engineWithAll(all: readonly unknown[]): Engine {
  return engineDoing(async (path) => {
    const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
    const offset = Number(asked.get('offset') ?? 0);
    const limit = Number(asked.get('limit') ?? 10);

    return respondWith(200, {
      from: FROM,
      to: TO,
      pageViews: 500,
      totalPaths: all.length,
      mostPageViews: 50,
      pages: all.slice(offset, offset + limit),
    });
  });
}

function show() {
  return renderScreen(<SitePages siteId={SITE_ID} window={WINDOW} />);
}

describe('the pages a website’s traffic went to', () => {
  it('lists the addresses people went to', async () => {
    engineWith(PAGES);

    show();

    expect(await screen.findByText('/')).toBeInTheDocument();
    expect(screen.getByText('/integrations')).toBeInTheDocument();
    expect(screen.getByText('/legal/privacy')).toBeInTheDocument();
  });

  it('says how much each page was read, and by how many', async () => {
    engineWith(PAGES);

    show();

    expect(await screen.findByText(/21 views/)).toBeInTheDocument();
    expect(screen.getByText(/8 visitors/)).toBeInTheDocument();
  });

  /**
   * A slice is one screenful of a longer list, so a share taken against the rows shown would put
   * the busiest page of a large site at several times the share it has.
   */
  it('takes a share against the whole period rather than against the rows shown', async () => {
    engineWith(PAGES, 40);

    show();

    expect(await screen.findByText('53%')).toBeInTheDocument();
    expect(screen.getByText('13%')).toBeInTheDocument();
    expect(screen.getByText('5%')).toBeInTheDocument();
  });

  /**
   * Further down a long list every row would otherwise read as nought, which says a page had no
   * traffic when in fact somebody read it.
   */
  it('writes a share too small to round to a whole per cent with a decimal', async () => {
    engineWith([{ path: '/docs/reference/setting-13', pageViews: 2, visitors: 1 }], 462);

    show();

    expect(await screen.findByText('0.4%')).toBeInTheDocument();
  });

  it('says what the shares are a share of', async () => {
    engineWith(PAGES, 40);

    show();

    expect(await screen.findByText('40 page views')).toBeInTheDocument();
  });

  it('writes a single page and a single visitor as one rather than as a plural', async () => {
    engineWith([{ path: '/about', pageViews: 1, visitors: 1 }], 1);

    show();

    expect(await screen.findByText(/1 view(?! s)/)).toBeInTheDocument();
    expect(screen.getByText('1 page view')).toBeInTheDocument();
  });

  /**
   * An address is written by whoever asked for the page. It is shown as text, so a website could
   * not be made to send its own readers somewhere else from this list.
   */
  it('never turns an address into something to follow', async () => {
    engineWith([{ path: '/posts/hello', pageViews: 3, visitors: 1 }]);

    show();

    expect(await screen.findByText('/posts/hello')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('reads an address written outside the English alphabet back as words', async () => {
    engineWith([{ path: '/blog/caf%C3%A9', pageViews: 4, visitors: 2 }]);

    show();

    expect(await screen.findByText('/blog/café')).toBeInTheDocument();
  });

  it('explains itself rather than showing an empty box when nothing has been read', async () => {
    engineWith([], 0, 0);

    show();

    expect(await screen.findByText('No pages read yet')).toBeInTheDocument();
  });

  it('says so plainly when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });
});

describe('moving through the whole list of pages', () => {
  it('says where in the list the reader is', async () => {
    engineWithAll(MANY);

    show();

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();
  });

  it('brings back the next addresses, and then the ones after those', async () => {
    engineWithAll(MANY);

    show();

    expect(await screen.findByText('/page-01')).toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));

    expect(await screen.findByText('/page-11')).toBeInTheDocument();
    expect(screen.getByText('11–20 of 23')).toBeInTheDocument();
    expect(screen.queryByText('/page-01')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(await screen.findByText('/page-21')).toBeInTheDocument();
    expect(screen.getByText('21–23 of 23')).toBeInTheDocument();
  });

  it('goes back the way it came', async () => {
    engineWithAll(MANY);

    show();

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));
    await screen.findByText('/page-11');

    await userEvent.click(screen.getByRole('button', { name: /previous/i }));

    expect(await screen.findByText('/page-01')).toBeInTheDocument();
    expect(screen.getByText('1–10 of 23')).toBeInTheDocument();
  });

  it('offers no way back from the beginning, and none onward from the end', async () => {
    engineWithAll(MANY);

    show();

    await screen.findByText('/page-01');
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('/page-11');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('/page-21');

    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /previous/i })).toBeEnabled();
  });

  /**
   * Two dead arrows under a list of three addresses is chrome for its own sake.
   */
  it('shows no way through a list that already fits on one screen', async () => {
    engineWith(PAGES);

    show();

    await screen.findByText('/integrations');

    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /previous/i })).not.toBeInTheDocument();
  });

  /**
   * Asking the engine for a slice it has already been asked for would be a request per keypress
   * on a list somebody is stepping back and forth through.
   */
  it('asks the engine once for each slice and remembers the answer', async () => {
    const engine = engineWithAll(MANY);

    show();

    await screen.findByText('/page-01');

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('/page-11');

    await userEvent.click(screen.getByRole('button', { name: /previous/i }));
    await screen.findByText('/page-01');

    expect(engine.count).toBe(2);
  });
});

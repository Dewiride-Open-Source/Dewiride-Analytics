import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteDevices } from '@/components/dashboard/site-devices';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

const DEVICES = [
  { kind: 'desktop', visitors: 24, pageViews: 61 },
  { kind: 'phone', visitors: 12, pageViews: 20 },
  { kind: 'unknown', visitors: 4, pageViews: 7 },
];

const BROWSERS = [
  { name: 'Chrome', visitors: 20, pageViews: 50 },
  { name: 'Safari', visitors: 12, pageViews: 30 },
  { name: '', visitors: 8, pageViews: 8 },
];

const SYSTEMS = [
  { name: 'Windows', visitors: 18, pageViews: 44 },
  { name: 'Android', visitors: 10, pageViews: 25 },
];

/** One website with more browsers than fit on a screen, answered a slice at a time. */
const MANY = Array.from({ length: 23 }, (_, rank) => ({
  name: `Browser ${String.fromCharCode(65 + rank)}`,
  visitors: 50 - rank,
  pageViews: 100 - rank,
}));

/**
 * Answers both questions the card asks: how the audience divides between kinds of device, and
 * which browsers or systems they used.
 */
function engineWith(
  devices: readonly unknown[],
  browsers: readonly unknown[] = BROWSERS,
  systems: readonly unknown[] = SYSTEMS,
  visitors = 40,
): Engine {
  return engineDoing(async (path) => {
    if (path.includes('/devices')) {
      return respondWith(200, { from: FROM, to: TO, visitors, devices });
    }

    const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
    const grouping = asked.get('grouping') ?? 'browser';
    const offset = Number(asked.get('offset') ?? 0);
    const limit = Number(asked.get('limit') ?? 10);
    const all = grouping === 'system' ? systems : browsers;

    return respondWith(200, {
      from: FROM,
      to: TO,
      grouping,
      visitors,
      totalNames: all.length,
      mostVisitors: 20,
      names: all.slice(offset, offset + limit),
    });
  });
}

function show() {
  return renderScreen(<SiteDevices siteId={SITE_ID} window={WINDOW} />);
}

describe('what a website’s readers use', () => {
  it('names each kind of device in words a reader would use', async () => {
    engineWith(DEVICES);

    show();

    expect(await screen.findByText('Computers')).toBeInTheDocument();
    expect(screen.getByText('Phones')).toBeInTheDocument();
  });

  /** The same word for the same population as every other card on the screen. */
  it('says how many visitors were on each kind, and how much they read', async () => {
    engineWith(DEVICES);

    show();

    expect(await screen.findByText(/24 visitors/)).toBeInTheDocument();
    expect(screen.getByText(/61 views/)).toBeInTheDocument();
  });

  /** Every visitor is on exactly one row, so the shares are shares of the whole card. */
  it('takes each share against everyone the period held', async () => {
    engineWith(DEVICES);

    show();

    expect(await screen.findByText('60%')).toBeInTheDocument();
    expect(screen.getByText('30%')).toBeInTheDocument();
    expect(screen.getByText('10%')).toBeInTheDocument();
  });

  /**
   * Much of what reaches a website is not a device at all. Leaving those visits out would
   * describe a different audience from the one that was there.
   */
  it('shows the visits it could not identify rather than leaving them out', async () => {
    engineWith(DEVICES);

    show();

    expect(await screen.findByText('Not known')).toBeInTheDocument();
  });

  /**
   * A website whose visits mostly carry nothing that names a device is usually a website whose
   * traffic is mostly not people. Left unexplained, that reads as the product having failed.
   */
  it('explains itself when most visits named no device at all', async () => {
    engineWith(
      [
        { kind: 'unknown', visitors: 171, pageViews: 460 },
        { kind: 'desktop', visitors: 18, pageViews: 52 },
      ],
      BROWSERS,
      SYSTEMS,
      189,
    );

    show();

    expect(await screen.findByText(/carried nothing that names a device/)).toBeInTheDocument();
  });

  it('says nothing of the sort when most visits did name one', async () => {
    engineWith(DEVICES);

    show();

    await screen.findByText('Computers');

    expect(screen.queryByText(/carried nothing that names a device/)).not.toBeInTheDocument();
  });

  it('says a single visitor is one rather than a plural', async () => {
    engineWith([{ kind: 'tablet', visitors: 1, pageViews: 1 }], BROWSERS, SYSTEMS, 1);

    show();

    expect(await screen.findByText('1 visitor')).toBeInTheDocument();
  });

  it('explains itself rather than showing an empty box when nobody has visited', async () => {
    engineWith([], [], [], 0);

    show();

    expect(await screen.findByText('No devices seen yet')).toBeInTheDocument();
  });

  it('says so plainly when the engine cannot be reached', async () => {
    engineStopped();

    show();

    expect(await screen.findByText("Can't reach Dewiride Analytics")).toBeInTheDocument();
  });

  /**
   * A card opened on the device split has no reason to fetch a browser list nobody has asked
   * for.
   */
  it('asks nothing about browsers until somebody looks at them', async () => {
    const engine = engineWith(DEVICES);

    show();

    await screen.findByText('Computers');

    expect(engine.all().every((sent) => sent.path.includes('/devices'))).toBe(true);
  });
});

describe('reading the same audience by browser and by system', () => {
  it('swaps the device split for browsers when asked', async () => {
    engineWith(DEVICES);

    show();

    await screen.findByText('Computers');

    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    expect(await screen.findByText('Chrome')).toBeInTheDocument();
    expect(screen.getByText('Safari')).toBeInTheDocument();
    expect(screen.queryByText('Computers')).not.toBeInTheDocument();
  });

  it('shows operating systems when those are asked for instead', async () => {
    engineWith(DEVICES);

    show();

    await screen.findByText('Computers');

    await userEvent.click(screen.getByRole('radio', { name: 'Systems' }));

    expect(await screen.findByText('Windows')).toBeInTheDocument();
    expect(screen.getByText('Android')).toBeInTheDocument();
  });

  /**
   * A browser nothing could be established about is a row for the reason the unresolved device
   * is: an install seeing nothing but crawlers should be able to tell.
   */
  it('shows the browsers it could not establish rather than leaving them out', async () => {
    engineWith(DEVICES);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    expect(await screen.findByText('Chrome')).toBeInTheDocument();
    expect(screen.getByText('Not known')).toBeInTheDocument();
  });

  /** A position in one list means nothing in the other. */
  it('returns to the top of the list when the views are swapped', async () => {
    engineWith(DEVICES, MANY);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    await userEvent.click(await screen.findByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Systems' }));

    expect(await screen.findByText('Windows')).toBeInTheDocument();
    expect(screen.queryByText('11–20 of 23')).not.toBeInTheDocument();
  });

  it('keeps the same total across all three views', async () => {
    engineWith(DEVICES);

    show();

    expect(await screen.findByText('40 visitors')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));
    await screen.findByText('Chrome');

    expect(screen.getByText('40 visitors')).toBeInTheDocument();
  });
});

describe('moving through a long list of browsers', () => {
  it('says where in the list the reader is', async () => {
    engineWith(DEVICES, MANY);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();
  });

  it('brings back the next names, and then the ones after those', async () => {
    engineWith(DEVICES, MANY);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    expect(await screen.findByText('1–10 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('11–20 of 23')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(await screen.findByText('21–23 of 23')).toBeInTheDocument();
  });

  it('offers no way back from the beginning, and none onward from the end', async () => {
    engineWith(DEVICES, MANY);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

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
    engineWith(DEVICES);

    show();

    await screen.findByText('Computers');
    await userEvent.click(screen.getByRole('radio', { name: 'Browsers' }));

    await screen.findByText('Chrome');

    expect(screen.queryByRole('button', { name: /next/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /previous/i })).not.toBeInTheDocument();
  });
});

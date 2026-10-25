import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteActions } from '@/components/dashboard/site-actions';
import type { Population } from '@/lib/analytics/people-only';
import { type Engine, engineDoing, engineStopped, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SITE_ID = '01a013fa-49d6-77be-b65d-20ec86e9df78';
const FROM = '2026-08-11T00:00:00+00:00';
const TO = '2026-08-18T00:00:00+00:00';
const WINDOW = { from: FROM, to: TO };

interface Row {
  readonly name: string;
  readonly control: 'unknown' | 'link' | 'button' | 'field';
  readonly presses: number;
  readonly visitors: number;
}

const CONTROLS: Row[] = [
  { name: 'Subscribe', control: 'button', presses: 40, visitors: 31 },
  { name: 'Pricing', control: 'link', presses: 10, visitors: 9 },
];

const DESTINATIONS: Row[] = [{ name: 'github.com', control: 'unknown', presses: 12, visitors: 11 }];

/** One website with more clicked things than fit on a screen, answered a slice at a time. */
const MANY: Row[] = Array.from({ length: 23 }, (_, rank) => ({
  name: `Button ${rank}`,
  control: 'button' as const,
  presses: 40 - rank,
  visitors: 1,
}));

/**
 * Answers what the card asks: what was pressed, where presses led, and whether the website is
 * recording them at all — or, for that last question, refuses with a status, or answers only once
 * the test lets it.
 */
function engineWith(
  controls: readonly Row[] = CONTROLS,
  destinations: readonly Row[] = DESTINATIONS,
  recording: boolean | number | Promise<boolean> = true,
): Engine {
  return engineDoing(async (path) => {
    if (path.includes('/settings')) {
      if (typeof recording === 'number') {
        return respondWith(recording, { title: 'The engine is having trouble.' });
      }

      return respondWith(200, {
        displayName: 'Example',
        timeZoneId: 'Etc/UTC',
        captureClicks: await recording,
      });
    }

    const asked = new URLSearchParams(path.slice(path.indexOf('?') + 1));
    const grouping = asked.get('grouping') ?? 'control';
    const offset = Number(asked.get('offset') ?? 0);
    const limit = Number(asked.get('limit') ?? 10);
    const rows = grouping === 'destination' ? destinations : controls;

    return respondWith(200, {
      from: FROM,
      to: TO,
      grouping,
      presses: rows.reduce((total, row) => total + row.presses, 0),
      totalControls: rows.length,
      mostPresses: rows[0]?.presses ?? 0,
      controls: rows.slice(offset, offset + limit),
    });
  });
}

function show(
  population: Population = 'everybody',
  mayChangeSettings = true,
  onShowSettings: () => void = () => undefined,
) {
  return renderScreen(
    <SiteActions
      siteId={SITE_ID}
      window={WINDOW}
      population={population}
      mayChangeSettings={mayChangeSettings}
      onShowSettings={onShowSettings}
    />,
  );
}

describe('what people clicked', () => {
  /**
   * Kept to people, every question the card asks about clicks is asked of them and of nobody
   * else. Whether the website records clicks at all is a question about the website.
   */
  it('asks about the people when the card is kept to them', async () => {
    const engine = engineWith();

    show('people');

    await screen.findByText('Button');

    const aboutClicks = engine.all().filter((sent) => !sent.path.includes('/settings'));

    expect(aboutClicks.length).toBeGreaterThan(0);
    expect(aboutClicks.every((sent) => sent.path.includes('only=people'))).toBe(true);
  });
  it('lists what was clicked, most clicked first', async () => {
    engineWith();

    show();

    const names = await screen.findAllByText(/^(Subscribe|Pricing)$/);

    expect(names.map((name) => name.textContent)).toEqual(['Subscribe', 'Pricing']);
    expect(screen.getByText('40 clicks')).toBeInTheDocument();
  });

  /**
   * A page describes its own controls however it likes. What reaches the screen is one of this
   * dashboard's own words, so a site cannot write its own labels onto somebody else's screen.
   */
  it('says what sort of thing each one was, in its own words', async () => {
    engineWith();

    show();

    expect(await screen.findByText('Button')).toBeInTheDocument();
    expect(screen.getByText('Link')).toBeInTheDocument();
  });

  it('says plainly when no name was kept', async () => {
    engineWith([
      { name: '', control: 'button', presses: 88, visitors: 40 },
      { name: '', control: 'field', presses: 5, visitors: 3 },
    ]);

    show();

    expect(await screen.findAllByText('No name kept')).toHaveLength(2);
    expect(screen.getByText('Button')).toBeInTheDocument();
    expect(screen.getByText('Box to fill in')).toBeInTheDocument();
  });

  it('shows where clicks led away when that is asked for', async () => {
    engineWith();

    show();

    await screen.findByText('Subscribe');
    await userEvent.click(screen.getByRole('radio', { name: 'Where it took them' }));

    expect(await screen.findByText('github.com')).toBeInTheDocument();
    expect(screen.getByText('12 clicks')).toBeInTheDocument();
  });

  /**
   * A share of the clicks that led off the site, sitting under a total of every click, would read
   * as a share of the larger number. The heading counts what is on the screen.
   */
  it('counts what is on the screen rather than everything', async () => {
    engineWith();

    show();

    expect(await screen.findByText('50 clicks')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Where it took them' }));

    expect(await screen.findByText('12 clicks that led off your site')).toBeInTheDocument();
  });

  it('takes every share against the clicks the whole period held', async () => {
    engineWith();

    show();

    // Forty of fifty.
    expect(await screen.findByText('80%')).toBeInTheDocument();
  });

  it('steps through the list a screenful at a time', async () => {
    engineWith(MANY, MANY);

    show();

    expect(await screen.findByText('Button 0')).toBeInTheDocument();
    expect(screen.queryByText('Button 10')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /next/i }));

    expect(await screen.findByText('Button 10')).toBeInTheDocument();
    expect(screen.getByText('11–20 of 23')).toBeInTheDocument();
  });

  it('starts the list again when the reader switches what they are looking at', async () => {
    engineWith(MANY, MANY);

    show();

    await screen.findByText('Button 0');
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    await screen.findByText('Button 10');

    await userEvent.click(screen.getByRole('radio', { name: 'Where it took them' }));

    expect(await screen.findByText('Button 0')).toBeInTheDocument();
  });

  it('shows a designed screen rather than an empty card before anybody has clicked', async () => {
    engineWith([], []);

    show();

    expect(await screen.findByText('No clicks yet')).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'What they clicked' })).not.toBeInTheDocument();
  });

  /**
   * Recording starts switched off, and a card promising clicks are on their way would be waiting
   * for something that is never collected. It says what is happening and where to change it.
   */
  it('says clicks are not being recorded, rather than waiting for them, where the website switched it off', async () => {
    engineWith([], [], false);
    const opened = vi.fn();

    show('everybody', true, opened);

    expect(await screen.findByText("Clicks aren't being recorded")).toBeInTheDocument();
    expect(screen.getByText(/Record what people click/)).toBeInTheDocument();
    expect(screen.queryByText('No clicks yet')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Open settings' }));

    expect(opened).toHaveBeenCalledOnce();
  });

  /** A button to a switch somebody cannot move would be a door that does not open. */
  it('tells somebody who may only read who can switch recording on, without a way they cannot take', async () => {
    engineWith([], [], false);

    show('everybody', false);

    expect(
      await screen.findByText(
        'Somebody who manages this website can switch on “Record what people click” in its settings.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open settings' })).not.toBeInTheDocument();
  });

  /**
   * Neither empty state is drawn until the settings say which of them is true — checked once the
   * list of clicks has been answered and drawn from, which is when a card that did not wait would
   * already have picked one.
   */
  it('says nothing about recording until the website settings have been read', async () => {
    let answer: (recording: boolean) => void = () => undefined;

    engineWith(
      [],
      [],
      new Promise<boolean>((resolve) => {
        answer = resolve;
      }),
    );

    const { cache } = show();

    await waitFor(() => expect(cache.isFetching()).toBe(1));
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));

    expect(document.querySelector('.animate-pulse')).not.toBeNull();
    expect(screen.queryByText('No clicks yet')).not.toBeInTheDocument();
    expect(screen.queryByText("Clicks aren't being recorded")).not.toBeInTheDocument();

    answer(false);

    expect(await screen.findByText("Clicks aren't being recorded")).toBeInTheDocument();
  });

  it('says new clicks are not being recorded beside the ones kept from before', async () => {
    engineWith(CONTROLS, DESTINATIONS, false);

    show();

    await screen.findByText('Subscribe');

    expect(await screen.findByText('Not recording new clicks')).toBeInTheDocument();
  });

  it('says nothing about recording while the website records', async () => {
    engineWith(CONTROLS, DESTINATIONS, true);

    show();

    await screen.findByText('Subscribe');

    expect(screen.queryByText('Not recording new clicks')).not.toBeInTheDocument();
  });

  /** A setting that could not be read is not taken to be either answer: the failure is said instead. */
  it('says what went wrong rather than either answer when the website settings cannot be read', async () => {
    engineWith([], [], 500);

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('No clicks yet')).not.toBeInTheDocument();
    expect(screen.queryByText("Clicks aren't being recorded")).not.toBeInTheDocument();
  });

  /**
   * A site that links nowhere else is a site with nothing to show under one of the two views, and
   * that is a sentence rather than a blank space.
   */
  it('says why one of the two lists is empty rather than showing nothing', async () => {
    engineWith(CONTROLS, []);

    show();

    await screen.findByText('Subscribe');
    await userEvent.click(screen.getByRole('radio', { name: 'Where it took them' }));

    expect(
      await screen.findByText('Nobody has followed a link away from your site yet.'),
    ).toBeInTheDocument();
  });

  /**
   * A count of nought above a sentence saying there is nothing is the same fact told twice, and
   * the sentence is the half worth reading.
   */
  it('says nothing about a count when there is nothing to count', async () => {
    engineWith(CONTROLS, []);

    show();

    await screen.findByText('Subscribe');
    await userEvent.click(screen.getByRole('radio', { name: 'Where it took them' }));

    await screen.findByText('Nobody has followed a link away from your site yet.');

    expect(screen.queryByText(/clicks that led off your site/)).not.toBeInTheDocument();
  });
  it('says the list could not be read rather than showing none', async () => {
    engineStopped();

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  /**
   * A control's name is written by whoever wrote the page, and a page may carry writing somebody
   * else put there. It reaches the screen as text and nothing else.
   */
  it('never turns what a page called its control into something that can be followed', async () => {
    engineWith([
      { name: '<img src=x onerror=alert(1)>', control: 'button', presses: 3, visitors: 3 },
    ]);

    show();

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});

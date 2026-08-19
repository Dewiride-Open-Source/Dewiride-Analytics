import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteActions } from '@/components/dashboard/site-actions';
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

/** Answers the two questions the card asks: what was pressed, and where presses led. */
function engineWith(
  controls: readonly Row[] = CONTROLS,
  destinations: readonly Row[] = DESTINATIONS,
): Engine {
  return engineDoing(async (path) => {
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

function show() {
  renderScreen(<SiteActions siteId={SITE_ID} window={WINDOW} />);
}

describe('what people clicked', () => {
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

  it('says plainly when a site gave something no name at all', async () => {
    engineWith([{ name: '', control: 'button', presses: 88, visitors: 40 }]);

    show();

    expect(await screen.findByText('Unnamed')).toBeInTheDocument();
    expect(screen.getByText('Button')).toBeInTheDocument();
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

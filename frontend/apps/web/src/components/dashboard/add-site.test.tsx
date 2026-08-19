import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddSite } from '@/components/dashboard/add-site';
import { type Engine, engineAnswering, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
  vi.unstubAllGlobals();
});

/**
 * The zones a browser offers, stated rather than taken from the machine the test runs on.
 *
 * Deliberately spelled `Asia/Calcutta`, which is the older of the two names for the same place.
 * Platforms disagree about which they list, and a website counted in the other spelling is the
 * case this panel has to get right rather than the exotic one.
 */
const OFFERED = ['Asia/Calcutta', 'Asia/Tokyo', 'Europe/London', 'Africa/Abidjan'];

function offering(zones: readonly string[] = OFFERED, here = 'Asia/Calcutta'): void {
  vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue([...zones]);
  vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({
    locale: 'en-GB',
    calendar: 'gregory',
    numberingSystem: 'latn',
    timeZone: here,
  } as Intl.ResolvedDateTimeFormatOptions);
}

function show(likelyTimeZoneId?: string) {
  return renderScreen(
    <AddSite open onClose={() => {}} likelyTimeZoneId={likelyTimeZoneId} onAdded={() => {}} />,
  );
}

function zonePicker(): Promise<HTMLElement> {
  return screen.findByRole('combobox', { name: 'Count its days in' });
}

function addButton(): Promise<HTMLElement> {
  return screen.findByRole('button', { name: 'Add website' });
}

function additions(engine: Engine): Sent[] {
  return engine.all().filter((sent) => sent.init.method === 'POST');
}

describe('the zone a new website is counted in', () => {
  /**
   * The reason this panel asks at all: somebody adding a second website almost always runs it for
   * the same readers as the first, and whoever administers it may be nowhere near either.
   */
  it('opens on the zone the website already on screen is counted in', async () => {
    offering();
    engineAnswering(200, {});

    show('Europe/London');

    expect(await zonePicker()).toHaveValue('Europe/London');
  });

  /**
   * The same place goes by two names and browsers disagree about which they list. Without widening
   * the choices the picker cannot offer the zone at all, and quietly opens on somewhere else — so
   * a second website in the same city as the first would be counted on a different day boundary.
   */
  it('offers the zone even when this browser spells it the other way', async () => {
    offering();
    engineAnswering(200, {});

    show('Asia/Kolkata');

    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
  });

  /**
   * Nothing to borrow and nothing to fall back on but the machine somebody is sitting at. It is a
   * worse guess than another website's, and it is still a guess somebody would recognise.
   */
  it('falls back to the zone this machine is in', async () => {
    offering(OFFERED, 'Europe/London');
    engineAnswering(200, {});

    show(undefined);

    expect(await zonePicker()).toHaveValue('Europe/London');
  });

  it('sends the zone on screen rather than the one it opened on', async () => {
    offering();
    const engine = engineAnswering(200, { id: 'x', domain: 'blog.example.com' });

    show('Europe/London');

    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Website address' }),
      'blog.example.com',
    );
    await userEvent.selectOptions(await zonePicker(), 'Asia/Tokyo');
    await userEvent.click(await addButton());

    await waitFor(() => expect(additions(engine)).toHaveLength(1));
    expect(additions(engine)[0]?.init.body).toContain('Asia/Tokyo');
  });
});

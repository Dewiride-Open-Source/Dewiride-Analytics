import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddSite } from '@/components/dashboard/add-site';
import { type Engine, engineAnswering, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';
import { CURRENT_BROWSER, hydrated, STALE_SERVER } from '@/test/hydration';

afterEach(() => {
  vi.unstubAllGlobals();
});

/**
 * The zones a browser offers, stated rather than taken from the machine the test runs on.
 *
 * Deliberately listing `Asia/Calcutta`, the earlier of the two names for the same place, as Chrome
 * does. A browser that calls a place by a name it no longer goes by, beside a website stored under
 * the current one, is the case this panel has to get right rather than the exotic one.
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
   * The same place goes by two names, and this browser lists it under the earlier one. It is
   * offered under the name it goes by now — once — so a second website in the same city as the
   * first opens on the first one's zone rather than quietly on somewhere else.
   */
  it('opens on that zone where this browser lists it under an earlier name', async () => {
    offering(OFFERED, 'Europe/London');
    engineAnswering(200, {});

    show('Asia/Kolkata');

    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
    expect(screen.getByRole('option', { name: 'Kolkata (GMT+5:30)' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Calcutta (GMT+5:30)' })).not.toBeInTheDocument();
  });

  /**
   * A website stored under the earlier name opens on the same place, under its current name. The
   * device is somewhere else, so only the website on screen can have put the picker there.
   */
  it('opens on that zone where the website on screen is stored under an earlier name', async () => {
    offering(OFFERED, 'Europe/London');
    engineAnswering(200, {});

    show('Asia/Calcutta');

    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
  });

  /** A device that reports its zone by an earlier name is placed under the current one. */
  it('falls back to this machine’s zone under the name it goes by now', async () => {
    offering(OFFERED, 'Asia/Calcutta');
    engineAnswering(200, {});

    show(undefined);

    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
  });

  /** The new website is stored under the current name, which every engine's copy of the rules knows. */
  it('saves the zone under the name it goes by now', async () => {
    offering(OFFERED, 'Europe/London');
    const engine = engineAnswering(200, { id: 'x', domain: 'blog.example.com' });

    show('Asia/Calcutta');

    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Website address' }),
      'blog.example.com',
    );
    await userEvent.click(await addButton());

    await waitFor(() => expect(additions(engine)).toHaveLength(1));
    expect(JSON.parse(String(additions(engine)[0]?.init.body))).toMatchObject({
      timeZoneId: 'Asia/Kolkata',
    });
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

describe('a page drawn by a server whose time zones are not the browser’s', () => {
  /**
   * The bar across the top holds this panel on every screen, closed, with its picker inside it. A
   * picker drawn from the server's copy of the rules disagrees with the browser's wherever the two
   * copies do, and React answers that by throwing away everything the server drew — on every
   * screen, whether or not anybody ever adds a website.
   */
  it('is taken over while closed without either side giving way', () => {
    const page = hydrated(<AddSite open={false} onClose={() => {}} onAdded={() => {}} />, {
      server: STALE_SERVER,
      browser: CURRENT_BROWSER,
    });

    expect(page.html).not.toContain('Casablanca');
    expect(page.recovered).not.toHaveBeenCalled();
    expect(page.complaints).not.toHaveBeenCalled();
  });

  it('is taken over while open without either side giving way', () => {
    const page = hydrated(
      <AddSite open onClose={() => {}} likelyTimeZoneId="Europe/London" onAdded={() => {}} />,
      { server: STALE_SERVER, browser: CURRENT_BROWSER },
    );

    expect(page.html).not.toContain('Casablanca');
    expect(page.recovered).not.toHaveBeenCalled();
    expect(page.complaints).not.toHaveBeenCalled();
  });

  it('offers this browser’s zones once the page is its own', () => {
    hydrated(<AddSite open={false} onClose={() => {}} onAdded={() => {}} />, {
      server: STALE_SERVER,
      browser: CURRENT_BROWSER,
    });

    const picker = screen.getByRole('combobox', { name: 'Count its days in', hidden: true });

    expect(picker).toBeEnabled();
    expect(picker).toHaveValue('Africa/Casablanca');
    expect(
      screen.getByRole('option', { name: 'Casablanca (GMT)', hidden: true }),
    ).toBeInTheDocument();
  });

  it('opens on the zone it borrowed, among this browser’s zones', async () => {
    hydrated(
      <AddSite open onClose={() => {}} likelyTimeZoneId="Europe/London" onAdded={() => {}} />,
      { server: STALE_SERVER, browser: CURRENT_BROWSER },
    );

    expect(await zonePicker()).toHaveValue('Europe/London');
    expect(screen.getByRole('option', { name: 'London (GMT)' })).toBeInTheDocument();
  });
});

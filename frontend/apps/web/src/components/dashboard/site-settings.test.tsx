import { focusManager } from '@tanstack/react-query';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteActions } from '@/components/dashboard/site-actions';
import { SiteSettings } from '@/components/dashboard/site-settings';
import type { Site, SitemapStatus, SiteSettings as StoredSettings } from '@/lib/api/schemas';
import { siteSettingsKey } from '@/lib/queries/keys';
import { type Engine, engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';
import { CURRENT_BROWSER, hydrated, STALE_SERVER } from '@/test/hydration';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const SITE: Site = {
  id: '01a013fa-49d6-77be-b65d-20ec86e9df78',
  domain: 'example.com',
  displayName: 'example.com',
  timeZoneId: 'Europe/London',
  role: 'owner',
};

/** What the engine holds for this website before a test asks it to change anything. */
const STORED: StoredSettings = {
  displayName: SITE.displayName,
  timeZoneId: SITE.timeZoneId,
  captureClicks: true,
  sitemapAddress: null,
};

const SITEMAP = 'https://example.com/sitemap.xml';

const ANOTHER_SITEMAP = 'https://example.com/wp-sitemap.xml';

/** How a reading stands where nothing has happened yet, for a test to say what has. */
const NOTHING_READ: SitemapStatus = {
  address: SITEMAP,
  state: 'waiting',
  problem: null,
  readAt: null,
  succeededAt: null,
  posts: 0,
  dated: 0,
  reading: 0,
  turnedAway: 0,
};

/** How a reading stands for a website holding one address, as the engine would say at first. */
function readingOf(address: string | null): SitemapStatus {
  return address === null
    ? { ...NOTHING_READ, address, state: 'none' }
    : { ...NOTHING_READ, address };
}

/** The moment some number of hours before this one, as the engine writes an instant. */
function hoursAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

/**
 * The settings once a change has been applied to them, the way the engine does it: a setting left
 * out stays as it was, and an empty address means no sitemap at all.
 */
function changedBy(settings: StoredSettings, change: Partial<StoredSettings>): StoredSettings {
  const applied = { ...settings, ...change };

  return { ...applied, sitemapAddress: applied.sitemapAddress || null };
}

/**
 * The zones a browser offers, stated rather than taken from the machine the test runs on.
 *
 * Which identifiers a platform knows, and which spelling it knows them under, differ between
 * machines and between releases. A test about which zone a picker opens on would otherwise also
 * be a test of that machine's copy of the world's time zone rules, and would start failing on
 * somebody else's laptop for reasons that have nothing to do with this screen.
 */
const OFFERED = ['Asia/Kolkata', 'Asia/Tokyo', 'Europe/London', 'Europe/Paris'];

function offering(zones: readonly string[] = OFFERED): void {
  vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue([...zones]);
}

/** Answers with what the website records, and remembers what it is asked to change it to. */
function engineHolding(held: Partial<StoredSettings> = {}): Engine {
  let settings: StoredSettings = { ...STORED, ...held };

  return engineDoing(async (path, init) => {
    if (init.method === 'DELETE') {
      return respondWith(204, null);
    }

    if (path.endsWith('/sitemap')) {
      return respondWith(200, readingOf(settings.sitemapAddress));
    }

    if (init.method === 'PUT') {
      settings = changedBy(settings, JSON.parse(String(init.body)) as Partial<StoredSettings>);
    }

    return respondWith(200, settings);
  });
}

interface Showing {
  readonly open?: boolean;
  /** What the person looking at the panel is allowed to do to this website. */
  readonly role?: Site['role'];
  readonly onRemoved?: () => void;
}

function show({ open = true, role = 'owner', onRemoved = () => {} }: Showing = {}) {
  return renderScreen(
    <SiteSettings open={open} onClose={() => {}} site={{ ...SITE, role }} onRemoved={onRemoved} />,
  );
}

function nameBox(): Promise<HTMLElement> {
  return screen.findByRole('textbox', { name: 'Website name' });
}

function zonePicker(): Promise<HTMLElement> {
  return screen.findByRole('combobox', { name: 'Count its days in' });
}

function saveButton(): Promise<HTMLElement> {
  return screen.findByRole('button', { name: 'Save changes' });
}

/** Types a new name over whatever is in the box, the way somebody renaming a website would. */
async function rename(to: string): Promise<void> {
  const box = await nameBox();

  await userEvent.clear(box);
  await userEvent.type(box, to);
}

/** Opens the confirmation under the removal heading and types whatever is given into it. */
async function askToRemove(typed: string): Promise<void> {
  await userEvent.click(await screen.findByRole('button', { name: 'Remove website' }));
  await userEvent.type(
    await screen.findByRole('textbox', { name: 'Type example.com to confirm' }),
    typed,
  );
}

describe('what a website is called and when its days begin', () => {
  /**
   * The screen behind this panel is already showing a name and a zone, and they are not
   * necessarily the ones in force: a website renamed in another tab, or by somebody else, leaves
   * the list stale until it is asked for again. Starting the form from the list would offer to
   * save a stale value back over the stored one.
   */
  it('opens on the name and zone the engine holds, not on the ones the screen was showing', async () => {
    offering();
    engineHolding({ displayName: 'Reader Weekly', timeZoneId: 'Asia/Tokyo' });

    show();

    expect(await nameBox()).toHaveValue('Reader Weekly');
    expect(await zonePicker()).toHaveValue('Asia/Tokyo');
  });

  it('cannot be saved while nothing has been changed', async () => {
    offering();
    engineHolding();

    show();

    expect(await saveButton()).toBeDisabled();
  });

  /**
   * A setting left out of the change is left as it was. Somebody who came to rename their website
   * must not move the boundary of its day by passing through the field beneath the one they came
   * for, so what is sent is what actually differs and nothing else.
   */
  it('sends the new name and nothing else', async () => {
    offering();
    const engine = engineHolding();

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ displayName: 'Reader Weekly' });
  });

  it('sends the new zone and nothing else', async () => {
    offering();
    const engine = engineHolding();

    show();
    await userEvent.selectOptions(await zonePicker(), 'Asia/Tokyo');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ timeZoneId: 'Asia/Tokyo' });
  });

  it('sends both when both were changed, in one go', async () => {
    offering();
    const engine = engineHolding();

    show();
    await rename('Reader Weekly');
    await userEvent.selectOptions(await zonePicker(), 'Asia/Tokyo');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({
      displayName: 'Reader Weekly',
      timeZoneId: 'Asia/Tokyo',
    });
  });

  /**
   * A cookie the browser returns on its own is not proof that this page meant to send the change,
   * so the pair the engine issued travels with it.
   */
  it('proves where the change came from', async () => {
    offering();
    const engine = engineHolding();

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(proofOn(changes(engine)[0])).toBe('proof-value');
  });

  it('says the change went through', async () => {
    offering();
    engineHolding();

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    expect(await screen.findByText('Saved')).toHaveRole('status');
  });

  it('offers nothing more to save once what was typed is what is stored', async () => {
    offering();
    engineHolding();

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(async () => expect(await saveButton()).toBeDisabled());
  });

  /**
   * The boxes settle on whatever the engine ended up holding, not on what it was asked to hold.
   * Left on what was typed, they would sit disagreeing with the stored value while the button
   * beside them compared against it — so the button would arm itself with nobody having touched
   * anything, and pressing it would put the older name back.
   */
  it('settles on the name the engine ended up holding, not the one it was sent', async () => {
    offering();
    engineDoing(async (path, init) =>
      path.endsWith('/sitemap')
        ? respondWith(200, readingOf(null))
        : respondWith(200, {
            ...STORED,
            displayName: init.method === 'PUT' ? 'Reader Weekly Ltd' : STORED.displayName,
          }),
    );

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(async () => expect(await nameBox()).toHaveValue('Reader Weekly Ltd'));
    expect(await saveButton()).toBeDisabled();
  });

  /**
   * The switch below shares these settings, so saving it hands the form a fresh answer. Following
   * that answer wholesale would take a half-typed name away from somebody who had only reached
   * across to turn recording off, and leave nothing on screen saying where it went.
   */
  it('keeps a half-typed name when the recording switch is used beside it', async () => {
    offering();
    engineHolding({ captureClicks: true });

    show();
    await rename('Reader Weekly');
    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('switch')).not.toBeChecked();
    expect(await nameBox()).toHaveValue('Reader Weekly');
    expect(await saveButton()).toBeEnabled();
  });

  it('says why a name was refused rather than showing it as saved', async () => {
    offering();
    refusing('PUT', 400, {
      code: 'SiteNameRejected',
      description: 'That name is longer than the engine will keep.',
    });

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    expect(
      await screen.findByText("We couldn't use that name. Try a shorter one."),
    ).toBeInTheDocument();
    expect(screen.queryByText('Saved')).not.toBeInTheDocument();
  });

  it('says why a zone was refused rather than showing it as saved', async () => {
    offering();
    refusing('PUT', 400, {
      code: 'SiteTimeZoneRejected',
      description: 'The engine does not know that zone.',
    });

    show();
    await userEvent.selectOptions(await zonePicker(), 'Asia/Tokyo');
    await userEvent.click(await saveButton());

    expect(
      await screen.findByText("We couldn't use that time zone. Pick another one from the list."),
    ).toBeInTheDocument();
  });
});

describe('a website counted in a zone this browser does not offer', () => {
  /**
   * The engine's copy of the rules can know a zone the browser's does not. A picker that cannot
   * offer the zone a website is actually counted in opens on whatever it does have, and the first
   * thing ever saved from this form then moves the website's day boundary without anybody having
   * asked for it.
   */
  it('still offers the zone the website is counted in', async () => {
    offering();
    engineHolding({ timeZoneId: 'Asia/Dubai' });

    show();

    expect(await zonePicker()).toHaveValue('Asia/Dubai');
    expect(screen.getByRole('option', { name: 'Dubai (GMT+4:00)' })).toBeInTheDocument();
  });

  it('does not move the day boundary when only the name was changed', async () => {
    offering();
    const engine = engineHolding({ timeZoneId: 'Asia/Dubai' });

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ displayName: 'Reader Weekly' });
    expect(await zonePicker()).toHaveValue('Asia/Dubai');
  });
});

describe('a website counted in a zone stored under an earlier name', () => {
  /**
   * Browsers built on ICU still call some places by a name they no longer go by — `Asia/Calcutta`
   * for Kolkata — and a website set up from one of them is stored under it. The picker shows the
   * place under the name it goes by now, and once, rather than offering the same place twice.
   */
  it('shows the zone under the name it goes by now, and only once', async () => {
    offering();
    engineHolding({ timeZoneId: 'Asia/Calcutta' });

    show();

    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
    expect(screen.getByRole('option', { name: 'Kolkata (GMT+5:30)' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Calcutta (GMT+5:30)' })).not.toBeInTheDocument();
  });

  /** The same place under the name it goes by now is not a change to the website. */
  it('cannot be saved while nothing has been changed', async () => {
    offering();
    engineHolding({ timeZoneId: 'Asia/Calcutta' });

    show();

    await zonePicker();

    expect(await saveButton()).toBeDisabled();
  });

  it('does not touch the stored zone when only the name was changed', async () => {
    offering();
    const engine = engineHolding({ timeZoneId: 'Asia/Calcutta' });

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ displayName: 'Reader Weekly' });
    expect(await zonePicker()).toHaveValue('Asia/Kolkata');
  });
});

describe('what a website measures', () => {
  it('shows whether clicks are being recorded', async () => {
    engineHolding({ captureClicks: true });

    show();

    expect(await screen.findByRole('switch', { name: /Record what people click/ })).toBeChecked();
  });

  it('shows when they are not', async () => {
    engineHolding({ captureClicks: false });

    show();

    expect(
      await screen.findByRole('switch', { name: /Record what people click/ }),
    ).not.toBeChecked();
  });

  it('asks for nothing until the panel is opened', async () => {
    const engine = engineHolding();

    show({ open: false });

    expect(engine.count).toBe(0);
  });

  it('turns recording off and says so', async () => {
    engineHolding({ captureClicks: true });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('switch')).not.toBeChecked();
  });

  /**
   * A setting left out is left as it was, so a panel that has never heard of a setting cannot
   * switch it off by saving.
   */
  it('sends only the setting that is being changed', async () => {
    const engine = engineHolding({ captureClicks: true });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ captureClicks: false });
  });

  /**
   * A cookie the browser returns on its own is not proof that this page meant to send the change,
   * so the pair the engine issued travels with it.
   */
  it('proves where the change came from', async () => {
    const engine = engineHolding({ captureClicks: true });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(proofOn(changes(engine)[0])).toBe('proof-value');
  });

  it('says the settings could not be read rather than showing a guess', async () => {
    engineStopped();

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  });

  /**
   * Placeholders and a refusal at the same time say two opposite things at once: that the answer
   * is still coming, and that it is not. Once the read has failed there is nothing on its way.
   */
  it('stops looking as though the settings are still on their way', async () => {
    engineStopped();

    const { container } = show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(container.querySelectorAll('.animate-pulse')).toHaveLength(0);
  });

  it('says a change could not be saved rather than pretending it was', async () => {
    let asked = 0;

    engineDoing(async (path, init) => {
      asked += 1;

      if (init.method === 'PUT') {
        throw new TypeError('Failed to fetch');
      }

      return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
    });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('switch')).toBeChecked();
    expect(asked).toBeGreaterThan(1);
  });
});

/** An engine that gives these answers about a reading in turn, and stays on the last of them. */
function engineReading(
  answers: readonly SitemapStatus[],
  held: Partial<StoredSettings> = { sitemapAddress: SITEMAP },
): Engine {
  let settings: StoredSettings = { ...STORED, ...held };
  let asked = 0;

  return engineDoing(async (path, init) => {
    if (path.endsWith('/sitemap')) {
      asked += 1;

      return respondWith(200, answers[Math.min(asked, answers.length) - 1]);
    }

    if (init.method === 'PUT') {
      settings = changedBy(settings, JSON.parse(String(init.body)) as Partial<StoredSettings>);
    }

    return respondWith(200, settings);
  });
}

/**
 * The panel, closed and then opened on a cache set up the way the dashboard's own is.
 *
 * The cache a test screen gets asks again as eagerly as the library does unless told otherwise, and
 * the dashboard's does not. A test about when this question is asked again needs the one it will
 * really meet, or it would pass for the library's reasons rather than this screen's.
 */
function openedOnTheDashboardsCache() {
  const page = show({ open: false });

  page.cache.setDefaultOptions({
    queries: { retry: false, refetchOnWindowFocus: false, staleTime: 30_000 },
  });
  page.rerender(<SiteSettings open onClose={() => {}} site={SITE} onRemoved={() => {}} />);

  return page;
}

/** Every time the engine was asked how reading the sitemap is going. */
function readingsAsked(engine: Engine): number {
  return engine.all().filter((sent) => sent.path.endsWith('/sitemap')).length;
}

function sitemapBox(): Promise<HTMLElement> {
  return screen.findByRole('textbox', { name: 'Sitemap address' });
}

function saveAddress(): Promise<HTMLElement> {
  return screen.findByRole('button', { name: 'Save address' });
}

/** Types a new address over whatever is in the box. */
async function retype(to: string): Promise<void> {
  const box = await sitemapBox();

  await userEvent.clear(box);
  await userEvent.type(box, to);
}

/**
 * Puts something in the box the way a browser does when somebody types or pastes it.
 *
 * A browser keeps the space round what was typed into a box for a web address, and the document
 * these tests run against takes it off. So what the box reports is stated here, or a test of what the
 * panel does with that space would pass whatever the panel did.
 */
function pasteIntoBox(box: HTMLElement, pasted: string): void {
  Object.defineProperty(box, 'value', { configurable: true, get: () => pasted, set: () => {} });
  fireEvent.input(box);
}

/**
 * The line saying how a reading is going, once it holds this sentence.
 *
 * Found by what it says, because the panel carries a second announcement beside it while a save has
 * just gone through.
 */
async function standing(sentence: string): Promise<HTMLElement> {
  return (await screen.findByText(sentence)).closest('[role="status"]') as HTMLElement;
}

describe('where a website lists its posts', () => {
  /**
   * A setting left out is left as it was, and naming a sitemap sends this engine off to read
   * somebody's website. So nothing is sent until the address differs from the one in force, and what
   * is sent is the address and nothing else.
   */
  it('sends the sitemap address only when it changed', async () => {
    offering();
    const engine = engineHolding({ sitemapAddress: SITEMAP });

    show();

    expect(await sitemapBox()).toHaveValue(SITEMAP);
    expect(await saveAddress()).toBeDisabled();

    await retype(SITEMAP);
    expect(await saveAddress()).toBeDisabled();

    await retype(ANOTHER_SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ sitemapAddress: ANOTHER_SITEMAP });
  });

  /** Copied from a browser's address bar or a page, an address often brings space along with it. */
  it('takes no notice of space pasted round an address', async () => {
    offering();
    const engine = engineHolding({ sitemapAddress: SITEMAP });

    show();

    pasteIntoBox(await sitemapBox(), `  ${SITEMAP}\n`);
    expect(await saveAddress()).toBeDisabled();

    pasteIntoBox(await sitemapBox(), `  ${ANOTHER_SITEMAP}\n`);
    await userEvent.click(await saveAddress());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ sitemapAddress: ANOTHER_SITEMAP });
  });

  it('takes a box left holding only space as emptied', async () => {
    offering();
    const engine = engineHolding({ sitemapAddress: SITEMAP });

    show();

    pasteIntoBox(await sitemapBox(), '   ');
    await userEvent.click(await screen.findByRole('button', { name: 'Remove sitemap' }));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ sitemapAddress: '' });
  });

  /** An impatient Enter in a box that says what is already stored is not a change. */
  it('sends nothing when Enter is pressed on the address already in force', async () => {
    offering();
    const engine = engineHolding({ sitemapAddress: SITEMAP });

    show();
    await userEvent.type(await sitemapBox(), '{Enter}');

    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));

    expect(changes(engine)).toHaveLength(0);
  });

  /**
   * Null means unchanged on the way in, as it does for every other setting, so a box emptied into a
   * null would look as though it had worked and leave the sitemap being read.
   */
  it('removes the sitemap when the box is emptied', async () => {
    offering();
    const engine = engineHolding({ sitemapAddress: SITEMAP });

    show();
    await userEvent.clear(await sitemapBox());
    await userEvent.click(await screen.findByRole('button', { name: 'Remove sitemap' }));

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ sitemapAddress: '' });

    expect(await screen.findByText('Add your sitemap to count your posts.')).toBeInTheDocument();
    expect(await sitemapBox()).toHaveValue('');
  });

  it('offers nothing to remove while no sitemap is named', async () => {
    offering();
    engineHolding();

    show();

    expect(await sitemapBox()).toHaveValue('');
    expect(await saveAddress()).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Remove sitemap' })).not.toBeInTheDocument();
  });

  it('says the change went through', async () => {
    offering();
    engineHolding();

    show();
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    expect(await screen.findByText('Saved')).toHaveRole('status');
  });

  it('stops saying so as soon as something else is being typed', async () => {
    offering();
    engineHolding();

    show();
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());
    await screen.findByText('Saved');

    await userEvent.type(await sitemapBox(), '/');

    expect(screen.queryByText('Saved')).not.toBeInTheDocument();
  });

  /** The engine keeps one spelling of an address, and the box comes to rest on it. */
  it('settles on the spelling the engine keeps, even of the address already in force', async () => {
    offering();
    engineDoing(async (path) => {
      if (path.endsWith('/sitemap')) {
        return respondWith(200, readingOf(SITEMAP));
      }

      return respondWith(200, { ...STORED, sitemapAddress: SITEMAP });
    });

    show();
    await retype('https://EXAMPLE.com/sitemap.xml');
    await userEvent.click(await saveAddress());

    await waitFor(async () => expect(await sitemapBox()).toHaveValue(SITEMAP));
    expect(await saveAddress()).toBeDisabled();
  });

  /**
   * The box and the name above it are two saves. Somebody who came to point at a new sitemap has not
   * thereby renamed their website, and somebody renaming it has not sent this engine off to read
   * whatever was half typed below.
   */
  it('saves apart from the name above it, and leaves each half-typed for the other', async () => {
    offering();
    const engine = engineHolding();

    show();
    await rename('Reader Weekly');
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ sitemapAddress: SITEMAP });
    expect(await nameBox()).toHaveValue('Reader Weekly');

    await retype(ANOTHER_SITEMAP);
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(2));
    expect(sentIn(changes(engine)[1])).toStrictEqual({ displayName: 'Reader Weekly' });
    expect(await sitemapBox()).toHaveValue(ANOTHER_SITEMAP);
  });

  it('keeps a half-typed address when the recording switch is used beside it', async () => {
    offering();
    engineHolding({ sitemapAddress: SITEMAP });

    show();
    await retype(ANOTHER_SITEMAP);
    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('switch')).not.toBeChecked();
    expect(await sitemapBox()).toHaveValue(ANOTHER_SITEMAP);
    expect(await saveAddress()).toBeEnabled();
  });

  /** What arrives in the box is an address on any phone, in any language this ever reads in. */
  it('asks for an address the way a phone keyboard can help with, written left to right', async () => {
    offering();
    engineHolding();

    show();

    const box = await sitemapBox();

    expect(box).toHaveAttribute('type', 'url');
    expect(box).toHaveAttribute('inputmode', 'url');
    expect(box).toHaveAttribute('dir', 'ltr');
    expect(box).toHaveAttribute('autocapitalize', 'none');
    expect(box).toHaveAttribute('autocorrect', 'off');
    expect(box).toHaveAttribute('placeholder', 'https://example.com/sitemap.xml');
  });

  /**
   * Nothing could make these right, so nothing is gained by sending them. Whether an address is on
   * this website is another matter, which only the engine can say: a website can be published at
   * addresses this screen has never heard of.
   */
  it.each(['example.com/sitemap.xml', 'sitemap.xml', 'ftp://example.com/sitemap.xml'])(
    'stops %s here, saying what to do, and sends nothing',
    async (address) => {
      offering();
      const engine = engineHolding();

      show();
      await retype(address);
      await userEvent.click(await saveAddress());

      const refusal = await screen.findByRole('alert');

      expect(refusal).toHaveTextContent(
        "Use the full address of this website's own sitemap, starting with https://.",
      );
      expect(await sitemapBox()).toHaveAttribute('aria-invalid', 'true');
      expect(changes(engine)).toHaveLength(0);
    },
  );

  it('takes the refusal back as soon as the address is being put right', async () => {
    offering();
    engineHolding();

    show();
    await retype('example.com/sitemap.xml');
    await userEvent.click(await saveAddress());
    await screen.findByRole('alert');

    await userEvent.type(await sitemapBox(), '/');

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(await sitemapBox()).toHaveAttribute('aria-invalid', 'false');
  });

  /**
   * What is typed while a save is on its way would be wiped when the answer arrives, because the box
   * comes to rest on what the engine says it now holds.
   */
  it('holds the address still while it is being saved', async () => {
    offering();
    let answerTheSave: (answer: Response) => void = () => {};

    engineDoing(async (path, init) => {
      if (init.method === 'PUT') {
        return new Promise<Response>((resolve) => {
          answerTheSave = resolve;
        });
      }

      return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
    });

    show();
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(async () => expect(await sitemapBox()).toHaveAttribute('readonly'));
    await userEvent.type(await sitemapBox(), 'x');
    expect(await sitemapBox()).toHaveValue(SITEMAP);

    answerTheSave(respondWith(200, { ...STORED, sitemapAddress: SITEMAP }));

    await waitFor(async () => expect(await sitemapBox()).not.toHaveAttribute('readonly'));
  });

  /** Between what a website records and stopping altogether, which is the last thing on the panel. */
  it('sits under the switch and above the way to remove the website', async () => {
    offering();
    engineHolding();

    show();

    const box = await sitemapBox();
    const recording = await screen.findByRole('switch');
    const removal = await screen.findByRole('button', { name: 'Remove website' });

    expect(recording.compareDocumentPosition(box)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(box.compareDocumentPosition(removal)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  /** An address on another host may be right, so it is the engine that is asked and that refuses. */
  it('a refusal from the engine is shown with its catalogue sentence', async () => {
    offering();
    engineDoing(async (path, init) => {
      if (init.method === 'PUT') {
        return respondWith(400, {
          title: 'That sitemap address could not be saved.',
          problems: [{ code: 'SiteSitemapRejected', description: 'The engine says no.' }],
        });
      }

      return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
    });

    show();
    await retype('https://elsewhere.example.org/sitemap.xml');
    await userEvent.click(await saveAddress());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "Use the full address of this website's own sitemap, starting with https://.",
    );
    expect(screen.queryByText('The engine says no.')).not.toBeInTheDocument();
    expect(screen.queryByText('Saved')).not.toBeInTheDocument();

    const box = await sitemapBox();

    expect(box).toHaveValue('https://elsewhere.example.org/sitemap.xml');
    expect(box).toHaveAttribute('aria-invalid', 'true');
  });

  it('takes the engine’s refusal back as soon as the address is being put right', async () => {
    offering();
    refusing('PUT', 400, { code: 'SiteSitemapRejected', description: 'The engine says no.' });

    show();
    await retype('https://elsewhere.example.org/sitemap.xml');
    await userEvent.click(await saveAddress());
    await screen.findByRole('alert');

    await userEvent.type(await sitemapBox(), '/');

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /** Nothing about the address was refused, so nothing about the address is said to have been. */
  it('says a save did not go through, and leaves the address alone, when it was not refused', async () => {
    offering();
    engineDoing(async (path, init) => {
      if (init.method === 'PUT') {
        throw new TypeError('Failed to fetch');
      }

      return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
    });

    show();
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    expect(await screen.findByRole('alert')).toHaveTextContent("Can't reach Dewiride Analytics");

    const box = await sitemapBox();

    expect(box).toHaveValue(SITEMAP);
    expect(box).toHaveAttribute('aria-invalid', 'false');
  });

  it('proves where the change came from', async () => {
    offering();
    const engine = engineHolding();

    show();
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(proofOn(changes(engine)[0])).toBe('proof-value');
  });
});

/** One way a reading can stand, and every sentence that has to be said about it. */
interface Standing {
  readonly name: string;
  readonly status: Partial<SitemapStatus>;
  readonly sentences: readonly string[];
}

const PROBLEM_SENTENCE = "Your website didn't answer. Check that the address opens in a browser.";

const STANDINGS: readonly Standing[] = [
  {
    name: 'nothing named yet',
    status: { address: null, state: 'none' },
    sentences: ['Add your sitemap to count your posts.'],
  },
  {
    name: 'reading switched off, nothing read',
    status: { state: 'off' },
    sentences: [
      'Reading sitemaps is switched off on this server',
      'Whoever runs this server can switch it on.',
    ],
  },
  {
    name: 'reading switched off after a reading',
    status: { state: 'off', posts: 128, dated: 120, readAt: hoursAgo(2), succeededAt: hoursAgo(2) },
    sentences: [
      'Reading sitemaps is switched off on this server',
      'Whoever runs this server can switch it on.',
      '128 posts found so far, last read 2 hours ago.',
    ],
  },
  {
    name: 'being read for the first time',
    status: { state: 'waiting' },
    sentences: ['Reading your sitemap…'],
  },
  {
    name: 'read, every post dated',
    status: {
      state: 'read',
      posts: 128,
      dated: 128,
      readAt: hoursAgo(2),
      succeededAt: hoursAgo(2),
    },
    sentences: ['128 posts found', 'Last read 2 hours ago.'],
  },
  {
    name: 'read, one post',
    status: { state: 'read', posts: 1, dated: 1, readAt: hoursAgo(5), succeededAt: hoursAgo(5) },
    sentences: ['1 post found', 'Last read 5 hours ago.'],
  },
  {
    name: 'read, some posts undated',
    status: {
      state: 'read',
      posts: 128,
      dated: 120,
      readAt: hoursAgo(2),
      succeededAt: hoursAgo(2),
    },
    sentences: ['128 posts found', '120 have a publishing date.', 'Last read 2 hours ago.'],
  },
  {
    name: 'read, one post dated',
    status: { state: 'read', posts: 2, dated: 1, readAt: hoursAgo(2), succeededAt: hoursAgo(2) },
    sentences: ['2 posts found', '1 has a publishing date.', 'Last read 2 hours ago.'],
  },
  {
    name: 'read, no post dated',
    status: { state: 'read', posts: 128, dated: 0, readAt: hoursAgo(2), succeededAt: hoursAgo(2) },
    sentences: ['128 posts found', 'None have a publishing date.', 'Last read 2 hours ago.'],
  },
  {
    name: 'read, no posts in it',
    status: { state: 'read', readAt: hoursAgo(2), succeededAt: hoursAgo(2) },
    sentences: [
      'No posts found',
      'If you have some, check that this is the sitemap that lists them.',
      'Last read 2 hours ago.',
    ],
  },
  {
    name: 'read, some pages turned the reader away',
    status: {
      state: 'read',
      posts: 12,
      dated: 12,
      turnedAway: 3,
      readAt: hoursAgo(2),
      succeededAt: hoursAgo(2),
    },
    sentences: [
      '12 posts found',
      'Your website turned us away from 3 pages. If a firewall, a security plugin or your robots.txt file blocks DewirideAnalytics, let it through.',
      'Last read 2 hours ago.',
    ],
  },
  {
    name: 'read, every page turned the reader away',
    status: { state: 'read', turnedAway: 1, readAt: hoursAgo(2), succeededAt: hoursAgo(2) },
    sentences: [
      'No posts found',
      'Your website turned us away from 1 page. If a firewall, a security plugin or your robots.txt file blocks DewirideAnalytics, let it through.',
      'Last read 2 hours ago.',
    ],
  },
  {
    name: 'read, with pages still to go',
    status: {
      state: 'read',
      posts: 86,
      reading: 42,
      readAt: hoursAgo(1),
      succeededAt: hoursAgo(1),
    },
    sentences: ['86 posts found so far', '42 pages still to read.'],
  },
  {
    name: 'read, with one page to go and no posts yet',
    status: { state: 'read', reading: 1, readAt: hoursAgo(1), succeededAt: hoursAgo(1) },
    sentences: ['Reading your posts…', '1 page still to read.'],
  },
  {
    name: 'partly read',
    status: {
      state: 'partly-read',
      problem: 'unreachable',
      posts: 86,
      dated: 80,
      readAt: hoursAgo(1),
      succeededAt: hoursAgo(1),
    },
    sentences: [
      "86 posts found, but part of your sitemap couldn't be read",
      "Your website didn't answer when we asked for one of the files your sitemap lists.",
      "Last read 1 hour ago. We'll read it again.",
    ],
  },
  {
    name: 'partly read, one of the files it lists missing',
    status: {
      state: 'partly-read',
      problem: 'not-found',
      posts: 86,
      dated: 86,
      readAt: hoursAgo(1),
      succeededAt: hoursAgo(1),
    },
    sentences: [
      "86 posts found, but part of your sitemap couldn't be read",
      "One of the files your sitemap lists isn't there. Check that each file it lists opens in a browser.",
      "Last read 1 hour ago. We'll read it again.",
    ],
  },
  {
    name: 'partly read, no posts, pages to go',
    status: {
      state: 'partly-read',
      problem: 'addresses-with-queries',
      reading: 3,
      readAt: hoursAgo(1),
    },
    sentences: [
      "Part of your sitemap couldn't be read",
      "Your posts' addresses differ only after a question mark, so we can't tell them apart. Switch your website to readable post addresses.",
      '3 pages still to read.',
      "Last read 1 hour ago. We'll read it again.",
    ],
  },
  {
    name: 'failed, nothing ever read',
    status: { state: 'failed', problem: 'unreachable', readAt: hoursAgo(1) },
    sentences: [
      "We couldn't read your sitemap",
      PROBLEM_SENTENCE,
      "Last tried 1 hour ago. We'll try again.",
    ],
  },
  {
    name: 'failed after a reading that got through',
    status: {
      state: 'failed',
      problem: 'robots-disallowed',
      posts: 128,
      dated: 120,
      readAt: hoursAgo(1),
      succeededAt: hoursAgo(26),
    },
    sentences: [
      "We couldn't read your sitemap",
      "Your website's robots.txt file asks us not to read it. Allow DewirideAnalytics in that file.",
      '128 posts found so far, last read 1 day ago.',
      "Last tried 1 hour ago. We'll try again.",
    ],
  },
  {
    name: 'failed with posts kept from an earlier sitemap',
    status: { state: 'failed', problem: 'unreachable', posts: 1, readAt: hoursAgo(1) },
    sentences: [
      "We couldn't read your sitemap",
      PROBLEM_SENTENCE,
      '1 post found so far.',
      "Last tried 1 hour ago. We'll try again.",
    ],
  },
];

describe('how reading a sitemap is going', () => {
  describe('says what came of reading the sitemap in every state', () => {
    it.each(STANDINGS)('$name', async ({ status, sentences }) => {
      offering();
      const answer = { ...NOTHING_READ, ...status };

      engineReading([answer], { sitemapAddress: answer.address });

      show();

      const line = await standing(sentences[0] as string);

      expect([...line.querySelectorAll('p')].map((sentence) => sentence.textContent)).toStrictEqual(
        sentences,
      );
    });
  });

  /** Whichever problem it is, the words on the screen are a sentence and never the word itself. */
  it('never shows the words the engine answers in', async () => {
    offering();
    engineReading(
      [
        {
          ...NOTHING_READ,
          state: 'partly-read',
          problem: 'too-many-redirects',
          readAt: hoursAgo(1),
        },
      ],
      { sitemapAddress: SITEMAP },
    );

    show();

    const line = await standing("Part of your sitemap couldn't be read");

    expect(line).not.toHaveTextContent('partly-read');
    expect(line).not.toHaveTextContent('too-many-redirects');
  });

  /**
   * A clock on somebody's device can be behind the engine's, and a reading that ended a moment ago
   * would then be said to finish in a few seconds' time.
   */
  it('never says a reading finishes in the future, on a clock behind the engine’s', async () => {
    offering();
    engineReading([
      {
        ...NOTHING_READ,
        state: 'read',
        posts: 3,
        dated: 3,
        readAt: new Date(Date.now() + 30_000).toISOString(),
      },
    ]);

    show();

    expect(await screen.findByText('Last read 1 minute ago.')).toBeInTheDocument();
  });

  it('asks nothing about it until the panel is opened', async () => {
    const engine = engineReading([NOTHING_READ]);

    show({ open: false });

    expect(readingsAsked(engine)).toBe(0);
  });

  /** A status line is drawn and left alone: the answer changing is what gets it read out. */
  it('announces itself politely, from one place that stays there while it changes', async () => {
    offering();
    engineReading([NOTHING_READ]);

    show();

    expect(await standing('Reading your sitemap…')).toHaveRole('status');
  });

  /**
   * Nothing else can start this question again once the last answer said there was nothing more to
   * wait for, so without being asked again the new address would sit under the old one's verdict.
   */
  it('asks again after a save that changed the address, and not after any other', async () => {
    offering();
    const engine = engineReading([
      { ...NOTHING_READ, state: 'read', posts: 3, dated: 3, readAt: hoursAgo(2) },
      { ...NOTHING_READ, address: ANOTHER_SITEMAP },
    ]);

    show();
    await screen.findByText('3 posts found');
    expect(readingsAsked(engine)).toBe(1);

    await rename('Reader Weekly');
    await userEvent.click(await saveButton());
    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(readingsAsked(engine)).toBe(1);

    await retype(ANOTHER_SITEMAP);
    await userEvent.click(await saveAddress());

    expect(await screen.findByText('Reading your sitemap…')).toBeInTheDocument();
    expect(readingsAsked(engine)).toBe(2);
  });

  /**
   * The answer about the new address takes a moment to arrive, and the verdict on the old one is not
   * an answer about it. Somebody who just replaced an address the panel said was wrong must not be
   * told, under the new one, that it is still wrong.
   */
  it('takes the verdict on the old address away while the new one is being asked about', async () => {
    offering();
    let answerTheNew: (answer: Response) => void = () => {};
    let asked = 0;
    let settings: StoredSettings = { ...STORED, sitemapAddress: SITEMAP };

    engineDoing(async (path, init) => {
      if (path.endsWith('/sitemap')) {
        asked += 1;

        return asked === 1
          ? respondWith(200, {
              ...NOTHING_READ,
              state: 'failed',
              problem: 'not-found',
              readAt: hoursAgo(1),
            })
          : new Promise<Response>((resolve) => {
              answerTheNew = resolve;
            });
      }

      if (init.method === 'PUT') {
        settings = changedBy(settings, JSON.parse(String(init.body)) as Partial<StoredSettings>);
      }

      return respondWith(200, settings);
    });

    show();
    await screen.findByText("We couldn't read your sitemap");

    await retype(ANOTHER_SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(() =>
      expect(screen.queryByText("We couldn't read your sitemap")).not.toBeInTheDocument(),
    );

    answerTheNew(respondWith(200, { ...NOTHING_READ, address: ANOTHER_SITEMAP }));

    expect(await screen.findByText('Reading your sitemap…')).toBeInTheDocument();
  });

  /** An answer already on the screen is still the best there is, and the next beat asks again. */
  it('keeps what it knows when asking again fails, and recovers on its own', async () => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let asked = 0;

    engineDoing(async (path) => {
      if (path.endsWith('/sitemap')) {
        asked += 1;

        if (asked === 2) {
          throw new TypeError('Failed to fetch');
        }

        return respondWith(
          200,
          asked === 1
            ? NOTHING_READ
            : { ...NOTHING_READ, state: 'read', posts: 4, dated: 4, readAt: hoursAgo(0) },
        );
      }

      return respondWith(200, { ...STORED, sitemapAddress: SITEMAP });
    });

    show();
    await screen.findByText('Reading your sitemap…');

    await vi.advanceTimersByTimeAsync(5_000);
    expect(asked).toBe(2);
    expect(screen.getByText('Reading your sitemap…')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(5_000);
    expect(await screen.findByText('4 posts found')).toBeInTheDocument();
  });

  it('says so when it cannot find out, and leaves the address free to change', async () => {
    offering();
    engineDoing(async (path) => {
      if (path.endsWith('/sitemap')) {
        throw new TypeError('Failed to fetch');
      }

      return respondWith(200, { ...STORED, sitemapAddress: SITEMAP });
    });

    show();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(await sitemapBox()).toHaveValue(SITEMAP);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('how often it asks how a reading is going', () => {
  /** Lets the given number of seconds pass and says how many times the engine was asked since. */
  async function askedOver(engine: Engine, seconds: number): Promise<number> {
    const before = readingsAsked(engine);

    await vi.advanceTimersByTimeAsync(seconds * 1000);

    return readingsAsked(engine) - before;
  }

  it('keeps asking while the first reading is under way and stops when it ends', async () => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const engine = engineReading([
      NOTHING_READ,
      NOTHING_READ,
      { ...NOTHING_READ, state: 'read', posts: 4, dated: 4, readAt: hoursAgo(0) },
    ]);

    show();
    await screen.findByText('Reading your sitemap…');

    expect(await askedOver(engine, 5)).toBe(1);
    expect(screen.getByText('Reading your sitemap…')).toBeInTheDocument();

    expect(await askedOver(engine, 5)).toBe(1);
    expect(await screen.findByText('4 posts found')).toBeInTheDocument();

    expect(await askedOver(engine, 300)).toBe(0);
  });

  it('asks twice a minute while the pages are being read, and stops when they are done', async () => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const reading = { ...NOTHING_READ, state: 'read', posts: 4, readAt: hoursAgo(0) } as const;
    const engine = engineReading([
      { ...reading, reading: 2 },
      { ...reading, reading: 1 },
      { ...reading, dated: 4 },
    ]);

    show();
    await screen.findByText('2 pages still to read.');

    expect(await askedOver(engine, 25)).toBe(0);
    expect(await askedOver(engine, 5)).toBe(1);
    expect(await screen.findByText('1 page still to read.')).toBeInTheDocument();

    expect(await askedOver(engine, 30)).toBe(1);
    expect(await screen.findByText('4 posts found')).toBeInTheDocument();

    expect(await askedOver(engine, 300)).toBe(0);
  });

  it.each<readonly [string, Partial<SitemapStatus>]>([
    ['read to the end', { state: 'read', posts: 4, dated: 4, readAt: hoursAgo(1) }],
    ['partly read', { state: 'partly-read', problem: 'too-slow', readAt: hoursAgo(1) }],
    ['failed', { state: 'failed', problem: 'not-found', readAt: hoursAgo(1) }],
    ['switched off, with pages left unread', { state: 'off', reading: 40 }],
    ['not named', { address: null, state: 'none' }],
  ])('does not ask again once a reading is %s', async (_name, status) => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const answer = { ...NOTHING_READ, ...status };
    const engine = engineReading([answer], { sitemapAddress: answer.address });

    show();
    await waitFor(() => expect(readingsAsked(engine)).toBe(1));
    await screen.findAllByRole('status');

    expect(await askedOver(engine, 300)).toBe(0);
  });

  /** Somebody who went off to put their website right comes back to find out whether it worked. */
  it('asks once more when somebody comes back to the tab', async () => {
    offering();
    const engine = engineReading([
      { ...NOTHING_READ, state: 'failed', problem: 'not-found', readAt: hoursAgo(1) },
    ]);

    openedOnTheDashboardsCache();
    await screen.findByText("We couldn't read your sitemap");
    expect(readingsAsked(engine)).toBe(1);

    act(() => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
    });
    focusManager.setFocused(undefined);

    await waitFor(() => expect(readingsAsked(engine)).toBe(2));
  });

  /** A panel opened a moment ago says how things stand now, however recently it last asked. */
  it('asks afresh each time the panel is opened', async () => {
    offering();
    const engine = engineReading([
      { ...NOTHING_READ, state: 'read', posts: 4, dated: 4, readAt: hoursAgo(1) },
    ]);
    const page = openedOnTheDashboardsCache();

    await screen.findByText('4 posts found');
    expect(readingsAsked(engine)).toBe(1);

    page.rerender(
      <SiteSettings open={false} onClose={() => {}} site={SITE} onRemoved={() => {}} />,
    );
    page.rerender(<SiteSettings open onClose={() => {}} site={SITE} onRemoved={() => {}} />);

    await waitFor(() => expect(readingsAsked(engine)).toBe(2));
  });

  /** The panel is mounted whether or not it is open, and a closed one is watching nothing. */
  it('stops asking once the panel is closed', async () => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const engine = engineReading([NOTHING_READ]);
    const page = show();

    await screen.findByText('Reading your sitemap…');

    page.rerender(
      <SiteSettings open={false} onClose={() => {}} site={SITE} onRemoved={() => {}} />,
    );

    expect(await askedOver(engine, 300)).toBe(0);
  });
});

describe('where a website lists its posts, for somebody who may only read the numbers', () => {
  it('a viewer sees no input and no button', async () => {
    offering();
    engineReading([{ ...NOTHING_READ, state: 'read', posts: 3, dated: 3, readAt: hoursAgo(2) }]);

    show({ role: 'viewer' });

    await screen.findByText('3 posts found');

    expect(screen.queryByRole('textbox', { name: 'Sitemap address' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save address' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove sitemap' })).not.toBeInTheDocument();
  });

  /** It came from a website's settings, so it is shown as words and nothing on it can be pressed. */
  it('shows the address as text and never as a link', async () => {
    offering();
    engineReading([{ ...NOTHING_READ, state: 'read', posts: 3, dated: 3, readAt: hoursAgo(2) }]);

    const { container } = show({ role: 'viewer' });

    const address = await screen.findByText(SITEMAP);

    expect(address).toHaveAttribute('dir', 'ltr');
    expect(within(container).queryByRole('link')).not.toBeInTheDocument();
    expect(container.querySelector('a')).toBeNull();
  });

  /** Nothing they could do with an invitation to add one, so they are told who can. */
  it('says who can add one, rather than inviting somebody who cannot', async () => {
    offering();
    engineReading([readingOf(null)], { sitemapAddress: null });

    show({ role: 'viewer' });

    expect(
      await screen.findByText('No sitemap yet. Somebody who manages this website can add one.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Add your sitemap to count your posts.')).not.toBeInTheDocument();
  });

  it('is still told how it is going, and it is still asked again', async () => {
    offering();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const engine = engineReading([NOTHING_READ]);

    show({ role: 'viewer' });

    await screen.findByText('Reading your sitemap…');

    const before = readingsAsked(engine);

    await vi.advanceTimersByTimeAsync(5_000);

    expect(readingsAsked(engine)).toBe(before + 1);
  });
});

describe('where a website lists its posts, for somebody who manages it', () => {
  it.each(['owner', 'editor'] as const)('lets the %s change it', async (role) => {
    offering();
    const engine = engineHolding();

    show({ role });
    await retype(SITEMAP);
    await userEvent.click(await saveAddress());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
  });
});

describe('stopping measuring a website altogether', () => {
  it('offers nothing of the sort to somebody who may only read the numbers', async () => {
    offering();
    engineHolding();

    show({ role: 'viewer' });

    await nameBox();
    expect(screen.queryByRole('button', { name: 'Remove website' })).not.toBeInTheDocument();
  });

  /**
   * Changing what a website is called and deleting everything ever measured for it are not the
   * same size of decision, so they are not behind the same permission.
   */
  it('offers nothing of the sort to somebody who may change the settings but does not own it', async () => {
    offering();
    engineHolding();

    show({ role: 'editor' });

    await nameBox();
    expect(screen.queryByRole('button', { name: 'Remove website' })).not.toBeInTheDocument();
  });

  it('offers it to the owner', async () => {
    offering();
    engineHolding();

    show({ role: 'owner' });

    expect(await screen.findByRole('button', { name: 'Remove website' })).toBeInTheDocument();
  });

  it('asks for the address to be typed before anything can go', async () => {
    offering();
    const engine = engineHolding();

    show();
    await userEvent.click(await screen.findByRole('button', { name: 'Remove website' }));

    expect(
      await screen.findByRole('textbox', { name: 'Type example.com to confirm' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete everything' })).toBeDisabled();
    expect(removals(engine)).toHaveLength(0);
  });

  /**
   * Nothing here can be undone, so the address is the whole safety. A near miss has to be exactly
   * as inert as an empty box, including for somebody who finishes typing and presses Enter out of
   * habit rather than aiming at the button.
   */
  it('removes nothing while what was typed is not the address', async () => {
    offering();
    const engine = engineHolding();

    show();
    await askToRemove('example.co{Enter}');

    expect(screen.getByRole('button', { name: 'Delete everything' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    expect(removals(engine)).toHaveLength(0);
  });

  it('removes it once the address has been typed', async () => {
    offering();
    const engine = engineHolding();

    show();
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(removals(engine)).toHaveLength(1));
    expect(removals(engine)[0]?.path).toContain(SITE.id);
  });

  /**
   * A phone capitalises the first letter of a box on its own, so an address typed correctly on one
   * arrives with a capital on the front. Refusing that would leave the button dead with nothing on
   * the screen saying why.
   */
  it('takes the address however it was capitalised', async () => {
    offering();
    const engine = engineHolding();

    show();
    await askToRemove('Example.com');
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(removals(engine)).toHaveLength(1));
  });

  it('asks the phone keyboard not to capitalise or correct what is typed', async () => {
    offering();
    engineHolding();

    show();
    await userEvent.click(await screen.findByRole('button', { name: 'Remove website' }));

    const box = await screen.findByRole('textbox', { name: 'Type example.com to confirm' });

    expect(box).toHaveAttribute('autocapitalize', 'none');
    expect(box).toHaveAttribute('autocorrect', 'off');
  });

  it('proves where the removal came from', async () => {
    offering();
    const engine = engineHolding();

    show();
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(removals(engine)).toHaveLength(1));
    expect(proofOn(removals(engine)[0])).toBe('proof-value');
  });

  /**
   * The panel is showing a website that no longer exists the moment this succeeds, so it has to
   * tell the screen above it rather than wait to be closed.
   */
  it('tells the screen above it, so the panel is put away', async () => {
    offering();
    const removed = vi.fn();

    engineHolding();

    show({ onRemoved: removed });
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(removed).toHaveBeenCalledOnce());
  });

  /**
   * The last website somebody owns is kept, because a new one is added alongside an owned one and
   * removing the last would leave them unable to add another. That refusal has a reason of its
   * own, and it has to reach the screen as a sentence somebody can act on.
   */
  it('says why the only website somebody owns cannot go', async () => {
    offering();
    const removed = vi.fn();

    refusing('DELETE', 409, {
      code: 'SiteIsOnlyOne',
      description: 'It is the only website you own.',
    });

    show({ onRemoved: removed });
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    expect(
      await screen.findByText(
        "This is your only website, so it can't be removed. Add another one first.",
      ),
    ).toBeInTheDocument();
    expect(removed).not.toHaveBeenCalled();
  });

  it('says so when a removal was refused for a reason it has no words for', async () => {
    offering();
    const removed = vi.fn();

    engineDoing(async (path, init) => {
      if (init.method === 'DELETE') {
        return respondWith(403, { title: 'Not allowed.' });
      }

      return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
    });

    show({ onRemoved: removed });
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(removed).not.toHaveBeenCalled();
  });

  /**
   * The clicks card reads the same settings as this panel. Taking the card's copy away with the
   * website would send it straight back to ask about a website that no longer exists, and the
   * refusal would flash on a screen that is about to be replaced.
   */
  it('asks nothing more about a removed website while its clicks are still on the screen', async () => {
    offering();
    let removed = false;

    const engine = engineDoing(async (path, init) => {
      if (init.method === 'DELETE') {
        removed = true;

        return respondWith(204, null);
      }

      if (path.includes('/actions')) {
        return respondWith(200, NO_PRESSES);
      }

      if (path.endsWith('/sitemap')) {
        return respondWith(200, readingOf(null));
      }

      return removed && path.includes('/settings')
        ? respondWith(404, { title: 'There is no such website.' })
        : respondWith(200, STORED);
    });

    renderScreen(<RemovedWithItsClicksShowing />);
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(removals(engine)).toHaveLength(1));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    const asked = settingsReads(engine);

    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));

    expect(settingsReads(engine)).toBe(asked);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('an open panel drawn by a server whose time zones are not the browser’s', () => {
  /** The panel as a page that had already read the website's settings would send it. */
  function drawnOpen() {
    engineHolding();

    return hydrated(<SiteSettings open onClose={() => {}} site={SITE} onRemoved={() => {}} />, {
      server: STALE_SERVER,
      browser: CURRENT_BROWSER,
      known: (cache) => cache.setQueryData(siteSettingsKey(SITE.id), STORED),
    });
  }

  it('is taken over without either side giving way', () => {
    const page = drawnOpen();

    expect(page.html).toContain('Count its days in');
    expect(page.html).not.toContain('Casablanca');
    expect(page.recovered).not.toHaveBeenCalled();
    expect(page.complaints).not.toHaveBeenCalled();
  });

  it('offers this browser’s zones, holding the one the website is counted in', async () => {
    drawnOpen();

    expect(await zonePicker()).toHaveValue('Europe/London');
    expect(screen.getByRole('option', { name: 'London (GMT)' })).toBeInTheDocument();
  });
});

/** An engine that refuses one kind of request with a named reason and answers the rest normally. */
function refusing(
  method: 'PUT' | 'DELETE',
  status: number,
  reason: { readonly code: string; readonly description: string },
): Engine {
  return engineDoing(async (path, init) => {
    if (init.method === method) {
      return respondWith(status, { title: 'That could not be done.', problems: [reason] });
    }

    return respondWith(200, path.endsWith('/sitemap') ? readingOf(null) : STORED);
  });
}

/** Every change the panel asked the engine to make. */
function changes(engine: Engine): readonly Sent[] {
  return engine.all().filter((sent) => sent.init.method === 'PUT');
}

/** Every removal the panel asked the engine for. */
function removals(engine: Engine): readonly Sent[] {
  return engine.all().filter((sent) => sent.init.method === 'DELETE');
}

/** How many times the website's settings were read. */
function settingsReads(engine: Engine): number {
  return engine
    .all()
    .filter((sent) => sent.path.includes('/settings') && (sent.init.method ?? 'GET') === 'GET')
    .length;
}

/** A week in which nobody pressed anything, as the clicks card is answered. */
const NO_PRESSES = {
  from: '2026-08-11T00:00:00+00:00',
  to: '2026-08-18T00:00:00+00:00',
  grouping: 'control',
  presses: 0,
  totalControls: 0,
  mostPresses: 0,
  controls: [],
};

/**
 * The clicks card and this panel on one screen, the way the overview has them, with removing the
 * website putting the panel away.
 */
function RemovedWithItsClicksShowing() {
  const [open, setOpen] = useState(true);

  return (
    <>
      <SiteActions
        siteId={SITE.id}
        window={{ from: NO_PRESSES.from, to: NO_PRESSES.to }}
        population="everybody"
        mayChangeSettings
        onShowSettings={() => setOpen(true)}
      />
      <SiteSettings
        open={open}
        onClose={() => setOpen(false)}
        site={SITE}
        onRemoved={() => setOpen(false)}
      />
    </>
  );
}

/** What a request actually carried, read back as the object it was sent as. */
function sentIn(request: Sent | undefined): unknown {
  if (!request) {
    throw new Error('Nothing was sent to the engine.');
  }

  return JSON.parse(String(request.init.body));
}

/** The proof-of-origin value a request travelled with. */
function proofOn(request: Sent | undefined): string | undefined {
  if (!request) {
    throw new Error('Nothing was sent to the engine.');
  }

  return (request.init.headers as Record<string, string> | undefined)?.['X-Csrf-Token'];
}

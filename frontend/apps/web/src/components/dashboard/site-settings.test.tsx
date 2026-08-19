import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SiteSettings } from '@/components/dashboard/site-settings';
import type { Site, SiteSettings as StoredSettings } from '@/lib/api/schemas';
import { type Engine, engineDoing, engineStopped, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

afterEach(() => {
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
};

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

  return engineDoing(async (_path, init) => {
    if (init.method === 'DELETE') {
      return respondWith(204, null);
    }

    if (init.method === 'PUT') {
      settings = { ...settings, ...(JSON.parse(String(init.body)) as Partial<StoredSettings>) };
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

    expect(await screen.findByRole('status')).toHaveTextContent('Saved');
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
    engineDoing(async (_path, init) =>
      respondWith(200, {
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
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
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

describe('a website counted in a zone this browser spells differently', () => {
  /**
   * Platforms disagree about zone names: the same place is `Asia/Calcutta` on one and
   * `Asia/Kolkata` on another. A picker that cannot offer the zone a website is actually counted
   * in opens on whatever it does have, and the first thing ever saved from this form then moves
   * the website's day boundary without anybody having asked for it.
   */
  it('still offers the zone the website is counted in', async () => {
    offering();
    engineHolding({ timeZoneId: 'Asia/Calcutta' });

    show();

    expect(await zonePicker()).toHaveValue('Asia/Calcutta');
    expect(screen.getByRole('option', { name: 'Calcutta (GMT+5:30)' })).toBeInTheDocument();
  });

  it('does not move the day boundary when only the name was changed', async () => {
    offering();
    const engine = engineHolding({ timeZoneId: 'Asia/Calcutta' });

    show();
    await rename('Reader Weekly');
    await userEvent.click(await saveButton());

    await waitFor(() => expect(changes(engine)).toHaveLength(1));
    expect(sentIn(changes(engine)[0])).toStrictEqual({ displayName: 'Reader Weekly' });
    expect(await zonePicker()).toHaveValue('Asia/Calcutta');
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

    engineDoing(async (_path, init) => {
      asked += 1;

      if (init.method === 'PUT') {
        throw new TypeError('Failed to fetch');
      }

      return respondWith(200, STORED);
    });

    show();

    await userEvent.click(await screen.findByRole('switch'));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('switch')).toBeChecked();
    expect(asked).toBeGreaterThan(1);
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

    engineDoing(async (_path, init) =>
      init.method === 'DELETE'
        ? respondWith(403, { title: 'Not allowed.' })
        : respondWith(200, STORED),
    );

    show({ onRemoved: removed });
    await askToRemove(SITE.domain);
    await userEvent.click(screen.getByRole('button', { name: 'Delete everything' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(removed).not.toHaveBeenCalled();
  });
});

/** An engine that refuses one kind of request with a named reason and answers the rest normally. */
function refusing(
  method: 'PUT' | 'DELETE',
  status: number,
  reason: { readonly code: string; readonly description: string },
): Engine {
  return engineDoing(async (_path, init) =>
    init.method === method
      ? respondWith(status, { title: 'That could not be done.', problems: [reason] })
      : respondWith(200, STORED),
  );
}

/** Every change the panel asked the engine to make. */
function changes(engine: Engine): readonly Sent[] {
  return engine.all().filter((sent) => sent.init.method === 'PUT');
}

/** Every removal the panel asked the engine for. */
function removals(engine: Engine): readonly Sent[] {
  return engine.all().filter((sent) => sent.init.method === 'DELETE');
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

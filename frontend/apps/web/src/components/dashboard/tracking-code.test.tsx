import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import type { SiteRole } from '@/lib/api/schemas';
import { WORDPRESS_PLUGIN } from '@/lib/downloads';
import { engineDoing, respondWith } from '@/test/engine';
import { renderScreen } from '@/test/harness';

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';

/** A key the plugin reports with, as the engine describes it. */
const WORDPRESS_KEY = {
  id: '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c78',
  name: 'WordPress',
  preview: 'wxyz',
  createdAt: '2026-11-01T00:00:00+00:00',
  lastUsedAt: null,
};

/** A secret shaped as the engine issues them, and plainly not a real one. */
const SECRET = 'dwk_wordpresswordpresswordpresswordpresswordp';

/**
 * An engine holding the website's keys, which creates one the way the real one does.
 *
 * @param existing The keys the website already has.
 */
function engineWith(existing: readonly unknown[] = []) {
  const held = [...existing];

  return engineDoing(async (_path, init) => {
    if (init.method === 'POST') {
      const created = {
        ...WORDPRESS_KEY,
        id: '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c79',
        preview: 'rdp9',
      };
      held.push(created);

      return respondWith(200, { key: created, secret: SECRET });
    }

    return respondWith(200, held);
  });
}

/**
 * Somebody using the screen, and the clipboard they are copying to.
 *
 * The order matters: setting up a person replaces the clipboard with one of its own, so ours has
 * to go on afterwards or every copy lands somewhere this test cannot see.
 */
function person() {
  const acting = userEvent.setup();
  const writeText = vi.fn((_text: string) => Promise.resolve());

  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

  return { acting, writeText };
}

function show(onClose = () => {}, role: SiteRole = 'editor') {
  return renderScreen(
    <TrackingCode
      open
      onClose={onClose}
      siteId={SITE}
      siteDomain="example.com"
      siteRole={role}
      timeZoneId="Etc/UTC"
    />,
  );
}

/**
 * The panel opened on the steps for a WordPress website, for somebody with the given role on a
 * website with the given keys.
 */
async function showWordPress(role: SiteRole = 'editor', keys: readonly unknown[] = []) {
  const someone = person();
  const engine = engineWith(keys);

  show(() => {}, role);
  await someone.acting.click(screen.getByRole('radio', { name: 'WordPress' }));

  return { ...someone, engine };
}

/** The panel as a screen actually holds it: opened and closed from outside. */
function Panel() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      <TrackingCode
        open={open}
        onClose={() => setOpen(false)}
        siteId={SITE}
        siteDomain="example.com"
        siteRole="editor"
        timeZoneId="Etc/UTC"
      />
    </>
  );
}

describe('the tracking code somebody is given', () => {
  /**
   * The address is the one the reader is looking at, never one written into configuration. A
   * self-hoster reaches the dashboard on whatever name they gave it, and a second place to record
   * that is a second place for it to be wrong.
   */
  it('points at the address the dashboard is being read on', () => {
    show();

    expect(screen.getByRole('dialog')).toHaveTextContent(`${window.location.origin}/dw.js`);
  });

  it('names the website it belongs to', () => {
    show();

    expect(screen.getByRole('dialog')).toHaveTextContent(SITE);
    expect(screen.getByText(/into example.com once/)).toBeInTheDocument();
  });

  /**
   * A reader whose browser runs no scripts is a reader, and the second line is the only way they
   * are ever counted.
   */
  it('includes the line that counts a reader whose browser runs no scripts', () => {
    show();

    const dialog = screen.getByRole('dialog');

    expect(dialog).toHaveTextContent('noscript');
    expect(dialog).toHaveTextContent('referrerpolicy="no-referrer-when-downgrade"');
  });

  it('hands the whole thing over when it is copied, and says so', async () => {
    const { acting, writeText } = person();

    show();

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledOnce();
    expect(writeText.mock.calls[0]?.[0]).toContain('<script defer');
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
  });

  /**
   * Copying is refused on an address the browser does not consider secure. The code is on screen
   * and can be selected, so there is still a way through and nothing worth interrupting for.
   */
  it('says nothing alarming when the browser refuses to copy', async () => {
    const { acting, writeText } = person();

    writeText.mockRejectedValueOnce(new Error('refused'));
    show();

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
  });

  it('can be closed', async () => {
    const { acting } = person();
    const closed = vi.fn();

    show(closed);

    await acting.click(screen.getByRole('button', { name: 'Close' }));

    expect(closed).toHaveBeenCalledOnce();
  });
});

describe('the steps for a WordPress website', () => {
  /**
   * Two ways to the same place, and what changes is which answer is shown rather than which panel
   * is open — so a labelled radio group, opening on the way that works on every website.
   */
  it('is offered beside the way that works on any website', () => {
    show();

    const choice = screen.getByRole('radiogroup', { name: 'Instructions for' });

    expect(within(choice).getByRole('radio', { name: 'Any website' })).toBeChecked();
    expect(within(choice).getByRole('radio', { name: 'WordPress' })).not.toBeChecked();
  });

  it('numbers what there is to do, in the order it is done', async () => {
    await showWordPress();

    const steps = within(screen.getByRole('list')).getAllByRole('listitem');

    expect(screen.getByRole('radio', { name: 'WordPress' })).toBeChecked();
    expect(
      steps.map((step) => within(step).getByRole('heading', { level: 3 }).textContent),
    ).toEqual([
      'Download the plugin',
      'Add it to WordPress',
      'Connect it',
      'Remove any code you added before',
      'See robots and AI crawlers too',
    ]);
  });

  /**
   * Served by the dashboard itself, which is the one address certain to be reachable by whoever
   * is reading it, and labelled with the version inside the file.
   */
  it('downloads the plugin from the dashboard, and says which version it is', async () => {
    await showWordPress();

    const download = screen.getByRole('link', { name: 'Download' });

    expect(download).toHaveAttribute('href', '/downloads/dewiride-analytics.zip');
    expect(download).toHaveAttribute('download');
    expect(screen.getByText(`Version ${WORDPRESS_PLUGIN.version}`)).toBeInTheDocument();
  });

  /**
   * These are followed on another screen, looking for each name in turn, so every name is the one
   * WordPress prints there.
   */
  it('names each thing to press the way WordPress does', async () => {
    await showWordPress();

    const steps = screen.getByRole('list');

    for (const name of [
      'Plugins',
      'Add Plugin',
      'Upload Plugin',
      'Install Now',
      'Activate Plugin',
      'Settings',
      'Dewiride Analytics',
    ]) {
      expect(within(steps).getByText(name)).toBeInTheDocument();
    }

    expect(screen.getByText('Replace current with uploaded')).toBeInTheDocument();
  });

  /**
   * The plugin reports where the two lines would have, which is the address the reader is looking
   * at, for the website the panel was opened on.
   */
  it('gives the address and the website identifier to paste into WordPress', async () => {
    await showWordPress();

    expect(screen.getByText('Address')).toBeInTheDocument();
    expect(screen.getByText(window.location.origin)).toBeInTheDocument();
    expect(screen.getByText('Website ID')).toBeInTheDocument();
    expect(screen.getByText(SITE)).toBeInTheDocument();
  });

  it('copies each of them on its own, and says which was copied', async () => {
    const { acting, writeText } = await showWordPress();

    await acting.click(screen.getByRole('button', { name: 'Copy Address' }));

    expect(writeText).toHaveBeenLastCalledWith(window.location.origin);
    expect(await screen.findByRole('button', { name: 'Address copied' })).toBeInTheDocument();

    await acting.click(screen.getByRole('button', { name: 'Copy Website ID' }));

    expect(writeText).toHaveBeenLastCalledWith(SITE);
    expect(await screen.findByRole('button', { name: 'Website ID copied' })).toBeInTheDocument();
  });

  it('puts the two lines back when any website is chosen again', async () => {
    const { acting } = await showWordPress();

    expect(screen.getByRole('dialog')).not.toHaveTextContent('noscript');

    await acting.click(screen.getByRole('radio', { name: 'Any website' }));

    expect(screen.getByRole('dialog')).toHaveTextContent(`${window.location.origin}/dw.js`);
    expect(screen.queryByRole('link', { name: 'Download' })).not.toBeInTheDocument();
  });

  /** Somebody who closes the panel half-way through comes back to the steps they were following. */
  it('is still chosen when the panel is opened again', async () => {
    const { acting } = person();

    engineWith();
    renderScreen(<Panel />);

    await acting.click(screen.getByRole('button', { name: 'Open' }));
    await acting.click(screen.getByRole('radio', { name: 'WordPress' }));
    await acting.click(screen.getByRole('button', { name: 'Close' }));
    await acting.click(screen.getByRole('button', { name: 'Open' }));

    expect(screen.getByRole('radio', { name: 'WordPress' })).toBeChecked();
    expect(screen.getByRole('link', { name: 'Download' })).toBeInTheDocument();
  });
});

describe('the key that lets WordPress report robots too', () => {
  /**
   * Made where somebody setting up WordPress already is, named for what uses it, and shown in full
   * the once it exists anywhere but in WordPress.
   */
  it('is created here, named for WordPress, and shown once with where it goes', async () => {
    const { acting, engine, writeText } = await showWordPress();

    await acting.click(await screen.findByRole('button', { name: 'Create a key for WordPress' }));

    expect(await screen.findByText(SECRET)).toBeInTheDocument();
    expect(screen.getByText(SECRET).closest('[tabindex="-1"]')).toHaveFocus();
    expect(
      engine
        .all()
        .some(
          ({ init }) =>
            init.method === 'POST' && init.body === JSON.stringify({ name: 'WordPress' }),
        ),
    ).toBe(true);
    expect(screen.getByText('Server key')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create a new key' })).not.toBeInTheDocument();
    expect(screen.getByText('Check connection')).toBeInTheDocument();

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenLastCalledWith(SECRET);
  });

  /** Somebody who may only look is told who can, rather than shown a button that would be refused. */
  it('is left to an editor or the owner', async () => {
    await showWordPress('viewer');

    expect(screen.getByText(/Ask an editor or the owner of example\.com/)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create a key for WordPress' }),
    ).not.toBeInTheDocument();
  });

  /** Once a key exists, when it was last used is how somebody tells the plugin is reporting. */
  it('says when the WordPress key was last used, by its ending', async () => {
    await showWordPress('editor', [{ ...WORDPRESS_KEY, lastUsedAt: '2026-11-14T09:30:00+00:00' }]);

    expect(
      await screen.findByText('Your WordPress key, ending in wxyz, was last used on Nov 14, 2026.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create a new key' })).toBeInTheDocument();
  });

  it('says plainly when the WordPress key has not been used yet', async () => {
    await showWordPress('editor', [
      WORDPRESS_KEY,
      { ...WORDPRESS_KEY, id: '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c7a', name: 'Cloudflare' },
    ]);

    expect(
      await screen.findByText("Your WordPress key, ending in wxyz, hasn't been used yet."),
    ).toBeInTheDocument();
  });

  /** The secret goes with the panel, so opening it again cannot show a key already walked away from. */
  it('is not shown again once the panel has been closed', async () => {
    const { acting } = person();

    engineWith();
    renderScreen(<Panel />);

    await acting.click(screen.getByRole('button', { name: 'Open' }));
    await acting.click(screen.getByRole('radio', { name: 'WordPress' }));
    await acting.click(await screen.findByRole('button', { name: 'Create a key for WordPress' }));
    expect(await screen.findByText(SECRET)).toBeInTheDocument();

    await acting.click(screen.getByRole('button', { name: 'Close' }));
    await acting.click(screen.getByRole('button', { name: 'Open' }));

    expect(screen.queryByText(SECRET)).not.toBeInTheDocument();
  });
});

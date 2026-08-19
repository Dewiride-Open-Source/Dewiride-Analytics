import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ServerKeys } from '@/components/dashboard/server-keys';
import { engineDoing, respondWith, type Sent } from '@/test/engine';
import { renderScreen } from '@/test/harness';

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';
const KEY_ID = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c78';

const EXISTING = {
  id: KEY_ID,
  name: 'Cloudflare',
  preview: 'wxyz',
  createdAt: '2026-08-01T00:00:00+00:00',
  lastUsedAt: null,
};

const SECRET = 'dwk_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG';

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

/** An engine holding a list of keys that answers a creation and a removal the way the real one does. */
function engineWith(existing: readonly unknown[]) {
  const held = [...existing];

  return engineDoing(async (_path, init) => {
    if (init.method === 'POST') {
      const created = { ...EXISTING, id: '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c79', name: 'Netlify' };
      held.push(created);

      return respondWith(200, { key: created, secret: SECRET });
    }

    if (init.method === 'DELETE') {
      held.length = 0;

      return respondWith(204, null);
    }

    return respondWith(200, held);
  });
}

function show(existing: readonly unknown[] = []) {
  const engine = engineWith(existing);
  const rendered = renderScreen(
    <ServerKeys
      open
      onClose={() => {}}
      siteId={SITE}
      siteDomain="example.com"
      timeZoneId="Etc/UTC"
    />,
  );

  return { engine, ...rendered };
}

/** The panel as a screen actually holds it: opened and closed from outside. */
function Panel() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      <ServerKeys
        open={open}
        onClose={() => setOpen(false)}
        siteId={SITE}
        siteDomain="example.com"
        timeZoneId="Etc/UTC"
      />
    </>
  );
}

describe('the keys a website’s own server reports with', () => {
  it('says what a key is for, in terms of what it counts', async () => {
    show();

    expect(await screen.findByText(/Crawlers and AI assistants/)).toBeInTheDocument();
    expect(screen.getByText(/example\.com/)).toBeInTheDocument();
  });

  it('cannot create a key that has not been named', async () => {
    show();

    expect(await screen.findByRole('button', { name: 'Create key' })).toBeDisabled();
  });

  it('lists what is already reporting', async () => {
    show([EXISTING]);

    expect(await screen.findByText('Cloudflare')).toBeInTheDocument();
    expect(screen.getByText(/Not used yet/)).toBeInTheDocument();
  });

  /**
   * The question somebody is actually asking before they take a key away: is anything still
   * using it?
   */
  it('says when a key was last used', async () => {
    show([{ ...EXISTING, lastUsedAt: '2026-08-17T09:30:00+00:00' }]);

    expect(await screen.findByText(/Last used/)).toBeInTheDocument();
    expect(screen.queryByText(/Not used yet/)).not.toBeInTheDocument();
  });

  /**
   * Every other date on the dashboard is counted in the website's own zone, and this one has to
   * agree with them. Read where the reader happens to be sitting, a key created late one evening
   * appears to have been created the day before.
   */
  it('counts a key’s dates in the website’s own zone', async () => {
    engineWith([{ ...EXISTING, createdAt: '2026-08-01T21:00:00+00:00' }]);

    renderScreen(
      <ServerKeys
        open
        onClose={() => {}}
        siteId={SITE}
        siteDomain="example.com"
        timeZoneId="Asia/Kolkata"
      />,
    );

    // Nine at night in London is half past two the next morning in Kolkata.
    expect(await screen.findByText(/Added Aug 2, 2026/)).toBeInTheDocument();
  });

  it('says so when the list could not be read', async () => {
    engineDoing(async () => respondWith(500, { title: 'Nope' }));

    renderScreen(
      <ServerKeys
        open
        onClose={() => {}}
        siteId={SITE}
        siteDomain="example.com"
        timeZoneId="Etc/UTC"
      />,
    );

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  /**
   * "There are none" and "we could not find out" are different answers, and shown together the
   * first one tells somebody their keys are gone when they may be perfectly fine.
   */
  it('does not also claim there is nothing there', async () => {
    engineDoing(async () => respondWith(500, { title: 'Nope' }));

    renderScreen(
      <ServerKeys
        open
        onClose={() => {}}
        siteId={SITE}
        siteDomain="example.com"
        timeZoneId="Etc/UTC"
      />,
    );

    await screen.findByRole('alert');

    expect(
      screen.queryByText('Nothing is reporting from your server yet.'),
    ).not.toBeInTheDocument();
  });

  it('says plainly when nothing is reporting yet', async () => {
    show();

    expect(
      await screen.findByText('Nothing is reporting from your server yet.'),
    ).toBeInTheDocument();
  });

  /**
   * The secret exists in this one answer and nowhere else, so it has to reach the screen whole
   * rather than as the shortened form the list shows afterwards.
   */
  it('shows a new key in full, once', async () => {
    const { acting } = person();

    show();

    await acting.type(await screen.findByLabelText('What will use this key?'), 'Netlify');
    await acting.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByText(SECRET)).toBeInTheDocument();
    expect(screen.getByText('Copy it now. It is not shown again.')).toBeInTheDocument();
  });

  it('hands the key over when it is copied, and says so', async () => {
    const { acting, writeText } = person();

    show();

    await acting.type(await screen.findByLabelText('What will use this key?'), 'Netlify');
    await acting.click(screen.getByRole('button', { name: 'Create key' }));
    await acting.click(await screen.findByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledWith(SECRET);
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
  });

  /**
   * Removing a key stops whatever was reporting with it, without anything else on the customer's
   * side going wrong that they could notice. One press must not be enough.
   */
  it('asks before taking a key away', async () => {
    const { acting } = person();
    const { engine } = show([EXISTING]);

    await acting.click(await screen.findByRole('button', { name: 'Remove' }));

    expect(screen.getByText('Remove this key?')).toBeInTheDocument();
    expect(removals(engine.all())).toHaveLength(0);
  });

  it('keeps the key when the asking is declined', async () => {
    const { acting } = person();
    const { engine } = show([EXISTING]);

    await acting.click(await screen.findByRole('button', { name: 'Remove' }));
    await acting.click(screen.getByRole('button', { name: 'Keep' }));

    expect(screen.queryByText('Remove this key?')).not.toBeInTheDocument();
    expect(removals(engine.all())).toHaveLength(0);
  });

  it('takes the key away once the asking is answered', async () => {
    const { acting } = person();
    const { engine } = show([EXISTING]);

    await acting.click(await screen.findByRole('button', { name: 'Remove' }));
    await acting.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => {
      expect(removals(engine.all())).toHaveLength(1);
    });
    expect(removals(engine.all())[0]?.path).toContain(KEY_ID);
  });

  /**
   * The secret is on the screen and nowhere else. Leaving it there for whoever opens the panel
   * next would undo the reason it is only ever shown once.
   */
  it('forgets the new key once the panel is closed', async () => {
    const { acting } = person();

    engineWith([]);
    renderScreen(<Panel />);

    await acting.click(screen.getByRole('button', { name: 'Open' }));
    await acting.type(await screen.findByLabelText('What will use this key?'), 'Netlify');
    await acting.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByText(SECRET)).toBeInTheDocument();

    await acting.click(screen.getByRole('button', { name: 'Close' }));
    await acting.click(screen.getByRole('button', { name: 'Open' }));

    expect(screen.queryByText(SECRET)).not.toBeInTheDocument();
  });

  /**
   * Copying is refused on an address the browser does not consider secure. The key is on screen
   * and can be selected, so there is still a way through and nothing worth interrupting for.
   */
  it('says nothing alarming when the browser refuses to copy', async () => {
    const { acting, writeText } = person();

    writeText.mockRejectedValueOnce(new Error('refused'));
    show();

    await acting.type(await screen.findByLabelText('What will use this key?'), 'Netlify');
    await acting.click(screen.getByRole('button', { name: 'Create key' }));
    await acting.click(await screen.findByRole('button', { name: 'Copy' }));

    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
  });

  it('says so when a key could not be created', async () => {
    const { acting } = person();

    engineDoing(async (_path, init) =>
      init.method === 'POST' ? respondWith(403, { title: 'Not allowed' }) : respondWith(200, []),
    );

    renderScreen(
      <ServerKeys
        open
        onClose={() => {}}
        siteId={SITE}
        siteDomain="example.com"
        timeZoneId="Etc/UTC"
      />,
    );

    await acting.type(await screen.findByLabelText('What will use this key?'), 'Netlify');
    await acting.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});

function removals(sent: readonly Sent[]): readonly Sent[] {
  return sent.filter((one) => one.init.method === 'DELETE');
}

import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import { renderScreen } from '@/test/harness';

const SITE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';

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

function show(onClose = () => {}) {
  return renderScreen(
    <TrackingCode open onClose={onClose} siteId={SITE} siteDomain="example.com" />,
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

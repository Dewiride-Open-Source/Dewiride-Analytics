import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CopyButton } from '@/components/ui/copy-button';
import { renderScreen } from '@/test/harness';

const VALUE = '0199c8f4-6c1e-7a3b-9d21-5f0b8e2a4c77';

afterEach(() => {
  vi.useRealTimers();
});

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

describe('a button that copies one value', () => {
  it('hands the value over, and says so', async () => {
    const { acting, writeText } = person();

    renderScreen(<CopyButton value={VALUE} />);

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledExactlyOnceWith(VALUE);
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
  });

  /**
   * The words on a button that has changed are not read out by every screen reader, so the
   * confirmation is also written where each of them announces it.
   */
  it('tells a screen reader it worked', async () => {
    const { acting } = person();

    renderScreen(<CopyButton value={VALUE} />);

    expect(screen.getByRole('status')).toBeEmptyDOMElement();

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Copied');
  });

  /**
   * Long enough to be noticed, and then back to what pressing it again would do.
   *
   * The clock is the test's alone. Pressed inside one act, the copy and the wait it starts are both
   * settled before the clock moves, so the moment it reverts is the same however busy the machine
   * running the test happens to be.
   */
  it('goes back to offering a copy after a moment', async () => {
    vi.useFakeTimers();
    person();
    renderScreen(<CopyButton value={VALUE} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy' }));
    });

    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1_999);
    });

    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1);
    });

    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });

  /**
   * Copying is refused on an address the browser does not consider secure. The value is on screen
   * and can be selected, so there is still a way through and nothing worth interrupting for.
   */
  it('says nothing alarming when the browser refuses to copy', async () => {
    const { acting, writeText } = person();

    writeText.mockRejectedValueOnce(new Error('refused'));
    renderScreen(<CopyButton value={VALUE} />);

    await acting.click(screen.getByRole('button', { name: 'Copy' }));

    await waitFor(() => {
      expect(writeText).toHaveBeenCalledOnce();
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });

  /**
   * Two buttons that both say "Copy" are two buttons nobody can tell apart without seeing the
   * panel. Named, each says out loud what it copies, and still contains the word on it.
   */
  it('says what it copies, where it is told', async () => {
    const { acting, writeText } = person();

    renderScreen(
      <>
        <CopyButton value="https://analytics.example.com" what="Address" />
        <CopyButton value={VALUE} what="Website ID" />
      </>,
    );

    await acting.click(screen.getByRole('button', { name: 'Copy Website ID' }));

    expect(writeText).toHaveBeenCalledExactlyOnceWith(VALUE);
    expect(await screen.findByRole('button', { name: 'Website ID copied' })).toHaveTextContent(
      'Copied',
    );
    expect(screen.getByRole('button', { name: 'Copy Address' })).toHaveTextContent('Copy');
    expect(screen.getAllByRole('status').map((region) => region.textContent)).toEqual([
      '',
      'Website ID copied',
    ]);
  });
});

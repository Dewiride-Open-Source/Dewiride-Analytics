import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Dialog } from '@/components/ui/dialog';
import { renderScreen } from '@/test/harness';

function show(open: boolean, onClose = () => {}) {
  return renderScreen(
    <Dialog open={open} onClose={onClose} title="Your tracking code" closeLabel="Close">
      <p>Something to read.</p>
    </Dialog>,
  );
}

/** An open dialog with something on the page that shuts it, so closing happens the way it does. */
function Dismissable() {
  const [open, setOpen] = useState(true);

  return (
    <>
      <button type="button" onClick={() => setOpen(false)}>
        Dismiss
      </button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Your tracking code"
        closeLabel="Close"
      >
        <p>Something to read.</p>
      </Dialog>
    </>
  );
}

describe('a focal overlay', () => {
  /**
   * Opened through the browser's own modal machinery rather than by making it visible, because
   * that is what makes the rest of the page inert and holds focus inside. A dialog merely shown
   * is one a keyboard can wander out of.
   */
  it('is opened by the browser, not merely made visible', () => {
    show(true);

    expect(screen.getByRole('dialog')).toHaveAttribute('open');
  });

  it('is not there at all until it is asked for', () => {
    show(false);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('closes when the screen stops asking for it', async () => {
    const acting = userEvent.setup();

    renderScreen(<Dismissable />);

    const element = screen.getByRole('dialog') as HTMLDialogElement;

    await acting.click(screen.getByRole('button', { name: 'Dismiss' }));

    expect(element.open).toBe(false);
  });

  /**
   * Escape is handled by the browser and closes the element without anything here running, so the
   * state that opened it has to be told rather than left believing it is still open.
   */
  it('says so when the browser closed it without being asked', () => {
    const closed = vi.fn();

    show(true, closed);

    screen.getByRole('dialog').dispatchEvent(new Event('close'));

    expect(closed).toHaveBeenCalled();
  });

  it('gives itself a name, so it is announced as more than "dialog"', () => {
    show(true);

    expect(screen.getByRole('dialog', { name: 'Your tracking code' })).toBeInTheDocument();
  });

  /**
   * Left to itself it starts on the first thing that can hold a cursor, which is the button that
   * closes it — so a panel somebody asked for greets a keyboard with the way out of it.
   */
  it('starts on the control a panel says it would rather start on', () => {
    renderScreen(
      <Dialog open onClose={() => {}} title="Add a website" closeLabel="Close">
        <input aria-label="Address" data-opens-on />
      </Dialog>,
    );

    expect(screen.getByLabelText('Address')).toHaveFocus();
  });

  it('starts there when the panel is opened from a screen already drawn', async () => {
    const acting = userEvent.setup();

    renderScreen(<Openable />);

    await acting.click(screen.getByRole('button', { name: 'Open' }));

    expect(screen.getByLabelText('Address')).toHaveFocus();
  });
});

/** A panel that is asked for rather than already open, which is how every one of them arrives. */
function Openable() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      <Dialog open={open} onClose={() => setOpen(false)} title="Add a website" closeLabel="Close">
        <input aria-label="Address" data-opens-on />
      </Dialog>
    </>
  );
}

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ComparedWith } from '@/lib/analytics/compared-with';
import { useComparedWith } from '@/lib/analytics/use-compared-with';

interface ProbeProps {
  readonly pick: ComparedWith;
}

/** Prints the earlier days the address resolved to, and writes a choice back into it when pressed. */
function Probe({ pick }: ProbeProps) {
  const { comparedWith, compareWith } = useComparedWith();

  return (
    <button type="button" onClick={() => compareWith(pick)}>
      {comparedWith ?? 'nothing'}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: ComparedWith;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = 'day-before', watching }: Arriving = {}) {
  const wrapper = withNuqsTestingAdapter({
    searchParams: at,
    onUrlUpdate: watching,
    hasMemory: true,
  });

  // The stand-in address renders once more the moment it is mounted, and throws away whatever is
  // waiting to be written each time it does, so the screen arrives one step after the address — as
  // it does in the browser, whose address keeps what a screen seeds on arrival.
  const shown = render(<></>, { wrapper });

  shown.rerender(<Probe pick={picking} />);

  return shown;
}

/** Which earlier days the screen currently believes the period is set beside. */
function comparing() {
  return screen.getByRole('button').textContent;
}

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('the earlier days the period is set beside', () => {
  /** Nothing chosen means each period is set beside its own stretch. */
  it('is nothing chosen when the address says nothing', () => {
    arrive();

    expect(comparing()).toBe('nothing');
  });

  it('is whatever a link somebody followed names', () => {
    arrive({ at: '?vs=day-before' });

    expect(comparing()).toBe('day-before');
  });

  /**
   * An address is written by whoever sent the link, so nothing in it is taken on trust — and the
   * period's own stretch is never written, so naming it is not a choice either.
   */
  it.each(['year', 'before', ''])('reads %j as silence', (written) => {
    arrive({ at: `?vs=${written}` });

    expect(comparing()).toBe('nothing');
  });

  /** Setting the period beside other days is one decision, and the way back is the button at hand. */
  it('travels in the address as a new entry in the history', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(comparing()).toBe('day-before'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('vs=day-before');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
  });

  /** Somebody who reads every morning against the morning before should find it that way. */
  it('is remembered once chosen', async () => {
    arrive({ picking: '4-weeks-before' });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(comparing()).toBe('4-weeks-before'));
    expect(window.localStorage.getItem('dewiride.compared-with')).toBe('4-weeks-before');
  });

  /** The period's own stretch is what an address saying nothing already means, so it is never written. */
  it("leaves the address plain, and forgets the choice, for the period's own stretch", async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.compared-with', 'day-before');

    arrive({ at: '?vs=day-before', picking: 'before', watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(comparing()).toBe('nothing'));
    expect(watching.mock.calls.at(-1)?.[0].queryString).not.toContain('vs=');
    expect(window.localStorage.getItem('dewiride.compared-with')).toBeNull();
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the choice there, so it is written in over the entry somebody is already on.
   */
  it('opens on the earlier days last chosen when the address says nothing', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.compared-with', '4-weeks-before');

    arrive({ watching });

    expect(comparing()).toBe('4-weeks-before');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('vs=4-weeks-before');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /** A link names what its sender was looking at, and the reader's own habit gives way to it. */
  it('lets a link name the earlier days over the ones remembered', () => {
    window.localStorage.setItem('dewiride.compared-with', '4-weeks-before');

    arrive({ at: '?vs=day-before' });

    expect(comparing()).toBe('day-before');
  });

  /** A record written by hand, or by another version of the product, is not taken on trust either. */
  it('remembers nothing it cannot read', () => {
    window.localStorage.setItem('dewiride.compared-with', 'last-year');

    arrive();

    expect(comparing()).toBe('nothing');
  });
});

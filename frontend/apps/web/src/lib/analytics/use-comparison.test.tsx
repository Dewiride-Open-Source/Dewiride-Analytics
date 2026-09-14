import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useComparison } from '@/lib/analytics/use-comparison';

interface ProbeProps {
  readonly pick: boolean;
}

/** Prints whether the earlier period is drawn, and asks for the other answer when pressed. */
function Probe({ pick }: ProbeProps) {
  const { against, compare } = useComparison();

  return (
    <button type="button" onClick={() => compare(pick)}>
      {against ? 'on' : 'off'}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: boolean;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = true, watching }: Arriving = {}) {
  const wrapper = withNuqsTestingAdapter({
    searchParams: at,
    onUrlUpdate: watching,
    hasMemory: true,
  });

  // The stand-in address renders once more the moment it is mounted, to take up what it was given,
  // and throws away whatever is waiting to be written every time it renders. A screen mounted in
  // that same moment would have what it seeds thrown away with it — which the browser's own
  // address never does — so the screen arrives one step after the address.
  const shown = render(<></>, { wrapper });

  shown.rerender(<Probe pick={picking} />);

  return shown;
}

/** Whether the screen currently believes it is drawing the earlier period. */
function drawing() {
  return screen.getByRole('button').textContent;
}

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('whether the earlier period is drawn behind this one', () => {
  it('is off when the address says nothing', () => {
    arrive();

    expect(drawing()).toBe('off');
  });

  it('is on for a link that was sent with it on', () => {
    arrive({ at: '?against=before' });

    expect(drawing()).toBe('on');
  });

  /** An address is written by whoever sent the link, so nothing in it is taken on trust. */
  it('draws this period alone when a link asks for a comparison this product has never had', () => {
    arrive({ at: '?against=sideways' });

    expect(drawing()).toBe('off');
  });

  it('travels in the address, so what somebody found can be sent to somebody else', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('on'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('against=before');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
  });

  it('leaves the address alone again once it is turned off', async () => {
    const watching = vi.fn();

    arrive({ at: '?against=before', picking: false, watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('off'));
    expect(watching.mock.calls[0]?.[0].queryString).not.toContain('against');
  });

  /** Somebody who reads their weeks against the ones before should find the earlier period drawn. */
  it('is remembered once turned on', async () => {
    arrive();

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('on'));
    expect(window.localStorage.getItem('dewiride.chart-against')).toBe('before');
  });

  it('is forgotten once turned off', async () => {
    window.localStorage.setItem('dewiride.chart-against', 'before');

    arrive({ at: '?against=before', picking: false });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(drawing()).toBe('off'));
    expect(window.localStorage.getItem('dewiride.chart-against')).toBeNull();
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the comparison there, so it is written in over the entry somebody is already on.
   */
  it('opens with the earlier period drawn when that was remembered', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.chart-against', 'before');

    arrive({ watching });

    expect(drawing()).toBe('on');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('against=before');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });
});

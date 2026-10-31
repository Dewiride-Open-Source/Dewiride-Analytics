import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChartBy } from '@/lib/analytics/chart-by';
import { useChartBy } from '@/lib/analytics/use-chart-by';

interface ProbeProps {
  readonly pick: ChartBy | null;
}

/** Prints the way the address resolved to, and writes one back into it when pressed. */
function Probe({ pick }: ProbeProps) {
  const { by, showBy } = useChartBy();

  return (
    <button type="button" onClick={() => showBy(pick)}>
      {by ?? 'nothing'}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: ChartBy | null;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = 'week', watching }: Arriving = {}) {
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

/** Which way the screen currently believes the picture is cut. */
function cutting() {
  return screen.getByRole('button').textContent;
}

// The browser's storage outlives a test, so each one starts with nothing remembered.
beforeEach(() => {
  window.localStorage.clear();
});

describe('the way the picture is cut', () => {
  /** Nothing chosen means each period is drawn its own way. */
  it('is nothing chosen when the address says nothing', () => {
    arrive();

    expect(cutting()).toBe('nothing');
  });

  it('is whatever a link somebody followed names', () => {
    arrive({ at: '?by=month' });

    expect(cutting()).toBe('month');
  });

  /** An address is written by whoever sent the link, so nothing in it is taken on trust. */
  it('reads junk as silence', () => {
    arrive({ at: '?by=fortnight' });

    expect(cutting()).toBe('nothing');
  });

  /** Cutting the picture another way is one decision, and the way back from it is the button at hand. */
  it('travels in the address as a new entry in the history', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(cutting()).toBe('week'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('by=week');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
  });

  /** Somebody who reads their quarters by the week should find them that way next time. */
  it('is remembered once chosen', async () => {
    arrive();

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(cutting()).toBe('week'));
    expect(window.localStorage.getItem('dewiride.chart-by')).toBe('week');
  });

  /** A period's own way is what an address saying nothing already means, so it is never written. */
  it("leaves the address plain for the period's own way", async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.chart-by', 'week');

    arrive({ at: '?by=week', picking: null, watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(cutting()).toBe('nothing'));
    expect(watching.mock.calls.at(-1)?.[0].queryString).not.toContain('by=');
    expect(window.localStorage.getItem('dewiride.chart-by')).toBeNull();
  });

  /**
   * The link in the bar should still say what is on the screen, and nothing anybody pressed put
   * the choice there, so it is written in over the entry somebody is already on.
   */
  it('opens on the way last chosen when the address says nothing', async () => {
    const watching = vi.fn();

    window.localStorage.setItem('dewiride.chart-by', 'month');

    arrive({ watching });

    expect(cutting()).toBe('month');

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].queryString).toContain('by=month');
    expect(watching.mock.calls[0]?.[0].options.history).toBe('replace');
  });

  /** A link names what its sender was looking at, and the reader's own habit gives way to it. */
  it('lets a link name the way over the one remembered', () => {
    window.localStorage.setItem('dewiride.chart-by', 'month');

    arrive({ at: '?by=week' });

    expect(cutting()).toBe('week');
  });
});

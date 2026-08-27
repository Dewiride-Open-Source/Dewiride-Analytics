import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { describe, expect, it, vi } from 'vitest';
import type { ChartView } from '@/lib/analytics/chart-view';
import { useChartView } from '@/lib/analytics/use-chart-view';

interface ProbeProps {
  readonly pick: ChartView;
}

/** Prints the view the address resolved to, and writes one back into it when pressed. */
function Probe({ pick }: ProbeProps) {
  const { view, show } = useChartView();

  return (
    <button type="button" onClick={() => show(pick)}>
      {view}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: ChartView;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = 'activity', watching }: Arriving = {}) {
  return render(<Probe pick={picking} />, {
    wrapper: withNuqsTestingAdapter({
      searchParams: at,
      onUrlUpdate: watching,
      hasMemory: true,
    }),
  });
}

/** Which view the screen currently believes it is on. */
function showing() {
  return screen.getByRole('button').textContent;
}

describe('which view the picture is on', () => {
  it('is who the traffic was when the address says nothing', () => {
    arrive();

    expect(showing()).toBe('who');
  });

  it('is whatever a link somebody followed names', () => {
    arrive({ at: '?show=activity' });

    expect(showing()).toBe('activity');
  });

  /** An address is written by whoever sent the link, so nothing in it is taken on trust. */
  it('opens on a working screen when a link names a view this product has never had', () => {
    arrive({ at: '?show=sideways' });

    expect(showing()).toBe('who');
  });

  it('travels in the address, so a link opens on the view it was sent from', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(showing()).toBe('activity'));
    expect(watching.mock.calls[0]?.[0].queryString).toContain('show=activity');
  });

  /** The plain address of the overview should stay plain. */
  it('leaves the address alone for the view everything opens on', async () => {
    const watching = vi.fn();

    arrive({ at: '?show=activity', picking: 'who', watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(showing()).toBe('who'));
    expect(watching.mock.calls[0]?.[0].queryString).not.toContain('show');
  });

  /** Switching views is one decision, and the way back from it is the button already at hand. */
  it('is a new entry in the history rather than a rewritten one', async () => {
    const watching = vi.fn();

    arrive({ watching });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(watching).toHaveBeenCalled());
    expect(watching.mock.calls[0]?.[0].options.history).toBe('push');
  });
});

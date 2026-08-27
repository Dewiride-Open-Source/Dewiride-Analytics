import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { describe, expect, it, vi } from 'vitest';
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
  return render(<Probe pick={picking} />, {
    wrapper: withNuqsTestingAdapter({
      searchParams: at,
      onUrlUpdate: watching,
      hasMemory: true,
    }),
  });
}

/** Whether the screen currently believes it is drawing the earlier period. */
function drawing() {
  return screen.getByRole('button').textContent;
}

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
});

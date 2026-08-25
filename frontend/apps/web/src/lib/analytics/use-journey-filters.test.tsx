import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type OnUrlUpdateFunction, withNuqsTestingAdapter } from 'nuqs/adapters/testing';
import { describe, expect, it, vi } from 'vitest';
import {
  EVERY_JOURNEY,
  type JourneyFilters,
  MOST_VALUES,
  narrowingParams,
} from '@/lib/analytics/journeys';
import { useJourneyFilters } from '@/lib/analytics/use-journey-filters';

const IN_INDIA: JourneyFilters = { ...EVERY_JOURNEY, countries: ['IN'] };

interface ProbeProps {
  readonly pick: JourneyFilters;
}

/** Prints what the address resolved to, and writes a narrowing back into it when pressed. */
function Probe({ pick }: ProbeProps) {
  const { filters, narrow } = useJourneyFilters();

  return (
    <button type="button" onClick={() => narrow(pick)}>
      {narrowingParams(filters).toString()}
    </button>
  );
}

interface Arriving {
  readonly at?: string;
  readonly picking?: JourneyFilters;
  readonly watching?: OnUrlUpdateFunction;
}

function arrive({ at, picking = IN_INDIA, watching }: Arriving = {}) {
  return render(<Probe pick={picking} />, {
    wrapper: withNuqsTestingAdapter({
      searchParams: at,
      onUrlUpdate: watching,
      // The address remembers what is written to it, the way the browser's own does.
      hasMemory: true,
    }),
  });
}

/** The question the screen would put to the engine, as it stands. */
function asked() {
  return new URLSearchParams(screen.getByRole('button').textContent ?? '');
}

/** Everything the address says at the moment, written out. */
function showing() {
  return asked().toString();
}

/** A query string naming one thing many times over. */
function repeated(name: string, count: number): string {
  return Array.from({ length: count }, (_, index) => `${name}=value-${index}`).join('&');
}

describe('what a list of journeys is narrowed to', () => {
  it('is everything when the address says nothing', () => {
    arrive();

    expect(showing()).toBe('');
  });

  it('is what the address names, so a link opens on what its sender had narrowed to', () => {
    arrive({ at: '?country=IN&country=FR' });

    expect(asked().getAll('country')).toEqual(['FR', 'IN']);
  });

  it('reads several things at once', () => {
    arrive({ at: '?category=likely-human&device=phone&town=Jaipur&strength=moderate&minPages=2' });

    expect(showing()).toBe(
      'category=likely-human&device=phone&town=Jaipur&strength=moderate&minPages=2',
    );
  });

  /**
   * An address is typed, edited and forwarded by people, and one of them may be trying it on, so
   * nothing in it is taken on trust. Every one of these has to leave somebody on a working screen
   * rather than on a refusal, because there is nothing they could do about it from there.
   */
  it('leaves out a value the engine has no such thing as, and keeps the rest', () => {
    arrive({ at: '?device=phone&device=hovercraft' });

    expect(asked().getAll('device')).toEqual(['phone']);
  });

  it('narrows by nothing at all when none of what was named exists', () => {
    arrive({ at: '?device=hovercraft&category=marvellous' });

    expect(showing()).toBe('');
  });

  /**
   * A floor of confirmed identity is not on offer, because nothing yet reaches it; a floor of
   * nothing to go on is every visit. Both are things the engine has a word for, which is exactly
   * why an address naming one has to be turned away here rather than passed along.
   */
  it('turns away a floor the screen does not offer, even one the engine names', () => {
    arrive({ at: '?strength=verified&minPages=9' });

    expect(showing()).toBe('');
  });

  /**
   * Asking for the visits nothing could be established about is a real question and a different
   * one from asking for all of them, so the empty value has to survive being written in an
   * address and read back out of it.
   */
  it('keeps the visits nothing was established about as a question of its own', () => {
    arrive({ at: '?browser=' });

    expect(asked().getAll('browser')).toEqual(['']);
  });

  /**
   * A page is whatever a visitor asked the website for, so it may hold anything at all — a comma,
   * a question mark, another address. Each value stands on its own in the address rather than
   * inside a separated list, so there is nothing for one of them to break out of.
   */
  it('keeps a page that reads like a question of its own in one piece', () => {
    arrive({ at: '?entryPage=/a,b&entryPage=/c?d=e' });

    expect(asked().getAll('entryPage')).toEqual(['/a,b', '/c?d=e']);
  });

  /**
   * The engine refuses a question naming more than this, and a refusal is not something somebody
   * who followed a link can do anything about. They are shown the first of them narrowed down
   * instead.
   */
  it('carries no more of one thing than the engine will answer for', () => {
    arrive({ at: `?${repeated('town', MOST_VALUES + 12)}` });

    expect(asked().getAll('town')).toHaveLength(MOST_VALUES);
  });

  it('is written into the address as soon as it is narrowed', async () => {
    const written = vi.fn();

    arrive({ watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].queryString).toContain('country=IN');
    expect(showing()).toBe('country=IN');
  });

  /** An address that says what it would have said anyway is one more thing in the bar for nothing. */
  it('leaves the address alone again when everything is put back', async () => {
    const written = vi.fn();

    arrive({ at: '?country=IN', picking: EVERY_JOURNEY, watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].queryString).toBe('');
    expect(showing()).toBe('');
  });

  /**
   * Unlike the period, which is one decision. Narrowing is a handful of small presses, each of
   * them already on screen as something a single press takes off, and a reader who ticked six
   * things should not have to press the browser's own way back six times to leave.
   */
  it('is written over the entry the reader is on rather than leaving one behind', async () => {
    const written = vi.fn();

    arrive({ watching: written });

    await userEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(written).toHaveBeenCalled());
    expect(written.mock.calls[0]?.[0].options.history).toBe('replace');
  });
});

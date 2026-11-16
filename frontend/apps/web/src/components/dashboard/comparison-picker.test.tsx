import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ComparisonPicker } from '@/components/dashboard/comparison-picker';
import type { ComparedWith } from '@/lib/analytics/compared-with';
import { comparisonsFor, type Period, type PeriodPreset } from '@/lib/analytics/period';
import { renderScreen } from '@/test/harness';

/** Mid-afternoon on Monday the seventeenth of August in Kolkata. */
const NOW = new Date('2026-08-17T09:37:12Z');

const ZONE = 'Asia/Kolkata';

/** One of the named periods, written the way a screen holds it. */
function named(preset: PeriodPreset): Period {
  return { kind: 'preset', preset };
}

/** The control for a period, as the overview hands it what the period may be set beside. */
function show(period: Period, value: ComparedWith | null = null) {
  const chose = vi.fn();
  const reach = vi.fn();

  renderScreen(
    <ComparisonPicker
      offered={comparisonsFor(period, ZONE, NOW)}
      value={value}
      onChange={chose}
      timeZoneId={ZONE}
      onReach={reach}
    />,
  );

  return { chose, reach };
}

/** The list, as it is reached without seeing it. */
function list() {
  return screen.getByRole('combobox', { name: 'Compared with' });
}

/** Normalises the thin spaces the reader's language puts around a dash, so a test can name them. */
function plainly(written: string | null | undefined): string {
  return (written ?? '').replace(/\s+/g, ' ');
}

describe('what the period is compared with', () => {
  /** Each stretch is named the way the cards name it, so the list and the cards read as one. */
  it('offers every earlier stretch the period may be set beside, its own first', () => {
    show(named('today'));

    expect(
      within(list())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['vs the same day last week', 'vs yesterday', 'vs the same day 4 weeks ago']);
    expect(list()).toHaveValue('before');
  });

  /** "Yesterday" and the date it means are one answer, read out together. */
  it('prints beneath it the days the figures are measured against, and reads them out with it', () => {
    show(named('today'), 'day-before');

    expect(list()).toHaveValue('day-before');
    expect(list()).toHaveAccessibleDescription('Aug 16, 2026');
  });

  it('says which stretch somebody chose', async () => {
    const { chose } = show(named('last-7-days'));

    expect(
      within(list())
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['vs the same days last week', 'vs the same days 4 weeks earlier']);

    await userEvent.selectOptions(list(), '4-weeks-before');

    expect(chose).toHaveBeenCalledWith('4-weeks-before');
  });

  /** A choice the period cannot honour is set aside, and the list reads as what is in force. */
  it('reads as the period’s own stretch where the one chosen is not offered', () => {
    show(named('last-7-days'), 'day-before');

    expect(list()).toHaveValue('before');
    expect(list()).toHaveAccessibleDescription(/^Aug 4\s–\s10, 2026$/);
  });

  /**
   * The last 30 days are set beside one stretch alone. There is nothing to choose, so nothing to
   * press — but the reader still sees which days the figures are measured against.
   */
  it('writes the one stretch a period is offered, rather than offering it', () => {
    show(named('last-30-days'));

    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByText('vs the same days 5 weeks earlier')).toBeInTheDocument();
    expect(plainly(screen.getByText(/Jun/).textContent)).toBe('Jun 14 – Jul 13, 2026');
  });

  it('shows nothing where the period is set beside nothing', () => {
    show(named('this-year'));

    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.queryByText(/^vs /)).not.toBeInTheDocument();
  });

  /** The screen asks ahead for every stretch's figures once somebody reaches for the list. */
  it('says when somebody reaches for it, and not when focus merely arrives', () => {
    const { reach } = show(named('today'));

    list().focus();
    expect(reach).not.toHaveBeenCalled();

    fireEvent.pointerDown(list());
    expect(reach).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(list(), { key: 'ArrowDown' });
    expect(reach).toHaveBeenCalledTimes(2);

    fireEvent.keyDown(list(), { key: 'Tab' });
    expect(reach).toHaveBeenCalledTimes(2);
  });
});

import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { type Option, OptionList } from '@/components/ui/option-list';
import { renderScreen } from '@/test/harness';

const WORDS = {
  searchLabel: 'Search these options',
  searchPlaceholder: 'Search',
  nothingLabel: 'Nothing recorded in this period.',
  noMatchLabel: 'Nothing matches that.',
};

/** As many values as a website's own traffic would turn out to hold. */
function many(count: number): readonly Option[] {
  return Array.from({ length: count }, (_, index) => ({
    value: `value-${index}`,
    label: `Town ${index}`,
    count: index + 1,
  }));
}

function showing(
  options: readonly Option[],
  extra: Partial<Parameters<typeof OptionList>[0]> = {},
) {
  const picked = vi.fn();

  renderScreen(
    <OptionList options={options} chosen={[]} multiple onPick={picked} {...WORDS} {...extra} />,
  );

  return picked;
}

describe('picking from what a period held', () => {
  /**
   * Real checkboxes rather than pressable rows, so what somebody hears is "checked" and "three of
   * nine" with nothing written to say so, and so a keyboard and a voice command work without any
   * of it being wired up by hand.
   */
  it('offers each value as something that can be ticked', async () => {
    const picked = showing([
      { value: 'IN', label: 'India', count: 6 },
      { value: 'FR', label: 'France', count: 3 },
    ]);

    await userEvent.click(screen.getByRole('checkbox', { name: 'India 6' }));

    expect(picked).toHaveBeenCalledWith('IN');
  });

  it('shows which are already being asked for', () => {
    showing([{ value: 'IN', label: 'India', count: 6 }], { chosen: ['IN'] });

    expect(screen.getByRole('checkbox', { name: 'India 6' })).toBeChecked();
  });

  /** One of several is a different control from one of many, and the platform draws it as one. */
  it('offers one at a time where only one may be asked for', () => {
    showing([{ value: 'moderate', label: 'Some signs or stronger' }], { multiple: false });

    expect(screen.getByRole('radio', { name: 'Some signs or stronger' })).toBeInTheDocument();
  });

  /**
   * The figure is the number of visits that held the value, and it is read out as a separate word.
   * Written without the space it would reach a screen reader as "India6".
   */
  it('reads the figure beside a value as a word of its own', () => {
    showing([{ value: 'IN', label: 'India', count: 1234 }]);

    expect(screen.getByRole('checkbox', { name: 'India 1,234' })).toBeInTheDocument();
  });

  it('leaves the figure off where it would not describe the list', () => {
    showing([{ value: 'IN', label: 'India' }]);

    expect(screen.getByRole('checkbox', { name: 'India' })).toBeInTheDocument();
  });
});

describe('finding one value among many', () => {
  /**
   * A box over four values is chrome in front of a list somebody has already read; two hundred
   * towns without one is unusable.
   */
  it('offers no way to search a list somebody can take in at a glance', () => {
    showing(many(8));

    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('offers one as soon as the list is longer than that', () => {
    showing(many(9));

    expect(screen.getByRole('searchbox', { name: 'Search these options' })).toBeInTheDocument();
  });

  it('cuts the list down to what was typed', async () => {
    showing(many(9));

    await userEvent.type(screen.getByRole('searchbox'), 'Town 3');

    expect(screen.getAllByRole('checkbox')).toHaveLength(1);
    expect(screen.getByRole('checkbox', { name: 'Town 3 4' })).toBeInTheDocument();
  });

  it('finds a value however it was capitalised', async () => {
    showing(many(9));

    await userEvent.type(screen.getByRole('searchbox'), 'TOWN 5');

    expect(screen.getByRole('checkbox', { name: 'Town 5 6' })).toBeInTheDocument();
  });

  it('says so rather than showing an empty box when nothing matches', async () => {
    showing(many(9));

    await userEvent.type(screen.getByRole('searchbox'), 'Bengaluru');

    expect(screen.getByText('Nothing matches that.')).toBeInTheDocument();
  });

  /** Two different nothings, and a reader who typed nothing has not mistyped anything. */
  it('says something different when the period itself held nothing', () => {
    showing([]);

    expect(screen.getByText('Nothing recorded in this period.')).toBeInTheDocument();
  });
});

/**
 * There is a limit on how many values of one thing may be asked for at once, and a control that
 * let somebody go past it would hand them a screen that had stopped working with no way back to
 * the one they had. So it stops offering instead, and says why.
 */
describe('picking as many as may be asked for at once', () => {
  it('stops offering the ones that would go over', () => {
    showing(many(4), { chosen: ['value-0', 'value-1'], full: true, fullLabel: 'No more.' });

    expect(screen.getByRole('checkbox', { name: 'Town 2 3' })).toBeDisabled();
  });

  /** Taking one off is how somebody gets back under the limit, so it has to go on working. */
  it('goes on offering to take off the ones already picked', async () => {
    const picked = showing(many(4), {
      chosen: ['value-0', 'value-1'],
      full: true,
      fullLabel: 'No more.',
    });

    await userEvent.click(screen.getByRole('checkbox', { name: 'Town 0 1' }));

    expect(picked).toHaveBeenCalledWith('value-0');
  });

  /** A row that has quietly stopped responding is a fault as far as anybody can tell. */
  it('says why rather than leaving the rest of the list to look broken', () => {
    showing(many(4), { chosen: ['value-0'], full: true, fullLabel: 'No more.' });

    expect(screen.getByText('No more.')).toBeInTheDocument();
  });

  it('offers everything while there is still room', () => {
    showing(many(4), { chosen: ['value-0'] });

    expect(screen.getByRole('checkbox', { name: 'Town 2 3' })).toBeEnabled();
    expect(screen.queryByText('No more.')).not.toBeInTheDocument();
  });
});

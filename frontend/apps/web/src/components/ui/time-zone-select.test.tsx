import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Field } from '@/components/ui/field';
import { TimeZoneSelect } from '@/components/ui/time-zone-select';
import type { TimeZoneGroup } from '@/lib/time-zones';
import { renderScreen } from '@/test/harness';

/**
 * Three regions, with the one that sorts first holding the zone a picker falls on when it has not
 * been told where to start.
 */
const GROUPS: readonly TimeZoneGroup[] = [
  { area: 'Africa', zones: [{ id: 'Africa/Abidjan', label: 'Abidjan (GMT)' }] },
  { area: 'Asia', zones: [{ id: 'Asia/Kolkata', label: 'Kolkata (GMT+5:30)' }] },
  { area: 'Europe', zones: [{ id: 'Europe/London', label: 'London (GMT+1:00)' }] },
];

interface PickerProps {
  readonly zones: readonly TimeZoneGroup[] | null;
  readonly startsOn?: (zones: readonly TimeZoneGroup[]) => string;
}

/** The picker as a form holds it: labelled, described, and nobody else holding its value. */
function Picker({ zones, startsOn }: PickerProps) {
  return (
    <Field label="Reporting time zone" hint="Where your readers are.">
      {(attributes) => (
        <TimeZoneSelect {...attributes} zones={zones} startsOn={startsOn} name="timeZoneId" />
      )}
    </Field>
  );
}

function picker(): HTMLElement {
  return screen.getByRole('combobox', { name: 'Reporting time zone' });
}

describe('a time zone picker before the browser has offered any zones', () => {
  it('stands empty and unavailable under its own label', () => {
    renderScreen(<Picker zones={null} />);

    expect(picker()).toBeDisabled();
    expect(within(picker()).queryAllByRole('option')).toHaveLength(0);
  });

  it('keeps what describes it attached while it waits', () => {
    renderScreen(<Picker zones={null} />);

    expect(picker()).toHaveAccessibleDescription('Where your readers are.');
  });
});

describe('a time zone picker once there are zones', () => {
  it('offers every zone under the region it is in', () => {
    renderScreen(<Picker zones={GROUPS} />);

    const asia = within(picker()).getByRole('group', { name: 'Asia' });

    expect(
      within(asia)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toStrictEqual(['Kolkata (GMT+5:30)']);
    expect(within(picker()).getAllByRole('option')).toHaveLength(3);
  });

  it('starts where it was told to', () => {
    renderScreen(<Picker zones={GROUPS} startsOn={() => 'Europe/London'} />);

    expect(picker()).toBeEnabled();
    expect(picker()).toHaveValue('Europe/London');
  });

  /**
   * The reason the empty picker is a picker of its own. A picker takes the choice it starts on
   * only as it first appears, so one filled in after appearing empty would open on whichever zone
   * sorts first — Abidjan — and somebody would be counted in Côte d'Ivoire without choosing it.
   */
  it('starts where it was told to even when it first appeared empty', () => {
    const { rerender } = renderScreen(<Picker zones={null} startsOn={() => 'Europe/London'} />);

    rerender(<Picker zones={GROUPS} startsOn={() => 'Europe/London'} />);

    expect(picker()).toHaveValue('Europe/London');
  });
});

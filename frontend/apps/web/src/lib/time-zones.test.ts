import { describe, expect, it } from 'vitest';
import { readableZone, thisDeviceTimeZone, timeZoneGroups } from '@/lib/time-zones';

describe('choosing a time zone', () => {
  const groups = timeZoneGroups();

  it('gathers zones under the region they are in', () => {
    const asia = groups.find((group) => group.area === 'Asia');

    expect(asia).toBeDefined();
    expect(asia?.zones.length).toBeGreaterThan(20);
  });

  /**
   * The identifier is a wire format, and a list of several hundred of them is the difference
   * between choosing a time zone and reading a database.
   */
  it('names each zone by its place and how far it stands from London', () => {
    const india = groups
      .flatMap((group) => group.zones)
      .find((zone) => zone.id === 'Asia/Kolkata' || zone.id === 'Asia/Calcutta');

    expect(india?.label).toMatch(/^(Kolkata|Calcutta) \(GMT\+5:30\)$/);
  });

  it('puts the regions in the order somebody would look through them', () => {
    const areas = groups.map((group) => group.area);

    expect(areas).toStrictEqual([...areas].toSorted((a, b) => a.localeCompare(b)));
  });

  it('sorts the zones inside a region by what they are called, not by their identifier', () => {
    const labels = groups[0]?.zones.map((zone) => zone.label) ?? [];

    expect(labels.length).toBeGreaterThan(1);
    expect(labels).toStrictEqual([...labels].toSorted((a, b) => a.localeCompare(b)));
  });

  it('offers the zone this device is set to', () => {
    const here = Intl.DateTimeFormat().resolvedOptions().timeZone;

    expect(thisDeviceTimeZone(groups)).toBe(here);
  });

  /**
   * A device set to a zone the platform will not offer would otherwise leave the form quietly
   * showing the first entry in the list while somebody assumed it had found theirs.
   */
  it('falls back to something real when this device names a zone nobody offers', () => {
    const onlyLondon = [
      { area: 'Europe', zones: [{ id: 'Europe/London', label: 'London (GMT+1)' }] },
    ];

    expect(thisDeviceTimeZone(onlyLondon)).toBe('Europe/London');
  });
});

describe('writing a zone into a sentence', () => {
  it.each([
    ['Asia/Kolkata', 'Kolkata'],
    ['America/Argentina/Buenos_Aires', 'Buenos Aires'],
    ['Europe/Isle_of_Man', 'Isle of Man'],
    ['UTC', 'UTC'],
  ])('reads %s as %s', (id, expected) => {
    expect(readableZone(id)).toBe(expected);
  });
});

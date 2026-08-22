import { describe, expect, it } from 'vitest';
import {
  offeredZone,
  readableZone,
  type TimeZoneGroup,
  thisDeviceTimeZone,
  timeZoneGroups,
  withZone,
} from '@/lib/time-zones';

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

describe('the zone a picker starts on', () => {
  const groups = timeZoneGroups();

  it('starts on the wanted zone when the platform offers it', () => {
    expect(offeredZone(groups, 'Europe/London')).toBe('Europe/London');
  });

  /**
   * Platforms disagree about zone names, so a stored zone is not always among the choices a
   * particular browser offers. Falling through to whichever happens to be first is how somebody
   * ends up measuring a website in a country nobody involved has ever been to.
   */
  it('falls back to this device rather than to whichever zone happens to be first', () => {
    const invented = 'Mars/Olympus_Mons';

    expect(offeredZone(groups, invented)).toBe(thisDeviceTimeZone(groups));
    expect(offeredZone(groups, invented)).not.toBe(invented);
  });

  it('falls back the same way when nothing was wanted at all', () => {
    expect(offeredZone(groups, undefined)).toBe(thisDeviceTimeZone(groups));
  });
});

describe('the zones a picker offers', () => {
  const offered: readonly TimeZoneGroup[] = [
    { area: 'Asia', zones: [{ id: 'Asia/Kolkata', label: 'Kolkata (GMT+5:30)' }] },
    { area: 'Europe', zones: [{ id: 'Europe/London', label: 'London (GMT+1)' }] },
  ];

  it('leaves them alone when the zone is already one of them', () => {
    expect(withZone(offered, 'Europe/London')).toBe(offered);
  });

  it('leaves them alone when there is no zone to place', () => {
    expect(withZone(offered, undefined)).toBe(offered);
  });

  /**
   * The reason this exists. A website counted in a zone this browser spells differently would
   * otherwise open on a fall-back, and somebody who came to rename it would move the boundary of
   * its day by saving a field they never touched.
   */
  it('puts a zone this browser does not offer under the region it belongs to', () => {
    const asia = withZone(offered, 'Asia/Calcutta').find((group) => group.area === 'Asia');

    expect(asia?.zones.map((zone) => zone.id)).toStrictEqual(['Asia/Calcutta', 'Asia/Kolkata']);
  });

  it('names it the way every other zone is named', () => {
    const added = withZone(offered, 'Asia/Calcutta')
      .flatMap((group) => group.zones)
      .find((zone) => zone.id === 'Asia/Calcutta');

    expect(added?.label).toBe('Calcutta (GMT+5:30)');
  });

  it('opens a region for a zone that belongs to none of them, in its place in the order', () => {
    const widened = withZone(offered, 'America/Sao_Paulo');

    expect(widened.map((group) => group.area)).toStrictEqual(['America', 'Asia', 'Europe']);
  });

  /**
   * A zone nothing on this platform recognises is still the zone a website is counted in, and a
   * missing offset beside its name is a far smaller problem than a panel that will not draw.
   */
  it('still offers a zone whose offset nothing here can work out', () => {
    const invented = withZone(offered, 'Mars/Olympus_Mons');

    expect(invented.find((group) => group.area === 'Mars')?.zones).toStrictEqual([
      { id: 'Mars/Olympus_Mons', label: 'Olympus Mons' },
    ]);
  });

  it('makes a stored zone one a picker can start on', () => {
    const widened = withZone(timeZoneGroups(), 'Asia/Calcutta');

    expect(offeredZone(widened, 'Asia/Calcutta')).toBe('Asia/Calcutta');
  });
});

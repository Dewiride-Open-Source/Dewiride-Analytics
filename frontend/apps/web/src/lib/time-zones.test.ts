import { describe, expect, it, vi } from 'vitest';
import {
  currentZoneName,
  offeredZone,
  readableZone,
  type TimeZoneGroup,
  thisDeviceTimeZone,
  timeZoneGroups,
  withZone,
} from '@/lib/time-zones';

/** Every identifier a list of groups offers. */
function idsIn(groups: readonly TimeZoneGroup[]): readonly string[] {
  return groups.flatMap((group) => group.zones.map((zone) => zone.id));
}

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
    const india = groups.flatMap((group) => group.zones).find((zone) => zone.id === 'Asia/Kolkata');

    expect(india?.label).toBe('Kolkata (GMT+5:30)');
  });

  /**
   * The platform these tests run on answers, as Chrome does, with CLDR's stable identifiers, some
   * of which keep a name the place no longer goes by. Each is offered under its current name.
   */
  it('offers every zone under the name it goes by now', () => {
    const ids = idsIn(groups);

    expect(ids).toEqual(
      expect.arrayContaining(['Asia/Kolkata', 'Europe/Kyiv', 'Asia/Ho_Chi_Minh', 'America/Nuuk']),
    );
    expect(ids).not.toEqual(
      expect.arrayContaining([
        expect.stringMatching(/^(Asia\/Calcutta|Europe\/Kiev|Asia\/Saigon)$/),
      ]),
    );
  });

  /** A place a platform happens to name twice is one place, and one choice. */
  it('offers a place the platform names twice once', () => {
    vi.spyOn(Intl, 'supportedValuesOf').mockReturnValue(['Asia/Calcutta', 'Asia/Kolkata']);

    expect(idsIn(timeZoneGroups())).toStrictEqual(['Asia/Kolkata', 'UTC']);
  });

  /** The engine is sent the current name, so this platform has to be able to read it as well. */
  it('offers only names this platform can read', () => {
    for (const id of Intl.supportedValuesOf('timeZone')) {
      expect(() => new Intl.DateTimeFormat('en', { timeZone: currentZoneName(id) })).not.toThrow();
    }
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

    expect(thisDeviceTimeZone(groups)).toBe(currentZoneName(here));
  });

  /** A device that reports its zone by an earlier name is started on the name it goes by now. */
  it('starts a device that calls its zone by an earlier name on the current one', () => {
    const reported = Intl.DateTimeFormat().resolvedOptions();

    vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockReturnValue({
      ...reported,
      timeZone: 'Asia/Calcutta',
    });

    expect(thisDeviceTimeZone(groups)).toBe('Asia/Kolkata');
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

  /**
   * `Intl.supportedValuesOf` answers with geographic zones only, so this one is never among them
   * and has to be added. A browser reporting it is ordinary rather than exotic — it is what a
   * machine with no zone configured says, and what Firefox tells every website when its
   * fingerprint resistance is on — and without it such a device is quietly placed in Abidjan,
   * which is simply the zone that sorts first.
   */
  it('offers a universal zone, which the platform itself never does', () => {
    expect(Intl.supportedValuesOf('timeZone')).not.toContain('UTC');
    expect(groups.flatMap((group) => group.zones).map((zone) => zone.id)).toContain('UTC');
  });
});

describe('writing a zone into a sentence', () => {
  it.each([
    ['Asia/Kolkata', 'Kolkata'],
    ['Asia/Calcutta', 'Kolkata'],
    ['Europe/Kiev', 'Kyiv'],
    ['America/Argentina/Buenos_Aires', 'Buenos Aires'],
    ['America/Buenos_Aires', 'Buenos Aires'],
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

  it('starts on a wanted zone named by an earlier name under the name it goes by now', () => {
    expect(offeredZone(groups, 'Asia/Calcutta')).toBe('Asia/Kolkata');
  });

  /**
   * A stored zone is not always among the choices a particular browser offers. Falling through to
   * whichever happens to be first is how somebody ends up measuring a website in a country nobody
   * involved has ever been to.
   */
  it('falls back to this device rather than to whichever zone happens to be first', () => {
    const invented = 'Mars/Olympus_Mons';

    expect(offeredZone(groups, invented)).toBe(thisDeviceTimeZone(groups));
    expect(offeredZone(groups, invented)).not.toBe(invented);
  });

  it('falls back the same way when nothing was wanted at all', () => {
    expect(offeredZone(groups, undefined)).toBe(thisDeviceTimeZone(groups));
  });

  /**
   * The last resort, for a device the platform cannot place at all. Falling through to the first
   * entry in the list puts somebody in Côte d'Ivoire without their having chosen it.
   */
  it('falls back to the universal zone rather than to a country nobody chose', () => {
    const abidjanFirst = [
      { area: 'Africa', zones: [{ id: 'Africa/Abidjan', label: 'Abidjan (GMT+0:00)' }] },
      { area: 'UTC', zones: [{ id: 'UTC', label: 'UTC (GMT+0:00)' }] },
    ];

    expect(offeredZone(abidjanFirst, 'Mars/Olympus_Mons')).toBe('UTC');
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
   * The reason this exists. A website counted in a zone this browser does not offer would
   * otherwise open on a fall-back, and somebody who came to rename it would move the boundary of
   * its day by saving a field they never touched.
   */
  it('puts a zone this browser does not offer under the region it belongs to', () => {
    const asia = withZone(offered, 'Asia/Dubai').find((group) => group.area === 'Asia');

    expect(asia?.zones.map((zone) => zone.id)).toStrictEqual(['Asia/Dubai', 'Asia/Kolkata']);
  });

  it('names it the way every other zone is named', () => {
    const added = withZone(offered, 'Asia/Dubai')
      .flatMap((group) => group.zones)
      .find((zone) => zone.id === 'Asia/Dubai');

    expect(added?.label).toBe('Dubai (GMT+4:00)');
  });

  /** A zone stored under an earlier name is already on offer under its current one. */
  it('adds nothing for a zone stored under an earlier name of one it offers', () => {
    expect(withZone(offered, 'Asia/Calcutta')).toBe(offered);
  });

  /** And where it is not on offer at all, it is added under its current name. */
  it('adds a zone stored under an earlier name under the name it goes by now', () => {
    const europe = withZone(offered, 'Europe/Kiev').find((group) => group.area === 'Europe');

    expect(europe?.zones.map((zone) => zone.label)).toStrictEqual([
      expect.stringMatching(/^Kyiv \(GMT\+[23]:00\)$/),
      'London (GMT+1)',
    ]);
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
    const widened = withZone(timeZoneGroups(), 'Mars/Olympus_Mons');

    expect(offeredZone(widened, 'Mars/Olympus_Mons')).toBe('Mars/Olympus_Mons');
  });

  it('starts a picker on a zone stored under an earlier name under its current one', () => {
    const widened = withZone(timeZoneGroups(), 'Asia/Calcutta');

    expect(offeredZone(widened, 'Asia/Calcutta')).toBe('Asia/Kolkata');
  });
});

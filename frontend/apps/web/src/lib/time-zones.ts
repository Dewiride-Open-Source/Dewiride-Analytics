/**
 * The time zones somebody can choose from, taken from the platform rather than from a list.
 *
 * A list bundled with the dashboard would start drifting from the engine's the first time a
 * country changed its rules, and the engine refuses a zone it does not recognise — so a stale
 * list becomes a setup screen that offers a choice the engine will not accept.
 *
 * What the platform hands back is a wire format: several hundred identifiers like
 * `Asia/Kolkata`, sorted by the continent that happens to be spelled first. Nobody chooses a
 * time zone that way. They are grouped by region and labelled by city and offset here, and only
 * the identifier travels to the engine.
 */

const FALLBACK = 'Etc/UTC';

/** A region heading and the zones under it. */
export interface TimeZoneGroup {
  /** What the region is called: `Asia`, `Europe`, `Other`. */
  readonly area: string;
  readonly zones: readonly TimeZoneChoice[];
}

/** One zone, as an identifier for the engine and a sentence for a person. */
export interface TimeZoneChoice {
  /** The identifier the engine stores, such as `Asia/Kolkata`. */
  readonly id: string;
  /** What it is called on screen, such as `Kolkata (GMT+5:30)`. */
  readonly label: string;
}

/**
 * Every zone this platform knows, grouped by region and labelled for reading.
 *
 * Computed once per screen rather than memoised at module scope: it costs a few milliseconds,
 * and caching it across a session would outlive a device whose own zone has since changed.
 */
export function timeZoneGroups(): readonly TimeZoneGroup[] {
  const supported = Intl.supportedValuesOf('timeZone');
  const identifiers = supported.length > 0 ? supported : [FALLBACK];
  const regions = new Map<string, TimeZoneChoice[]>();

  for (const id of identifiers) {
    const area = areaOf(id);
    const region = regions.get(area) ?? [];

    region.push(choiceFor(id));
    regions.set(area, region);
  }

  return [...regions.entries()]
    .map(([area, zones]) => ({ area, zones: zones.toSorted(byLabel) }))
    .toSorted(byArea);
}

/**
 * The same zones, with the one a website is already counted in among them.
 *
 * Platforms disagree about zone names — the same place is `Asia/Calcutta` on one and
 * `Asia/Kolkata` on another — so a stored zone is not always one of the choices the browser
 * somebody happens to be using offers. A picker that cannot offer it opens on a fall-back
 * instead, and somebody who came to rename their website moves the boundary of its day by saving
 * a field they never touched. Adding the stored zone to the choices is what stops that.
 *
 * @param groups Every zone this platform offers.
 * @param id The zone that has to be among them, where there is one.
 * @returns The groups unchanged, or with that zone under the region it belongs to.
 */
export function withZone(
  groups: readonly TimeZoneGroup[],
  id: string | undefined,
): readonly TimeZoneGroup[] {
  if (id === undefined || id.length === 0 || offers(groups, id)) {
    return groups;
  }

  const area = areaOf(id);
  const added = choiceFor(id);

  if (!groups.some((group) => group.area === area)) {
    return [...groups, { area, zones: [added] }].toSorted(byArea);
  }

  return groups.map((group) =>
    group.area === area ? { area, zones: [...group.zones, added].toSorted(byLabel) } : group,
  );
}

/** The zone this device is set to, when the platform also offers it as a choice. */
export function thisDeviceTimeZone(groups: readonly TimeZoneGroup[]): string {
  return offeredZone(groups, Intl.DateTimeFormat().resolvedOptions().timeZone);
}

/**
 * The zone a picker should start on, given the one that would suit.
 *
 * Platforms disagree about zone names — the same place is `Asia/Calcutta` on one and
 * `Asia/Kolkata` on another — so a stored zone is not always among the choices a particular
 * browser offers. A picker asked to start on a choice it does not have starts on whichever
 * happens to be first, which is how somebody ends up measuring a website in a country nobody
 * involved has ever been to. So the fall-back is stated rather than left to the browser.
 *
 * @param groups Every zone this platform offers.
 * @param wanted The zone that would suit, if it is offered.
 * @returns The wanted zone, this device's zone, or the first there is.
 */
export function offeredZone(groups: readonly TimeZoneGroup[], wanted: string | undefined): string {
  if (wanted !== undefined && offers(groups, wanted)) {
    return wanted;
  }

  const here = Intl.DateTimeFormat().resolvedOptions().timeZone;

  return offers(groups, here) ? here : (groups[0]?.zones[0]?.id ?? FALLBACK);
}

/** Whether a zone is one of the choices. */
function offers(groups: readonly TimeZoneGroup[], id: string): boolean {
  return groups.some((group) => group.zones.some((zone) => zone.id === id));
}

/** One zone, named by its place and by where it stands against London today. */
function choiceFor(id: string): TimeZoneChoice {
  const place = readablePlace(placeIn(id));
  const offset = offsetOf(id);

  return { id, label: offset === null ? place : `${place} (${offset})` };
}

/** The region an identifier opens with: `Asia` in `Asia/Kolkata`, and `UTC` in `UTC`. */
function areaOf(id: string): string {
  const divider = id.indexOf('/');

  return divider > 0 ? id.slice(0, divider) : id;
}

/** Everything after it: `Kolkata`, or `Argentina/Buenos_Aires` where the place has two parts. */
function placeIn(id: string): string {
  const divider = id.indexOf('/');

  return divider > 0 ? id.slice(divider + 1) : id;
}

function byLabel(a: TimeZoneChoice, b: TimeZoneChoice): number {
  return a.label.localeCompare(b.label);
}

function byArea(a: TimeZoneGroup, b: TimeZoneGroup): number {
  return a.area.localeCompare(b.area);
}

/** `Argentina/Buenos_Aires` reads as `Argentina — Buenos Aires`. */
function readablePlace(place: string): string {
  return place.replaceAll('_', ' ').replaceAll('/', ' — ');
}

/**
 * The zone's current offset from UTC, written the way a clock is: `GMT+5:30`, not `GMT+05:30`.
 *
 * This is the offset in force today, so a zone that observes daylight saving reads differently in
 * June than in December. That is the honest thing to show somebody choosing one now.
 *
 * An identifier this platform has never heard of is refused outright rather than answered
 * vaguely, so the lookup is guarded. A zone stored by an engine running somewhere else still has
 * to be offered, and a place shown without an offset beside it is far better than a panel that
 * fails to draw at all.
 */
function offsetOf(id: string): string | null {
  try {
    const parts = new Intl.DateTimeFormat('en', {
      timeZone: id,
      timeZoneName: 'longOffset',
    }).formatToParts();

    const named = parts.find((part) => part.type === 'timeZoneName')?.value ?? 'GMT';

    return named.replace(/([+-])0(\d)/, '$1$2');
  } catch {
    return null;
  }
}

/**
 * A zone identifier written the way somebody would say it: `Asia/Kolkata` reads as `Kolkata`.
 *
 * An identifier is a wire format and never belongs on a screen, but the place inside it is
 * exactly what a person needs when they are being told which day a total was counted in.
 */
export function readableZone(id: string): string {
  const divider = id.lastIndexOf('/');

  return readablePlace(divider > 0 ? id.slice(divider + 1) : id);
}

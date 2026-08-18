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
  const byArea = new Map<string, TimeZoneChoice[]>();

  for (const id of identifiers) {
    const divider = id.indexOf('/');
    const area = divider > 0 ? id.slice(0, divider) : id;
    const place = divider > 0 ? id.slice(divider + 1) : id;
    const group = byArea.get(area) ?? [];

    group.push({ id, label: `${readablePlace(place)} (${offsetOf(id)})` });
    byArea.set(area, group);
  }

  return [...byArea.entries()]
    .map(([area, zones]) => ({
      area,
      zones: zones.toSorted((a, b) => a.label.localeCompare(b.label)),
    }))
    .toSorted((a, b) => a.area.localeCompare(b.area));
}

/** The zone this device is set to, when the platform also offers it as a choice. */
export function thisDeviceTimeZone(groups: readonly TimeZoneGroup[]): string {
  const here = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const offered = groups.some((group) => group.zones.some((zone) => zone.id === here));

  return offered ? here : (groups[0]?.zones[0]?.id ?? FALLBACK);
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
 */
function offsetOf(id: string): string {
  const parts = new Intl.DateTimeFormat('en', {
    timeZone: id,
    timeZoneName: 'longOffset',
  }).formatToParts();

  const named = parts.find((part) => part.type === 'timeZoneName')?.value ?? 'GMT';

  return named.replace(/([+-])0(\d)/, '$1$2');
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

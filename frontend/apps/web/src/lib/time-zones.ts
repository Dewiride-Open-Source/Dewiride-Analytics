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
 *
 * Taken from the platform means taken from whichever machine asks, and a page is drawn on two: the
 * server that sends it and the browser that takes it over, whose copies of the rules and whose own
 * zones need not agree. So a screen does not draw these up itself. It asks `useTimeZoneGroups`,
 * which leaves them out until the page is the browser's.
 */

/**
 * The zone for somebody the platform cannot place.
 *
 * Not a placeholder. `Intl.supportedValuesOf` answers with geographic zones only and therefore
 * never contains this one — while a browser reporting it is ordinary: it is what a machine with no
 * zone configured says, and what Firefox tells every website when its fingerprint resistance is
 * switched on. Left out, the picker opens on whichever zone sorts first, which is Abidjan, and the
 * screen tells somebody their website is counted in Côte d'Ivoire.
 */
const UNIVERSAL = 'UTC';

/**
 * The name a zone goes by now, for every zone a platform may still call by an earlier one.
 *
 * V8 — the engine of Chrome and Edge, and of Node on a server — answers with CLDR's stable
 * identifiers, which keep the name a zone had when CLDR first listed it: `Asia/Calcutta` for
 * Kolkata, `Europe/Kiev` for Kyiv, `Asia/Saigon` for Ho Chi Minh City. The time zone database
 * every engine reads has renamed them since, and knows them by both names. So a zone is offered,
 * started on and saved under the name it goes by now, and a website stored under an earlier one is
 * shown as the place it is, whichever browser it is read in.
 *
 * The pairs are CLDR's own: every zone in `common/bcp47/timezone.xml` whose stable identifier
 * differs from the IANA name its `iana` attribute records. A browser whose engine already answers
 * with the current names, as Firefox's does, passes through unchanged — and the pairs go on being
 * needed after every browser does, for the websites stored under an earlier name, which are never
 * rewritten.
 */
const CURRENT_NAMES: ReadonlyMap<string, string> = new Map([
  ['Africa/Asmera', 'Africa/Asmara'],
  ['America/Buenos_Aires', 'America/Argentina/Buenos_Aires'],
  ['America/Catamarca', 'America/Argentina/Catamarca'],
  ['America/Coral_Harbour', 'America/Atikokan'],
  ['America/Cordoba', 'America/Argentina/Cordoba'],
  ['America/Godthab', 'America/Nuuk'],
  ['America/Indianapolis', 'America/Indiana/Indianapolis'],
  ['America/Jujuy', 'America/Argentina/Jujuy'],
  ['America/Louisville', 'America/Kentucky/Louisville'],
  ['America/Mendoza', 'America/Argentina/Mendoza'],
  ['Asia/Calcutta', 'Asia/Kolkata'],
  ['Asia/Katmandu', 'Asia/Kathmandu'],
  ['Asia/Rangoon', 'Asia/Yangon'],
  ['Asia/Saigon', 'Asia/Ho_Chi_Minh'],
  ['Atlantic/Faeroe', 'Atlantic/Faroe'],
  ['Europe/Kiev', 'Europe/Kyiv'],
  ['Pacific/Enderbury', 'Pacific/Kanton'],
  ['Pacific/Ponape', 'Pacific/Pohnpei'],
  ['Pacific/Truk', 'Pacific/Chuuk'],
]);

/**
 * The name a zone goes by now: its own, unless it is one of the earlier names above.
 *
 * @param id A zone identifier, as a platform or the engine wrote it.
 * @returns The identifier to offer, show and save it under.
 */
export function currentZoneName(id: string): string {
  return CURRENT_NAMES.get(id) ?? id;
}

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
 * Computed for each screen that asks, through `useTimeZoneGroups`, rather than memoised at module
 * scope: it costs a few milliseconds, and a list kept for the life of the page would outlive a
 * device whose own zone has since changed. The bar across the top stays for the whole of a visit,
 * so the list its picker draws is the one exception, kept until the page is next loaded.
 *
 * Each zone is offered under the name it goes by now. Two names a platform offers for one place
 * are one choice.
 */
export function timeZoneGroups(): readonly TimeZoneGroup[] {
  const supported = [...new Set(Intl.supportedValuesOf('timeZone').map(currentZoneName))];
  const identifiers = supported.includes(UNIVERSAL) ? supported : [...supported, UNIVERSAL];
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
 * A stored zone is not always one the browser somebody happens to be using offers — the engine's
 * copy of the rules can know a zone the browser's does not. A picker that cannot offer it opens on
 * a fall-back instead, and somebody who came to rename their website moves the boundary of its day
 * by saving a field they never touched. Adding the stored zone to the choices is what stops that.
 * A zone stored under an earlier name is the choice offered under its current one, and is not
 * added a second time.
 *
 * @param groups Every zone this platform offers.
 * @param id The zone that has to be among them, where there is one.
 * @returns The groups unchanged, or with that zone under the region it belongs to.
 */
export function withZone(
  groups: readonly TimeZoneGroup[],
  id: string | undefined,
): readonly TimeZoneGroup[] {
  if (id === undefined || id.length === 0) {
    return groups;
  }

  const current = currentZoneName(id);

  if (offers(groups, current)) {
    return groups;
  }

  const area = areaOf(current);
  const added = choiceFor(current);

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
 * A zone is not always among the choices a particular browser offers. A picker asked to start on a
 * choice it does not have starts on whichever happens to be first, which is how somebody ends up
 * measuring a website in a country nobody involved has ever been to. So the fall-back is stated
 * rather than left to the browser, and where this device cannot be placed either it is the
 * universal zone rather than a country nobody chose. The wanted zone and this device's are each
 * looked for under the name they go by now, which is the name they are offered under.
 *
 * @param groups Every zone this platform offers.
 * @param wanted The zone that would suit, if it is offered.
 * @returns The wanted zone, this device's zone, the universal zone, or the first there is.
 */
export function offeredZone(groups: readonly TimeZoneGroup[], wanted: string | undefined): string {
  const asked = wanted === undefined ? undefined : currentZoneName(wanted);

  if (asked !== undefined && offers(groups, asked)) {
    return asked;
  }

  const here = currentZoneName(Intl.DateTimeFormat().resolvedOptions().timeZone);

  if (offers(groups, here)) {
    return here;
  }

  return offers(groups, UNIVERSAL) ? UNIVERSAL : (groups[0]?.zones[0]?.id ?? UNIVERSAL);
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
 * A zone identifier written the way somebody would say it: `Asia/Kolkata` reads as `Kolkata`, and
 * so does `Asia/Calcutta`, the place being called by the name it goes by now.
 *
 * An identifier is a wire format and never belongs on a screen, but the place inside it is
 * exactly what a person needs when they are being told which day a total was counted in.
 */
export function readableZone(id: string): string {
  const current = currentZoneName(id);
  const divider = current.lastIndexOf('/');

  return readablePlace(divider > 0 ? current.slice(divider + 1) : current);
}

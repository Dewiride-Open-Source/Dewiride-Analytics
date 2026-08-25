import type {
  DeviceKind,
  EvidenceStrength,
  TrafficCategory,
  TrafficGroup,
  VisitSourceKind,
} from '@/lib/api/schemas';

/**
 * How a list of journeys is narrowed down, and what may be asked for.
 *
 * Kept apart from the controls that set it so the rules can be checked without rendering anything,
 * and so the screen and the question sent to the engine cannot drift apart.
 */

/**
 * The floors a reader may put under the evidence.
 *
 * Three, not five. Nothing is worth a floor of "nothing to go on", which is every visit; and
 * "confirmed" is reserved for an identity established from an operator's own published addresses,
 * which nothing yet reaches — offering it would be offering a choice that can only ever come back
 * empty, which reads as a fault in the product rather than as an honest limit.
 */
export const STRENGTH_FLOORS = ['weak', 'moderate', 'strong'] as const satisfies EvidenceStrength[];

/** One of the floors on offer. */
export type StrengthFloor = (typeof STRENGTH_FLOORS)[number];

/** How much of the website a journey must have covered to be listed. */
export const PAGE_FLOORS = [0, 1, 2] as const;

/** One of those. */
export type PageFloor = (typeof PAGE_FLOORS)[number];

/** How many journeys a page may hold, smallest first. */
export const PAGE_SIZES = [10, 25, 50, 100] as const;

/**
 * How many a page holds until somebody says otherwise.
 *
 * Every journey carries its whole case, so a page is a slice rather than the period: a hundred at
 * once is a slower screen nobody scrolls to the end of.
 */
export const DEFAULT_PAGE_SIZE = 25;

/**
 * The things about a visit a reader may pick values from, in the order they are offered.
 *
 * Held as a list rather than written out again at each place that walks them. A tenth added and
 * forgotten in one of those places is a control that looks like it narrows the list and does
 * nothing, which is the one failure a reader has no way of noticing.
 */
export const JOURNEY_DIMENSIONS = [
  'categories',
  'devices',
  'sourceKinds',
  'browsers',
  'systems',
  'countries',
  'towns',
  'networks',
  'sources',
  'entryPages',
] as const;

/** One of them. */
export type JourneyDimension = (typeof JOURNEY_DIMENSIONS)[number];

/**
 * The nine whose values come from what a period actually held.
 *
 * Every one of them is an open set — nobody can write down in advance which browsers or which
 * towns a website's own traffic will turn out to hold — so the values on offer are asked for
 * rather than listed. The conclusion is the exception: it is a vocabulary the engine names, and
 * its counts come from the same answer the rest of the screen is already reading.
 */
export const DETAIL_DIMENSIONS = [
  'devices',
  'sourceKinds',
  'browsers',
  'systems',
  'countries',
  'towns',
  'networks',
  'sources',
  'entryPages',
] as const satisfies readonly JourneyDimension[];

/** One of those. */
export type DetailDimension = (typeof DETAIL_DIMENSIONS)[number];

/**
 * How many values of one thing may be asked for at once.
 *
 * The engine's own limit, kept here as well so that both ways of reaching it agree: a control
 * stops offering more once a reader has picked this many, and an address naming more than this
 * opens on the first of them rather than on a refusal nobody can act on.
 */
export const MOST_VALUES = 25;

/**
 * What each narrowing is called, in the question the engine is asked and in the address a reader
 * shares alike.
 *
 * One spelling for both, because they are the same narrowing written down twice: a link somebody
 * sends says what it was narrowed to, and so does the request that answers it. Two lists would be
 * one list and a copy of it that drifts.
 *
 * Singular, because a set is written as its name repeated once per value rather than once with a
 * list inside it.
 */
export const NARROWING_NAMES = {
  categories: 'category',
  devices: 'device',
  sourceKinds: 'sourceKind',
  browsers: 'browser',
  systems: 'system',
  countries: 'country',
  towns: 'town',
  networks: 'network',
  sources: 'source',
  entryPages: 'entryPage',
  leastStrength: 'strength',
  leastPages: 'minPages',
} as const satisfies Readonly<Record<keyof JourneyFilters, string>>;

/**
 * What a reader has narrowed the list to.
 *
 * Three of the ten sets are vocabularies the engine names and can be checked against; the other
 * seven hold whatever a website's own traffic actually recorded, which nobody can write down in
 * advance. An empty set is the question "all of them", and the empty text inside one is the
 * question "the ones nothing was established about" — a different question, and a fair one.
 */
export interface JourneyFilters {
  /** Which conclusions to show. */
  readonly categories: readonly TrafficCategory[];
  /** Which kinds of device the visitors were on. */
  readonly devices: readonly DeviceKind[];
  /** Which kinds of place sent them. */
  readonly sourceKinds: readonly VisitSourceKind[];
  /** Which browsers, spelled as the website's own traffic recorded them. */
  readonly browsers: readonly string[];
  /** Which systems under those browsers. */
  readonly systems: readonly string[];
  /** Which countries they were in. */
  readonly countries: readonly string[];
  /** Which towns. */
  readonly towns: readonly string[];
  /** Which networks they reached the website over. */
  readonly networks: readonly string[];
  /** Which websites and services sent them. */
  readonly sources: readonly string[];
  /** Which pages they arrived on. */
  readonly entryPages: readonly string[];
  /** The least evidence a verdict must carry, or nothing for any. */
  readonly leastStrength: StrengthFloor | null;
  /** The fewest pages a journey must have gone to. */
  readonly leastPages: PageFloor;
}

/** Everything, which is where the screen opens. */
export const EVERY_JOURNEY: JourneyFilters = {
  categories: [],
  devices: [],
  sourceKinds: [],
  browsers: [],
  systems: [],
  countries: [],
  towns: [],
  networks: [],
  sources: [],
  entryPages: [],
  leastStrength: null,
  leastPages: 0,
};

/** The two floors, which are controls a reader narrows with but hold no set of values. */
export const JOURNEY_FLOORS = ['strength', 'pages'] as const;

/** One of the controls a reader narrows a list with, whether it holds values or a floor. */
export type NarrowingControl = JourneyDimension | (typeof JOURNEY_FLOORS)[number];

/** Which controls are narrowing the list at the moment, in the order they are offered. */
export function narrowedBy(filters: JourneyFilters): readonly NarrowingControl[] {
  return [
    ...JOURNEY_DIMENSIONS.filter((dimension) => filters[dimension].length > 0),
    ...(filters.leastStrength === null ? [] : (['strength'] as const)),
    ...(filters.leastPages === 0 ? [] : (['pages'] as const)),
  ];
}

/** Whether anything at all has been narrowed, which is when there is something to clear. */
export function isNarrowed(filters: JourneyFilters): boolean {
  return narrowedBy(filters).length > 0;
}

/**
 * Whether the figures a control offers beside its values still describe the list.
 *
 * They count the whole period rather than what is left of it, so they hold while nothing else has
 * narrowed it away — and within one control they go on holding however many values are picked,
 * because those values are alternatives to each other rather than conditions piled on top. Reading
 * "France 209" beside a list already cut down to phones would be reading a number that is true of
 * the period and false of anything the reader is about to see, so it goes.
 *
 * @param filters What is narrowed to now.
 * @param control The control asking.
 * @returns Whether its figures may be shown.
 */
export function countsHold(filters: JourneyFilters, control: NarrowingControl): boolean {
  return narrowedBy(filters).every((one) => one === control);
}

/**
 * Whether one control is already holding as many values as may be asked for at once.
 *
 * Asked so a control can stop offering more, which is the only honest way to reach a limit: a
 * value that could be picked and then made the whole screen fail is worse than one that says it
 * cannot be picked. Taking values off is never affected.
 *
 * @param filters What is narrowed to now.
 * @param dimension The control asking.
 */
export function isFull(filters: JourneyFilters, dimension: JourneyDimension): boolean {
  return filters[dimension].length >= MOST_VALUES;
}

/**
 * Puts one set of chosen values back, leaving every other narrowing alone.
 *
 * The values are handled as plain text here whatever they describe. Adding one and taking one away
 * is the same work for a country as for a browser, and written out ten times it is nine that stay
 * right and one that drifts. The callers keep the difference in their signatures, so nothing
 * outside this file can put a browser where a country belongs.
 */
function withChoices(
  filters: JourneyFilters,
  dimension: JourneyDimension,
  chosen: readonly string[],
): JourneyFilters {
  return { ...filters, [dimension]: chosen } as JourneyFilters;
}

/**
 * Adds a value to one of the sets, or takes it off again.
 *
 * The order they were chosen in is kept, so a set of chips does not rearrange itself under
 * somebody's finger as they pick a second one.
 *
 * @param filters What is narrowed to now.
 * @param dimension Which set the value belongs to.
 * @param value The value pressed.
 * @returns What is narrowed to next.
 */
export function toggleChoice<D extends JourneyDimension>(
  filters: JourneyFilters,
  dimension: D,
  value: JourneyFilters[D][number],
): JourneyFilters {
  const chosen: readonly string[] = filters[dimension];

  return chosen.includes(value)
    ? withoutChoice(filters, dimension, value)
    : withChoices(filters, dimension, [...chosen, value]);
}

/**
 * Takes one value off, for a control that stands for that one value rather than for the set.
 *
 * @param filters What is narrowed to now.
 * @param dimension Which set the value belongs to.
 * @param value The value being dropped.
 * @returns What is narrowed to next.
 */
export function withoutChoice<D extends JourneyDimension>(
  filters: JourneyFilters,
  dimension: D,
  value: JourneyFilters[D][number],
): JourneyFilters {
  const chosen: readonly string[] = filters[dimension];

  return withChoices(
    filters,
    dimension,
    chosen.filter((one) => one !== value),
  );
}

/**
 * The narrowing, written the way the engine is asked for it.
 *
 * The values within one set are put in a settled order rather than the order they were pressed in,
 * so two readers who picked the same things in a different order are asking one question and share
 * one answer. What is on screen keeps the order it was picked in; only the question is sorted.
 *
 * @param filters What is narrowed to now.
 * @returns The question, ready to be asked or to name the answer it comes back with.
 */
export function narrowingParams(filters: JourneyFilters): URLSearchParams {
  const asked = new URLSearchParams();

  for (const dimension of JOURNEY_DIMENSIONS) {
    const chosen: readonly string[] = filters[dimension];

    for (const value of [...chosen].sort((first, second) => first.localeCompare(second))) {
      asked.append(NARROWING_NAMES[dimension], value);
    }
  }

  if (filters.leastStrength !== null) {
    asked.set(NARROWING_NAMES.leastStrength, filters.leastStrength);
  }

  if (filters.leastPages > 0) {
    asked.set(NARROWING_NAMES.leastPages, String(filters.leastPages));
  }

  return asked;
}

/** One conclusion a period reached, and how many journeys reached it. */
export interface CategoryTally {
  readonly category: TrafficCategory;
  readonly journeys: number;
}

/**
 * The conclusions a period actually reached, most journeys first.
 *
 * The engine reports a category and the weight behind it together, because a hundred visits called
 * a crawler on slight evidence is a different statement from a hundred called one on strong
 * evidence. A control that narrows the list by category wants them added back up — the weight is
 * its own control beside it.
 *
 * Only what happened is offered. A list of fourteen possibilities, eleven of which never occurred
 * on this website, is a longer way of finding the three that did.
 *
 * @param groups What the period was judged to be, as the engine grouped it.
 * @returns One row per conclusion, busiest first, with the conclusion's own name breaking a tie.
 */
export function tallyCategories(groups: readonly TrafficGroup[]): readonly CategoryTally[] {
  const counted = new Map<TrafficCategory, number>();

  for (const group of groups) {
    counted.set(group.category, (counted.get(group.category) ?? 0) + group.sessions);
  }

  return [...counted]
    .map(([category, journeys]) => ({ category, journeys }))
    .sort(
      (first, second) =>
        second.journeys - first.journeys || first.category.localeCompare(second.category),
    );
}

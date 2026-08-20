import type { EvidenceStrength, TrafficCategory, TrafficGroup } from '@/lib/api/schemas';

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

/** What a reader has narrowed the list to. */
export interface JourneyFilters {
  /** Which conclusions to show, or none of them named for all of them. */
  readonly categories: readonly TrafficCategory[];
  /** The least evidence a verdict must carry, or nothing for any. */
  readonly leastStrength: StrengthFloor | null;
  /** The fewest pages a journey must have gone to. */
  readonly leastPages: PageFloor;
}

/** Everything, which is where the screen opens. */
export const EVERY_JOURNEY: JourneyFilters = {
  categories: [],
  leastStrength: null,
  leastPages: 0,
};

/** Whether anything at all has been narrowed, which is when there is something to clear. */
export function isNarrowed(filters: JourneyFilters): boolean {
  return (
    filters.categories.length > 0 || filters.leastStrength !== null || filters.leastPages !== 0
  );
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

/**
 * Adds a conclusion to the list, or takes it off again.
 *
 * The order they were chosen in is kept, so a set of chips does not rearrange itself under
 * somebody's finger as they pick a second one.
 *
 * @param filters What is narrowed to now.
 * @param category The conclusion pressed.
 * @returns What is narrowed to next.
 */
export function toggleCategory(filters: JourneyFilters, category: TrafficCategory): JourneyFilters {
  const chosen = filters.categories.includes(category)
    ? filters.categories.filter((one) => one !== category)
    : [...filters.categories, category];

  return { ...filters, categories: chosen };
}

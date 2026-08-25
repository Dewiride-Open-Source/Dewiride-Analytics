import { describe, expect, it } from 'vitest';
import {
  countsHold,
  DETAIL_DIMENSIONS,
  EVERY_JOURNEY,
  isFull,
  isNarrowed,
  JOURNEY_DIMENSIONS,
  type JourneyDimension,
  type JourneyFilters,
  MOST_VALUES,
  NARROWING_NAMES,
  narrowedBy,
  narrowingParams,
  STRENGTH_FLOORS,
  tallyCategories,
  toggleChoice,
  withoutChoice,
} from '@/lib/analytics/journeys';
import type { TrafficGroup } from '@/lib/api/schemas';

const GROUPS: TrafficGroup[] = [
  { category: 'likely-human', strength: 'moderate', sessions: 6, pageViews: 18 },
  { category: 'likely-human', strength: 'strong', sessions: 4, pageViews: 12 },
  { category: 'security-scanner', strength: 'strong', sessions: 9, pageViews: 90 },
];

/**
 * One real value for each of the sets, so a test can walk them all without knowing which is which.
 *
 * Written out rather than invented, because a set added later and left out of here stops the file
 * compiling — which is the point of the list existing at all.
 */
const ONE_OF_EACH = {
  categories: 'likely-human',
  devices: 'phone',
  sourceKinds: 'search',
  browsers: 'Firefox',
  systems: 'Android',
  countries: 'IN',
  towns: 'Jaipur',
  networks: 'Reliance Jio Infocomm Limited',
  sources: 'Google',
  entryPages: '/pricing',
} as const satisfies Record<JourneyDimension, string>;

/** Narrowed to one value of one set, and nothing else. */
function narrowedTo(dimension: JourneyDimension): JourneyFilters {
  return { ...EVERY_JOURNEY, [dimension]: [ONE_OF_EACH[dimension]] } as JourneyFilters;
}

/** Narrowed to one value of every set at once. */
const NARROWED_TO_ALL = JOURNEY_DIMENSIONS.reduce<JourneyFilters>(
  (filters, dimension) => ({ ...filters, [dimension]: [ONE_OF_EACH[dimension]] }) as JourneyFilters,
  EVERY_JOURNEY,
);

/** Names in a settled order, so two lists of the same names compare as the same list. */
function alphabetical(names: Iterable<string>): string[] {
  return [...names].sort((first, second) => first.localeCompare(second));
}

describe('which conclusions a period reached', () => {
  /**
   * The engine reports a category and the weight behind it together, because those are two
   * different statements. A control that narrows by category wants them added back up.
   */
  it('adds a conclusion up across the weights it was reached with', () => {
    expect(tallyCategories(GROUPS)).toContainEqual({ category: 'likely-human', journeys: 10 });
  });

  it('offers the busiest first', () => {
    expect(tallyCategories(GROUPS).map((tally) => tally.category)).toEqual([
      'likely-human',
      'security-scanner',
    ]);
  });

  it('offers only what actually happened', () => {
    expect(tallyCategories(GROUPS)).toHaveLength(2);
    expect(tallyCategories([])).toEqual([]);
  });

  it('puts two conclusions of the same size in a settled order', () => {
    const tied: TrafficGroup[] = [
      { category: 'unknown', strength: 'none', sessions: 3, pageViews: 3 },
      { category: 'content-scraper', strength: 'weak', sessions: 3, pageViews: 3 },
    ];

    expect(tallyCategories(tied).map((tally) => tally.category)).toEqual([
      'content-scraper',
      'unknown',
    ]);
  });
});

describe('narrowing a list of journeys', () => {
  it('starts with nothing narrowed', () => {
    expect(isNarrowed(EVERY_JOURNEY)).toBe(false);
  });

  /**
   * Two lists, and the shorter one has to stay the longer one minus the conclusion. A set added to
   * the model and left out of the values-a-period-held list would be a narrowing the screen never
   * offers, and nothing else would notice.
   */
  it('asks the period for the values of everything except the conclusion', () => {
    expect(['categories', ...DETAIL_DIMENSIONS]).toEqual([...JOURNEY_DIMENSIONS]);
  });

  it.each([...JOURNEY_DIMENSIONS])('counts a value of its own as narrowing the list: %s', (one) => {
    expect(isNarrowed(narrowedTo(one))).toBe(true);
  });

  it('counts either floor as narrowing the list', () => {
    expect(isNarrowed({ ...EVERY_JOURNEY, leastStrength: 'moderate' })).toBe(true);
    expect(isNarrowed({ ...EVERY_JOURNEY, leastPages: 1 })).toBe(true);
  });

  it('adds a value and takes it off again', () => {
    const one = toggleChoice(EVERY_JOURNEY, 'categories', 'likely-human');
    const two = toggleChoice(one, 'categories', 'known-ai-crawler');

    expect(two.categories).toEqual(['likely-human', 'known-ai-crawler']);
    expect(toggleChoice(two, 'categories', 'likely-human').categories).toEqual([
      'known-ai-crawler',
    ]);
  });

  /**
   * A set of chips that rearranged itself as a second one was pressed would move the next chip out
   * from under somebody's finger.
   */
  it('keeps the order they were picked in', () => {
    const picked = toggleChoice(
      toggleChoice(EVERY_JOURNEY, 'towns', 'Jaipur'),
      'towns',
      'Bengaluru',
    );

    expect(picked.towns).toEqual(['Jaipur', 'Bengaluru']);
  });

  it('leaves every other narrowing alone', () => {
    const narrowed = { ...NARROWED_TO_ALL, leastStrength: 'strong', leastPages: 2 } as const;

    expect(toggleChoice(narrowed, 'countries', 'FR')).toMatchObject({
      categories: ['likely-human'],
      towns: ['Jaipur'],
      leastStrength: 'strong',
      leastPages: 2,
    });
  });

  it('takes one value off and leaves the rest of its own set', () => {
    const picked = toggleChoice(
      toggleChoice(EVERY_JOURNEY, 'sources', 'Google'),
      'sources',
      'Bing',
    );

    expect(withoutChoice(picked, 'sources', 'Google').sources).toEqual(['Bing']);
  });

  it('changes nothing when a value that was never picked is taken off', () => {
    expect(withoutChoice(narrowedTo('sources'), 'sources', 'Bing').sources).toEqual(['Google']);
  });

  it('names which controls are doing the narrowing, in the order they are offered', () => {
    const narrowed = { ...narrowedTo('towns'), leastStrength: 'strong', leastPages: 2 } as const;

    expect(narrowedBy(narrowed)).toEqual(['towns', 'strength', 'pages']);
    expect(narrowedBy(EVERY_JOURNEY)).toEqual([]);
  });

  /**
   * Confirmed identity is reserved for an operator's own published addresses, which nothing yet
   * reaches, and "nothing to go on" is every visit. Either would be a choice that can only come
   * back empty or change nothing.
   */
  it('offers no floor that could never do anything', () => {
    expect(STRENGTH_FLOORS).not.toContain('verified');
    expect(STRENGTH_FLOORS).not.toContain('none');
  });

  /**
   * The engine will not answer a question naming more values of one thing than this, and a
   * refusal is not something a reader can act on from where they are — so the control has to stop
   * offering more rather than let somebody pick one and break the screen with it.
   */
  it('says when one control is holding as many values as may be asked for at once', () => {
    const crowded = {
      ...EVERY_JOURNEY,
      towns: Array.from({ length: MOST_VALUES }, (_, at) => `Town ${at}`),
    };

    expect(isFull(crowded, 'towns')).toBe(true);
    expect(isFull(crowded, 'countries')).toBe(false);
    expect(isFull(EVERY_JOURNEY, 'towns')).toBe(false);
  });
});

describe('whether a figure beside a value still describes the list', () => {
  it('holds while nothing at all has been narrowed', () => {
    expect(countsHold(EVERY_JOURNEY, 'countries')).toBe(true);
  });

  /**
   * The values within one control are alternatives to each other rather than conditions piled on
   * top, so a second country would still add exactly the visits its figure claims.
   */
  it('holds for the one control doing the narrowing', () => {
    expect(countsHold(narrowedTo('countries'), 'countries')).toBe(true);
  });

  it('does not hold for anything else once that control is narrowing', () => {
    expect(countsHold(narrowedTo('countries'), 'categories')).toBe(false);
    expect(countsHold(narrowedTo('countries'), 'towns')).toBe(false);
  });

  it('does not hold anywhere once two controls are narrowing', () => {
    const narrowed = { ...narrowedTo('countries'), towns: ['Jaipur'] };

    expect(countsHold(narrowed, 'countries')).toBe(false);
    expect(countsHold(narrowed, 'towns')).toBe(false);
  });

  it('counts a floor as narrowing, the same as a set of values', () => {
    expect(countsHold({ ...EVERY_JOURNEY, leastPages: 2 }, 'countries')).toBe(false);
    expect(countsHold({ ...EVERY_JOURNEY, leastPages: 2 }, 'pages')).toBe(true);
  });
});

describe('the question the engine is asked', () => {
  it('asks nothing when nothing is narrowed', () => {
    expect(narrowingParams(EVERY_JOURNEY).toString()).toBe('');
  });

  /**
   * Ten sets, ten names, and the engine reads each of them by that name. One left out of the list
   * this is built from is a control that looks like it narrows the list and quietly does not,
   * which is the one failure nothing on the screen would show.
   */
  it('names every set, and names no two of them alike', () => {
    const named = JOURNEY_DIMENSIONS.map((dimension) => {
      const asked = [...narrowingParams(narrowedTo(dimension)).keys()];

      expect(asked).toHaveLength(1);

      return asked[0];
    });

    expect(new Set(named).size).toBe(JOURNEY_DIMENSIONS.length);
  });

  /**
   * One narrowing gets written down twice — as the question the engine is asked, and as the
   * address a reader shares — and the two have to be one list of names. A name nothing asks under
   * is a key a link could carry that the engine never reads; a question asked under a name the
   * list does not hold is a narrowing no link can carry back.
   */
  it('asks under exactly the names an address is written with', () => {
    const everything = { ...NARROWED_TO_ALL, leastStrength: 'strong', leastPages: 2 } as const;

    expect(alphabetical(new Set(narrowingParams(everything).keys()))).toEqual(
      alphabetical(Object.values(NARROWING_NAMES)),
    );
  });

  it('spells each of them the way the engine reads it', () => {
    expect(narrowingParams(NARROWED_TO_ALL).toString()).toBe(
      'category=likely-human&device=phone&sourceKind=search&browser=Firefox&system=Android' +
        '&country=IN&town=Jaipur&network=Reliance+Jio+Infocomm+Limited&source=Google' +
        '&entryPage=%2Fpricing',
    );
  });

  /**
   * Two readers who picked the same values in a different order are asking one question, so they
   * are handed one answer rather than each waiting for their own.
   */
  it('asks the same question however the values were picked', () => {
    const one = toggleChoice(toggleChoice(EVERY_JOURNEY, 'countries', 'IN'), 'countries', 'FR');
    const other = toggleChoice(toggleChoice(EVERY_JOURNEY, 'countries', 'FR'), 'countries', 'IN');

    expect(one.countries).not.toEqual(other.countries);
    expect(narrowingParams(one).toString()).toBe(narrowingParams(other).toString());
  });

  it('asks for both floors, and asks for neither until one is set', () => {
    const floored = { ...EVERY_JOURNEY, leastStrength: 'moderate', leastPages: 2 } as const;
    const asked = narrowingParams(floored);

    expect(asked.get('strength')).toBe('moderate');
    expect(asked.get('minPages')).toBe('2');
    expect(narrowingParams(EVERY_JOURNEY).has('strength')).toBe(false);
  });

  /** No floor at all and a floor of none are the same question, so only one of them is asked. */
  it('does not ask for a page floor of none', () => {
    expect(narrowingParams({ ...EVERY_JOURNEY, leastPages: 0 }).has('minPages')).toBe(false);
  });

  /**
   * Asking for the visits nothing was established about is a real question and a different one
   * from asking for all of them, so the empty value has to survive being written down.
   */
  it('asks for the ones nothing was established about as a value of its own', () => {
    expect(narrowingParams({ ...EVERY_JOURNEY, countries: [''] }).toString()).toBe('country=');
  });

  /**
   * A page path is whatever the visitor asked the website for, so it arrives from outside and may
   * hold anything at all. Written into the question as it stands it would end that question and
   * begin another one nobody asked.
   */
  it('keeps a page path that reads like a question of its own as one value', () => {
    const asked = narrowingParams({ ...EVERY_JOURNEY, entryPages: ['/a?b=c&minPages=2'] });

    expect(asked.getAll('entryPage')).toEqual(['/a?b=c&minPages=2']);
    expect(asked.has('minPages')).toBe(false);
  });
});

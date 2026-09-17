/**
 * Whose figures a screen shows.
 *
 * Everything the website recorded is the honest whole, and it is what every figure counts unless
 * somebody asks otherwise. But the question most owners bring to a screen is how many people came,
 * and a machinery-heavy week buries that under a band three times its height. Asking for the
 * people alone changes what every figure on the screen counts rather than how one picture is
 * drawn, so it travels in the address as the period does, and both screens about a period read it.
 */

/** What asking for people alone is called in an address, and in a question put to the engine. */
export const PEOPLE_KEY = 'only';

/**
 * The words that key may carry.
 *
 * A closed set of one. The address is written by whoever sent the link, so a word that is not
 * this one leaves every figure counting everybody.
 */
export const PEOPLE = ['people'] as const;

/** Everybody the website recorded, or only the visits judged to be people. */
export type Population = 'everybody' | (typeof PEOPLE)[number];

/**
 * Writes the population into a question or an address, where it is narrower than everybody.
 *
 * Everybody is what an absent key means to the engine and to a screen alike, so it is never
 * written: the plain address of a screen stays plain, and a question about everybody carries no
 * word for it.
 */
export function withPopulation(asked: URLSearchParams, population: Population): URLSearchParams {
  if (population !== 'everybody') {
    asked.set(PEOPLE_KEY, population);
  }

  return asked;
}

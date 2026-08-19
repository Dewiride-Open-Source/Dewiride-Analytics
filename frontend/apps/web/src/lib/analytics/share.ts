/**
 * How much of a whole one part is.
 *
 * Kept apart from the areas that show shares, because more than one of them does and a helper
 * living inside the first area to need it makes the second one import that area for arithmetic
 * that has nothing to do with it.
 *
 * @param part The part.
 * @param whole Everything the part is measured against.
 * @returns The share, between nought and one, or nought when there is no whole to divide.
 */
export function shareOf(part: number, whole: number): number {
  return whole > 0 ? part / whole : 0;
}

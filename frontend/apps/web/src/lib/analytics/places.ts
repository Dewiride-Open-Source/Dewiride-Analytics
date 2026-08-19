/**
 * Writing a place out for somebody to read.
 *
 * Countries are stored as their two-letter code and written out here, so the same stored row
 * reads as "India" or "Inde" depending on who is looking. Towns have no such code and are stored
 * as the free geolocation data spells them, which is English — so a town name is passed through
 * rather than translated, and the interface does not pretend otherwise.
 */

/**
 * Builds something that turns a country code into a country name.
 *
 * @param locale The reader's language.
 * @returns A function from code to name, or to null when the code names no country.
 * @remarks
 * The formatter is built once per language and reused. Building one costs enough that doing it
 * per row of a list is noticeable, and a list is exactly what this is for.
 */
export function countryNames(locale: string): (code: string) => string | null {
  const names = displayNames(locale);

  return (code: string) => {
    const upper = code.toUpperCase();

    if (!isCountryCode(upper) || upper === NOWHERE) {
      return null;
    }

    const written = names?.of(upper);

    // An unrecognised code is handed straight back rather than refused, so a code that names no
    // country would otherwise be shown to a reader as itself.
    return written && written.toUpperCase() !== upper ? written : null;
  };
}

/**
 * The code the standard reserves for a region nobody could establish.
 *
 * Written out because it has a name — "Unknown Region" — and that name would go on the screen as
 * though it were a place somebody was, beside India and the United Kingdom. This card already
 * says what it means for a reader not to have been placed, in words a person would use.
 */
const NOWHERE = 'ZZ';

/**
 * Whether a value is shaped like a country code at all.
 *
 * The engine sends an empty string for an address it could not place, and a formatter given
 * anything other than two letters raises rather than returning nothing.
 */
function isCountryCode(value: string): boolean {
  return /^[A-Za-z]{2}$/.test(value);
}

function displayNames(locale: string): Intl.DisplayNames | null {
  try {
    return new Intl.DisplayNames([locale], { type: 'region' });
  } catch {
    // A language this runtime has no names for. The codes are shown as codes, which is worse
    // than a name and better than an empty list.
    return null;
  }
}

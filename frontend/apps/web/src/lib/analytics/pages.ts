/**
 * How a page's address reaches the screen.
 *
 * The address is written by whoever asked for the page, which on this product's own data includes
 * everything that goes looking for a way into a website. It is shown as text and never followed,
 * and the rules below are about making it readable without making it something else.
 */

/** What a page with no address of its own is called. */
const ROOT = '/';

/**
 * The address as somebody would read it.
 *
 * Addresses are stored the way they arrived, which for anything outside the English alphabet
 * means each letter has been rewritten as a run of per-cent signs and digits. A site whose pages
 * are named in Hindi or Japanese would otherwise have every row of this list read as machinery.
 *
 * Only the letters are turned back. The characters that divide one part of an address from
 * another are left as they were found, so a page cannot be made to look as though it sits
 * somewhere it does not — and an address encoded wrongly is shown exactly as it arrived rather
 * than not shown at all.
 *
 * @param path The address as it was asked for.
 * @returns The address to show.
 */
export function readablePath(path: string): string {
  if (path === '') {
    return ROOT;
  }

  try {
    return decodeURI(path);
  } catch {
    return path;
  }
}

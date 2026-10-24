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
 * The characters that exist only to change the direction the text around them runs in.
 *
 * Unicode's own list of them, by the property it gives them: twelve marks, embeddings, overrides
 * and isolates. None of them is visible, and each can change the order the characters after it
 * are drawn in.
 */
const DIRECTION_CONTROLS = /\p{Bidi_Control}/gu;

/**
 * The address as somebody would read it.
 *
 * Addresses are stored the way they arrived, which for anything outside the English alphabet
 * means each letter has been rewritten as a run of per-cent signs and digits. A site whose pages
 * are named in Hindi or Japanese would otherwise have every row of this list read as machinery.
 *
 * Only the letters are turned back. The characters that divide one part of an address from
 * another are left as they were found — an encoded `/`, `?` or `#` is never turned into a divider
 * — and an address encoded wrongly is shown as it arrived rather than not shown at all.
 *
 * A character that changes the direction of the text is written as a code wherever it appears,
 * whether it arrived as a code or as itself. One placed part-way through a page's name turns the
 * rest of the name around: "/invoice", the control, then "fdp.exe" is drawn as "/invoiceexe.pdf"
 * — on a row, in a trail and in the filter that offers it — while the address actually asked for
 * ends in ".exe".
 *
 * Which way an address runs is the screen's to set, and every screen sets it left to right, as
 * RFC 3987 asks of an address with right-to-left letters in it. Letters themselves still read the
 * way their script does: two neighbouring parts written right to left, or one followed by a
 * number, are drawn in that script's order, which can put them the other way round from the
 * address as it was asked for. The standard shows the same of every address drawn that way.
 *
 * @param path The address as it was asked for.
 * @returns The address to show.
 */
export function readablePath(path: string): string {
  if (path === '') {
    return ROOT;
  }

  return decoded(path).replaceAll(DIRECTION_CONTROLS, (control) => encodeURI(control));
}

/** The address with its letters turned back, or exactly as it arrived where it cannot be. */
function decoded(path: string): string {
  try {
    return decodeURI(path);
  } catch {
    return path;
  }
}

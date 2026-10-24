import type { DeviceKind, VisitContext } from '@/lib/api/schemas';

/**
 * How what was observed about a visitor reaches the screen.
 *
 * Roughly where they were and what they read on are each assembled from several facts, any of
 * which may be missing, and the rule in both cases is the same: give the one reading that tells
 * the reader something, and never a second that only repeats it. Four facts reading "not known" is
 * what a screen looks like when it is describing its own gaps instead of the visit.
 *
 * Kept apart from the components that show them so the rules can be checked without rendering
 * anything. What comes back is the reading and never the sentence: a sentence assembled here would
 * be assembled in English, and the words belong to the catalogue.
 */

/** What a visit was read on, where anything named it. */
export type ReadOn =
  | { readonly observed: 'browser-and-system'; readonly browser: string; readonly system: string }
  | { readonly observed: 'software'; readonly name: string }
  | { readonly observed: 'device'; readonly device: DeviceKind };

/**
 * How a visitor reached the site.
 *
 * Sent by another site, called by the catalogue's name for it or by its address; sent by nothing
 * at all; or unseen, where what is being read does not hold the moment they arrived and so cannot
 * say whether anything sent them.
 */
export type CameFrom =
  | { readonly arrived: 'from-site'; readonly site: string }
  | { readonly arrived: 'directly' }
  | { readonly arrived: 'unseen' };

/**
 * How a visitor reached the site, as far as what is being read can say.
 *
 * A site that sent them is named wherever one was. That nothing did is said only where the arrival
 * itself was seen. A visitor already reading when a half hour began carries no sender inside it,
 * and nor does a visit whose recorded activity is gone; "Came straight here" said of either would
 * be a guess written as a fact, and an arrival nobody saw is not one that named nowhere.
 *
 * @param context What was established about the visitor.
 * @param seen Whether what is being read holds the moment they arrived: for a finished visit,
 *   whether it holds the page the visit landed on; for a visitor here now, whether the half hour
 *   saw them arrive.
 */
export function cameFrom(context: VisitContext, seen: boolean): CameFrom {
  if (context.source !== '') {
    return { arrived: 'from-site', site: context.source };
  }

  return seen ? { arrived: 'directly' } : { arrived: 'unseen' };
}

/**
 * Roughly where the visit was, where anything placed it.
 *
 * A town is worth showing only with its country beside it: a great many town names belong to more
 * than one, and a reader shown "Springfield" alone has been told less than they think.
 */
export function placeOf(context: VisitContext, country: string | null): string | null {
  if (country === null) {
    return null;
  }

  return context.town === '' ? country : `${context.town}, ${country}`;
}

/**
 * What the visit was read on.
 *
 * The software when anything named it, since "Chrome on Android" says more than "a phone" does.
 * The kind of device is the fallback rather than an addition — a row carrying both would spend a
 * line saying twice what one of them already said.
 */
export function readOn(context: VisitContext): ReadOn | null {
  if (context.browser !== '' && context.system !== '') {
    return {
      observed: 'browser-and-system',
      browser: context.browser,
      system: context.system,
    };
  }

  if (context.browser !== '' || context.system !== '') {
    return { observed: 'software', name: context.browser || context.system };
  }

  return context.device === 'unknown' ? null : { observed: 'device', device: context.device };
}

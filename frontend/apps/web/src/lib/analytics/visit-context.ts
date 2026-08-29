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

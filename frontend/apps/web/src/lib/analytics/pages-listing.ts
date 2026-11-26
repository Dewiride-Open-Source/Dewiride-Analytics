import type { Population } from '@/lib/analytics/people-only';
import type { PagesListing } from '@/lib/analytics/posts';
import type { AllPages } from '@/lib/api/schemas';

/**
 * What stands where the list of every page of a website is shown, and what it is drawn from.
 *
 * Nothing yet while the first page of it is on its way, or what went wrong where none could be had.
 * Otherwise what the page on the screen says: nothing has been viewed at all, nothing has been
 * viewed by people, nothing is left to list since earlier months kept none of the pages viewed in
 * them and none has been viewed since, no page holds what was searched for, the page is further
 * along than the list reaches, or the pages themselves.
 */
export type PagesState =
  | { readonly look: 'waiting' }
  | { readonly look: 'failed'; readonly error: Error }
  | {
      readonly look: 'empty' | 'noPeople' | 'noneKept' | 'noMatch' | 'pastTheEnd' | 'listed';
      readonly answer: AllPages;
    };

/**
 * Which of them stands on the screen.
 *
 * Decided from the listing the page on the screen belongs to, which, while the next page is on its
 * way, is the listing of the page standing in for it: nothing is said about a page that is not the
 * one shown. A page standing in that would say there is nothing to list, for anybody or for people,
 * says nothing at all instead, since the page on its way may well list some, and offering the
 * tracking code to a website already measured, or everybody back to somebody about to see people,
 * is wrong for as long as it shows.
 *
 * Nothing listed, with nothing searched for, is nothing viewed only where no month left a page out.
 * Where one did, pages were viewed, in months that keep only the pages more than one visitor
 * opened, and saying that none was would be untrue.
 *
 * @param answer The page on the screen, whether it is the one asked for or stands in for it; what
 * went wrong asking for it, where nothing was had; or nothing while the first page is on its way.
 * @param onScreen What the page on the screen is a page of.
 * @param population Whose views it counts.
 * @param standingIn Whether the page on the screen stands in for the one asked for.
 */
export function pagesState(
  answer: AllPages | Error | undefined,
  onScreen: PagesListing,
  population: Population,
  standingIn: boolean,
): PagesState {
  if (answer === undefined) {
    return { look: 'waiting' };
  }

  if (answer instanceof Error) {
    return { look: 'failed', error: answer };
  }

  if (answer.matching > 0) {
    return { look: answer.shown.length === 0 ? 'pastTheEnd' : 'listed', answer };
  }

  if (onScreen.search !== '') {
    return { look: 'noMatch', answer };
  }

  if (standingIn) {
    return { look: 'waiting' };
  }

  if (answer.leftOut) {
    return { look: 'noneKept', answer };
  }

  return { look: population === 'everybody' ? 'empty' : 'noPeople', answer };
}

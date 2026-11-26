'use client';

import { FileClock, FileText, ScanSearch, SearchX } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import type { RefObject } from 'react';
import { ListEmpty } from '@/components/dashboard/ranked-list';
import {
  InPlaceOfRows,
  ListedSection,
  ListingWaiting,
  PRESSABLE,
  spokenOf,
  useHeld,
} from '@/components/insights/listing';
import { PageRow } from '@/components/insights/post-list';
import { viewsSince } from '@/components/insights/posts';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { type PagesState, pagesState } from '@/lib/analytics/pages-listing';
import { PAGES_LISTED_MOST, type PagesListing } from '@/lib/analytics/posts';
import type { ChosenListing } from '@/lib/analytics/use-posts-listing';
import type { AllPages, Site } from '@/lib/api/schemas';
import { sentences } from '@/lib/charts/use-left-out';
import { useAllPages } from '@/lib/queries/sites';

type AllPagesWords = ReturnType<typeof useTranslations<'insights.allPages'>>;

type AllPostsWords = ReturnType<typeof useTranslations<'insights.allPosts'>>;

interface PagesBodyProps {
  readonly site: Site;
  readonly chosen: ChosenListing;
  /** Searches for nothing, and brings the keyboard back to the box. */
  readonly onClearSearch: () => void;
  /** Opens the way of putting the tracking code on the website. */
  readonly onShowCode: () => void;
  /** The list's heading, which the keyboard is brought back to. */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * Every page of a website, with how often each has been viewed, a page of the list at a time; or
 * the one state in its place.
 *
 * Read from the record alone, whatever its sitemap says, so a website with no sitemap has a list
 * of its pages all the same. How many pages the list holds, or how many hold what was searched for,
 * and which of them the page shows, is said to anybody listening once as each answer lands. While a
 * page from before stands in for the one on its way, everything said about the list is said of that
 * page, the one on the screen.
 */
export function PagesBody({ site, chosen, onClearSearch, onShowCode, headingRef }: PagesBodyProps) {
  const t = useTranslations('insights.allPages');
  const words = useTranslations('insights.allPosts');
  const page = useAllPages(site, chosen.population, chosen.listing);
  const standingIn = page.isPlaceholderData;
  const onScreen = useHeld(chosen.listing, standingIn);
  const state = pagesState(
    page.data ?? page.error ?? undefined,
    onScreen,
    chosen.population,
    standingIn,
  );

  return (
    <>
      <output className="sr-only">{spokenOfPages(state, onScreen, t, words)}</output>
      <PagesInPlace
        state={state}
        site={site}
        chosen={chosen}
        onScreen={onScreen}
        standingIn={standingIn}
        onClearSearch={onClearSearch}
        onShowCode={onShowCode}
        headingRef={headingRef}
      />
    </>
  );
}

interface PagesInPlaceProps extends PagesBodyProps {
  readonly state: PagesState;
  /** What the page on the screen is a page of: the one asked for, or the one standing in for it. */
  readonly onScreen: PagesListing;
  readonly standingIn: boolean;
}

/** The page of the list on the screen, or the one state in its place. */
function PagesInPlace({
  state,
  site,
  chosen,
  onScreen,
  standingIn,
  onClearSearch,
  onShowCode,
  headingRef,
}: PagesInPlaceProps) {
  const t = useTranslations('insights.allPages');

  switch (state.look) {
    case 'waiting':
      return <ListingWaiting />;
    case 'failed':
      return <FailureNotice error={state.error} />;
    case 'empty':
      return (
        <ListEmpty
          icon={FileText}
          title={t('empty.title')}
          body={t('empty.body', { site: site.domain })}
          action={<Button onClick={onShowCode}>{t('empty.action')}</Button>}
        />
      );
    case 'noPeople':
      return (
        <ListEmpty
          icon={ScanSearch}
          title={t('absent.title')}
          body={t('absent.body')}
          action={<ShowEveryone chosen={chosen} />}
        />
      );
    case 'noneKept':
      return (
        <ListEmpty
          icon={FileClock}
          title={t('noneKept.title')}
          body={t('noneKept.body')}
          action={chosen.peopleOnly ? <ShowEveryone chosen={chosen} /> : undefined}
        />
      );
    case 'noMatch':
      return (
        <InPlaceOfRows
          icon={SearchX}
          title={t('noMatch.title')}
          body={t('noMatch.body')}
          action={t('noMatch.action')}
          onAction={onClearSearch}
          busy={standingIn}
        />
      );
    default:
      return (
        <PageOfPages
          answer={state.answer}
          chosen={chosen}
          onScreen={onScreen}
          standingIn={standingIn}
          headingRef={headingRef}
        />
      );
  }
}

/** The way from a list of the pages people viewed to everybody's, where it lists none. */
function ShowEveryone({ chosen }: { readonly chosen: ChosenListing }) {
  const population = useTranslations('dashboard.population.empty');

  return (
    <Button
      tone="secondary"
      size="sm"
      className={PRESSABLE}
      onClick={() => chosen.showOnlyPeople(false)}
    >
      {population('action')}
    </Button>
  );
}

interface PageOfPagesProps {
  readonly answer: AllPages;
  readonly chosen: ChosenListing;
  readonly onScreen: PagesListing;
  readonly standingIn: boolean;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * One page of the list, under a heading carrying how many pages there are and whatever qualifies
 * all of them: since when their views are counted; only where a month kept fewer pages than were
 * viewed in it, that a page few visitors opened then may show fewer views than it had; and only
 * where more pages match than the list reaches, that it lists just the most viewed, and how to find
 * the rest.
 *
 * The heading counts every page that matches, while the list is paged through only as far as it
 * reaches, so no page it offers is one with nothing on it.
 *
 * Every bar is drawn against the most any page on the list was viewed, whichever page of the list is
 * on the screen, so a page reads the same wherever in the list it is met.
 */
function PageOfPages({ answer, chosen, onScreen, standingIn, headingRef }: PageOfPagesProps) {
  const t = useTranslations('insights.allPages');
  const posts = useTranslations('insights.posts');
  const format = useFormatter();
  const beyondReach = answer.matching > PAGES_LISTED_MOST;
  const notes = sentences(
    viewsSince(answer.viewsFrom, posts, format),
    answer.leftOut ? t('leftOut') : null,
    beyondReach
      ? t(onScreen.search === '' ? 'mostListed' : 'mostListedSearched', { most: PAGES_LISTED_MOST })
      : null,
  );

  return (
    <ListedSection
      words="insights.allPages"
      counted={countOf(answer, onScreen, t)}
      notes={notes}
      headingRef={headingRef}
      answer={answer}
      reaches={Math.min(answer.matching, PAGES_LISTED_MOST)}
      onScreen={onScreen}
      standingIn={standingIn}
      goTo={chosen.goTo}
      resize={chosen.resize}
    >
      {answer.shown.map((page) => (
        <PageRow key={page.path} page={page} most={answer.mostPageViews} />
      ))}
    </ListedSection>
  );
}

/**
 * How many pages the list holds, or how many of them hold what was searched for.
 *
 * @param answer The page the figures came with.
 * @param listing What it is a page of.
 * @param t The words of the list.
 */
function countOf(answer: AllPages, { search }: PagesListing, t: AllPagesWords): string {
  return search === ''
    ? t('count', { pages: answer.matching })
    : t('found', { matching: answer.matching });
}

/**
 * What is said to anybody listening about the page on the screen, or nothing while there is none to
 * speak of: what stands in place of the list where there is nothing to list, and otherwise how many
 * pages there are, or match, and which of them the page shows.
 *
 * @param state What stands on the screen.
 * @param onScreen What the page on the screen is a page of.
 * @param t The words of the list.
 * @param words The words of the list of every post, which say which rows are shown.
 */
function spokenOfPages(
  state: PagesState,
  onScreen: PagesListing,
  t: AllPagesWords,
  words: AllPostsWords,
): string | null {
  switch (state.look) {
    case 'waiting':
    case 'failed':
      return null;
    case 'empty':
      return t('empty.title');
    case 'noPeople':
      return t('absent.title');
    case 'noneKept':
      return t('noneKept.title');
    default:
      return spokenOf(countOf(state.answer, onScreen, t), state.answer, onScreen, words);
  }
}

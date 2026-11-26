'use client';

import { ArrowLeft, SearchX } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, type RefObject, useRef, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
import { ListSwitch, ListWaiting } from '@/components/dashboard/ranked-list';
import { SiteSettings } from '@/components/dashboard/site-settings';
import { TrackingCode } from '@/components/dashboard/tracking-code';
import { PagesBody } from '@/components/insights/all-pages';
import { KeptToPeople } from '@/components/insights/kept-to-people';
import {
  InPlaceOfRows,
  ListedSection,
  ListingWaiting,
  PRESSABLE,
  spokenOf,
  useHeld,
} from '@/components/insights/listing';
import { PostRow } from '@/components/insights/post-list';
import { PostSearch } from '@/components/insights/post-search';
import { mayBeMissing, NoPosts, viewsSince } from '@/components/insights/posts';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Link } from '@/i18n/navigation';
import { withChoices } from '@/lib/analytics/period';
import { LISTS, POSTS_ORDERS, type PostsListing, postsLook } from '@/lib/analytics/posts';
import { type ChosenListing, usePostsListing } from '@/lib/analytics/use-posts-listing';
import type { PostsPage, Site, SitemapStatus } from '@/lib/api/schemas';
import { sentences } from '@/lib/charts/use-left-out';
import { useSitemapStatus } from '@/lib/queries/site-settings';
import { usePostsPage } from '@/lib/queries/sites';
import { INSIGHTS } from '@/lib/routes';

type AllPostsWords = ReturnType<typeof useTranslations<'insights.allPosts'>>;

/**
 * Every post a website's sitemap lists, or every page of it that was viewed, with how often each has
 * been read, a page at a time.
 *
 * A screen of its own under Insights, because it is worked through rather than looked back over:
 * Insights says how the posts are doing, and this finds the one somebody is looking for, by what it
 * is called or where it is, among the most read or the newest; or any page at all, by where it is,
 * among the most viewed. Each post opens onto its own page, and the way back is the same page of the
 * same list, since everything that makes the list the one being read is in the address.
 *
 * One screen from before its website is known until its list is in. It is handed over both as what
 * waits and as the screen itself, and both stand in the same place, so its heading is drawn once and
 * only filled in as the website arrives.
 */
export function AllPosts() {
  return (
    <SiteScreen waiting={<SiteAllPosts />}>{(site) => <SiteAllPosts site={site} />}</SiteScreen>
  );
}

/**
 * One website's list, for whoever the screen is kept to; and the same screen before its website is
 * known, with a stand-in for the list.
 *
 * The website's settings open from the posts, straight onto where its sitemap is named, since that
 * is the one thing on this screen they are opened for. Saving an address there can take away what
 * opened them, so focus comes back to the heading of whatever stands in the list's place by then.
 * The tracking code opens from a list of pages nothing has been viewed on yet.
 */
function SiteAllPosts({ site }: { readonly site?: Site }) {
  // Remembered people are written into the address once there is a website for them to be the people
  // of: until then the screen may still turn out to be one the choice means nothing on.
  const chosen = usePostsListing({ seeding: site !== undefined, siteId: site?.id });
  const [showingSettings, setShowingSettings] = useState(false);
  const [showingCode, setShowingCode] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);

  return (
    <div className="flex flex-col gap-6">
      <ScreenHeading site={site} chosen={chosen} />

      {site === undefined ? (
        <>
          <PeopleCaption shown={chosen.peopleOnly} />
          <ListWaiting />
        </>
      ) : (
        <>
          <ListPlace
            site={site}
            chosen={chosen}
            onCheckAddress={() => setShowingSettings(true)}
            onShowCode={() => setShowingCode(true)}
            headingRef={headingRef}
          />
          <TrackingCode
            open={showingCode}
            onClose={() => setShowingCode(false)}
            siteId={site.id}
            siteDomain={site.domain}
          />
          <SiteSettings
            open={showingSettings}
            onClose={() => setShowingSettings(false)}
            site={site}
            onRemoved={() => setShowingSettings(false)}
            focusOn="sitemap"
            returnFocusTo={headingRef}
          />
        </>
      )}
    </div>
  );
}

interface ScreenHeadingProps {
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  readonly chosen: ChosenListing;
}

/**
 * The way back to Insights, the screen's name and what it lists, and the two choices it takes
 * besides the list's own: which list, and whose views it counts. The words take the whole of a
 * phone's width, so the choices sit beneath them, side by side wherever there is room.
 */
function ScreenHeading({ site, chosen }: ScreenHeadingProps) {
  const t = useTranslations('insights.allPosts');
  const listed = useTranslations(
    chosen.list === 'pages' ? 'insights.allPages' : 'insights.allPosts',
  );
  const lists = LISTS.map((list) => ({ value: list, label: t(`list.${list}`) }));

  return (
    <header className="flex flex-col gap-2">
      <Link
        href={withChoices(INSIGHTS, { population: chosen.population })}
        className="-ml-1 inline-flex h-10 w-fit items-center gap-1.5 rounded-md px-1 text-sm font-medium text-foreground-muted transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
      >
        <ArrowLeft aria-hidden className="size-4" />
        {t('back')}
      </Link>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex w-full min-w-0 flex-col gap-1 sm:w-auto">
          <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            {listed('title')}
          </h1>
          <p className="max-w-2xl text-sm text-foreground-muted">
            {site === undefined ? (
              <span className="block h-5 w-64 max-w-full animate-pulse rounded-sm bg-surface-muted" />
            ) : (
              listed('caption', { site: site.displayName })
            )}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <ListSwitch
            label={t('list.label')}
            options={lists}
            value={chosen.list}
            onChange={chosen.showList}
          />
          <PopulationSwitch peopleOnly={chosen.peopleOnly} onChange={chosen.showOnlyPeople} />
        </div>
      </div>
    </header>
  );
}

/**
 * The one clause every figure below is read under, where the screen is kept to people. Said once,
 * under the screen's heading, wherever the list or the means of searching it are on the screen — so
 * it does not come and go above somebody typing a search — and not where a card says the sitemap
 * lists no posts, since there is then no figure for it to qualify.
 */
function PeopleCaption({ shown }: { readonly shown: boolean }) {
  return shown ? <KeptToPeople fallenBehind={false} /> : null;
}

interface ListPlaceProps {
  readonly site: Site;
  readonly chosen: ChosenListing;
  /** Opens the website's settings on the box its sitemap's address goes in. */
  readonly onCheckAddress: () => void;
  /** Opens the way of putting the tracking code on the website. */
  readonly onShowCode: () => void;
  /** The heading of whatever stands in the list's place, or of the list itself. */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * What stands under the screen's heading: the list chosen, or why there is none.
 *
 * The posts are read from how reading the sitemap stands, which is asked again by itself while a
 * reading is under way, so the posts of a sitemap read for the first time arrive without anybody
 * asking for them. A sitemap read again since can list no posts at all, whatever the reading last
 * heard of says, and is then said to list none in the words Insights uses for the same reading.
 * Every page is read from the record alone, and nothing is asked about the sitemap while it is shown.
 *
 * Whichever list is shown, the box it is searched from is one box in one place, so what is typed in
 * it, and the keyboard, stay with somebody who chooses the other list.
 */
function ListPlace({ site, chosen, onCheckAddress, onShowCode, headingRef }: ListPlaceProps) {
  const pages = chosen.list === 'pages';
  const reading = useSitemapStatus(site.id, !pages);
  const status = pages ? undefined : reading.data;
  const page = usePostsPage(site, chosen.population, chosen.listing, status);
  const searchRef = useRef<HTMLInputElement>(null);
  const mayChange = site.role !== 'viewer';

  const clearSearch = () => {
    chosen.searchFor('');
    searchRef.current?.focus();
  };

  if (pages) {
    return (
      <Listing chosen={chosen} searchRef={searchRef}>
        <PagesBody
          site={site}
          chosen={chosen}
          onClearSearch={clearSearch}
          onShowCode={onShowCode}
          headingRef={headingRef}
        />
      </Listing>
    );
  }

  // An answer already read is still the answer when asking again fails.
  if (status === undefined) {
    return reading.isError ? (
      <FailureNotice error={reading.error} />
    ) : (
      <>
        <PeopleCaption shown={chosen.peopleOnly} />
        <ListWaiting />
      </>
    );
  }

  const look = postsLook(page.data?.posts === 0 ? { ...status, posts: 0 } : status);

  if (look !== 'listed') {
    return (
      <NoPosts
        look={look}
        status={status}
        site={site}
        mayChange={mayChange}
        onCheckAddress={onCheckAddress}
        headingRef={headingRef}
      />
    );
  }

  return (
    <Listing chosen={chosen} searchRef={searchRef}>
      <PostsBody
        status={status}
        answer={page.data}
        error={page.data === undefined ? page.error : null}
        standingIn={page.isPlaceholderData}
        chosen={chosen}
        mayChange={mayChange}
        onCheckAddress={onCheckAddress}
        onClearSearch={clearSearch}
        headingRef={headingRef}
      />
    </Listing>
  );
}

interface ListingProps {
  readonly chosen: ChosenListing;
  /** The box the list is searched from, for a state that brings somebody back to it. */
  readonly searchRef: RefObject<HTMLInputElement | null>;
  /** The page of the list asked for, or the one state in its place. */
  readonly children: ReactNode;
}

/**
 * The list chosen with the means of searching it, and of ordering the posts, and under them the page
 * asked for or the one state in its place.
 */
function Listing({ chosen, searchRef, children }: ListingProps) {
  return (
    <>
      <PeopleCaption shown={chosen.peopleOnly} />
      <div className="flex flex-col gap-5">
        <ListingTools chosen={chosen} searchRef={searchRef} />
        {children}
      </div>
    </>
  );
}

interface ListingToolsProps {
  readonly chosen: ChosenListing;
  readonly searchRef: RefObject<HTMLInputElement | null>;
}

/**
 * The box the list is searched from, in the words of the list it searches, and the choice of order
 * the posts are read in, which pages, only ever read the most viewed first, do not have. A phone
 * sets one above the other.
 */
function ListingTools({ chosen, searchRef }: ListingToolsProps) {
  const t = useTranslations('insights.allPosts.order');
  const search = useTranslations(
    chosen.list === 'pages' ? 'insights.allPages.search' : 'insights.allPosts.search',
  );
  const orders = POSTS_ORDERS.map((order) => ({ value: order, label: t(`by.${order}`) }));

  return (
    <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
      <PostSearch
        search={chosen.listing.search}
        onSearch={chosen.searchFor}
        inputRef={searchRef}
        label={search('label')}
        placeholder={search('placeholder')}
        clear={search('clear')}
      />
      {chosen.list === 'posts' ? (
        <ListSwitch
          label={t('label')}
          options={orders}
          value={chosen.listing.order}
          onChange={chosen.orderBy}
        />
      ) : null}
    </div>
  );
}

/** A page of the posts as it is on the screen, and what is done with it. */
interface PostsBodyProps {
  readonly status: SitemapStatus;
  /**
   * The page asked for; the page before it, standing in while it is on its way; or nothing before
   * the first page has arrived, or where it could not be had.
   */
  readonly answer: PostsPage | undefined;
  /** What went wrong asking for it, where nothing was had. */
  readonly error: Error | null;
  /** Whether the page on the screen is the one before, standing in for the one asked for. */
  readonly standingIn: boolean;
  readonly chosen: ChosenListing;
  readonly mayChange: boolean;
  readonly onCheckAddress: () => void;
  /** Searches for nothing, and brings the keyboard back to the box. */
  readonly onClearSearch: () => void;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * The page of the posts asked for, or the one state in its place.
 *
 * How many posts the list holds, or how many hold what was searched for, and which of them the page
 * shows, is said to anybody listening once as each answer lands. While a page from before stands in
 * for the one on its way, everything said about the list is said of that page, the one on the
 * screen: nothing is said, on the screen or aloud, about a list that is not there.
 */
function PostsBody(props: PostsBodyProps) {
  const { answer, standingIn, chosen } = props;
  const t = useTranslations('insights.allPosts');
  const onScreen = useHeld(chosen.listing, standingIn);
  const counted = answer === undefined ? '' : countOf(answer, onScreen, t);

  return (
    <>
      <output className="sr-only">
        {answer === undefined ? null : spokenOf(counted, answer, onScreen, t)}
      </output>
      <ListingBody {...props} onScreen={onScreen} counted={counted} />
    </>
  );
}

/**
 * How many posts the list holds, or how many of them hold what was searched for.
 *
 * @param answer The page the figures came with.
 * @param listing What it is a page of.
 * @param t The words of the screen.
 */
function countOf(answer: PostsPage, { search }: PostsListing, t: AllPostsWords): string {
  return search === ''
    ? t('count', { posts: answer.posts })
    : t('found', { matching: answer.matching });
}

interface ListingBodyProps extends PostsBodyProps {
  /** What the page on the screen is a page of: the one asked for, or the one standing in for it. */
  readonly onScreen: PostsListing;
  /** How many posts the list holds, or how many match, as the screen says it. */
  readonly counted: string;
}

/** The page asked for, or the one state in its place. */
function ListingBody({ answer, error, standingIn, onClearSearch, ...rest }: ListingBodyProps) {
  const t = useTranslations('insights.allPosts.noMatch');

  if (answer === undefined) {
    return error === null ? <ListingWaiting /> : <FailureNotice error={error} />;
  }

  if (answer.matching === 0) {
    return (
      <InPlaceOfRows
        icon={SearchX}
        title={t('title')}
        body={t('body')}
        action={t('action')}
        onAction={onClearSearch}
        busy={standingIn}
      />
    );
  }

  return <PageOfPosts answer={answer} standingIn={standingIn} {...rest} />;
}

interface PageOfPostsProps extends Omit<ListingBodyProps, 'answer' | 'error' | 'onClearSearch'> {
  readonly answer: PostsPage;
}

/**
 * One page of the posts, under a heading carrying how many posts there are and whatever qualifies
 * all of them: since when their views are counted, that some may be missing where the last reading
 * of the sitemap went wrong, and how many of its pages are still to read.
 *
 * Every bar is drawn against the most any post on the list was read, whichever page is on the
 * screen, so a post reads the same wherever in the list it is met.
 */
function PageOfPosts({
  status,
  answer,
  standingIn,
  chosen,
  mayChange,
  onCheckAddress,
  headingRef,
  onScreen,
  counted,
}: PageOfPostsProps) {
  const posts = useTranslations('insights.posts');
  const sitemap = useTranslations('siteSettings.sitemap');
  const format = useFormatter();
  const missing = mayBeMissing(status.state, posts);
  const notes = sentences(
    viewsSince(answer.viewsFrom, posts, format),
    missing,
    status.reading > 0 ? sitemap('left', { reading: status.reading }) : null,
  );

  return (
    <ListedSection
      words="insights.allPosts"
      counted={counted}
      notes={notes}
      action={
        missing !== null && mayChange ? (
          <Button tone="secondary" size="sm" className={PRESSABLE} onClick={onCheckAddress}>
            {posts('check')}
          </Button>
        ) : undefined
      }
      headingRef={headingRef}
      answer={answer}
      onScreen={onScreen}
      standingIn={standingIn}
      goTo={chosen.goTo}
      resize={chosen.resize}
    >
      {answer.shown.map((post) => (
        <PostRow key={post.path} post={post} most={answer.mostPageViews} list={chosen.written} />
      ))}
    </ListedSection>
  );
}

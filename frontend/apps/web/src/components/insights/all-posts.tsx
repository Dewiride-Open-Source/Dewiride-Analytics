'use client';

import { ArrowLeft, ListX, type LucideIcon, SearchX } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type RefObject, useId, useRef, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
import { ListEmpty, ListSwitch, ListWaiting } from '@/components/dashboard/ranked-list';
import { SiteSettings } from '@/components/dashboard/site-settings';
import { KeptToPeople } from '@/components/insights/kept-to-people';
import { POSTS_CARD, PostRow, PostRowsWaiting } from '@/components/insights/post-list';
import { PostSearch } from '@/components/insights/post-search';
import { mayBeMissing, NoPosts, viewsSince } from '@/components/insights/posts';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Pagination } from '@/components/ui/pagination';
import { Link } from '@/i18n/navigation';
import { withChoices } from '@/lib/analytics/period';
import {
  POSTS_ORDERS,
  POSTS_PAGE_SIZES,
  type PostsListing,
  postsLook,
} from '@/lib/analytics/posts';
import { type ChosenListing, usePostsListing } from '@/lib/analytics/use-posts-listing';
import type { PostsOrder, PostsPage, Site, SitemapStatus } from '@/lib/api/schemas';
import { sentences } from '@/lib/charts/use-left-out';
import { pageOf } from '@/lib/paging';
import { useSitemapStatus } from '@/lib/queries/site-settings';
import { usePostsPage } from '@/lib/queries/sites';
import { INSIGHTS } from '@/lib/routes';
import { cn } from '@/lib/styling';

type AllPostsWords = ReturnType<typeof useTranslations<'insights.allPosts'>>;

/** How many rows stand in for the first page of posts while it is on its way. */
const ROWS_WAITING = 8;

/** How a list standing in for the one on its way is drawn: still there, and plainly not current. */
const STANDING_IN = 'opacity-60';

/** A small button of the screen's own, still large enough to press with a thumb on a phone. */
const PRESSABLE = 'h-10 sm:h-9';

/**
 * Every post a website's sitemap lists, with how often each has been read, a page at a time.
 *
 * A screen of its own under Insights, because it is worked through rather than looked back over:
 * Insights says how the posts are doing, and this finds the one somebody is looking for, by what it
 * is called or where it is, among the most read or the newest. Each post opens onto its own page,
 * and the way back is the same page of the same list, since everything that makes the list the one
 * being read is in the address.
 *
 * One screen from before its website is known until its posts are in. It is handed over both as what
 * waits and as the screen itself, and both stand in the same place, so its heading is drawn once and
 * only filled in as the website arrives.
 */
export function AllPosts() {
  return (
    <SiteScreen waiting={<SiteAllPosts />}>{(site) => <SiteAllPosts site={site} />}</SiteScreen>
  );
}

/**
 * One website's posts, for whoever the screen is kept to; and the same screen before its website is
 * known, with a stand-in for the list.
 *
 * The website's settings open from the posts, straight onto where its sitemap is named, since that
 * is the one thing on this screen they are opened for. Saving an address there can take away what
 * opened them, so focus comes back to the heading of whatever stands in the list's place by then.
 */
function SiteAllPosts({ site }: { readonly site?: Site }) {
  // Remembered people are written into the address once there is a website for them to be the people
  // of: until then the screen may still turn out to be one the choice means nothing on.
  const chosen = usePostsListing({ seeding: site !== undefined, siteId: site?.id });
  const [showingSettings, setShowingSettings] = useState(false);
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
          <PostsPlace
            site={site}
            chosen={chosen}
            onCheckAddress={() => setShowingSettings(true)}
            headingRef={headingRef}
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
 * The way back to Insights, the screen's name and what it lists, and the one choice it takes besides
 * the list's own. The words take the whole of a phone's width, so the choice sits beneath them.
 */
function ScreenHeading({ site, chosen }: ScreenHeadingProps) {
  const t = useTranslations('insights.allPosts');

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
            {t('title')}
          </h1>
          <p className="max-w-2xl text-sm text-foreground-muted">
            {site === undefined ? (
              <span className="block h-5 w-64 max-w-full animate-pulse rounded-sm bg-surface-muted" />
            ) : (
              t('caption', { site: site.displayName })
            )}
          </p>
        </div>
        <PopulationSwitch peopleOnly={chosen.peopleOnly} onChange={chosen.showOnlyPeople} />
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

interface PostsPlaceProps {
  readonly site: Site;
  readonly chosen: ChosenListing;
  /** Opens the website's settings on the box its sitemap's address goes in. */
  readonly onCheckAddress: () => void;
  /** The heading of whatever stands in the list's place, or of the list itself. */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * What stands under the screen's heading: the list, or why there is none.
 *
 * Read from how reading the sitemap stands, which is asked again by itself while a reading is under
 * way, so the posts of a sitemap read for the first time arrive without anybody asking for them. A
 * sitemap read again since can list no posts at all, whatever the reading last heard of says, and is
 * then said to list none in the words Insights uses for the same reading.
 */
function PostsPlace({ site, chosen, onCheckAddress, headingRef }: PostsPlaceProps) {
  const reading = useSitemapStatus(site.id);
  const status = reading.data;
  const page = usePostsPage(site, chosen.population, chosen.listing, status);
  const mayChange = site.role !== 'viewer';

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
    <ListedPlace
      status={status}
      answer={page.data}
      error={page.data === undefined ? page.error : null}
      standingIn={page.isPlaceholderData}
      chosen={chosen}
      mayChange={mayChange}
      onCheckAddress={onCheckAddress}
      headingRef={headingRef}
    />
  );
}

/** A page of the list as it is on the screen, and what is done with it. */
interface ListedProps {
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
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * The list with the means of searching and ordering it, and, under them, the page asked for or the
 * one state in its place.
 *
 * How many posts the list holds, or how many hold what was searched for, and which of them the page
 * shows, is said to anybody listening once as each answer lands. While a page from before stands in
 * for the one on its way, everything said about the list is said of that page, the one on the
 * screen: nothing is said, on the screen or aloud, about a list that is not there.
 */
function ListedPlace(props: ListedProps) {
  const { answer, standingIn, chosen } = props;
  const t = useTranslations('insights.allPosts');
  const searchRef = useRef<HTMLInputElement>(null);
  const onScreen = useHeld(chosen.listing, standingIn);

  const clearSearch = () => {
    chosen.searchFor('');
    searchRef.current?.focus();
  };

  return (
    <>
      <PeopleCaption shown={chosen.peopleOnly} />
      <div className="flex flex-col gap-5">
        <ListingTools
          listing={chosen.listing}
          onSearch={chosen.searchFor}
          onOrder={chosen.orderBy}
          searchRef={searchRef}
        />
        <output className="sr-only">
          {answer === undefined ? null : spokenOf(answer, onScreen, t)}
        </output>
        <ListingBody
          {...props}
          onScreen={onScreen}
          counted={answer === undefined ? '' : countOf(answer, onScreen, t)}
          onClearSearch={clearSearch}
        />
      </div>
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

/**
 * What is said to anybody listening about a page of the list: how many posts the list holds, or how
 * many hold what was searched for, and, where they take more than one page, which of them it shows.
 *
 * @param answer The page on the screen.
 * @param listing What it is a page of.
 * @param t The words of the screen.
 */
function spokenOf(answer: PostsPage, listing: PostsListing, t: AllPostsWords): string {
  const counted = countOf(answer, listing, t);

  if (answer.shown.length === 0 || answer.matching <= listing.limit) {
    return counted;
  }

  return t('spoken', {
    counted,
    first: listing.offset + 1,
    last: listing.offset + answer.shown.length,
  });
}

/**
 * What is on the screen while nothing stands in for the answer it is of, held while something does.
 *
 * @param now What is asked for now.
 * @param standingIn Whether what is on the screen stands in for an answer still on its way.
 */
function useHeld<T>(now: T, standingIn: boolean): T {
  const [held, setHeld] = useState(now);

  if (!standingIn && !Object.is(held, now)) {
    setHeld(now);
  }

  return standingIn ? held : now;
}

interface ListingToolsProps {
  readonly listing: PostsListing;
  readonly onSearch: (search: string) => void;
  readonly onOrder: (order: PostsOrder) => void;
  readonly searchRef: RefObject<HTMLInputElement | null>;
}

/** The box the posts are searched from, and the choice of order. A phone sets one above the other. */
function ListingTools({ listing, onSearch, onOrder, searchRef }: ListingToolsProps) {
  const t = useTranslations('insights.allPosts.order');
  const orders = POSTS_ORDERS.map((order) => ({ value: order, label: t(`by.${order}`) }));

  return (
    <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between">
      <PostSearch search={listing.search} onSearch={onSearch} inputRef={searchRef} />
      <ListSwitch label={t('label')} options={orders} value={listing.order} onChange={onOrder} />
    </div>
  );
}

interface ListingBodyProps extends ListedProps {
  /** What the page on the screen is a page of: the one asked for, or the one standing in for it. */
  readonly onScreen: PostsListing;
  /** How many posts the list holds, or how many match, as the screen says it. */
  readonly counted: string;
  /** Searches for nothing, and brings the keyboard back to the box. */
  readonly onClearSearch: () => void;
}

/** The page asked for, or the one state in its place. */
function ListingBody({ answer, error, standingIn, onClearSearch, ...rest }: ListingBodyProps) {
  const t = useTranslations('insights.allPosts.noMatch');

  if (answer === undefined) {
    return error === null ? <ListingWaiting /> : <FailureNotice error={error} />;
  }

  if (answer.matching === 0) {
    return (
      <InPlaceOfPosts
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

/** What stands for the first page while it is on its way: its count, and its rows. */
function ListingWaiting() {
  return (
    <div className="flex flex-col gap-3">
      <span aria-hidden className="block h-6 w-32 animate-pulse rounded-sm bg-surface-muted" />
      <Card className={POSTS_CARD}>
        <PostRowsWaiting rows={ROWS_WAITING} />
      </Card>
    </div>
  );
}

interface InPlaceOfPostsProps {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly body: string;
  /** The one thing that brings posts back onto the screen. */
  readonly action: string;
  readonly onAction: () => void;
  /** Where its heading stands among the screen's: beneath the screen's own name unless said. */
  readonly level?: 2 | 3;
  /** Whether it stands in for an answer still on its way. */
  readonly busy: boolean;
}

/**
 * What stands in place of the posts where a list holds some and none of them is on the screen: none
 * holds what was searched for, or the page is further along than the list reaches.
 */
function InPlaceOfPosts({ icon, title, body, action, onAction, level, busy }: InPlaceOfPostsProps) {
  return (
    <div aria-busy={busy} className={cn('transition-opacity', busy && STANDING_IN)}>
      <ListEmpty
        icon={icon}
        title={title}
        body={body}
        level={level}
        action={
          <Button tone="secondary" size="sm" className={PRESSABLE} onClick={onAction}>
            {action}
          </Button>
        }
      />
    </div>
  );
}

interface PageOfPostsProps extends Omit<ListingBodyProps, 'answer' | 'error' | 'onClearSearch'> {
  readonly answer: PostsPage;
}

/**
 * One page of the list, under a heading carrying how many posts there are and whatever qualifies all
 * of them: since when their views are counted, that some may be missing where the last reading of
 * the sitemap went wrong, and how many of its pages are still to read.
 *
 * Every bar is drawn against the most any post on the list was read, whichever page is on the
 * screen, so a post reads the same wherever in the list it is met. Moving to another page, or
 * choosing how many a page holds, brings the reader, and the keyboard, back to the top of the list.
 * The line under the rows says which of them are there and is not read out, since the screen says
 * so itself, once, as each answer lands.
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
  const t = useTranslations('insights.allPosts');
  const posts = useTranslations('insights.posts');
  const sitemap = useTranslations('siteSettings.sitemap');
  const format = useFormatter();
  const heading = useId();
  const { goTo, resize, written } = chosen;
  const missing = mayBeMissing(status.state, posts);
  const notes = sentences(
    viewsSince(answer.viewsFrom, posts, format),
    missing,
    status.reading > 0 ? sitemap('left', { reading: status.reading }) : null,
  );

  const moveTo = (page: number) => {
    goTo(page);
    bringToTop(headingRef);
  };

  const resizeTo = (perPage: number) => {
    resize(perPage);
    bringToTop(headingRef);
  };

  return (
    <section aria-labelledby={heading} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div className="flex w-full min-w-0 flex-col gap-0.5 sm:w-auto">
          <h2
            ref={headingRef}
            id={heading}
            tabIndex={-1}
            className="text-base font-semibold text-foreground"
          >
            {counted}
          </h2>
          {notes === '' ? null : <p className="text-sm text-foreground-muted">{notes}</p>}
        </div>
        {missing !== null && mayChange ? (
          <Button tone="secondary" size="sm" className={PRESSABLE} onClick={onCheckAddress}>
            {posts('check')}
          </Button>
        ) : null}
      </div>

      {answer.shown.length === 0 ? (
        <InPlaceOfPosts
          icon={ListX}
          title={t('pastTheEnd.title')}
          body={t('pastTheEnd.body')}
          action={t('pastTheEnd.action')}
          onAction={() => moveTo(1)}
          level={3}
          busy={standingIn}
        />
      ) : (
        <Card className={POSTS_CARD}>
          <ol
            aria-busy={standingIn}
            className={cn('flex flex-col gap-0.5 transition-opacity', standingIn && STANDING_IN)}
          >
            {answer.shown.map((post) => (
              <PostRow key={post.path} post={post} most={answer.mostPageViews} list={written} />
            ))}
          </ol>
          <Pagination
            label={t('nav.label')}
            total={answer.matching}
            perPage={onScreen.limit}
            offset={onScreen.offset}
            shown={answer.shown.length}
            sizes={POSTS_PAGE_SIZES}
            busy={standingIn}
            announced={false}
            onMove={(offset) => moveTo(pageOf(offset, onScreen.limit))}
            onResize={resizeTo}
          />
        </Card>
      )}
    </section>
  );
}

/**
 * Brings the reader to the top of the list, and the keyboard with them: to its heading, which says
 * how long the list is, and clear of the bar across the top of the screen.
 *
 * @param heading The list's heading.
 */
function bringToTop(heading: RefObject<HTMLHeadingElement | null>): void {
  heading.current?.focus({ preventScroll: true });
  heading.current?.scrollIntoView({ block: 'start' });
}

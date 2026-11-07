'use client';

import {
  CalendarX,
  CirclePause,
  Hourglass,
  type LucideIcon,
  Newspaper,
  SearchX,
  TriangleAlert,
} from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type RefObject, useId } from 'react';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { ListEmpty, ListWaiting } from '@/components/dashboard/ranked-list';
import { BestPost } from '@/components/insights/best-post';
import { LatestPosts } from '@/components/insights/post-list';
import { PostingCalendar } from '@/components/insights/posting-calendar';
import { Button } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { writableDay } from '@/lib/analytics/all-time';
import type { Population } from '@/lib/analytics/people-only';
import { type PostsLook, postsLook, postsNote, viewsCountedSince } from '@/lib/analytics/posts';
import type { InsightsPosts, Site, SitemapStatus } from '@/lib/api/schemas';
import { sentences } from '@/lib/charts/use-left-out';
import { useSitemapStatus } from '@/lib/queries/site-settings';
import { useInsightsPosts } from '@/lib/queries/sites';

type PostsWords = ReturnType<typeof useTranslations<'insights.posts'>>;
type SitemapWords = ReturnType<typeof useTranslations<'siteSettings.sitemap'>>;

/**
 * How many posts a website's sitemap lists, as one of the record's headline figures.
 *
 * The same number whoever the screen is kept to, since a post is a page on the website and not a
 * visit to it. Where there is no number to show, the card says why in a few words, and the posts
 * further down the screen say the rest.
 */
export function PostsCard({ site }: { readonly site: Site | undefined }) {
  const t = useTranslations('insights.posts.card');

  return site === undefined ? (
    <MetricCardSkeleton label={t('label')} />
  ) : (
    <SitePostsCard siteId={site.id} />
  );
}

function SitePostsCard({ siteId }: { readonly siteId: string }) {
  const t = useTranslations('insights.posts.card');
  const format = useFormatter();
  const { data: status, isError } = useSitemapStatus(siteId);

  if (status === undefined) {
    return isError ? (
      <MetricCard label={t('label')} value={null} />
    ) : (
      <MetricCardSkeleton label={t('label')} />
    );
  }

  return (
    <MetricCard
      label={t('label')}
      value={status.posts > 0 ? format.number(status.posts) : null}
      note={t(postsNote(status))}
    />
  );
}

interface PostsProps {
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  /** Whose views are counted. */
  readonly population: Population;
  /** The first month of the website's record, written `2026-08`, or nothing while it is not known. */
  readonly firstMonth: string | undefined;
  /** Opens the website's settings on the box its sitemap's address goes in. */
  readonly onCheckAddress: () => void;
  /** Where focus is brought back to when the settings close having taken away what opened them. */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * A website's posts: the newest with how much each was read, the one read most of all, and when it
 * posts; or, where its sitemap lists none, why not and what would change that.
 *
 * Read from how reading its sitemap stands, which is asked again by itself while a reading is under
 * way, so a first sitemap's posts arrive on the screen without anybody asking for them.
 */
export function Posts({ site, ...rest }: PostsProps) {
  return site === undefined ? <ListWaiting /> : <SitePosts site={site} {...rest} />;
}

interface SitePostsProps extends Omit<PostsProps, 'site'> {
  readonly site: Site;
}

function SitePosts({ site, population, firstMonth, onCheckAddress, headingRef }: SitePostsProps) {
  const reading = useSitemapStatus(site.id);
  const status = reading.data;
  const posts = useInsightsPosts(site, population, status);
  const mayChange = site.role !== 'viewer';

  // An answer already read is still the answer when asking again fails.
  if (status === undefined) {
    return reading.isError ? <FailureNotice error={reading.error} /> : <ListWaiting />;
  }

  const look = postsLook(status);

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
    <ListedPosts
      status={status}
      answer={posts.data}
      error={posts.data === undefined ? posts.error : null}
      firstMonth={firstMonth}
      mayChange={mayChange}
      onCheckAddress={onCheckAddress}
      headingRef={headingRef}
    />
  );
}

/** Why a website has no posts to show, and what would change that. */
interface Absence {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly body: string;
  /** The one thing somebody who manages the website can do about it, where there is one. */
  readonly asks: 'add' | 'check' | null;
}

/**
 * What stands in place of the posts, for each reason there are none.
 *
 * Said in the words the website's settings use for the same reading, so the two never disagree about
 * it. Somebody who may only read the numbers is told who can add a sitemap rather than invited to.
 */
function absence(
  look: Exclude<PostsLook, 'listed'>,
  { problem, reading, turnedAway }: SitemapStatus,
  viewerOf: string | null,
  t: PostsWords,
  sitemap: SitemapWords,
): Absence {
  const unexplained = t('unexplained');

  switch (look) {
    case 'none':
      return {
        icon: Newspaper,
        title: t('none.title'),
        body: viewerOf === null ? t('none.body') : t('none.viewer', { site: viewerOf }),
        asks: 'add',
      };
    case 'off':
      return {
        icon: CirclePause,
        title: sitemap('state.off'),
        body: sitemap('switchOn'),
        asks: null,
      };
    case 'waiting':
      return { icon: Hourglass, title: sitemap('state.waiting'), body: t('waiting'), asks: null };
    case 'pages':
      return {
        icon: Hourglass,
        title: sitemap('underway', { posts: 0 }),
        body: sitemap('left', { reading }),
        asks: null,
      };
    case 'failed':
      return {
        icon: TriangleAlert,
        title: sitemap('state.failed'),
        body: problem === null ? unexplained : sitemap(`problem.${problem}`),
        asks: 'check',
      };
    case 'partly':
      return {
        icon: TriangleAlert,
        title: sitemap('state.partly-read', { posts: 0 }),
        body: sentences(
          problem === null ? unexplained : sitemap(`partlyProblem.${problem}`),
          reading > 0 ? sitemap('left', { reading }) : null,
        ),
        asks: 'check',
      };
    default:
      return {
        icon: SearchX,
        title: sitemap('state.read', { posts: 0 }),
        body: turnedAway > 0 ? sitemap('turnedAway', { pages: turnedAway }) : sitemap('noPosts'),
        asks: 'check',
      };
  }
}

interface NoPostsProps {
  readonly look: Exclude<PostsLook, 'listed'>;
  readonly status: SitemapStatus;
  readonly site: Site;
  readonly mayChange: boolean;
  readonly onCheckAddress: () => void;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/** The one card standing where the posts would be. */
function NoPosts({ look, status, site, mayChange, onCheckAddress, headingRef }: NoPostsProps) {
  const t = useTranslations('insights.posts');
  const sitemap = useTranslations('siteSettings.sitemap');
  const { icon, title, body, asks } = absence(
    look,
    status,
    mayChange ? null : site.domain,
    t,
    sitemap,
  );

  return (
    <ListEmpty
      icon={icon}
      title={title}
      body={body}
      headingRef={headingRef}
      action={
        mayChange && asks !== null ? (
          <Button tone={asks === 'add' ? 'primary' : 'secondary'} onClick={onCheckAddress}>
            {asks === 'add' ? t('none.action') : t('check')}
          </Button>
        ) : undefined
      }
    />
  );
}

interface ListedPostsProps {
  readonly status: SitemapStatus;
  /** What the record says about the posts, or nothing while it is on its way or could not be had. */
  readonly answer: InsightsPosts | undefined;
  /** What went wrong asking for it, where nothing was had. */
  readonly error: Error | null;
  readonly firstMonth: string | undefined;
  readonly mayChange: boolean;
  readonly onCheckAddress: () => void;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * What may be missing from the posts shown, where the last reading of the sitemap went wrong: part
 * of it could not be read, or none of it could, when the posts are those an earlier reading found.
 */
function mayBeMissing(state: SitemapStatus['state'], t: PostsWords): string | null {
  switch (state) {
    case 'partly-read':
      return t('partly');
    case 'failed':
      return t('failed');
    default:
      return null;
  }
}

/**
 * The posts themselves, under one heading carrying whatever qualifies all of them: since when their
 * views are counted, where that is later than the record begins, and that some may be missing where
 * the last reading of the sitemap went wrong or its pages are still being read.
 */
function ListedPosts({
  status,
  answer,
  error,
  firstMonth,
  mayChange,
  onCheckAddress,
  headingRef,
}: ListedPostsProps) {
  const t = useTranslations('insights.posts');
  const sitemap = useTranslations('siteSettings.sitemap');
  const format = useFormatter();
  const heading = useId();
  const since = answer === undefined ? null : viewsCountedSince(answer.viewsFrom, firstMonth);
  const missing = mayBeMissing(status.state, t);
  const notes = sentences(
    since === null
      ? null
      : t('viewsSince', {
          month: format.dateTime(writableDay(`${since}-01`), {
            month: 'long',
            year: 'numeric',
            timeZone: 'UTC',
          }),
        }),
    missing,
    status.reading > 0 ? sitemap('left', { reading: status.reading }) : null,
  );

  return (
    <section aria-labelledby={heading} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <h2
            ref={headingRef}
            id={heading}
            tabIndex={-1}
            className="text-base font-semibold text-foreground"
          >
            {t('title')}
          </h2>
          {notes === '' ? null : <p className="text-sm text-foreground-muted">{notes}</p>}
        </div>
        {missing !== null && mayChange ? (
          <Button tone="secondary" size="sm" onClick={onCheckAddress}>
            {t('check')}
          </Button>
        ) : null}
      </div>

      {error === null ? (
        <PostsSections answer={answer} pagesRead={status.reading === 0} />
      ) : (
        <FailureNotice error={error} />
      )}
    </section>
  );
}

interface PostsSectionsProps {
  /** What the record says about the posts, or nothing while it is on its way. */
  readonly answer: InsightsPosts | undefined;
  /** Whether every page the sitemap lists has been read, which is when each post's date is known. */
  readonly pagesRead: boolean;
}

/**
 * The newest posts, the one read most and when the website posts; or, where none of its posts says
 * when it was published, the one read most beside a card saying why the rest cannot be shown.
 *
 * A post says when it was published only once its page has been read, so nothing is said of posts
 * without a date while pages are still to read: the card in place of the rest then says they are
 * still being read, rather than that none of them says.
 */
function PostsSections({ answer, pagesRead }: PostsSectionsProps) {
  const t = useTranslations('insights.posts');
  const sitemap = useTranslations('siteSettings.sitemap');
  const undated = answer === undefined || !pagesRead ? 0 : answer.posts - answer.dated;
  const leftOut =
    undated > 0 ? (
      <p className="text-xs text-foreground-muted">{t('undated', { count: undated })}</p>
    ) : null;

  if (answer?.dated === 0) {
    return (
      <>
        {pagesRead ? (
          <ListEmpty
            icon={CalendarX}
            title={t('undatedAll.title')}
            body={t('undatedAll.body')}
            level={3}
          />
        ) : (
          <ListEmpty
            icon={Hourglass}
            title={sitemap('underway', { posts: 0 })}
            body={t('undatedYet')}
            level={3}
          />
        )}
        <BestPost post={answer.mostRead} />
      </>
    );
  }

  return (
    <>
      <LatestPosts posts={answer?.latest} leftOut={leftOut} />
      <BestPost post={answer?.mostRead} />
      <PostingCalendar
        calendar={
          answer === undefined
            ? undefined
            : { from: answer.calendarFrom, to: answer.calendarTo, days: answer.calendar }
        }
        leftOut={leftOut}
      />
    </>
  );
}

'use client';

import { ExternalLink } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { Card } from '@/components/ui/card';
import { writableDay } from '@/lib/analytics/all-time';
import { readablePath } from '@/lib/analytics/pages';
import { postLink } from '@/lib/analytics/posts';
import { shareOf } from '@/lib/analytics/share';
import type { InsightsPost } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

/** How many rows stand in for the newest posts while they are on their way. */
const ROWS_WAITING = 5;

/** The card's own spacing. */
export const POSTS_CARD = 'flex min-w-0 flex-col gap-4 p-5 sm:p-6';

/** A card's own heading, beneath the heading of the posts as a whole. */
export const POSTS_HEADING = 'text-base font-semibold text-foreground';

interface PostTitleProps {
  readonly post: InsightsPost;
  /**
   * Whether the title has the room of a card to itself, and is set large; otherwise it is a row of a
   * list. Either way it takes two lines at most, which on a phone is what a title needs to be told
   * from the next, and is cut short beyond them.
   */
  readonly featured?: boolean;
}

/**
 * A post's title, or its address where its page gave none, as a link to the post.
 *
 * Opened in a tab of its own, so the screen it was opened from is still there, and with nothing
 * passed on about where it was opened from. A post's address and title come from the website's own
 * pages, so both are only ever text: the address is a link only where it is a web page's, and the
 * title is written in its own direction, cut off from whatever is beside it.
 */
export function PostTitle({ post, featured = false }: PostTitleProps) {
  const t = useTranslations('insights.posts');
  const href = postLink(post.address);
  const words = post.title ?? readablePath(post.path);
  const title = (
    <bdi
      // An address reads left to right, whatever it is written in.
      dir={post.title === null ? 'ltr' : 'auto'}
      className={featured ? 'wrap-break-word' : 'line-clamp-2 min-w-0 wrap-break-word'}
    >
      {words}
    </bdi>
  );

  if (href === null) {
    return (
      <span title={words} className={cn('min-w-0', featured ? 'line-clamp-2' : 'flex')}>
        {title}
      </span>
    );
  }

  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      title={words}
      className={cn(
        'min-w-0 rounded-sm decoration-accent/60 underline-offset-2 hover:text-accent-strong hover:underline',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
        featured ? 'line-clamp-2' : 'flex items-start gap-1.5',
      )}
    >
      {title}
      <ExternalLink
        aria-hidden
        className={cn(
          'shrink-0 text-foreground-subtle',
          featured ? 'ml-1.5 inline size-4 align-[-0.125em]' : 'mt-[3px] size-3.5',
        )}
      />{' '}
      <span className="sr-only">{t('newTab')}</span>
    </a>
  );
}

/**
 * The day a post says it was published, and the address its views are counted under where its title
 * is what names it.
 */
export function PostFacts({ post }: { readonly post: InsightsPost }) {
  const format = useFormatter();
  const published =
    post.published === null
      ? null
      : format.dateTime(writableDay(post.published), { dateStyle: 'medium', timeZone: 'UTC' });
  const path = post.title === null ? null : readablePath(post.path);

  if (published === null && path === null) {
    return null;
  }

  return (
    <p className="flex min-w-0 items-center gap-1.5 text-xs font-normal text-foreground-muted">
      {published === null ? null : <span className="shrink-0">{published}</span>}
      {published === null || path === null ? null : <span aria-hidden>·</span>}
      {path === null ? null : (
        <span dir="ltr" className="min-w-0 truncate">
          {path}
        </span>
      )}
    </p>
  );
}

interface LatestPostsProps {
  /** The newest posts, newest first, or nothing while they are on their way. */
  readonly posts: readonly InsightsPost[] | undefined;
  /** What is said under them about the posts they leave out, where they leave any out. */
  readonly leftOut: ReactNode;
}

/**
 * The newest posts and how much each has been read.
 *
 * Each with a bar behind it, drawn against the most read of them, so the ones that took off stand out
 * before a number is read. The bar is the figure drawn and hidden from anybody reading rather than
 * looking, since the figure beside it says the same in words.
 */
export function LatestPosts({ posts, leftOut }: LatestPostsProps) {
  const t = useTranslations('insights.posts');
  const most = Math.max(0, ...(posts ?? []).map((post) => post.pageViews));

  return (
    <Card className={POSTS_CARD}>
      <h3 className={POSTS_HEADING}>{t('latest')}</h3>

      {posts === undefined ? (
        <div aria-hidden className="flex flex-col gap-1.5">
          {Array.from({ length: ROWS_WAITING }, (_, row) => (
            <div key={row} className="h-14 animate-pulse rounded-md bg-surface-muted" />
          ))}
        </div>
      ) : (
        <ol className="flex flex-col gap-0.5">
          {posts.map((post) => (
            <LatestPost key={post.path} post={post} most={most} />
          ))}
        </ol>
      )}

      {leftOut}
    </Card>
  );
}

interface LatestPostProps {
  readonly post: InsightsPost;
  /** The most any of the newest posts has been read, which every bar is drawn against. */
  readonly most: number;
}

/** One of the newest posts: its title, when it was published and where, and how much it was read. */
function LatestPost({ post, most }: LatestPostProps) {
  const t = useTranslations('insights.posts');

  return (
    <li className="relative isolate rounded-md">
      <span
        aria-hidden
        className="absolute inset-y-0 left-0 -z-10 rounded-md bg-accent-soft"
        style={{ width: barWidth(post.pageViews, most) }}
      />
      <div className="flex items-center gap-4 px-3 py-2.5">
        <div className="flex min-w-0 flex-1 flex-col gap-0.5 text-sm font-medium text-foreground">
          <PostTitle post={post} />
          <PostFacts post={post} />
        </div>
        <span className="shrink-0 whitespace-nowrap text-sm font-medium text-foreground tabular-nums">
          {t('views', { count: post.pageViews })}
        </span>
      </div>
    </li>
  );
}

/**
 * A row's bar: none for a post nobody has read, and otherwise never so narrow that it vanishes.
 *
 * @param views How much the post was read.
 * @param most The most any row was read.
 */
function barWidth(views: number, most: number): string {
  return views > 0 ? `${Math.max(shareOf(views, most) * 100, 2)}%` : '0%';
}

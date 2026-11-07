'use client';

import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { POSTS_CARD, POSTS_HEADING, PostFacts, PostTitle } from '@/components/insights/post-list';
import { Card } from '@/components/ui/card';
import type { InsightsPost } from '@/lib/api/schemas';

interface BestPostProps {
  /** The post read most, nothing where none has been read, or undefined while it is on its way. */
  readonly post: InsightsPost | null | undefined;
}

/**
 * The post read most of all, and how much.
 *
 * Its views alone, with no share of anything beside them: a website's posts are not all of what it
 * holds, and a share of its posts' views would read as a share of the website.
 */
export function BestPost({ post }: BestPostProps) {
  const t = useTranslations('insights.posts.best');

  return (
    <Card className={POSTS_CARD}>
      <h3 className={POSTS_HEADING}>{t('title')}</h3>
      <BestPostBody post={post} />
    </Card>
  );
}

/** What the card holds: the post, the sentence saying none has been read, or a stand-in for either. */
function BestPostBody({ post }: BestPostProps) {
  const t = useTranslations('insights.posts.best');

  if (post === undefined) {
    return <div aria-hidden className="h-16 animate-pulse rounded-md bg-surface-muted" />;
  }

  if (post === null) {
    return <p className="text-sm text-foreground-muted">{t('unread')}</p>;
  }

  return (
    <div className="flex flex-wrap items-end justify-between gap-x-8 gap-y-3">
      <div className="flex min-w-0 flex-1 basis-64 flex-col gap-1">
        <p className="text-lg font-semibold tracking-tight text-foreground sm:text-xl">
          <PostTitle post={post} featured />
        </p>
        <PostFacts post={post} />
      </div>
      <p className="shrink-0 text-sm text-foreground-muted">
        {t.rich('views', { count: post.pageViews, figure: emphasised })}
      </p>
    </div>
  );
}

/** The figure itself, set large beside the word naming it. */
function emphasised(figure: ReactNode) {
  return (
    <span className="text-3xl font-semibold tracking-tight text-foreground tabular-nums">
      {figure}
    </span>
  );
}

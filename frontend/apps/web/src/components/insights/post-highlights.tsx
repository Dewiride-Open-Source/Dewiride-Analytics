'use client';

import { useFormatter, useTranslations } from 'next-intl';
import { useId, useMemo } from 'react';
import { MetricCard, MetricCardSkeleton } from '@/components/dashboard/metric-card';
import { writeFigure, writtenMonth } from '@/lib/analytics/all-time';
import { leftOutOf, postHighlights, postViewsSince } from '@/lib/analytics/post';
import type { PostInsights } from '@/lib/api/schemas';
import { sentences, useLeftOut } from '@/lib/charts/use-left-out';

interface PostHighlightsProps {
  /** The post's record, or nothing while it is on its way. */
  readonly answer: PostInsights | undefined;
  /** The website's zone, which the months are named in, or nothing while the website is not known. */
  readonly timeZoneId: string | undefined;
  readonly peopleOnly: boolean;
}

/**
 * What a post's whole life comes to: how often it has been read, how often a day, and the month it
 * was read most in; or their stand-ins until they are in. Under them, whatever the figures need to be
 * read honestly: since when its views are counted, where some of its life went uncounted, and the
 * months they leave out.
 */
export function PostHighlights({ answer, timeZoneId, peopleOnly }: PostHighlightsProps) {
  const t = useTranslations('insights.post.highlights');
  const highlights = useTranslations('insights.highlights');
  const heading = useId();
  const views = peopleOnly ? t('viewsPeople') : t('views');

  return (
    <section aria-labelledby={heading} className="flex flex-col gap-3">
      <div className="flex flex-col gap-0.5">
        <h2 id={heading} className="text-base font-semibold text-foreground">
          {highlights('title')}
        </h2>
        {answer === undefined || timeZoneId === undefined ? null : (
          <HighlightNotes answer={answer} timeZoneId={timeZoneId} />
        )}
      </div>
      <div className="grid gap-4 lg:grid-cols-3">
        {answer === undefined ? (
          <>
            <MetricCardSkeleton label={views} />
            <MetricCardSkeleton label={t('perDay')} />
            <MetricCardSkeleton label={t('bestMonth')} />
          </>
        ) : (
          <HighlightCards answer={answer} views={views} />
        )}
      </div>
    </section>
  );
}

interface HighlightNotesProps {
  readonly answer: PostInsights;
  /** The website's zone, which the months are named in. */
  readonly timeZoneId: string;
}

/** The sentences the figures are read under, where they need any. */
function HighlightNotes({ answer, timeZoneId }: HighlightNotesProps) {
  const t = useTranslations('insights.posts');
  const format = useFormatter();
  const { missing, unjudged } = useMemo(() => leftOutOf(answer), [answer]);
  const leftOut = useLeftOut(missing, unjudged, timeZoneId);
  const since = postViewsSince(answer);
  const notes = sentences(
    since === null ? null : t('viewsSince', { month: writtenMonth(since, format) }),
    leftOut,
  );

  return notes === '' ? null : <p className="text-sm text-foreground-muted">{notes}</p>;
}

interface HighlightCardsProps {
  readonly answer: PostInsights;
  /** What the post's views are called, which says whose they are. */
  readonly views: string;
}

/** The three figures themselves. */
function HighlightCards({ answer, views }: HighlightCardsProps) {
  const t = useTranslations('insights.post.highlights');
  const format = useFormatter();
  const { views: read, perDay, bestMonth } = useMemo(() => postHighlights(answer), [answer]);
  // A month still running is read most so far, and its views say so.
  const howMuch = bestMonth?.running ? 'bestMonthSoFar' : 'bestMonthViews';

  return (
    <>
      <MetricCard label={views} value={format.number(read)} />
      <MetricCard
        label={t('perDay')}
        value={perDay === null ? null : writeFigure(perDay, 'average', format)}
      />
      <MetricCard
        label={t('bestMonth')}
        value={bestMonth === null ? null : writtenMonth(bestMonth.month, format)}
        note={bestMonth === null ? undefined : t(howMuch, { count: bestMonth.pageViews })}
      />
    </>
  );
}

'use client';

import { ArrowLeft, BookOpen, ExternalLink, ScanSearch, SearchX } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';
import { SiteScreen } from '@/components/chrome/site-screen';
import { PopulationSwitch } from '@/components/dashboard/population-switch';
import { ListEmpty } from '@/components/dashboard/ranked-list';
import { KeptToPeople } from '@/components/insights/kept-to-people';
import { MonthsTable, type MonthsWords } from '@/components/insights/months-table';
import { PostChart, PostChartWaiting } from '@/components/insights/post-chart';
import { PostHighlights } from '@/components/insights/post-highlights';
import { RecentWeeks } from '@/components/insights/recent-weeks';
import { Button, buttonStyle } from '@/components/ui/button';
import { FailureNotice } from '@/components/ui/failure-notice';
import { Link } from '@/i18n/navigation';
import {
  JUDGING_BEHIND_AFTER,
  type Measure,
  writableDay,
  writtenMonth,
} from '@/lib/analytics/all-time';
import { readablePath } from '@/lib/analytics/pages';
import type { Population } from '@/lib/analytics/people-only';
import { withChoices } from '@/lib/analytics/period';
import {
  beforeOfPost,
  monthsRecordOf,
  type PostShown,
  postBeginsOn,
  postCalled,
  postShown,
  postViewsSince,
  readsNothing,
} from '@/lib/analytics/post';
import { postLink } from '@/lib/analytics/posts';
import { usePeopleOnly } from '@/lib/analytics/use-people-only';
import { useOpenedFromList, usePostPath } from '@/lib/analytics/use-post-path';
import { listAddress } from '@/lib/analytics/use-posts-listing';
import type { InsightsPost, PostInsights as PostAnswer, Site } from '@/lib/api/schemas';
import { usePostInsights } from '@/lib/queries/sites';
import { INSIGHTS } from '@/lib/routes';

/** The screen's name, and the post's where it is known, as the screen's heading sets it. */
const HEADING = 'text-2xl font-semibold tracking-tight text-foreground sm:text-3xl';

/**
 * What one post's whole life says: how often it has been read, how it was read over whichever period
 * somebody chooses, every month and year it was read in, and its recent weeks a day at a time.
 *
 * Opened from the post's title on Insights or in the list of every post, and leading back to
 * whichever it was opened from. Drawn once from before its website is known until its figures are
 * in, so that whatever somebody is on when they arrive is still under them. How the months are read
 * is held above the screen, so that it is one choice whatever the screen passes through.
 */
export function PostInsights() {
  const [measure, setMeasure] = useState<Measure>('total');
  const reading: MonthsReading = { measure, onMeasure: setMeasure };

  return (
    <SiteScreen waiting={<SitePost reading={reading} />}>
      {(site) => <SitePost site={site} reading={reading} />}
    </SiteScreen>
  );
}

/** How the reader has chosen to read the post's months. */
interface MonthsReading {
  readonly measure: Measure;
  readonly onMeasure: (measure: Measure) => void;
}

interface SitePostProps {
  /** The website, or nothing while which one it is has still to be read. */
  readonly site?: Site;
  readonly reading: MonthsReading;
}

/**
 * One post's record, for whoever the screen is kept to; and the same screen before its website is
 * known, with stand-ins for whatever only the record can say.
 *
 * Everybody's record is asked for as well only where the screen is kept to people and their record
 * reads nothing, since it is then what tells a post nobody has read from one none of whose readers
 * was judged a person. Under everybody it is the screen's own question, so it costs nothing.
 */
function SitePost({ site, reading }: SitePostProps) {
  // Remembered people are written into the address once there is a website for them to be the people
  // of: until then the screen may still turn out to be one the choice means nothing on.
  const { peopleOnly, population, showOnlyPeople } = usePeopleOnly({ seeding: site !== undefined });
  const path = usePostPath();
  const own = usePostInsights(site, path, population);
  const everybody = usePostInsights(
    site,
    path,
    'everybody',
    population === 'everybody' || readsNothing(own.data),
  );
  const shown = postShown(path, everybody, own);
  const fallenBehind =
    shown.state === 'ready' &&
    Date.parse(shown.answer.to) - Date.parse(shown.answer.completeTo) > JUDGING_BEHIND_AFTER;

  return (
    <div className="flex flex-col gap-6">
      <PostHeading
        shown={shown}
        population={population}
        peopleOnly={peopleOnly}
        onShowOnlyPeople={showOnlyPeople}
      />

      {/* Only above figures, or stand-ins for them: with no number on the screen it qualifies nothing. */}
      {peopleOnly && (shown.state === 'ready' || shown.state === 'waiting') ? (
        <KeptToPeople fallenBehind={fallenBehind} />
      ) : null}

      <PostPlace
        shown={shown}
        site={site}
        named={path !== null}
        population={population}
        peopleOnly={peopleOnly}
        reading={reading}
        onShowEveryone={() => showOnlyPeople(false)}
      />
    </div>
  );
}

/** The post a screen shows, where it shows one. */
function postOf(shown: PostShown): InsightsPost | undefined {
  return 'answer' in shown ? shown.answer.post : undefined;
}

interface PostHeadingProps {
  readonly shown: PostShown;
  /** Whose views the screen counts, which the way back carries. */
  readonly population: Population;
  readonly peopleOnly: boolean;
  readonly onShowOnlyPeople: (peopleOnly: boolean) => void;
}

/** Where the screen leads back to, and what that is called. */
interface WayBack {
  readonly href: string;
  readonly label: string;
}

/**
 * The way back from the screen: to the page of the list of every post it was opened from, where it
 * was opened from there, and otherwise to Insights; either way counting whoever the screen counts.
 *
 * @param population Whose views the screen counts.
 */
function useWayBack(population: Population): WayBack {
  const t = useTranslations('insights.post');
  const list = useOpenedFromList();

  return list === null
    ? { href: withChoices(INSIGHTS, { population }), label: t('back') }
    : { href: listAddress(list, population), label: t('backToList') };
}

/**
 * The way back to where the post was opened from, the post's name and what is known about it, and
 * the one choice the screen takes. The words take the whole of a phone's width, so the choice sits
 * beneath them however long the post's name turns out to be.
 */
function PostHeading({ shown, population, peopleOnly, onShowOnlyPeople }: PostHeadingProps) {
  const back = useWayBack(population);
  const post = postOf(shown);

  return (
    <header className="flex flex-col gap-2">
      <Link
        href={back.href}
        className="-ml-1 inline-flex h-10 w-fit items-center gap-1.5 rounded-md px-1 text-sm font-medium text-foreground-muted transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
      >
        <ArrowLeft aria-hidden className="size-4" />
        {back.label}
      </Link>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex w-full min-w-0 flex-col gap-1.5 sm:w-auto sm:flex-1">
          <PostName shown={shown} />
          {post === undefined ? (
            <PostLineWaiting waiting={shown.state === 'waiting'} />
          ) : (
            <PostLine post={post} />
          )}
        </div>
        <PopulationSwitch peopleOnly={peopleOnly} onChange={onShowOnlyPeople} />
      </div>
    </header>
  );
}

/**
 * The screen's name: the post's own where there is a post to show, written in its own direction and
 * cut off from whatever is beside it; a stand-in while it is on its way; and otherwise what the
 * screen is.
 */
function PostName({ shown }: { readonly shown: PostShown }) {
  const t = useTranslations('insights.post');
  const post = postOf(shown);

  if (shown.state === 'waiting') {
    return (
      <h1 className={HEADING}>
        <span className="sr-only">{t('heading')}</span>
        <span
          aria-hidden
          className="block h-8 w-80 max-w-full animate-pulse rounded-sm bg-surface-muted sm:h-9"
        />
      </h1>
    );
  }

  if (post === undefined) {
    return <h1 className={HEADING}>{t('heading')}</h1>;
  }

  const words = postCalled(post);

  return (
    <h1 className={HEADING} title={words}>
      <bdi
        // An address reads left to right, whatever it is written in.
        dir={post.title === null ? 'ltr' : 'auto'}
        className="line-clamp-2 wrap-break-word"
      >
        {words}
      </bdi>
    </h1>
  );
}

/** What stands for the line about the post while it is on its way, and nothing once it is not coming. */
function PostLineWaiting({ waiting }: { readonly waiting: boolean }) {
  return waiting ? (
    <span
      aria-hidden
      className="block h-5 w-64 max-w-full animate-pulse rounded-sm bg-surface-muted"
    />
  ) : null;
}

/**
 * When the post says it was published, the address its views are counted under where its title is
 * what names it, and the way to the post itself on the website, where its address is a web page's.
 */
function PostLine({ post }: { readonly post: InsightsPost }) {
  const t = useTranslations('insights.post');
  const posts = useTranslations('insights.posts');
  const format = useFormatter();
  const href = postLink(post.address);

  return (
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-foreground-muted">
      <span>
        {post.published === null
          ? t('undated')
          : t('published', {
              date: format.dateTime(writableDay(post.published), {
                dateStyle: 'long',
                timeZone: 'UTC',
              }),
            })}
      </span>
      {post.title === null ? null : (
        <>
          <span aria-hidden>·</span>
          <span dir="ltr" className="max-w-full min-w-0 truncate">
            {readablePath(post.path)}
          </span>
        </>
      )}
      {href === null ? null : (
        <>
          <span aria-hidden>·</span>
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex min-h-6 items-center gap-1 rounded-sm font-medium text-accent-strong decoration-accent/60 underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
          >
            {posts('open')}
            <ExternalLink aria-hidden className="size-3.5" />{' '}
            <span className="sr-only">{posts('newTab')}</span>
          </a>
        </>
      )}
    </p>
  );
}

interface PostPlaceProps {
  readonly shown: PostShown;
  /** The website, or nothing while which one it is has still to be read. */
  readonly site: Site | undefined;
  /** Whether the address names a post at all. */
  readonly named: boolean;
  /** Whose views the screen counts. */
  readonly population: Population;
  readonly peopleOnly: boolean;
  readonly reading: MonthsReading;
  readonly onShowEveryone: () => void;
}

/**
 * What stands under the screen's heading: the post's record, or the one state in its place.
 *
 * The record on its way and the record itself are the same cards, and a record whose website is not
 * known yet can only be on its way, unless the address names no post at all.
 */
function PostPlace({
  shown,
  site,
  named,
  population,
  peopleOnly,
  reading,
  onShowEveryone,
}: PostPlaceProps) {
  const t = useTranslations('insights.post');
  const people = useTranslations('dashboard.population.empty');

  if (shown.state === 'missing') {
    return <NoPost site={named ? site : undefined} population={population} />;
  }

  if (site === undefined || shown.state === 'waiting' || shown.state === 'ready') {
    return (
      <PostRecord
        answer={shown.state === 'ready' ? shown.answer : undefined}
        site={site}
        population={population}
        peopleOnly={peopleOnly}
        reading={reading}
      />
    );
  }

  switch (shown.state) {
    case 'refused':
      return <FailureNotice error={shown.error} />;
    case 'unread':
      return <PostUnread answer={shown.answer} />;
    default:
      return (
        <ListEmpty
          icon={ScanSearch}
          title={t('absent.title')}
          body={t('absent.body')}
          action={
            <Button tone="secondary" size="sm" onClick={onShowEveryone}>
              {people('action')}
            </Button>
          }
        />
      );
  }
}

interface NoPostProps {
  /** The website whose sitemap was asked, or nothing where the address names no post to ask about. */
  readonly site: Site | undefined;
  /** Whose views the screen counts, which the way back carries. */
  readonly population: Population;
}

/**
 * What stands in place of a post the website does not list, or of one the address does not name,
 * with the way back to where it was opened from.
 *
 * Nothing the address says is written back: it is whatever somebody sent, and only a post the engine
 * found is ever named on the screen.
 */
function NoPost({ site, population }: NoPostProps) {
  const t = useTranslations('insights.post');
  const back = useWayBack(population);

  return (
    <ListEmpty
      icon={SearchX}
      title={t('missing.title')}
      body={site === undefined ? t('missing.unnamed') : t('missing.body', { site: site.domain })}
      action={
        <Link href={back.href} className={buttonStyle({ tone: 'secondary', size: 'sm' })}>
          {back.label}
        </Link>
      }
    />
  );
}

/**
 * What stands in place of the record of a post nobody has read, or nobody since its views began to be
 * counted, where some of its life went uncounted.
 */
function PostUnread({ answer }: { readonly answer: PostAnswer }) {
  const t = useTranslations('insights.post.unread');
  const format = useFormatter();
  const since = postViewsSince(answer);

  return (
    <ListEmpty
      icon={BookOpen}
      title={since === null ? t('title') : t('titleSince', { month: writtenMonth(since, format) })}
      body={t('body')}
    />
  );
}

/**
 * The words a post's months are read in: whether their views for each day are counted from the day it
 * was published, and what each of the months before its first is.
 *
 * @param answer The post's record, or nothing while it is on its way, when they are read as a dated
 * post's with nothing measured before them.
 */
function usePostMonthsWords(answer: PostAnswer | undefined): MonthsWords {
  const t = useTranslations('insights.post.months');
  const months = useTranslations('insights.months');

  return {
    caption: {
      total: t('caption.total'),
      average: answer?.post.published === null ? t('caption.averageUndated') : t('caption.average'),
    },
    before: (month) => {
      const before = answer === undefined ? 'unmeasured' : beforeOfPost(answer, month);

      return before === 'unmeasured'
        ? { cell: months('unmeasured'), legend: months('legend.unmeasured') }
        : { cell: t(before), legend: t(`legend.${before}`) };
    },
  };
}

interface PostRecordProps {
  /** The post's record, or nothing while it is on its way. */
  readonly answer: PostAnswer | undefined;
  /** The website, or nothing while it is not known. */
  readonly site: Site | undefined;
  /** Whose views the screen counts. */
  readonly population: Population;
  readonly peopleOnly: boolean;
  readonly reading: MonthsReading;
}

/**
 * Everything the post's record says, in the order somebody looking back over a post would ask it —
 * what it has come to, how it was read over the period they choose, then month by month and day by
 * day — and the same cards while it is on its way, each holding the place of its own figures.
 */
function PostRecord({ answer, site, population, peopleOnly, reading }: PostRecordProps) {
  const words = usePostMonthsWords(answer);
  const record = useMemo(
    () => (answer === undefined ? undefined : monthsRecordOf(answer)),
    [answer],
  );

  return (
    <>
      <PostHighlights answer={answer} timeZoneId={site?.timeZoneId} peopleOnly={peopleOnly} />
      {answer === undefined || site === undefined ? (
        <PostChartWaiting />
      ) : (
        <PostChart site={site} answer={answer} population={population} peopleOnly={peopleOnly} />
      )}
      <MonthsTable
        record={record}
        measure={reading.measure}
        onMeasure={reading.onMeasure}
        words={words}
      />
      <RecentWeeks
        days={answer?.days}
        from={answer?.from ?? null}
        publishedOn={answer === undefined ? null : postBeginsOn(answer)}
      />
    </>
  );
}

'use client';

import { ListX, type LucideIcon } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type ReactNode, type RefObject, useId, useState } from 'react';
import { ListEmpty } from '@/components/dashboard/ranked-list';
import { POSTS_CARD, PostRowsWaiting } from '@/components/insights/post-list';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Pagination } from '@/components/ui/pagination';
import { type PagesListing, POSTS_PAGE_SIZES } from '@/lib/analytics/posts';
import { pageOf } from '@/lib/paging';
import { cn } from '@/lib/styling';

/**
 * What the list of every post and the list of every page are each drawn with: the page of rows
 * under a heading saying how long the list is, what stands in for it while it is on its way, and
 * what stands in its place where a page has no rows.
 */

/** The words of either list, each of which names its rows its own way. */
type ListWords = 'insights.allPosts' | 'insights.allPages';

/** How many rows stand in for the first page of a list while it is on its way. */
const ROWS_WAITING = 8;

/** How a list standing in for the one on its way is drawn: still there, and plainly not current. */
const STANDING_IN = 'opacity-60';

/** A small button of the screen's own, still large enough to press with a thumb on a phone. */
export const PRESSABLE = 'h-10 sm:h-9';

/** A page of a list as far as saying which of its rows are on the screen goes. */
interface Shown {
  /** How many rows the list holds across every page, or how many hold what was searched for. */
  readonly matching: number;
  /** The rows of this page. */
  readonly shown: readonly unknown[];
}

/**
 * What is on the screen while nothing stands in for the answer it is of, held while something does.
 *
 * @param now What is asked for now.
 * @param standingIn Whether what is on the screen stands in for an answer still on its way.
 */
export function useHeld<T>(now: T, standingIn: boolean): T {
  const [held, setHeld] = useState(now);

  if (!standingIn && !Object.is(held, now)) {
    setHeld(now);
  }

  return standingIn ? held : now;
}

/**
 * What is said to anybody listening about a page of a list: how many rows the list holds, or how
 * many hold what was searched for, and, where they take more than one page, which of them it shows.
 *
 * @param counted How many the list holds, or how many match, as the screen says it.
 * @param answer The page on the screen.
 * @param listing What it is a page of.
 * @param t The words of the list of every post, which say which rows are shown for either list.
 */
export function spokenOf(
  counted: string,
  answer: Shown,
  listing: PagesListing,
  t: ReturnType<typeof useTranslations<'insights.allPosts'>>,
): string {
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
 * Brings the reader to the top of the list, and the keyboard with them: to its heading, which says
 * how long the list is, and clear of the bar across the top of the screen.
 *
 * @param heading The list's heading.
 */
export function bringToTop(heading: RefObject<HTMLHeadingElement | null>): void {
  heading.current?.focus({ preventScroll: true });
  heading.current?.scrollIntoView({ block: 'start' });
}

/** What stands for the first page of a list while it is on its way: its count, and its rows. */
export function ListingWaiting() {
  return (
    <div className="flex flex-col gap-3">
      <span aria-hidden className="block h-6 w-32 animate-pulse rounded-sm bg-surface-muted" />
      <Card className={POSTS_CARD}>
        <PostRowsWaiting rows={ROWS_WAITING} />
      </Card>
    </div>
  );
}

interface InPlaceOfRowsProps {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly body: string;
  /** The one thing that brings rows back onto the screen. */
  readonly action: string;
  readonly onAction: () => void;
  /** Where its heading stands among the screen's: beneath the screen's own name unless said. */
  readonly level?: 2 | 3;
  /** Whether it stands in for an answer still on its way. */
  readonly busy: boolean;
}

/**
 * What stands in place of the rows where a list holds some and none of them is on the screen: none
 * holds what was searched for, or the page is further along than the list reaches.
 */
export function InPlaceOfRows({
  icon,
  title,
  body,
  action,
  onAction,
  level,
  busy,
}: InPlaceOfRowsProps) {
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

interface ListedSectionProps {
  /** Whose words the list is written in. */
  readonly words: ListWords;
  /** How many rows the list holds, or how many of them match, as the screen says it. */
  readonly counted: string;
  /** Whatever qualifies every row of the list, as the screen says it, or nothing. */
  readonly notes: string;
  /** The one thing to do about what qualifies them, where there is one. */
  readonly action?: ReactNode;
  /** The list's heading, which says how long it is and which the keyboard is brought back to. */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  /** The page on the screen. */
  readonly answer: Shown;
  /**
   * How many of its rows the list is paged through, where that is fewer than it holds: all of them
   * unless said.
   */
  readonly reaches?: number;
  /** What the page on the screen is a page of: the one asked for, or the one standing in for it. */
  readonly onScreen: PagesListing;
  /** Whether the page on the screen stands in for the one asked for. */
  readonly standingIn: boolean;
  /** Moves to a page, counting from one. */
  readonly goTo: (page: number) => void;
  /** Sets how many rows a page holds. */
  readonly resize: (perPage: number) => void;
  /** The rows of the page, one for each the answer shows. */
  readonly children: ReactNode;
}

/**
 * One page of a list, under a heading carrying how long the list is and whatever qualifies all of
 * it; or, for a page further along than the list reaches, the same heading over the way back to its
 * first page.
 *
 * The heading is one heading whichever of the two stands under it, so moving to another page, or
 * choosing how many a page holds, brings the reader, and the keyboard, back to it wherever they
 * were. The line under the rows says which of them are there and is not read out, since the screen
 * says so itself, once, as each answer lands. Where the list reaches fewer rows than it holds, the
 * heading still counts every one of them, while the line and the pages offered end where it does.
 */
export function ListedSection({
  words,
  counted,
  notes,
  action,
  headingRef,
  answer,
  reaches = answer.matching,
  onScreen,
  standingIn,
  goTo,
  resize,
  children,
}: ListedSectionProps) {
  const t = useTranslations(words);
  const heading = useId();

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
          {notes === '' ? null : (
            <p className="text-sm text-balance text-foreground-muted">{notes}</p>
          )}
        </div>
        {action}
      </div>

      {answer.shown.length === 0 ? (
        <InPlaceOfRows
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
            {children}
          </ol>
          <Pagination
            label={t('nav.label')}
            total={reaches}
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

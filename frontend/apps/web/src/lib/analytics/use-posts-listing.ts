'use client';

import {
  createLoader,
  createParser,
  createSerializer,
  parseAsNumberLiteral,
  parseAsStringLiteral,
  useQueryStates,
} from 'nuqs';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { type Population, withPopulation } from '@/lib/analytics/people-only';
import {
  DEFAULT_POSTS_ORDER,
  POSTS_ORDERS,
  POSTS_PAGE_SIZE,
  POSTS_PAGE_SIZES,
  type PostsListing,
  pageFrom,
  searchFrom,
} from '@/lib/analytics/posts';
import {
  type ChosenPeople,
  type PeopleOptions,
  usePeopleOnly,
} from '@/lib/analytics/use-people-only';
import type { PostsOrder } from '@/lib/api/schemas';
import { offsetOf } from '@/lib/paging';
import { ALL_POSTS } from '@/lib/routes';

/**
 * Which page of the list of every post is on the screen, kept in the address itself.
 *
 * Everything that makes the list the one being read — what was searched for, the order, the page,
 * how many a page holds, and whose views are counted — is in the address, so the way back from a
 * post is the same page of the same list, a reload keeps it, and it can be sent to somebody. What
 * the list opens on where nobody has said is left out of the address, so the plain address of the
 * screen stays plain.
 *
 * The order, the page and how many a page holds are each a decision, and each is a new entry in the
 * browser's history, so the button somebody already has takes it back. A search is written over the
 * entry it is typed on: it changes as the words go down, and a reader who typed a title should not
 * have to press the way back once for every pause in it.
 */

/** A search, read out of an address exactly as the engine reads one. */
const SEARCH = createParser({
  parse: searchFrom,
  serialize: (search: string) => search,
}).withDefault('');

/** A page of the list, counting from one. */
const PAGE = createParser({ parse: pageFrom, serialize: String })
  .withDefault(1)
  .withOptions({ history: 'push' });

const LISTING = {
  search: SEARCH,
  order: parseAsStringLiteral(POSTS_ORDERS)
    .withDefault(DEFAULT_POSTS_ORDER)
    .withOptions({ history: 'push' }),
  page: PAGE,
  size: parseAsNumberLiteral(POSTS_PAGE_SIZES)
    .withDefault(POSTS_PAGE_SIZE)
    .withOptions({ history: 'push' }),
};

/** Writes a page of the list as its own address says it, leaving out whatever it opens on anyway. */
const writeListing = createSerializer(LISTING);

/** Reads a page of the list out of what an address says, by the rules the list itself reads it by. */
const readListing = createLoader(LISTING);

export interface ListingOptions extends PeopleOptions {
  /** The website whose posts are listed, or nothing while which one it is has still to be read. */
  readonly siteId?: string;
}

export interface ChosenListing extends ChosenPeople {
  /** The page on the screen, and what it is a page of. */
  readonly listing: PostsListing;
  /** Which page it is, counting from one. */
  readonly page: number;
  /**
   * The page as the list's own address writes it, without whose views are counted: what a post
   * opened from it names as the way back.
   */
  readonly written: string;
  readonly searchFor: (search: string) => void;
  readonly orderBy: (order: PostsOrder) => void;
  /** Moves to a page, counting from one. */
  readonly goTo: (page: number) => void;
  /** Sets how many posts a page holds. */
  readonly resize: (perPage: number) => void;
}

/**
 * Reads the page of the list out of the address, and writes another back into it.
 *
 * Where somebody is in the list only means anything in the list they were reading, so whatever
 * makes it another list — another search, another order, another size of page, or other people —
 * starts it again at its first page, in the same entry of the history as the change itself. The way
 * back is then the page they were on, of the list they were reading.
 *
 * Another website chosen is another list too, though nothing about it is in the address: the bar
 * across the top chooses it. Its list opens on its first page at once, and the page is then taken
 * out of the address in the entry it is in, since nobody chose to leave the page they were on.
 *
 * @param options Which website's posts are listed, and whether a remembered population is written
 * into an address that says nothing, as the screen the population is the subject of asks for.
 */
export function usePostsListing({ siteId, ...options }: ListingOptions = {}): ChosenListing {
  const [{ search, order, page, size }, write] = useQueryStates(LISTING);
  const people = usePeopleOnly(options);
  const keepToPeople = people.showOnlyPeople;
  const { moved: elsewhere, settle } = useListedOn(siteId);
  const onPage = elsewhere ? 1 : page;

  useEffect(() => {
    if (!elsewhere) {
      return;
    }

    if (page !== 1) {
      void write({ page: null }, { history: 'replace' });
    }

    settle();
  }, [elsewhere, page, settle, write]);

  const searchFor = useCallback(
    (searched: string) => {
      void write({ search: searchFrom(searched), page: null }, { history: 'replace' });
    },
    [write],
  );

  // The order already chosen can be pressed again, and is then the same list on the same page.
  const orderBy = useCallback(
    (ordered: PostsOrder) => {
      if (ordered !== order) {
        void write({ order: ordered, page: null });
      }
    },
    [order, write],
  );

  const goTo = useCallback(
    (moved: number) => {
      void write({ page: moved });
    },
    [write],
  );

  const resize = useCallback(
    (perPage: number) => {
      void write({ size: POSTS_PAGE_SIZES.find((one) => one === perPage) ?? null, page: null });
    },
    [write],
  );

  const showOnlyPeople = useCallback(
    (peopleOnly: boolean) => {
      keepToPeople(peopleOnly);
      void write({ page: null });
    },
    [keepToPeople, write],
  );

  const listing = useMemo(
    () => ({ search, order, limit: size, offset: offsetOf(onPage, size) }),
    [search, order, onPage, size],
  );
  const written = useMemo(
    () => queryOf(writeListing({ search, order, page: onPage, size })),
    [search, order, onPage, size],
  );

  return {
    ...people,
    showOnlyPeople,
    listing,
    page: onPage,
    written,
    searchFor,
    orderBy,
    goTo,
    resize,
  };
}

/** Which website the page in the address was reached on, and whether another has been chosen since. */
interface ListedOn {
  /** Whether a website other than the one the page was reached on is chosen now. */
  readonly moved: boolean;
  /** Takes the website chosen now as the one the page in the address belongs to. */
  readonly settle: () => void;
}

/**
 * Remembers which website the page in the address was reached on.
 *
 * The first website the screen is shown for is the one the address was written on, whether the
 * screen opened on it or was waiting for it to be known.
 *
 * @param siteId The website chosen now, or nothing while which one it is has still to be read.
 */
function useListedOn(siteId: string | undefined): ListedOn {
  const [listedOn, setListedOn] = useState(siteId);

  if (listedOn === undefined && siteId !== undefined) {
    setListedOn(siteId);
  }

  const settle = useCallback(() => setListedOn(siteId), [siteId]);

  return { moved: listedOn !== undefined && siteId !== undefined && listedOn !== siteId, settle };
}

/**
 * The address of the page of the list a post's screen was opened from, counting whoever that screen
 * counts now.
 *
 * Whatever the post's address says the list was is read by the list's own rules and written again,
 * so the way back is only ever a page of the list of every post, and nothing else the address says
 * is passed on.
 *
 * @param written What the post's address names the list as.
 * @param population Whose views the post's screen counts, which the list goes on counting.
 */
export function listAddress(written: string, population: Population): string {
  const asked = new URLSearchParams(writeListing(readListing(written)));
  const query = withPopulation(asked, population).toString();

  return query === '' ? ALL_POSTS : `${ALL_POSTS}?${query}`;
}

/**
 * The query an address is written with, without the question mark that begins it.
 *
 * @param written The address's query, as the list's own address writes it.
 */
function queryOf(written: string): string {
  return written.startsWith('?') ? written.slice(1) : written;
}

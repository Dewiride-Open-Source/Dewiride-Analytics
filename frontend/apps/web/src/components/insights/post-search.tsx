'use client';

import { Search, X } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type RefObject, type SubmitEvent, useEffect, useId, useRef, useState } from 'react';
import { TextInput } from '@/components/ui/field';
import { POSTS_SEARCH_MOST, SEARCH_SETTLES_AFTER, searchFrom } from '@/lib/analytics/posts';

interface PostSearchProps {
  /** What the list is searched for now, as the address says it: nothing where it is not searched. */
  readonly search: string;
  /** Searches the list for something else, or for nothing. */
  readonly onSearch: (search: string) => void;
  /** The box itself, for a screen that brings somebody back to it. */
  readonly inputRef?: RefObject<HTMLInputElement | null>;
}

/**
 * The box a website's posts are searched from, by their title or their address.
 *
 * What is typed is searched for once typing stops, rather than at every letter, so a word is asked
 * about once it is down rather than once for each letter on the way to it; and at once where
 * somebody presses Enter, or empties the box, since either is the end of what they were typing. The
 * box holds what was typed, spaces and all, while the list is searched for it read as words.
 *
 * Whatever the address says instead is what the box holds — the browser's own way back, or the
 * search cleared from elsewhere on the screen — unless it is what the box already says.
 *
 * A box holding something carries the way to empty it, drawn in the product's own colours and large
 * enough to press with a thumb, in place of the small mark each browser draws in its own; emptying
 * it leaves the keyboard in the box.
 */
export function PostSearch({ search, onSearch, inputRef }: PostSearchProps) {
  const t = useTranslations('insights.allPosts.search');
  const id = useId();
  const own = useRef<HTMLInputElement>(null);
  const box = inputRef ?? own;
  const [typed, setTyped] = useState(search);
  const [heard, setHeard] = useState(search);

  if (search !== heard) {
    setHeard(search);

    if (searchFrom(typed) !== search) {
      setTyped(search);
    }
  }

  useEffect(() => {
    const wanted = searchFrom(typed);

    if (wanted === search) {
      return;
    }

    const settling = setTimeout(() => onSearch(wanted), SEARCH_SETTLES_AFTER);

    return () => clearTimeout(settling);
  }, [typed, search, onSearch]);

  const searchNow = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();

    const wanted = searchFrom(typed);

    if (wanted !== search) {
      onSearch(wanted);
    }
  };

  const type = (next: string) => {
    setTyped(next);

    if (next === '' && search !== '') {
      onSearch('');
    }
  };

  const empty = () => {
    type('');
    box.current?.focus();
  };

  return (
    <form role="search" onSubmit={searchNow} className="relative w-full sm:max-w-md">
      <label htmlFor={id} className="sr-only">
        {t('label')}
      </label>
      <Search
        aria-hidden
        className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-foreground-subtle"
      />
      <TextInput
        ref={box}
        id={id}
        type="search"
        enterKeyHint="search"
        autoComplete="off"
        maxLength={POSTS_SEARCH_MOST}
        placeholder={t('placeholder')}
        value={typed}
        onChange={(event) => type(event.target.value)}
        className="pr-11 pl-9 [&::-webkit-search-cancel-button]:appearance-none"
      />
      {typed === '' ? null : (
        <button
          type="button"
          aria-label={t('clear')}
          title={t('clear')}
          onClick={empty}
          className="absolute top-1/2 right-0.5 inline-flex size-10 -translate-y-1/2 items-center justify-center rounded-md text-foreground-subtle transition-colors hover:bg-accent-soft hover:text-accent-strong focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-strong"
        >
          <X aria-hidden className="size-4" />
        </button>
      )}
    </form>
  );
}

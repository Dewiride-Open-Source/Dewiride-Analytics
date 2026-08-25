'use client';

import { ChevronDown } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useState } from 'react';
import { type Option, OptionList } from '@/components/ui/option-list';
import { Popover } from '@/components/ui/popover';
import { cn } from '@/lib/styling';

interface FilterButtonProps {
  /** What about a visit this narrows, which is what the button and its panel are called. */
  readonly label: string;
  readonly options: readonly Option[];
  readonly chosen: readonly string[];
  /** Whether more than one value may be asked for at a time. */
  readonly multiple: boolean;
  readonly onPick: (value: string) => void;
  /** Told the first time the panel is opened, for choices whose values have to be asked for. */
  readonly onOpen?: () => void;
  /** Whether those values are still on their way. */
  readonly waiting?: boolean;
  /** Whether as many values as may be asked for at once have already been picked. */
  readonly full?: boolean;
}

/**
 * One thing about a visit, and the values of it this period held.
 *
 * The button says what it narrows and how much of it has been asked for; the panel is where the
 * picking happens. Keeping the two together means a row of them can be laid out as a row of
 * buttons — which is what lets eleven of these sit above a list on a phone without becoming a
 * form somebody has to work down.
 */
export function FilterButton({
  label,
  options,
  chosen,
  multiple,
  onPick,
  onOpen,
  waiting = false,
  full = false,
}: FilterButtonProps) {
  const t = useTranslations('journeys.filters');
  const format = useFormatter();
  const [open, setOpen] = useState(false);

  function opening(next: boolean) {
    setOpen(next);

    if (next) {
      onOpen?.();
    }
  }

  return (
    <Popover
      open={open}
      onOpenChange={opening}
      label={label}
      trigger={
        <button
          type="button"
          className={cn(
            'glow-control inline-flex h-9 items-center gap-1.5 rounded-full border px-3 text-sm',
            'whitespace-nowrap',
            chosen.length > 0
              ? 'border-accent/40 bg-accent-soft font-medium text-accent-strong'
              : 'border-border bg-surface text-foreground-muted hover:text-foreground',
          )}
        >
          {label}
          {chosen.length > 0 ? (
            <>
              {/*
                A real space before the figure, so the control is announced as "Country 2 chosen"
                rather than as one word nobody would recognise. What the figure counts is said in
                words for anyone listening and drawn as a badge for anyone looking — a number on
                its own means nothing read out.
              */}{' '}
              <span className="rounded-full bg-accent/15 px-1.5 text-xs font-semibold tabular-nums">
                {format.number(chosen.length)} <span className="sr-only">{t('chosen')}</span>
              </span>
            </>
          ) : null}
          <ChevronDown aria-hidden className="size-4 opacity-70" />
        </button>
      }
    >
      {waiting ? (
        <p className="px-4 py-8 text-center text-sm text-foreground-muted" aria-live="polite">
          {t('loading')}
        </p>
      ) : (
        <OptionList
          options={options}
          chosen={chosen}
          multiple={multiple}
          onPick={onPick}
          searchLabel={t('searchLabel')}
          searchPlaceholder={t('searchPlaceholder')}
          nothingLabel={t('nothing')}
          noMatchLabel={t('noMatch')}
          full={full}
          fullLabel={t('full')}
        />
      )}
    </Popover>
  );
}

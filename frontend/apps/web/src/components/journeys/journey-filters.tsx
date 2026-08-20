'use client';

import { X } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { useId } from 'react';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { SelectInput } from '@/components/ui/field';
import {
  type CategoryTally,
  EVERY_JOURNEY,
  type JourneyFilters,
  isNarrowed,
  type PageFloor,
  PAGE_FLOORS,
  type StrengthFloor,
  STRENGTH_FLOORS,
  toggleCategory,
} from '@/lib/analytics/journeys';
import { CATEGORY_TONES } from '@/lib/analytics/verdicts';
import { cn } from '@/lib/styling';

interface JourneyFilterPanelProps {
  /** The conclusions this period reached, most journeys first. */
  readonly available: readonly CategoryTally[];
  /** Whether that is still being read, so the panel does not claim there were none. */
  readonly pending: boolean;
  readonly value: JourneyFilters;
  readonly onChange: (filters: JourneyFilters) => void;
}

/** How each floor on the evidence is named. */
const STRENGTH_LABELS: Readonly<Record<StrengthFloor, string>> = {
  weak: 'strengthWeak',
  moderate: 'strengthModerate',
  strong: 'strengthStrong',
};

/** How each floor on the pages is named. */
const PAGE_LABELS: Readonly<Record<PageFloor, string>> = {
  0: 'pagesAny',
  1: 'pagesOne',
  2: 'pagesMany',
};

/**
 * The controls that cut a period's journeys down to the ones somebody came for.
 *
 * On a website of any size most journeys are machinery, so "show me the ones that were people" is
 * the question this screen exists to answer and it has to be one press away. The conclusions
 * offered are the ones this period actually reached, with their counts beside them: a list of
 * fourteen possibilities, most of which never happened here, is a longer way of finding the three
 * that did.
 *
 * Narrowing is asked of the engine rather than done to what came back, so the figures beside the
 * list keep describing the list.
 */
export function JourneyFilterPanel({
  available,
  pending,
  value,
  onChange,
}: JourneyFilterPanelProps) {
  const t = useTranslations('journeys.filters');
  const categories = useTranslations('verdicts.category');
  const format = useFormatter();
  const strengthId = useId();
  const pagesId = useId();

  return (
    <Card className="glow-card flex flex-col gap-5 p-5 sm:p-6">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
        {isNarrowed(value) ? (
          <Button tone="quiet" size="sm" onClick={() => onChange(EVERY_JOURNEY)}>
            <X aria-hidden className="size-4" />
            {t('clear')}
          </Button>
        ) : null}
      </div>

      <fieldset className="flex flex-col gap-2.5">
        <legend className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
          {t('categories')}
        </legend>

        {pending ? (
          <div aria-hidden className="flex flex-wrap gap-2">
            {['first', 'second', 'third'].map((chip) => (
              <span key={chip} className="h-8 w-32 animate-pulse rounded-full bg-surface-muted" />
            ))}
          </div>
        ) : (
          <div className="flex flex-wrap gap-2">
            {available.map((tally) => {
              const chosen = value.categories.includes(tally.category);

              return (
                <button
                  key={tally.category}
                  type="button"
                  aria-pressed={chosen}
                  onClick={() => onChange(toggleCategory(value, tally.category))}
                  className={cn(
                    'inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition-colors',
                    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong',
                    chosen
                      ? 'border-accent/40 bg-accent-soft font-medium text-accent-strong'
                      : 'border-border bg-surface text-foreground-muted hover:bg-surface-muted hover:text-foreground',
                  )}
                >
                  <span
                    aria-hidden
                    className={cn(
                      'size-2 rounded-full',
                      TONE_FILLS[CATEGORY_TONES[tally.category]],
                    )}
                  />
                  {/*
                    A real space between the name and the figure, so what a screen reader reads out
                    is "A person 6" rather than one word nobody would recognise. The gap between
                    them on screen is drawn by the layout and says nothing to anybody listening.
                  */}
                  {categories(tally.category)}{' '}
                  <span className="tabular-nums opacity-70">{format.number(tally.journeys)}</span>
                </button>
              );
            })}
          </div>
        )}
      </fieldset>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor={strengthId}
            className="text-xs font-medium tracking-wide text-foreground-muted uppercase"
          >
            {t('strength')}
          </label>
          <SelectInput
            id={strengthId}
            value={value.leastStrength ?? ''}
            onChange={(event) =>
              onChange({
                ...value,
                leastStrength: (event.target.value || null) as StrengthFloor | null,
              })
            }
          >
            <option value="">{t('strengthAny')}</option>
            {STRENGTH_FLOORS.map((floor) => (
              <option key={floor} value={floor}>
                {t(STRENGTH_LABELS[floor])}
              </option>
            ))}
          </SelectInput>
        </div>

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor={pagesId}
            className="text-xs font-medium tracking-wide text-foreground-muted uppercase"
          >
            {t('pages')}
          </label>
          <SelectInput
            id={pagesId}
            value={value.leastPages}
            onChange={(event) =>
              onChange({ ...value, leastPages: Number(event.target.value) as PageFloor })
            }
          >
            {PAGE_FLOORS.map((floor) => (
              <option key={floor} value={floor}>
                {t(PAGE_LABELS[floor])}
              </option>
            ))}
          </SelectInput>
        </div>
      </div>
    </Card>
  );
}

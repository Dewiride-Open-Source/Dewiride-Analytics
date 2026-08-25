'use client';

import { X } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { PlaceCredit, RoutingCredit } from '@/components/dashboard/place-credit';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { FilterBar } from '@/components/journeys/filter-bar';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Chip } from '@/components/ui/chip';
import {
  type CategoryTally,
  countsHold,
  EVERY_JOURNEY,
  isNarrowed,
  type JourneyFilters,
  toggleChoice,
} from '@/lib/analytics/journeys';
import { CATEGORY_TONES } from '@/lib/analytics/verdicts';
import type { VisitFacets } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

interface JourneyFilterPanelProps {
  /** The conclusions this period reached, most journeys first. */
  readonly available: readonly CategoryTally[];
  /** Whether that is still being read, so the panel does not claim there were none. */
  readonly pending: boolean;
  /** What else this period turned out to hold, once a reader has reached for it. */
  readonly held: VisitFacets | undefined;
  /** Whether that is still on its way. */
  readonly heldPending: boolean;
  readonly onWantOptions: () => void;
  readonly value: JourneyFilters;
  readonly onChange: (filters: JourneyFilters) => void;
}

/**
 * The controls that cut a period's journeys down to the ones somebody came for.
 *
 * On a website of any size most journeys are machinery, so "show me the ones that were people" is
 * the question this screen exists to answer and it has to be one press away — which is why the
 * conclusions are chips across the top rather than one menu among twelve. Everything else about a
 * visit sits in the row below, where each is a press to open and does not take up the screen until
 * it is wanted. The conclusions offered are the ones this period actually reached: a list of
 * fourteen possibilities, most of which never happened here, is a longer way of finding the three
 * that did.
 *
 * Narrowing is asked of the engine rather than done to what came back, so the figures beside the
 * list keep describing the list.
 */
export function JourneyFilterPanel({
  available,
  pending,
  held,
  heldPending,
  onWantOptions,
  value,
  onChange,
}: JourneyFilterPanelProps) {
  const t = useTranslations('journeys.filters');
  const categories = useTranslations('verdicts.category');
  const format = useFormatter();

  /*
    Every figure on this panel counts the whole period rather than what is left of it once
    something has been narrowed away, so each control keeps its figures exactly while nothing
    else is narrowing the list. Picking a second conclusion leaves them, because two conclusions
    are alternatives rather than conditions piled up; picking a country as well takes them, because
    from then on they would be true of the period and false of the list underneath them.
  */
  const counted = countsHold(value, 'categories');

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

      <fieldset className="flex min-w-0 flex-col gap-2.5">
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
            {available.map((tally) => (
              <Chip
                key={tally.category}
                pressed={value.categories.includes(tally.category)}
                onPress={() => onChange(toggleChoice(value, 'categories', tally.category))}
                leading={
                  <span
                    aria-hidden
                    className={cn(
                      'size-2 shrink-0 rounded-full',
                      TONE_FILLS[CATEGORY_TONES[tally.category]],
                    )}
                  />
                }
              >
                {/*
                  A real space between the name and the figure, so what a screen reader reads out
                  is "A person 6" rather than one word nobody would recognise. The gap between them
                  on screen is drawn by the layout and says nothing to anybody listening.
                */}
                {categories(tally.category)}
                {counted ? (
                  <>
                    {' '}
                    <span className="tabular-nums opacity-70">{format.number(tally.journeys)}</span>
                  </>
                ) : null}
              </Chip>
            ))}
          </div>
        )}
      </fieldset>

      <fieldset className="flex min-w-0 flex-col gap-2.5">
        <legend className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
          {t('details')}
        </legend>

        <FilterBar
          value={value}
          onChange={onChange}
          held={held}
          onWantOptions={onWantOptions}
          waiting={heldPending}
        />
      </fieldset>

      {/*
        Required rather than courteous. Three of the things this panel narrows by — the country,
        the town, and the network a visit arrived over — are named from published data whose
        licences ask for a link back from anywhere their results appear, and a list of countries
        somebody can pick from is exactly that. Both are credited because this panel offers both.
      */}
      <div className="flex flex-col gap-1">
        <PlaceCredit />
        <RoutingCredit />
      </div>
    </Card>
  );
}

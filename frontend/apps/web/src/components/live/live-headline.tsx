'use client';

import { ArrowRight } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type ReactNode, useId, useMemo, useState } from 'react';
import { TONE_FILLS } from '@/components/dashboard/verdict-badge';
import { Card } from '@/components/ui/card';
import { Chip } from '@/components/ui/chip';
import { Link } from '@/i18n/navigation';
import {
  anybodyActive,
  arrivalChips,
  type LiveChoice,
  type LiveNarrowing,
  namedNow,
  pagesReadIn,
  pressChip,
  recognisedChips,
} from '@/lib/analytics/live';
import { CATEGORY_TONES } from '@/lib/analytics/verdicts';
import type { Live, TrafficCategory } from '@/lib/api/schemas';
import { JOURNEYS } from '@/lib/routes';
import { cn } from '@/lib/styling';

interface LiveHeadlineProps {
  readonly answer: Live;
  /** What the list of who is here is kept to. */
  readonly narrowing: LiveNarrowing;
  readonly onNarrow: (narrowing: LiveNarrowing) => void;
}

/**
 * How many visitors the half hour holds, how many pages they read, and the chips that keep the list
 * of who is here to some of them.
 *
 * The visitors are the whole reason somebody opened the screen, so their number is the largest thing
 * on it and sits on the one card the page is built around, with the pages read across the same half
 * hour at the card's other edge.
 *
 * Two rows of chips follow, and neither is a breakdown of that figure. What has been recognised is
 * named only where it has been, and counted over the visitors the reading carried: most visitors
 * carry no conclusion while their visit is still running, and dividing them all up would mean
 * inventing one, so the rest are counted apart and the screen says in one clause when they will be
 * answered. How they arrived is counted over everybody the half hour holds, each visitor by the kind
 * of the very site their row names after "From", and somebody nothing named a sender for is counted
 * under none of the four.
 *
 * Pressing a chip keeps the list of who is here to those visitors, one chip at a time, and pressing
 * it again shows everybody. Nothing on this card narrows with it: the figures go on counting
 * everybody. A recognised chip stays on offer while it is pressed or the keyboard is on it, however
 * many of that kind are left, so a reading never takes a control from under a hand or the keyboard.
 */
export function LiveHeadline({ answer, narrowing, onNarrow }: LiveHeadlineProps) {
  const t = useTranslations('live');
  const format = useFormatter();
  const words = useChoiceWords();
  const [held, setHeld] = useState<TrafficCategory | null>(null);
  const named = useMemo(
    () => namedNow(answer.visitors, answer.visitorsSeen),
    [answer.visitors, answer.visitorsSeen],
  );
  const read = useMemo(() => pagesReadIn(answer.minutes), [answer.minutes]);
  const busy = anybodyActive(answer.visitors, answer.at);
  const recognised = recognisedChips(named.groups, narrowing, held);
  const arrived = arrivalChips(answer.arrivals, narrowing);

  return (
    // Measured against the card rather than the window, so that the arrangement stays right
    // wherever the card is put: the two figures and the chips side by side while it has the width
    // of a wide screen, and the figures over the chips everywhere else. Side by side waits until
    // the chips can sit two to a line between the figures; one under another, they would stand
    // far taller than either figure and leave a well of nothing beneath both.
    <Card focal className="@container p-5 sm:p-6">
      <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-6 gap-y-5 @4xl:grid-cols-[14rem_minmax(0,1fr)_auto] @4xl:gap-x-8">
        <div className="flex flex-col gap-1">
          {/*
            The only part of this screen that announces itself. It holds the label and the figure
            together so that it is read out as "Visitors, seven" rather than as a bare number, and
            React writes nothing at all when a fresh reading leaves the figure where it was — so a
            quiet half hour is silent rather than counting itself aloud every ten seconds.
          */}
          <div aria-live="polite" aria-atomic="true" className="flex flex-col gap-1">
            <h2 className="text-sm font-medium text-foreground-muted">{t('count.label')}</h2>
            <p className="text-4xl font-semibold tracking-tight text-foreground tabular-nums sm:text-5xl">
              {format.number(answer.visitorsSeen)}
            </p>
          </div>

          {/*
            Which of the two is worth a line depends on the moment. While somebody is here, what the
            figure counts is the thing a reader has to know to trust it; once everybody has left,
            that they have left is the more useful fact and explains the figure at the same time.
          */}
          <p className="text-sm text-foreground-muted">
            {busy ? t('count.note') : t('count.quiet')}
          </p>
        </div>

        {/*
          A figure beside the heading rather than a heading of its own, so that what is recognised
          among the visitors stays under the visitors. It is left out of the announcement: it moves
          with every page anybody opens, and a busy half hour would read itself aloud on every beat.
        */}
        <div className="flex flex-col items-end gap-1 text-right @4xl:col-start-3 @4xl:row-start-1 @4xl:border-l @4xl:border-border @4xl:pl-8">
          <p className="text-sm font-medium text-foreground-muted">{t('views.label')}</p>
          <p className="text-4xl font-semibold tracking-tight text-foreground tabular-nums sm:text-5xl">
            {format.number(read)}
          </p>
        </div>

        {/*
          Held while the list is kept to something even once nobody is left in the half hour, so the
          chip that shows everybody again is still there for a row somebody is holding open.
        */}
        {answer.visitorsSeen === 0 && narrowing === null ? null : (
          <div className="col-span-2 flex min-w-0 flex-col gap-4 border-t border-border pt-5 @4xl:col-span-1 @4xl:col-start-2 @4xl:row-start-1 @4xl:border-t-0 @4xl:border-l @4xl:pt-0 @4xl:pl-8">
            {/*
              First, because its four chips are always there. Drawn beneath the recognised row,
              which comes and goes with the crawlers in the half hour, they would jump a row's height
              between a reader aiming and pressing.

              No dot. Every colour a dot comes in on this screen is a verdict — green for a person,
              violet for machinery, red for what nobody asked for — and a way of arriving is not
              one: violet on the chip for the people a search engine sent would read as the search
              engine itself.
            */}
            <ChipGroup title={t('arrived.title')}>
              {arrived.map((chip) => (
                <Chip
                  key={chip.choice.kind}
                  pressed={chip.pressed}
                  onPress={() => onNarrow(pressChip(narrowing, chip.choice))}
                  count={format.number(chip.visitors)}
                >
                  {words(chip.choice)}
                </Chip>
              ))}
            </ChipGroup>

            {/*
              The heading belongs to the chips and appears with them. On the great majority of half
              hours nothing has been settled about anybody, and a heading saying something has been
              recognised standing over a line saying nobody has been is the screen contradicting
              itself in two lines.
            */}
            {recognised.length === 0 ? null : (
              <ChipGroup title={t('named.title')}>
                {recognised.map((chip) => (
                  <Chip
                    key={chip.choice.category}
                    pressed={chip.pressed}
                    onPress={() => onNarrow(pressChip(narrowing, chip.choice))}
                    onFocus={() => setHeld(chip.choice.category)}
                    onBlur={() => setHeld(null)}
                    leading={
                      <span
                        aria-hidden
                        className={cn(
                          'size-2 shrink-0 rounded-full',
                          TONE_FILLS[CATEGORY_TONES[chip.choice.category]],
                        )}
                      />
                    }
                    count={format.number(chip.visitors)}
                  >
                    {words(chip.choice)}
                  </Chip>
                ))}
              </ChipGroup>
            )}

            {named.unnamed === 0 ? null : (
              <div className="flex flex-col gap-0.5">
                <p className="text-sm text-foreground-muted">
                  {t('named.watching', { count: named.unnamed })}
                </p>
                <p className="text-xs text-foreground-subtle">{t('named.settles')}</p>
              </div>
            )}

            <Link
              href={JOURNEYS}
              className="inline-flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-accent-strong hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent-strong"
            >
              {t('named.link')}
              <ArrowRight aria-hidden className="size-4" />
            </Link>
          </div>
        )}
      </div>
    </Card>
  );
}

interface ChipGroupProps {
  readonly title: string;
  readonly children: ReactNode;
}

/** A row of chips under the heading that names it, announced as one group by that name. */
function ChipGroup({ title, children }: ChipGroupProps) {
  const id = useId();

  return (
    <div className="flex flex-col gap-2">
      <h3 id={id} className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
        {title}
      </h3>

      <div role="group" aria-labelledby={id} className="flex flex-wrap gap-2">
        {children}
      </div>
    </div>
  );
}

/**
 * The words a choice is written in: the same on the chip that makes it and on the pill that undoes
 * it, and the catalogue's own wherever else the same thing is named.
 *
 * A way of arriving is written the way the User journey writes one standing on its own — "From a
 * search engine" — which keeps it apart from "A search engine's crawler" in the row beneath it: the
 * people a search engine sent, and the search engine itself.
 */
export function useChoiceWords(): (choice: LiveChoice) => string {
  const categories = useTranslations('verdicts.category');
  const arrived = useTranslations('journeys.filters.arrived');

  return (choice) =>
    choice.by === 'category' ? categories(choice.category) : arrived(choice.kind);
}

'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useMemo } from 'react';
import { FilterButton } from '@/components/journeys/filter-button';
import { ChoicePill } from '@/components/ui/chip';
import type { Option } from '@/components/ui/option-list';
import {
  countsHold,
  DETAIL_DIMENSIONS,
  type DetailDimension,
  isFull,
  type JourneyFilters,
  PAGE_FLOORS,
  type PageFloor,
  STRENGTH_FLOORS,
  type StrengthFloor,
  toggleChoice,
  withoutChoice,
} from '@/lib/analytics/journeys';
import { countryNames } from '@/lib/analytics/places';
import { deviceKindSchema, type VisitFacets, visitSourceKindSchema } from '@/lib/api/schemas';

interface FilterBarProps {
  readonly value: JourneyFilters;
  readonly onChange: (filters: JourneyFilters) => void;
  /** What this period turned out to hold, once it has been asked for. */
  readonly held: VisitFacets | undefined;
  /** Told the first time a reader reaches for one of these, which is when it is worth asking. */
  readonly onWantOptions: () => void;
  /** Whether that answer is still on its way. */
  readonly waiting: boolean;
}

/** The seven whose values are whatever a website's own traffic happened to record. */
type OpenDimension = Exclude<DetailDimension, 'devices' | 'sourceKinds'>;

/** What each thing about a visit is called. */
const LABELS = {
  devices: 'of.devices',
  sourceKinds: 'of.sourceKinds',
  browsers: 'of.browsers',
  systems: 'of.systems',
  countries: 'of.countries',
  towns: 'of.towns',
  networks: 'of.networks',
  sources: 'of.sources',
  entryPages: 'of.entryPages',
} as const satisfies Record<DetailDimension, string>;

/**
 * What the empty value means, which is a different answer for each of them.
 *
 * The engine reports it as a value rather than leaving the row out, because "the visits nothing
 * could be established about" is a real question and a different one from "all of them". It reads
 * as a sentence here for the same reason nothing else on this screen shows an empty cell.
 */
const NOTHING_ESTABLISHED = {
  browsers: 'unplaced.browsers',
  systems: 'unplaced.systems',
  countries: 'unplaced.countries',
  towns: 'unplaced.towns',
  networks: 'unplaced.networks',
  sources: 'unplaced.sources',
  entryPages: 'unplaced.entryPages',
} as const satisfies Record<OpenDimension, string>;

/** How each floor on the evidence is named. */
const STRENGTH_LABELS = {
  weak: 'strengthWeak',
  moderate: 'strengthModerate',
  strong: 'strengthStrong',
} as const satisfies Record<StrengthFloor, string>;

/**
 * How each floor on the pages is named.
 *
 * A floor of none is absent, because it is not a choice: it is what taking this off leaves behind,
 * and offering it as well would give a reader two ways to do one thing and a pill that narrows
 * nothing.
 */
const PAGE_LABELS = {
  1: 'pagesOne',
  2: 'pagesMany',
} as const satisfies Record<Exclude<PageFloor, 0>, string>;

/**
 * Writes one value of a vocabulary the engine names, or nothing at all where it names one this
 * screen has no word for.
 *
 * The vocabulary is searched rather than the value being asserted to belong to it, which is what
 * keeps a value from an engine further ahead than the screen reading it out of the list
 * altogether. The alternative is showing somebody the code it arrived as.
 */
function spell<T extends string>(
  words: readonly T[],
  recorded: string,
  written: (one: T) => string,
): string | null {
  const known = words.find((one) => one === recorded);

  return known === undefined ? null : written(known);
}

/** One control in the row, and everything it takes to draw it and to undo what it did. */
interface Control {
  readonly key: string;
  readonly label: string;
  readonly options: readonly Option[];
  readonly chosen: readonly string[];
  /** Whether more than one value may be asked for at a time. */
  readonly multiple: boolean;
  readonly pick: (value: string) => void;
  readonly remove: (value: string) => void;
  /** Whether its values come from what the period held, rather than from a settled list. */
  readonly fromHeld: boolean;
  /** Whether it is already holding as many values as may be asked for at once. */
  readonly full: boolean;
}

/**
 * The eleven controls that narrow a list of journeys, and everything a reader has picked.
 *
 * A row of buttons rather than a form: eleven labelled boxes stacked above a list would be a page
 * of chrome in front of the thing somebody came to read, and on a phone the list would begin below
 * the fold. Each button carries its own state, and everything picked is repeated underneath as
 * something one press takes off again — so a view arrived at through several menus can be undone
 * without going back through them.
 */
export function FilterBar({ value, onChange, held, onWantOptions, waiting }: FilterBarProps) {
  const t = useTranslations('journeys.filters');
  const deviceNames = useTranslations('dashboard.devices.kind');
  const locale = useLocale();

  // Built once per language rather than per row: building one costs enough to be noticeable
  // across a list of two hundred countries, which is exactly what this is for.
  const named = useMemo(() => countryNames(locale), [locale]);

  /** Writes one value of one of the seven open sets out for a reader. */
  function openSet(dimension: OpenDimension, recorded: string): string {
    if (dimension === 'countries') {
      return named(recorded) ?? t(NOTHING_ESTABLISHED.countries);
    }

    return recorded === '' ? t(NOTHING_ESTABLISHED[dimension]) : recorded;
  }

  /** How each thing about a visit turns one of its values into something a reader would say. */
  function writerFor(dimension: DetailDimension): (recorded: string) => string | null {
    if (dimension === 'devices') {
      return (recorded) => spell(deviceKindSchema.options, recorded, (kind) => deviceNames(kind));
    }

    if (dimension === 'sourceKinds') {
      return (recorded) =>
        spell(visitSourceKindSchema.options, recorded, (kind) => t(`arrived.${kind}`));
    }

    return (recorded) => openSet(dimension, recorded);
  }

  /**
   * What one control offers: the values this period held, busiest first as the engine ranked them,
   * followed by anything already picked that the period turns out not to hold.
   *
   * That tail matters. A period narrowed to a town it no longer holds still has to offer that
   * town, checked — a control that quietly dropped it would leave somebody looking at an empty
   * list with no way of seeing why, and nothing on the screen to press to fix it.
   */
  function optionsFor(dimension: DetailDimension): readonly Option[] {
    const rows = held?.[dimension] ?? [];
    const chosen: readonly string[] = value[dimension];
    const counted = countsHold(value, dimension);
    const written = writerFor(dimension);

    const offered: readonly { value: string; visits?: number }[] = [
      ...rows,
      ...chosen
        .filter((one) => !rows.some((row) => row.value === one))
        .map((one) => ({ value: one })),
    ];

    return offered.flatMap((row) => {
      const label = written(row.value);

      return label === null
        ? []
        : [{ value: row.value, label, count: counted ? row.visits : undefined }];
    });
  }

  const controls: readonly Control[] = [
    ...DETAIL_DIMENSIONS.map<Control>((dimension) => ({
      key: dimension,
      label: t(LABELS[dimension]),
      options: optionsFor(dimension),
      chosen: value[dimension],
      multiple: true,
      pick: (one) => onChange(toggleChoice(value, dimension, one)),
      remove: (one) => onChange(withoutChoice(value, dimension, one)),
      fromHeld: true,
      full: isFull(value, dimension),
    })),
    {
      key: 'strength',
      label: t('strength'),
      options: STRENGTH_FLOORS.map((floor) => ({
        value: floor,
        label: t(STRENGTH_LABELS[floor]),
      })),
      chosen: value.leastStrength === null ? [] : [value.leastStrength],
      multiple: false,
      pick: (one) =>
        onChange({
          ...value,
          leastStrength: STRENGTH_FLOORS.find((floor) => floor === one) ?? null,
        }),
      remove: () => onChange({ ...value, leastStrength: null }),
      fromHeld: false,
      full: false,
    },
    {
      key: 'pages',
      label: t('pages'),
      options: PAGE_FLOORS.filter((floor) => floor !== 0).map((floor) => ({
        value: String(floor),
        label: t(PAGE_LABELS[floor]),
      })),
      chosen: value.leastPages === 0 ? [] : [String(value.leastPages)],
      multiple: false,
      pick: (one) =>
        onChange({ ...value, leastPages: PAGE_FLOORS.find((floor) => String(floor) === one) ?? 0 }),
      remove: () => onChange({ ...value, leastPages: 0 }),
      fromHeld: false,
      full: false,
    },
  ];

  const picked = controls.flatMap((control) =>
    control.chosen.flatMap((one) => {
      const option = control.options.find((offered) => offered.value === one);

      return option === undefined ? [] : [{ control, option }];
    }),
  );

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {/*
        A row that runs off the side of a phone rather than wrapping onto four lines of it, so the
        list somebody came to read still begins above the fold. The negative margins let it scroll
        from edge to edge inside a card that has padding, which is what keeps the last button on
        screen from looking like the end of the row when it is not.

        The width is pinned rather than left to work itself out. Everything above this is a column
        laid out with flexbox, and an item of one is allowed to grow to whatever it holds unless it
        is told otherwise — so without this the row does not scroll at all: it simply makes the
        whole page wider than the phone it is being read on.
      */}
      <div className="-mx-5 w-[calc(100%+2.5rem)] overflow-x-auto px-5 sm:mx-0 sm:w-full sm:px-0">
        <div className="flex w-max gap-2 sm:w-auto sm:flex-wrap">
          {controls.map((control) => (
            <FilterButton
              key={control.key}
              label={control.label}
              options={control.options}
              chosen={control.chosen}
              multiple={control.multiple}
              onPick={control.pick}
              onOpen={control.fromHeld ? onWantOptions : undefined}
              waiting={control.fromHeld && waiting}
              full={control.full}
            />
          ))}
        </div>
      </div>

      {picked.length === 0 ? null : (
        <div className="flex flex-wrap gap-2">
          {picked.map(({ control, option }) => (
            <ChoicePill
              key={`${control.key}:${option.value}`}
              of={control.label}
              removeLabel={t('remove', { choice: `${control.label} ${option.label}` })}
              onRemove={() => control.remove(option.value)}
            >
              {option.label}
            </ChoicePill>
          ))}
        </div>
      )}
    </div>
  );
}

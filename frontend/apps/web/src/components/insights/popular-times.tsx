'use client';

import { useFormatter, useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { CELL, FIGURE, Figures } from '@/components/charts/figures';
import { writeShare } from '@/components/dashboard/ranked-list';
import { Card } from '@/components/ui/card';
import { A_MONDAY, weekdayName, writableDay } from '@/lib/analytics/all-time';
import { shareOf } from '@/lib/analytics/share';
import type { Insights } from '@/lib/api/schemas';
import { cn } from '@/lib/styling';

type Format = ReturnType<typeof useFormatter>;

/** Stands in for a day or a time that nothing read can name. */
const ABSENT = '—';

/** Every day of the week, as a place in the answer: Monday first. */
const WEEKDAYS = [0, 1, 2, 3, 4, 5, 6];

/** Every hour of the day, as a place in the answer: midnight first. */
const HOURS = Array.from({ length: 24 }, (_, hour) => hour);

/** The hours named under the picture of the day, which has no room to name them all. */
const NAMED_HOURS: ReadonlySet<number> = new Set([0, 6, 12, 18]);

const HOUR = 3_600_000;
const FIFTY_NINE_MINUTES = 59 * 60_000;

/** The card's own spacing. */
const CARD = 'flex flex-col gap-4 p-5 sm:p-6';

/** Where the card's records sit. */
const TILES = 'grid gap-3 md:grid-cols-2';

/** A record's own box. */
const TILE = 'rounded-md border border-border bg-surface-muted/60 p-4';

/** What the stand-ins for one run of figures are laid out from. */
interface Typical {
  /** A place whose name is as long as the busiest's usually is. */
  readonly place: number;
  /** How many places usually share the whole between them, which is how long a share is written. */
  readonly shared: number;
}

/** A busiest day and a busiest hour as they usually read. */
const TYPICAL_WEEKDAY: Typical = { place: 2, shared: 5 };
const TYPICAL_HOUR: Typical = { place: 14, shared: 11 };

/**
 * A best day ever of typical length when written out, so that the stand-in laid out from it is right
 * for most days. It is about the middle of how wide a day is written and takes one line from a phone
 * 360 wide up, where one of the longest would hold two lines open for a day that usually takes one.
 */
const TYPICAL_BEST_DAY = '2026-08-18';

/** As many page views as a figure usually runs to, for a stand-in to be laid out from. */
const TYPICAL_VIEWS = 100;

/** A week or a day as the card draws it. */
interface Stretch {
  /** Every place along it, in order. */
  readonly places: readonly number[];
  /** What a place is called in full. */
  readonly called: (place: number) => string;
  /** The mark under a place on the strip, or nothing where it has none. */
  readonly mark: (place: number) => string | null;
  /** Whether each mark sits under the middle of its bar rather than from its left edge. */
  readonly centred?: boolean;
  /** What stands in for its figures is laid out from this. */
  readonly typical: Typical;
}

/** One run of figures: what each place along a stretch held, and every place sharing the most. */
interface Run {
  readonly figures: readonly number[];
  readonly busiest: readonly number[];
}

/**
 * An hour of the day written as the minutes it runs over: `2:00 – 2:59 PM`. From the hour to its last
 * minute rather than to the next hour, which written from eleven at night would be a stretch across
 * two dates.
 */
function hourCalled(hour: number, format: Format): string {
  const begins = A_MONDAY + hour * HOUR;

  return format.dateTimeRange(new Date(begins), new Date(begins + FIFTY_NINE_MINUTES), {
    hour: 'numeric',
    minute: '2-digit',
    timeZone: 'UTC',
  });
}

/** An hour of the day as a mark on the picture of the day: `6 AM`. */
function hourMark(hour: number, format: Format): string {
  return format.dateTime(new Date(A_MONDAY + hour * HOUR), { hour: 'numeric', timeZone: 'UTC' });
}

/** A day on the website's calendar, written out in full. */
function dayInFull(day: string, format: Format): string {
  return format.dateTime(writableDay(day), { dateStyle: 'full', timeZone: 'UTC' });
}

/** Everything a run of figures comes to, which each of its shares is taken against. */
function sumOf(figures: readonly number[]): number {
  return figures.reduce((sum, figure) => sum + figure, 0);
}

interface PopularTimesProps {
  /** What the record says, or nothing while it is on its way. */
  readonly insights: Insights | undefined;
  /** The website's zone, as it is named to the reader, or nothing while the website is not known. */
  readonly zone: string | undefined;
  /** Whether the figures are unfolded, which outlives the card. */
  readonly figuresOpen: boolean;
  readonly onFiguresToggle: (open: boolean) => void;
}

/**
 * When a website is read most: its best day ever, and the day of the week and the hour with the most
 * page views. Where several share the most, all of them are named, because choosing one would say
 * something the figures do not. Each share is of its own run of figures, so a run adds up to the whole.
 *
 * One card whether or not its figures are in. Its headings, its tiles, the line that unfolds its
 * figures and the names of their rows stand through the wait, and each figure is a typical one written
 * out and hidden until the real one lands in its place. So the card below it stays where it is when
 * they land, save a line where a name turns out to wrap where the typical one did not, and whatever
 * somebody is on is still there under them: the line that unfolds the figures already works, and what
 * it is left at is how the figures arrive.
 */
export function PopularTimes({ insights, zone, figuresOpen, onFiguresToggle }: PopularTimesProps) {
  const t = useTranslations('insights.times');
  const format = useFormatter();
  const week: Stretch = {
    places: WEEKDAYS,
    called: (place) => weekdayName(place, 'long', format),
    mark: (place) => weekdayName(place, 'narrow', format),
    centred: true,
    typical: TYPICAL_WEEKDAY,
  };
  const day: Stretch = {
    places: HOURS,
    called: (hour) => hourCalled(hour, format),
    mark: (hour) => (NAMED_HOURS.has(hour) ? hourMark(hour, format) : null),
    typical: TYPICAL_HOUR,
  };

  return (
    <Card className={CARD}>
      <TimesHeading zone={zone} />

      <div className={TILES}>
        <BestDay insights={insights} />
        <Busiest
          label={t('weekday')}
          stretch={week}
          run={insights && { figures: insights.weekdays, busiest: insights.busiestWeekdays }}
        />
        <Busiest
          label={t('hour')}
          stretch={day}
          run={insights && { figures: insights.hours, busiest: insights.busiestHours }}
        />
      </div>

      <Figures
        label={t('table')}
        open={figuresOpen}
        onToggle={onFiguresToggle}
        uncapped
        waiting={insights === undefined}
      >
        <thead>
          <tr className="border-b border-border text-xs text-foreground-muted">
            <th scope="col" className={`${CELL} font-medium`}>
              {t('column.when')}
            </th>
            <th scope="col" className={`${FIGURE} font-medium`}>
              {t('column.views')}
            </th>
            <th scope="col" className={`${FIGURE} font-medium`}>
              {t('column.share')}
            </th>
          </tr>
        </thead>
        <FigureRows title={t('weekdays')} stretch={week} figures={insights?.weekdays} />
        <FigureRows title={t('hours')} stretch={day} figures={insights?.hours} />
      </Figures>
    </Card>
  );
}

/**
 * A figure on its way, as a bar where it will be: the words it stands in for are written out and
 * hidden, so it takes up the room they will.
 */
function StandIn({ words }: { readonly words: string }) {
  return (
    <span className="relative inline-block max-w-full">
      <span className="invisible">{words}</span>
      <span className="absolute inset-x-0 inset-y-1 animate-pulse rounded-sm bg-border/70" />
    </span>
  );
}

/** What the card is about, and the clock its days and hours are counted on. */
function TimesHeading({ zone }: { readonly zone: string | undefined }) {
  const t = useTranslations('insights.times');

  return (
    <header className="flex flex-col gap-0.5">
      <h2 className="text-base font-semibold text-foreground">{t('title')}</h2>
      <p className="text-sm text-foreground-muted">
        {zone === undefined ? <StandIn words={t('zone', { zone: '' })} /> : t('zone', { zone })}
      </p>
    </header>
  );
}

/**
 * The best day ever and how much was read on it; a mark where nothing read can name one; or, while it
 * is on its way, a day of typical length and as many page views standing in for it.
 */
function BestDay({ insights }: { readonly insights: Insights | undefined }) {
  const pages = useTranslations('dashboard.pages');
  const format = useFormatter();

  if (insights === undefined) {
    return (
      <BestDayTile
        day={<StandIn words={dayInFull(TYPICAL_BEST_DAY, format)} />}
        views={<StandIn words={pages('total', { count: TYPICAL_VIEWS })} />}
      />
    );
  }

  const { bestDay } = insights;

  return (
    <BestDayTile
      day={bestDay ? dayInFull(bestDay.day, format) : ABSENT}
      views={bestDay && pages('total', { count: bestDay.pageViews })}
    />
  );
}

interface BestDayTileProps {
  /** The day, or what stands in for it. */
  readonly day: ReactNode;
  /** How much was read on it, or nothing where nothing read can name a day. */
  readonly views: ReactNode;
}

/** The best day ever, across the card: it has no strip beneath it to fill a tile with. */
function BestDayTile({ day, views }: BestDayTileProps) {
  const t = useTranslations('insights.times');

  return (
    <div
      className={cn(
        TILE,
        'flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 md:col-span-2',
      )}
    >
      <div className="flex min-w-0 flex-col gap-1">
        <h3 className="text-sm font-medium text-foreground-muted">{t('bestDay')}</h3>
        <p className="text-xl font-semibold tracking-tight text-foreground">{day}</p>
      </div>
      {views ? <p className="text-sm text-foreground-muted tabular-nums">{views}</p> : null}
    </div>
  );
}

interface BusiestProps {
  readonly label: string;
  /** The week or the day it is the busiest of. */
  readonly stretch: Stretch;
  /** Its figures, or nothing while they are on their way. */
  readonly run: Run | undefined;
}

/**
 * The busiest of a week or of a day, with the whole of it drawn beneath; or, while its figures are on
 * their way, the same tile laid out from a typical one.
 */
function Busiest({ label, stretch, run }: BusiestProps) {
  const t = useTranslations('insights.times');
  const format = useFormatter();
  const { places, called, mark, centred, typical } = stretch;

  if (run === undefined) {
    return (
      <RecordTile
        label={label}
        value={<StandIn words={called(typical.place)} />}
        detail={<StandIn words={t('share', { share: writeShare(1, typical.shared, format) })} />}
      >
        <StripWaiting places={places} mark={mark} centred={centred} />
      </RecordTile>
    );
  }

  return (
    <RecordTile
      label={label}
      {...namedTogether(run.busiest.map(called), format)}
      detail={shareOfTheMost(run, t, format)}
    >
      <ShareStrip
        places={places}
        figures={run.figures}
        busiest={run.busiest}
        called={called}
        mark={mark}
        centred={centred}
      />
    </RecordTile>
  );
}

/** Names together longer than this are set smaller, so several sharing the most still fit their tile. */
const LONG_NAMES = 30;

/**
 * Names joined as the reader's language joins a list, each kept whole so that several wrap between the
 * names and never inside one, and whether they run long together; or nothing where there are none.
 *
 * @param names Every place sharing the most.
 * @param format The reader's own formatter.
 */
function namedTogether(
  names: readonly string[],
  format: Format,
): Pick<RecordTileProps, 'value' | 'long'> {
  if (names.length === 0) {
    return { value: null };
  }

  return {
    value: format.list(
      names.map((name) => (
        <span key={name} className="whitespace-nowrap">
          {name}
        </span>
      )),
      { type: 'conjunction' },
    ),
    long: format.list(names, { type: 'conjunction' }).length > LONG_NAMES,
  };
}

/**
 * How much of a run the busiest place in it holds, or of several sharing the most, each of them; or
 * nothing where nothing was read.
 *
 * @param run The whole run of figures, and every place in it sharing the most.
 * @param t The words of this card.
 * @param format The reader's own formatter.
 */
function shareOfTheMost(
  { figures, busiest }: Run,
  t: ReturnType<typeof useTranslations<'insights.times'>>,
  format: Format,
): string | null {
  const first = busiest[0];

  if (first === undefined) {
    return null;
  }

  return t(busiest.length > 1 ? 'shareEach' : 'share', {
    share: writeShare(figures[first] ?? 0, sumOf(figures), format),
  });
}

interface RecordTileProps {
  readonly label: string;
  /** What it names, or nothing where nothing read can name it. */
  readonly value: ReactNode;
  /** Whether that is long enough to be set smaller. */
  readonly long?: boolean;
  /** How much it holds, or nothing where nothing read can say. */
  readonly detail: ReactNode;
  readonly children: ReactNode;
}

/** One record: what it is, what holds it, and how much it holds. */
function RecordTile({ label, value, long = false, detail, children }: RecordTileProps) {
  return (
    <div className={cn(TILE, 'flex min-w-0 flex-col gap-1')}>
      <h3 className="text-sm font-medium text-foreground-muted">{label}</h3>
      <p
        className={cn(
          'font-semibold tracking-tight text-foreground',
          long ? 'text-base' : 'text-xl',
        )}
      >
        {value ?? ABSENT}
      </p>
      <p className="text-sm text-foreground-muted">{detail}</p>
      {children}
    </div>
  );
}

interface StripMarksProps {
  /** Every place along the strip, in order. */
  readonly places: readonly number[];
  /** The mark under a place, or nothing where it has none. */
  readonly mark: (place: number) => string | null;
  /** Whether each mark sits under the middle of its bar rather than from its left edge. */
  readonly centred?: boolean;
}

/** What runs along under a strip: the days of the week, or a few of the hours of the day. */
function StripMarks({ places, mark, centred = false }: StripMarksProps) {
  return (
    <div className="flex gap-0.5 text-[11px] text-foreground-muted">
      {places.map((place) => (
        <span
          key={place}
          className={cn('min-w-0 flex-1 whitespace-nowrap', centred && 'text-center')}
        >
          {mark(place)}
        </span>
      ))}
    </div>
  );
}

/** A strip while its figures are on their way, with its marks as they will be. */
function StripWaiting(props: StripMarksProps) {
  return (
    <div aria-hidden className="mt-auto flex flex-col gap-1 pt-3">
      <div className="h-12 animate-pulse rounded-sm bg-border/70" />
      <StripMarks {...props} />
    </div>
  );
}

interface ShareStripProps extends StripMarksProps {
  /** The figure for each place. */
  readonly figures: readonly number[];
  /** Every place sharing the most, which alone wear the accent. */
  readonly busiest: readonly number[];
  /** What a place is called in full, for whoever points at its bar. */
  readonly called: (place: number) => string;
}

/**
 * A run of figures drawn as a row of bars, so the shape of a week or a day reads at a glance, each
 * column naming its figure to whoever points anywhere along it. Hidden from anybody reading rather
 * than looking: the same figures are published in the table below. In forced colours the bars are
 * given colours of the system's own, because a background of any other kind is painted out.
 */
function ShareStrip({ places, figures, busiest, called, mark, centred }: ShareStripProps) {
  const t = useTranslations('insights.times');
  const most = Math.max(...figures);

  return (
    <div aria-hidden className="mt-auto flex flex-col gap-1 pt-3">
      <div className="flex h-12 gap-0.5">
        {places.map((place) => {
          const figure = figures[place] ?? 0;

          return (
            <span
              key={place}
              title={t('bar', { when: called(place), count: figure })}
              className="flex min-w-0 flex-1 items-end"
            >
              <span
                className={cn(
                  'min-h-0.5 w-full rounded-t-[2px]',
                  busiest.includes(place)
                    ? 'bg-chart-1 forced-colors:bg-[Highlight]'
                    : 'bg-border-strong forced-colors:bg-[CanvasText]',
                )}
                style={{ height: `${shareOf(figure, most) * 100}%` }}
              />
            </span>
          );
        })}
      </div>
      <StripMarks places={places} mark={mark} centred={centred} />
    </div>
  );
}

interface FigureRowsProps {
  readonly title: string;
  /** The week or the day the rows run along. */
  readonly stretch: Stretch;
  /** What each place held, or nothing while the figures are on their way. */
  readonly figures: readonly number[] | undefined;
}

/**
 * One run of figures as rows of the table: what each place held, and its share of the whole. The rows
 * and their names stand before the figures do, with a typical figure written out and hidden where
 * each will be.
 */
function FigureRows({ title, stretch, figures }: FigureRowsProps) {
  const format = useFormatter();
  const whole = figures === undefined ? 0 : sumOf(figures);

  return (
    <tbody>
      <tr>
        <th
          scope="rowgroup"
          colSpan={3}
          className={`${CELL} pt-3 text-xs font-medium text-foreground-muted`}
        >
          {title}
        </th>
      </tr>
      {stretch.places.map((place) => (
        <tr key={place} className="border-b border-border/60 last:border-0">
          <th scope="row" className={`${CELL} font-normal text-foreground`}>
            {stretch.called(place)}
          </th>
          <td className={`${FIGURE} text-foreground-muted`}>
            {figures === undefined ? (
              <StandIn words={format.number(TYPICAL_VIEWS)} />
            ) : (
              format.number(figures[place] ?? 0)
            )}
          </td>
          <td className={`${FIGURE} text-foreground-muted`}>
            {figures === undefined ? (
              <StandIn words={writeShare(1, stretch.typical.shared, format)} />
            ) : (
              writeShare(figures[place] ?? 0, whole, format)
            )}
          </td>
        </tr>
      ))}
    </tbody>
  );
}

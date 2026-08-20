'use client';

import { useFormatter, useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useMemo } from 'react';
import { PlaceCredit } from '@/components/dashboard/place-credit';
import { splitDuration } from '@/lib/analytics/duration';
import { readablePath } from '@/lib/analytics/pages';
import { countryNames } from '@/lib/analytics/places';
import type { ControlKind, VisitContext, VisitJourneyStep, VisitPress } from '@/lib/api/schemas';
import { useVisitJourney } from '@/lib/queries/sites';

interface VisitJourneyProps {
  readonly siteId: string;
  /** Which visit, as the list names it. */
  readonly visit: string;
  /** How many pages the visit asked for, so a journey cut short can say so. */
  readonly pageCount: number;
  /** The site's own zone, so a step is stamped with the time it happened where the site is. */
  readonly timeZoneId: string;
  /** Whether the visit has been opened. Nothing is asked for until it has. */
  readonly open: boolean;
}

/**
 * What one visit did, in the order it did it.
 *
 * The concrete half of a verdict. "Nine pages, judged a security scanner" is a conclusion somebody
 * has to take on trust; nine addresses that do not exist, asked for in a third of a second, is the
 * same conclusion they can reach themselves.
 *
 * Asked for only once the visit is opened. A screenful is twenty-five visits, and reading every
 * journey nobody has asked to see would be twenty-five questions of the store for one screen.
 */
export function VisitJourney({ siteId, visit, pageCount, timeZoneId, open }: VisitJourneyProps) {
  const t = useTranslations('dashboard.journey');
  const journey = useVisitJourney(siteId, visit, open);
  const steps = journey.data?.steps;

  return (
    <>
      {journey.data === undefined ? null : <Whose context={journey.data.context} />}

      <section className="flex flex-col gap-2">
        <h3 className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
          {t('title')}
        </h3>

        <Trail
          steps={steps}
          failed={journey.isError}
          pageCount={pageCount}
          timeZoneId={timeZoneId}
        />
      </section>
    </>
  );
}

/**
 * Who the visit was, before what it did.
 *
 * A list of pages and a verdict between them say what happened and what the engine made of it.
 * Neither says whether the reader came from a search, roughly where they were, or what they were
 * reading on — and those three are what turn a row on a list into somebody a site's owner can
 * picture. So they go above the trail rather than beside the numbers.
 *
 * Only what was established is shown. Four facts reading "not known" is what a screen looks like
 * when it is describing its own gaps instead of the visit, so a fact nothing answered is left out
 * and the section says once, quietly, that the rest went unobserved.
 */
function Whose({ context }: { readonly context: VisitContext }) {
  const t = useTranslations('dashboard.journey.about');
  const locale = useLocale();
  const named = useMemo(() => countryNames(locale), [locale]);

  const place = placeOf(context, named(context.countryCode));
  const read = readOn(context, t);

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-xs font-medium tracking-wide text-foreground-muted uppercase">
        {t('title')}
      </h3>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-4">
        {/*
          What kind of thing a site is goes under its name, because a name alone leaves the reader
          to know which of them are search engines — which is the whole reason the catalogue
          exists. An arrival that named nowhere is already written as what it is, and saying it a
          second time underneath would spend a line agreeing with itself.
        */}
        <Fact label={t('from')} value={context.source === '' ? t('direct') : context.source}>
          {context.kind === 'direct' ? undefined : t(`kind.${context.kind}`)}
        </Fact>

        {place === null ? null : <Fact label={t('place')} value={place} />}
        {read === null ? null : <Fact label={t('readOn')} value={read} />}
        {context.network === '' ? null : <Fact label={t('network')} value={context.network} />}
      </dl>

      {/*
        The licence behind every country and town this product shows asks for a link back wherever
        its results appear, and one visit showing one town is exactly that. It is here rather than
        under the whole card so that it appears with the data and not without it.
      */}
      {place === null ? null : <PlaceCredit note={t('estimate')} />}

      {place === null && read === null ? (
        <p className="text-sm text-foreground-muted">{t('unobserved')}</p>
      ) : null}
    </section>
  );
}

interface FactProps {
  readonly label: string;
  /** Written by whoever visited, so it reaches the screen as text and never as a link. */
  readonly value: string;
  /** A quieter word under it, where one says something the value does not. */
  readonly children?: ReactNode;
}

function Fact({ label, value, children }: FactProps) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="text-xs text-foreground-subtle">{label}</dt>
      <dd className="flex min-w-0 flex-col">
        <bdi className="truncate text-sm text-foreground" title={value}>
          {value}
        </bdi>
        {children === undefined ? null : (
          <span className="truncate text-xs text-foreground-muted">{children}</span>
        )}
      </dd>
    </div>
  );
}

/**
 * Roughly where the visit was, where anything placed it.
 *
 * A town is worth showing only with its country beside it: a great many town names belong to more
 * than one, and a reader shown "Springfield" alone has been told less than they think.
 */
function placeOf(context: VisitContext, country: string | null): string | null {
  if (country === null) {
    return null;
  }

  return context.town === '' ? country : `${context.town}, ${country}`;
}

/**
 * What the visit was read on.
 *
 * The software when anything named it, since "Chrome on Android" says more than "a phone" does.
 * The kind of device is the fallback rather than an addition — a row carrying both would spend a
 * line saying twice what one of them already said.
 */
function readOn(context: VisitContext, t: ReturnType<typeof useTranslations>): string | null {
  if (context.browser !== '' && context.system !== '') {
    return t('on', { browser: context.browser, system: context.system });
  }

  if (context.browser !== '' || context.system !== '') {
    return context.browser || context.system;
  }

  return context.device === 'unknown' ? null : t(`device.${context.device}`);
}

interface TrailProps {
  readonly steps: readonly VisitJourneyStep[] | undefined;
  readonly failed: boolean;
  readonly pageCount: number;
  readonly timeZoneId: string;
}

function Trail({ steps, failed, pageCount, timeZoneId }: TrailProps) {
  const t = useTranslations('dashboard.journey');

  if (failed) {
    return <p className="text-sm text-foreground-muted">{t('failed')}</p>;
  }

  if (!steps) {
    return <div className="h-16 animate-pulse rounded-md bg-surface-muted" />;
  }

  if (steps.length === 0) {
    return <p className="text-sm text-foreground-muted">{t('empty')}</p>;
  }

  // Said once rather than beside every step. A visit nothing ran on has a page against every
  // address, and repeating "not measured" down the whole list turns one honest fact into noise.
  const watched = steps.some((step) => step.engagedMs !== null);
  const arrivals = steps.filter((step) => step.press === null).length;

  return (
    <>
      {watched ? null : <p className="text-sm text-foreground-muted">{t('unwatched')}</p>}

      <ol className="flex flex-col">
        {steps.map((step, index) => (
          <Step
            key={`${step.at}:${step.path}:${index}`}
            step={step}
            watched={watched}
            timeZoneId={timeZoneId}
            last={index === steps.length - 1}
          />
        ))}
      </ol>

      {arrivals < pageCount ? (
        <p className="text-xs text-foreground-subtle">
          {t('shortened', { shown: arrivals, total: pageCount })}
        </p>
      ) : null}
    </>
  );
}

interface StepProps {
  readonly step: VisitJourneyStep;
  /** Whether anything in this visit was measured at all. */
  readonly watched: boolean;
  readonly timeZoneId: string;
  readonly last: boolean;
}

function Step({ step, watched, timeZoneId, last }: StepProps) {
  return (
    <li className="relative flex gap-3 pb-3 last:pb-0">
      {last ? null : (
        <span aria-hidden className="absolute top-3 bottom-0 left-[3px] w-px bg-border" />
      )}

      {step.press ? (
        <Pressed press={step.press} at={step.at} timeZoneId={timeZoneId} />
      ) : (
        <Arrived step={step} watched={watched} timeZoneId={timeZoneId} />
      )}
    </li>
  );
}

/** A page the visit arrived at. */
function Arrived({ step, watched, timeZoneId }: Omit<StepProps, 'last'>) {
  const t = useTranslations('dashboard.journey');

  // Written by whoever asked for the page, so it is shown as text and never followed.
  const address = readablePath(step.path);
  const trouble = step.statusCode === null ? null : troubleWith(step.statusCode);

  return (
    <>
      <span
        aria-hidden
        className={`mt-1.5 size-[7px] shrink-0 rounded-full ${trouble ? 'bg-accent-strong' : 'bg-accent'}`}
      />

      <div className="flex min-w-0 flex-1 flex-col gap-0.5 sm:flex-row sm:items-baseline sm:justify-between sm:gap-4">
        <span className="flex min-w-0 items-center gap-2">
          <bdi className="truncate text-sm text-foreground" title={address}>
            {address}
          </bdi>
          {trouble ? (
            <span className="shrink-0 rounded-full bg-accent-soft px-2 py-0.5 text-xs text-accent-strong">
              {t(`status.${trouble}`)}
            </span>
          ) : null}
        </span>

        <span className="shrink-0 text-xs text-foreground-muted tabular-nums">
          {watched ? <Reading step={step} /> : null}
          <Stamp at={step.at} timeZoneId={timeZoneId} />
        </span>
      </div>
    </>
  );
}

interface PressedProps {
  readonly press: VisitPress;
  readonly at: string;
  readonly timeZoneId: string;
}

/**
 * A control the visitor operated on the page above.
 *
 * Marked with a hollow point rather than a filled one, so a glance down the rail separates the
 * pages somebody went to from the things they did on the way. What the control said is the site's
 * own writing, so it reaches the screen as text and nothing else.
 */
function Pressed({ press, at, timeZoneId }: PressedProps) {
  const t = useTranslations('dashboard.journey');

  return (
    <>
      <span
        aria-hidden
        className="mt-1.5 size-[7px] shrink-0 rounded-full border border-accent bg-surface"
      />

      <div className="flex min-w-0 flex-1 flex-col gap-0.5 sm:flex-row sm:items-baseline sm:justify-between sm:gap-4">
        <span className="flex min-w-0 items-baseline gap-2">
          <bdi className="truncate text-sm text-foreground-muted">
            {press.name ? t('pressed', { name: press.name }) : t('pressedUnnamed')}
          </bdi>
          <span className="shrink-0 text-xs text-foreground-subtle">
            {t(controlWord(press.control))}
          </span>
        </span>

        <span className="shrink-0 text-xs text-foreground-muted tabular-nums">
          <Led press={press} />
          <Stamp at={at} timeZoneId={timeZoneId} />
        </span>
      </div>
    </>
  );
}

/** Where a press led, where that is worth saying. */
function Led({ press }: { readonly press: VisitPress }) {
  const t = useTranslations('dashboard.journey');

  if (press.targetKind === 'contact') {
    return (
      <>
        <span>{t('toContact')}</span>
        <span aria-hidden> · </span>
      </>
    );
  }

  // Somewhere on the site is left unsaid: the page it led to is the next step down the rail.
  if (press.targetKind !== 'external' || !press.target) {
    return null;
  }

  return (
    <>
      <bdi>{t('toHost', { host: press.target })}</bdi>
      <span aria-hidden> · </span>
    </>
  );
}

/** When a step happened, where the website is. */
function Stamp({ at, timeZoneId }: { readonly at: string; readonly timeZoneId: string }) {
  const format = useFormatter();

  return (
    <time dateTime={at}>
      {format.dateTime(new Date(at), {
        timeZone: timeZoneId,
        hour: 'numeric',
        minute: '2-digit',
        second: '2-digit',
      })}
    </time>
  );
}

/**
 * Which word describes a kind of control.
 *
 * Looked up through a fixed map rather than by building a key from the answer, so a value this
 * dashboard has never seen reaches the catalogue as a word it holds instead of as a missing one.
 */
function controlWord(control: ControlKind): string {
  return CONTROL_WORDS[control] ?? CONTROL_WORDS.unknown;
}

const CONTROL_WORDS: Readonly<Record<ControlKind, string>> = {
  link: 'control.link',
  button: 'control.button',
  field: 'control.field',
  unknown: 'control.unknown',
};

/**
 * How the page was read, where anything watched it being read.
 *
 * Each fact keeps its own element and the separators between them are hidden from anyone reading
 * rather than looking, so a screen reader announces three readings instead of one run-on sentence
 * punctuated by middots.
 */
function Reading({ step }: { readonly step: VisitJourneyStep }) {
  const t = useTranslations('dashboard.journey');
  const duration = useTranslations('dashboard.duration');

  if (step.engagedMs === null) {
    return (
      <>
        <span>{t('unmeasured')}</span>
        <span aria-hidden> · </span>
      </>
    );
  }

  const { minutes, seconds } = splitDuration(step.engagedMs);

  return (
    <>
      <span>
        {minutes > 0 ? duration('long', { minutes, seconds }) : duration('short', { seconds })}
      </span>
      <span aria-hidden> · </span>
      {step.depthPercent === null ? null : (
        <>
          <span>{t('down', { percent: step.depthPercent })}</span>
          <span aria-hidden> · </span>
        </>
      )}
    </>
  );
}

/**
 * What went wrong with a request, where anything did.
 *
 * A page that was delivered says so by saying nothing: a badge reading "200" beside every address
 * is an engineer describing the protocol. A page that was not delivered is a fact somebody wants,
 * and is most of what a stream of them says about the visitor.
 */
function troubleWith(status: number): string | null {
  if (status < 300) {
    return null;
  }

  if (status < 400) {
    return 'redirected';
  }

  if (status === 404 || status === 410) {
    return 'missing';
  }

  if (status < 500) {
    return 'refused';
  }

  return 'failed';
}

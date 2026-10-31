'use client';

import { ChevronDown } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type KeyboardEvent, type Ref, useId, useMemo, useState } from 'react';
import { CustomPeriod } from '@/components/dashboard/custom-period';
import {
  type DaySpan,
  isAllTime,
  isPreset,
  namedPeriod,
  type Period,
  type PeriodPreset,
  spanFor,
  spanInstants,
} from '@/lib/analytics/period';

interface PeriodPickerProps {
  readonly value: Period;
  readonly onChange: (period: Period) => void;
  /** The site's own zone, since a period is a run of days there rather than where the reader is. */
  readonly timeZoneId: string;
  /**
   * The control itself, for a screen that has to hand the reader back to it.
   *
   * It is the one thing on the screen that always names the period, so it is where the reading
   * position goes after the period is changed by something that has gone with the change.
   */
  readonly ref?: Ref<HTMLSelectElement>;
  /**
   * The first day of the website's record, for a screen whose figures reach back to it under all
   * time — or nothing while that is not known.
   *
   * Left out by a screen that reads only the days still held one by one, which is then shown the
   * days it covers, as under every other period.
   */
  readonly since?: string | null;
  /**
   * Whether the website has been asked about and nothing of it is held — never measured, or measured
   * only in months that went before any of them was kept — so all time has no first day to name and
   * nothing is printed under it.
   */
  readonly nothingHeld?: boolean;
  /**
   * Told when somebody reaches for the control — presses it, works it from the keyboard, or opens
   * the chooser — so a screen can ask for what the next period will need before it is chosen,
   * rather than asking on every screen nobody touches.
   */
  readonly onReach?: () => void;
}

/** A heading in the list, and the periods filed under it. */
interface PeriodGroup {
  readonly name: 'recent' | 'calendar';
  readonly presets: readonly PeriodPreset[];
}

/**
 * The named periods, grouped the way somebody looks for them.
 *
 * A run of days ending today is a different question from a calendar month, and mixing the two
 * into one list of nine makes both harder to find. All time is the last of the calendar periods,
 * the one every other is part of. Every named period belongs to exactly one group, which a test
 * holds to.
 */
const GROUPS: readonly PeriodGroup[] = [
  {
    name: 'recent',
    presets: ['today', 'yesterday', 'last-7-days', 'last-30-days', 'last-90-days'],
  },
  { name: 'calendar', presets: ['this-month', 'last-month', 'this-year', 'all-time'] },
];

/**
 * Keys that open the list, or move through it, with or without a modifier held: on one platform or
 * another each opens a closed list with one — Alt, Shift or ⌘ with the up and down arrows, Shift
 * with Space, Shift with Enter, Shift or ⌘ with F4.
 */
const OPENING_KEYS: ReadonlySet<string> = new Set(['ArrowUp', 'ArrowDown', ' ', 'Enter', 'F4']);

/**
 * Keys that move through the list, counted only when pressed on their own. With a modifier held they
 * are mostly the browser's — Alt or ⌘ with the left and right arrows go back and forward, Alt with
 * Home goes home, Ctrl with Page Up or Page Down changes tab — though some browsers still move a
 * closed list with Shift, or with Ctrl or ⌘ on the arrows, Home and End. A press of that kind that
 * does change the period asks for what the new period needs itself, so leaving it uncounted only
 * means the record is asked for a moment later.
 */
const MOVING_KEYS: ReadonlySet<string> = new Set([
  'ArrowLeft',
  'ArrowRight',
  'Home',
  'End',
  'PageUp',
  'PageDown',
]);

/**
 * Whether a key pressed on the control works it, rather than moving focus away from it or calling
 * on the browser: a key that opens the list, whatever is held with it; a key that moves through it,
 * pressed on its own; or a single character typed with no Ctrl, Alt or ⌘ held, which picks the
 * period whose name begins with it. Tab, F6, Escape, Ctrl+L, Alt+D, Ctrl+F and a modifier on its own
 * work nothing in it, whatever they are on their way to; a moving key with a modifier held is left
 * uncounted too, for the reason the moving keys give.
 *
 * @param event The key pressed.
 */
function works(event: KeyboardEvent<HTMLSelectElement>): boolean {
  const { key, altKey, ctrlKey, metaKey, shiftKey } = event;

  if (OPENING_KEYS.has(key)) {
    return true;
  }

  if (MOVING_KEYS.has(key)) {
    return !altKey && !ctrlKey && !metaKey && !shiftKey;
  }

  return key.length === 1 && !ctrlKey && !altKey && !metaKey;
}

/** Choosing this opens the chooser. Not a period, and no named period can ever spell it. */
const CHOOSE = 'choose';

/** The stretch already chosen, so that it is what the closed list reads as. */
const CHOSEN = 'chosen';

/**
 * How far back the screen is looking.
 *
 * The browser's own list rather than one built here, for the reasons the website switch already
 * records: it opens correctly on a phone, and it is reachable by keyboard and by voice without
 * anything being wired up.
 *
 * The dates a choice works out to are printed beneath it. That is the one thing a list of names
 * cannot show, and the difference between "Last 30 days" and knowing whether today is in it. All
 * time is printed as the day it began, on a screen whose figures reach back that far — and as
 * nothing at all where nothing of the website is held, so its record has not begun. The line keeps
 * its height either way, so nothing beside or below it moves when the day it names arrives.
 *
 * The chooser's refusal of days older than those still kept offers all time instead only on a screen
 * whose all time reaches further back than they do.
 *
 * A stretch somebody chose sits in the list as its own entry beside the way back into the chooser,
 * rather than as the chooser itself wearing its answer. A list gives nothing back when the entry
 * already showing is picked again, so a single entry that was both would be one nobody could
 * reopen to correct a date.
 *
 * A stretch chosen in the chooser that is exactly today, yesterday, this month, last month or this
 * year where the website is comes back as that named period, a single day being tried first, so the
 * same days typed in and pressed on the picture are one period under one name. It is settled here
 * rather than by each screen because the journeys screen offers the same control, and a day would
 * otherwise be "Yesterday" on one screen and a stretch of somebody's own choosing on the other. A
 * period handed in is shown as it is: a link naming a date opens on that date.
 */
export function PeriodPicker({
  value,
  onChange,
  timeZoneId,
  ref,
  since,
  nothingHeld = false,
  onReach,
}: PeriodPickerProps) {
  const t = useTranslations('dashboard.period');
  const coveredId = useId();
  const [choosing, setChoosing] = useState(false);

  const span = useMemo(() => spanFor(value, timeZoneId, new Date()), [value, timeZoneId]);

  function pick(picked: string) {
    if (picked === CHOOSE) {
      onReach?.();
      setChoosing(true);
    } else if (isPreset(picked)) {
      onChange({ kind: 'preset', preset: picked });
    }
  }

  return (
    <div className="flex w-full min-w-0 flex-col gap-1 sm:w-auto sm:items-end">
      <span className="relative flex items-center">
        <select
          ref={ref}
          aria-label={t('label')}
          aria-describedby={coveredId}
          value={value.kind === 'preset' ? value.preset : CHOSEN}
          onChange={(event) => pick(event.target.value)}
          // Pressed or worked from the keyboard, never merely focused or passed through: the screen
          // hands focus here itself after a press on its picture, and somebody tabbing on through
          // the header, or off to the address bar, is going somewhere else — neither is a reach
          // for another period.
          onPointerDown={onReach}
          onKeyDown={(event) => {
            if (works(event)) {
              onReach?.();
            }
          }}
          className="select-trigger h-9 w-full cursor-pointer appearance-none truncate rounded-md border border-border bg-surface pr-8 pl-3 text-sm font-medium text-foreground sm:w-auto"
        >
          {GROUPS.map((group) => (
            <optgroup key={group.name} label={t(`groups.${group.name}`)}>
              {group.presets.map((preset) => (
                <option key={preset} value={preset}>
                  {t(`presets.${preset}`)}
                </option>
              ))}
            </optgroup>
          ))}
          <optgroup label={t('groups.own')}>
            {value.kind === 'chosen' ? <option value={CHOSEN}>{t('chosen')}</option> : null}
            <option value={CHOOSE}>{t('choose')}</option>
          </optgroup>
        </select>
        <ChevronDown
          aria-hidden
          className="pointer-events-none absolute right-2.5 size-4 text-foreground-muted"
        />
      </span>

      {/* Read out with the control as well as printed under it: "Last 30 days" and its dates are one answer. */}
      <p id={coveredId} className="min-h-4 truncate text-xs text-foreground-subtle sm:text-right">
        {isAllTime(value) && nothingHeld ? null : (
          <Covered
            span={span}
            timeZoneId={timeZoneId}
            since={isAllTime(value) ? since : undefined}
          />
        )}
      </p>

      <CustomPeriod
        open={choosing}
        onClose={() => setChoosing(false)}
        timeZoneId={timeZoneId}
        start={span}
        allTimeSince={since}
        onChoose={(chosen) => {
          setChoosing(false);
          onChange(namedPeriod(chosen, timeZoneId, new Date()));
        }}
      />
    </div>
  );
}

interface CoveredProps {
  /** The days the period covers. */
  readonly span: DaySpan;
  readonly timeZoneId: string;
  /**
   * The first day all time reaches back to, nothing while it is not known, or undefined where the
   * days the period covers are what is printed.
   */
  readonly since: string | null | undefined;
}

/**
 * What the period on screen covers, as the line under the control prints it: its days, the day all
 * time began, or — while that day is not known — that all time runs from the first visit.
 */
function Covered({ span, timeZoneId, since }: CoveredProps) {
  const t = useTranslations('dashboard.period');
  const format = useFormatter();
  const written = {
    timeZone: timeZoneId,
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  } as const;

  if (since === undefined) {
    const covered = spanInstants(span, timeZoneId);

    return format.dateTimeRange(covered.first, covered.last, written);
  }

  return since === null
    ? t('sinceFirst')
    : t('since', {
        date: format.dateTime(
          spanInstants({ first: since, last: since }, timeZoneId).first,
          written,
        ),
      });
}

'use client';

import { ChevronDown } from 'lucide-react';
import { useFormatter, useTranslations } from 'next-intl';
import { type Ref, useId, useMemo, useState } from 'react';
import { CustomPeriod } from '@/components/dashboard/custom-period';
import {
  isPreset,
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
 * into one list of eight makes both harder to find. Every named period belongs to exactly one
 * group, which a test holds to.
 */
const GROUPS: readonly PeriodGroup[] = [
  {
    name: 'recent',
    presets: ['today', 'yesterday', 'last-7-days', 'last-30-days', 'last-90-days'],
  },
  { name: 'calendar', presets: ['this-month', 'last-month', 'this-year'] },
];

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
 * cannot show, and the difference between "Last 30 days" and knowing whether today is in it.
 *
 * A stretch somebody chose sits in the list as its own entry beside the way back into the chooser,
 * rather than as the chooser itself wearing its answer. A list gives nothing back when the entry
 * already showing is picked again, so a single entry that was both would be one nobody could
 * reopen to correct a date.
 */
export function PeriodPicker({ value, onChange, timeZoneId, ref }: PeriodPickerProps) {
  const t = useTranslations('dashboard.period');
  const format = useFormatter();
  const [choosing, setChoosing] = useState(false);
  const coveredId = useId();

  const span = useMemo(() => spanFor(value, timeZoneId, new Date()), [value, timeZoneId]);
  const covered = useMemo(() => spanInstants(span, timeZoneId), [span, timeZoneId]);

  function pick(picked: string) {
    if (picked === CHOOSE) {
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
      <p id={coveredId} className="truncate text-xs text-foreground-subtle sm:text-right">
        {format.dateTimeRange(covered.first, covered.last, {
          timeZone: timeZoneId,
          day: 'numeric',
          month: 'short',
          year: 'numeric',
        })}
      </p>

      <CustomPeriod
        open={choosing}
        onClose={() => setChoosing(false)}
        timeZoneId={timeZoneId}
        start={span}
        onChoose={(chosen) => {
          setChoosing(false);
          onChange(chosen);
        }}
      />
    </div>
  );
}

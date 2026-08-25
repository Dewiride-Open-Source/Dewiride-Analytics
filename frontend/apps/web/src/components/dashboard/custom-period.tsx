'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { Field, TextInput } from '@/components/ui/field';
import { type DaySpan, daysIn, type Period, problemWith, todayIn } from '@/lib/analytics/period';

interface CustomPeriodProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /** The site's own zone, so that the last day offered is the site's today rather than the reader's. */
  readonly timeZoneId: string;
  /** The days the screen is on now, which the two boxes open on. */
  readonly start: DaySpan;
  readonly onChoose: (period: Period) => void;
}

/**
 * Choosing an exact stretch of days.
 *
 * Two date boxes and nothing else. They are the browser's own, which is what makes them a wheel on
 * a phone and a month grid on a desktop, in the reader's language and calendar, with none of that
 * written here — and what keeps a date library out of a product that has managed without one.
 *
 * It opens on the days already being looked at rather than empty. Somebody who wants the fortnight
 * around a spike is starting from the week they can see, and an empty pair of boxes makes them
 * work out today's date before they can begin.
 *
 * The refusals are a courtesy rather than a defence: a stretch that reached the engine anyway is
 * pulled into range first. They exist so that a mistake is answered while it is still being made.
 */
export function CustomPeriod({ open, onClose, timeZoneId, start, onChoose }: CustomPeriodProps) {
  const t = useTranslations('dashboard.period.chooser');
  const [opened, setOpened] = useState(open);
  const [first, setFirst] = useState(start.first);
  const [last, setLast] = useState(start.last);

  // Opening it puts the boxes back on the days the screen is showing. Left alone they would hold
  // whatever was abandoned last time, which is a stretch nobody asked for sitting behind an
  // ordinary-looking panel.
  if (opened !== open) {
    setOpened(open);

    if (open) {
      setFirst(start.first);
      setLast(start.last);
    }
  }

  const today = todayIn(timeZoneId, new Date());
  const problem = problemWith(first, last, today);

  // A half-filled pair of boxes is somebody part-way through, not somebody who got it wrong. The
  // refusal waits until there are two dates to disagree about.
  const refusal =
    problem === null || problem === 'unreadable' ? undefined : t(`problems.${problem}`);
  const length = problem === null ? daysIn({ first, last }) : null;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('title')}
      closeLabel={t('close')}
      className="max-w-lg"
    >
      <form
        className="flex flex-col gap-5"
        onSubmit={(event) => {
          event.preventDefault();
          onChoose({ kind: 'chosen', first, last });
        }}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('first')}>
            {(attributes) => (
              <TextInput
                {...attributes}
                type="date"
                value={first}
                max={today}
                onChange={(event) => setFirst(event.target.value)}
                data-opens-on
                required
              />
            )}
          </Field>

          <Field label={t('last')} problem={refusal}>
            {(attributes) => (
              <TextInput
                {...attributes}
                type="date"
                value={last}
                max={today}
                onChange={(event) => setLast(event.target.value)}
                required
              />
            )}
          </Field>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {length === null ? null : (
            <p className="text-sm text-foreground-muted">{t('length', { days: length })}</p>
          )}
          <Button type="submit" disabled={problem !== null} className="ml-auto w-full sm:w-auto">
            {t('apply')}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

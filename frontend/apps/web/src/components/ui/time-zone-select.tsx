import type { ComponentProps } from 'react';
import { SelectInput } from '@/components/ui/field';
import type { TimeZoneGroup } from '@/lib/time-zones';

interface TimeZoneSelectProps extends Omit<ComponentProps<'select'>, 'children' | 'defaultValue'> {
  /** Every zone on offer, or nothing while this browser has not yet been asked for them. */
  readonly zones: readonly TimeZoneGroup[] | null;
  /**
   * Where the picker starts when nothing on the form keeps its value, worked out from the zones
   * once there are some.
   *
   * Asked for as a rule rather than as an answer, because the answer can only be worked out in the
   * browser and from the browser's own zones.
   */
  readonly startsOn?: (zones: readonly TimeZoneGroup[]) => string;
}

/**
 * A choice of time zone, standing empty until the browser has said which zones it knows.
 *
 * Empty rather than missing: the same size as the picker it stands in for, under the same label,
 * so nothing on the form moves when the zones arrive a moment after the page does. Unavailable
 * meanwhile, since there is nothing in it to choose.
 *
 * The empty one is a picker of its own rather than this one before its zones arrived. A picker
 * starts on the choice it was told to start on only as it first appears, so one that appeared
 * empty and was filled in afterwards would open on whichever zone happens to sort first — which is
 * how somebody ends up measuring a website in a country nobody involved has ever been to.
 */
export function TimeZoneSelect({
  zones,
  startsOn,
  id,
  'aria-describedby': describedBy,
  ...rest
}: Readonly<TimeZoneSelectProps>) {
  if (zones === null) {
    return <SelectInput key="waiting" id={id} aria-describedby={describedBy} disabled />;
  }

  return (
    <SelectInput
      key="offered"
      id={id}
      aria-describedby={describedBy}
      defaultValue={startsOn?.(zones)}
      {...rest}
    >
      {zones.map((group) => (
        <optgroup key={group.area} label={group.area}>
          {group.zones.map((zone) => (
            <option key={zone.id} value={zone.id}>
              {zone.label}
            </option>
          ))}
        </optgroup>
      ))}
    </SelectInput>
  );
}

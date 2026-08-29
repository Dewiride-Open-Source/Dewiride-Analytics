import type { LiveMinute, LiveVisitor, TrafficCategory } from '@/lib/api/schemas';

/**
 * The rules a screen about the present moment is drawn by.
 *
 * All of them are about time passing underneath a reader, which is the one thing this screen has
 * that no other screen in the product does: rows appear, rows go, and one of them may be open while
 * it happens. Kept apart from the components so they can be checked without a browser and without a
 * clock, and written against the moment the reading was taken rather than against the machine it is
 * being read on — a laptop an hour out would otherwise report visitors arriving in the future.
 */

/**
 * How long everybody has to have been silent before the moment reads as over rather than busy.
 *
 * Five minutes. A visitor counts towards the half hour for the whole of it, so a site that had a
 * flurry twenty-five minutes ago and nothing since would otherwise be described in the present
 * tense — somebody "reading" a page they closed before the reader sat down.
 */
export const QUIET_AFTER = 5;

/**
 * Whole minutes between a reading and something that happened before it.
 *
 * Never negative. A report can carry a stamp a shade later than the reading that found it, and a
 * row saying somebody was last active in a minute's time is worse than one saying "just now".
 */
export function minutesSince(at: string, moment: string): number {
  const passed = Date.parse(at) - Date.parse(moment);

  return passed > 0 ? Math.floor(passed / 60_000) : 0;
}

/** Whether one visitor has done something recently enough to be spoken about in the present. */
export function activeNow(at: string, lastSeen: string): boolean {
  return minutesSince(at, lastSeen) < QUIET_AFTER;
}

/** Whether anybody has, so the screen can say the moment is over rather than describing it. */
export function anybodyActive(visitors: readonly LiveVisitor[], at: string): boolean {
  return visitors.some((visitor) => activeNow(at, visitor.lastSeen));
}

/** How long a minute is, since every bucket on the chart is exactly one. */
const MINUTE = 60_000;

/**
 * Which bucket the reading was taken inside, or nothing where none of them holds it.
 *
 * That bucket is the only one on the chart still being added to, and it is drawn, said and tabled
 * as such: it is a fraction of a minute old and will almost always be shorter than the finished
 * ones beside it. Left alone, the newest column on a live chart reads as traffic falling away.
 */
export function runningMinute(minutes: readonly LiveMinute[], at: string): number | null {
  const moment = Date.parse(at);
  const running = minutes.findIndex((minute) => {
    const start = Date.parse(minute.start);

    return moment >= start && moment < start + MINUTE;
  });

  return running === -1 ? null : running;
}

/** Whether anything at all was read in the half hour, which decides whether it is worth drawing. */
export function anythingRead(minutes: readonly LiveMinute[]): boolean {
  return minutes.some((minute) => minute.pageViews > 0);
}

/** One thing that could be named, and how many of the visitors here are it. */
export interface NamedGroup {
  readonly category: TrafficCategory;
  readonly visitors: number;
}

/** What may be said about the visitors here, and how many nothing may yet be said about. */
export interface NamedNow {
  readonly groups: readonly NamedGroup[];
  readonly unnamed: number;
}

/**
 * What can be named among the visitors here, commonest first.
 *
 * Only what the engine actually named. A visitor it said nothing about is counted apart rather than
 * folded into a category meaning "not sure" — there is no such category, and borrowing one that
 * means something else would be this product asserting a thing it does not know to fill a column.
 *
 * Two categories with the same count are ordered by name, so that a reading arriving ten seconds
 * later does not shuffle them past each other for no reason a reader could see.
 */
export function namedNow(visitors: readonly LiveVisitor[]): NamedNow {
  const counted = new Map<TrafficCategory, number>();
  let unnamed = 0;

  for (const visitor of visitors) {
    if (visitor.category === null) {
      unnamed += 1;
    } else {
      counted.set(visitor.category, (counted.get(visitor.category) ?? 0) + 1);
    }
  }

  const groups = [...counted]
    .map(([category, count]) => ({ category, visitors: count }))
    .sort(
      (first, second) =>
        second.visitors - first.visitors || (first.category < second.category ? -1 : 1),
    );

  return { groups, unnamed };
}

/** One row on the list, and whether the visitor behind it is still reporting. */
export interface LiveRow {
  readonly visitor: LiveVisitor;
  readonly stillHere: boolean;
}

/**
 * The rows to draw, from the reading that has just arrived and the rows already on screen.
 *
 * A row somebody has opened is kept after its visitor has gone, marked as gone. A row vanishing
 * from under a reader part-way through it is the worst moment a screen like this has, and it is the
 * one thing here that no amount of care in the engine can prevent. Closing the row lets it go with
 * the next reading.
 *
 * Everything else keeps the engine's order exactly and is never sorted again here. A list that
 * rearranged itself under somebody's hand every ten seconds would be unreadable however correct
 * each rearrangement was — which is also why the rows that have gone go to the end rather than
 * holding the places they used to have.
 */
export function rowsToShow(
  here: readonly LiveVisitor[],
  shown: readonly LiveRow[],
  opened: ReadonlySet<string>,
): readonly LiveRow[] {
  const present = new Set(here.map((visitor) => visitor.visitor));

  const gone = shown
    .filter((row) => opened.has(row.visitor.visitor) && !present.has(row.visitor.visitor))
    .map((row) => (row.stillHere ? { visitor: row.visitor, stillHere: false } : row));

  return [...here.map((visitor) => ({ visitor, stillHere: true })), ...gone];
}

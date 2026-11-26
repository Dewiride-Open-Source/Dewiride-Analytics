import { CATEGORY_TONES } from '@/lib/analytics/verdicts';
import { type CameFrom, cameFrom } from '@/lib/analytics/visit-context';
import {
  type Live,
  type LiveArrivals,
  type LiveMinute,
  type LiveVisitor,
  type SourceKind,
  sourceKindSchema,
  type TrafficCategory,
  trafficCategorySchema,
} from '@/lib/api/schemas';

/**
 * The rules a screen about the present moment is drawn by.
 *
 * Most of them are about time passing underneath a reader, which is the one thing this screen has
 * that no other screen in the product does: rows appear, rows go, and one of them may be open while
 * it happens. The rest decide which of the visitors here the list is kept to, and what the list can
 * honestly say about the ones it does not show. Kept apart from the components so they can be
 * checked without a browser and without a clock, and written against the moment the reading was
 * taken rather than against the machine it is being read on — a laptop an hour out would otherwise
 * report visitors arriving in the future.
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

/**
 * How a visitor reached the site, as far as the half hour can say.
 *
 * A site that sent them is named whenever one did. Where none did, the half hour has to have held
 * the moment they arrived before it may say they came straight here: somebody already reading when
 * it began carries no sender either, and "Came straight here" said of them would be a guess written
 * as a fact on the line a row is read by. Their arrival is unseen, and the row says they arrived
 * earlier, which is all the half hour knows.
 */
export function cameHereFrom(visitor: LiveVisitor): CameFrom {
  return cameFrom(visitor.context, visitor.seenArriving);
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

/**
 * How many pages were read in the half hour: every minute of it, the one running now among them.
 *
 * The minutes cover the same stretch the visitors are counted over, so the two figures describe one
 * half hour.
 */
export function pagesReadIn(minutes: readonly LiveMinute[]): number {
  return minutes.reduce((read, minute) => read + minute.pageViews, 0);
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
 * What can be named among the visitors here, each in the place the vocabulary gives it.
 *
 * Only what the engine actually named. A visitor it said nothing about is counted apart rather than
 * folded into a category meaning "not sure" — there is no such category, and borrowing one that
 * means something else would be this product asserting a thing it does not know to fill a column.
 *
 * The rest are everybody the half hour holds who was not named, counted from the whole rather than
 * from the list. The list is cut short at a hundred, so a count taken from it would say a sweep of
 * three hundred had ninety-odd unnamed.
 *
 * Ordered by the vocabulary rather than by how many each holds. Every one of them is something to
 * press, and a reading arrives every ten seconds: ordered by count, two of them would trade places
 * between the moment a reader aimed at one and the moment they pressed it.
 *
 * @param visitors The visitors the reading carried.
 * @param visitorsSeen How many visitors the half hour holds altogether.
 */
export function namedNow(visitors: readonly LiveVisitor[], visitorsSeen: number): NamedNow {
  const counted = new Map<TrafficCategory, number>();

  for (const visitor of visitors) {
    if (visitor.category !== null) {
      counted.set(visitor.category, (counted.get(visitor.category) ?? 0) + 1);
    }
  }

  const groups = trafficCategorySchema.options.flatMap((category) => {
    const count = counted.get(category);

    return count === undefined ? [] : [{ category, visitors: count }];
  });

  const named = groups.reduce((sum, group) => sum + group.visitors, 0);

  return { groups, unnamed: Math.max(visitorsSeen - named, 0) };
}

/**
 * The kinds of visitor a reading of the present can recognise: machinery, whether or not the
 * website wants it.
 *
 * A person is never recognised while their visit is under way, and nor is the want of an answer, so
 * a link asking for the list to be kept to either is asking for something this screen never names.
 */
export const RECOGNISABLE_NOW: readonly TrafficCategory[] = trafficCategorySchema.options.filter(
  (category) =>
    CATEGORY_TONES[category] === 'automation' || CATEGORY_TONES[category] === 'unwanted',
);

/** A list kept to one recognised kind of visitor. */
export interface RecognisedChoice {
  readonly by: 'category';
  readonly category: TrafficCategory;
}

/** A list kept to the visitors one kind of site sent. */
export interface ArrivalChoice {
  readonly by: 'sourceKind';
  readonly kind: SourceKind;
}

/** One thing the list of who is here can be kept to. */
export type LiveChoice = RecognisedChoice | ArrivalChoice;

/** What the list of who is here is kept to, which is everybody until a chip is pressed. */
export type LiveNarrowing = LiveChoice | null;

/**
 * Whether a visitor belongs on a list kept to one thing.
 *
 * A way of arriving is the kind of the very site the row's own "From" names, so a list kept to the
 * visitors a search engine sent holds exactly the rows that name a search engine. Somebody nothing
 * named a sender for belongs to none of the four, whether they came straight here or were already
 * reading when the half hour began.
 */
export function keptTo(visitor: LiveVisitor, narrowing: LiveNarrowing): boolean {
  if (narrowing === null) {
    return true;
  }

  return narrowing.by === 'category'
    ? visitor.category === narrowing.category
    : visitor.context.kind === narrowing.kind;
}

/** Whether a choice is the one the list is kept to. */
function isChosen(narrowing: LiveNarrowing, choice: LiveChoice): boolean {
  if (narrowing === null) {
    return false;
  }

  if (narrowing.by === 'category') {
    return choice.by === 'category' && choice.category === narrowing.category;
  }

  return choice.by === 'sourceKind' && choice.kind === narrowing.kind;
}

/**
 * What the list is kept to once a chip has been pressed.
 *
 * One chip at a time. Pressing one keeps the list to it alone, whatever was pressed before, and
 * pressing the one already pressed shows everybody again. Two pressed together would have to mean
 * either "both" or "either", and somebody glancing at two lit chips could not tell which.
 *
 * @param narrowing What the list is kept to before the press.
 * @param chip What the pressed chip keeps it to.
 */
export function pressChip(narrowing: LiveNarrowing, chip: LiveChoice): LiveNarrowing {
  return isChosen(narrowing, chip) ? null : chip;
}

/** Whether two narrowings keep the list to the same visitors: both to everybody, or both to one choice. */
export function sameNarrowing(one: LiveNarrowing, other: LiveNarrowing): boolean {
  return one === null ? other === null : other !== null && isChosen(one, other);
}

/** One chip: what pressing it keeps the list to, how many it stands for, and whether it is pressed. */
export interface LiveChip<Choice extends LiveChoice = LiveChoice> {
  readonly choice: Choice;
  readonly visitors: number;
  readonly pressed: boolean;
}

/**
 * The recognised kinds of visitor on offer, in the order the vocabulary names them.
 *
 * A kind nobody here is offers nothing to press, save two: the one the list is kept to, and the one
 * the keyboard is on. Each stays where it was at nought — a reading in which the last of them left
 * must not take away the control that shows everybody again from under the hand that pressed it, nor
 * pull a control out from under the keyboard and leave it nowhere.
 *
 * @param groups What can be named among the visitors here.
 * @param narrowing What the list is kept to.
 * @param held The kind whose chip holds the keyboard, if any does.
 */
export function recognisedChips(
  groups: readonly NamedGroup[],
  narrowing: LiveNarrowing,
  held: TrafficCategory | null = null,
): readonly LiveChip<RecognisedChoice>[] {
  return trafficCategorySchema.options.flatMap((category) => {
    const choice: RecognisedChoice = { by: 'category', category };
    const visitors = groups.find((group) => group.category === category)?.visitors ?? 0;
    const pressed = isChosen(narrowing, choice);

    return visitors > 0 || pressed || category === held ? [{ choice, visitors, pressed }] : [];
  });
}

/**
 * The four ways of arriving on offer: always all four, always in the same order.
 *
 * A way nobody arrived by is offered at nought rather than left out. "From an AI assistant 0"
 * answers a question somebody opened the screen to ask, and a row of chips that never come and go,
 * drawn above anything that does, is a row that never moves under a pointer.
 *
 * @param arrivals How many of everybody the half hour holds each kind of site sent.
 * @param narrowing What the list is kept to.
 */
export function arrivalChips(
  arrivals: LiveArrivals,
  narrowing: LiveNarrowing,
): readonly LiveChip<ArrivalChoice>[] {
  return sourceKindSchema.options.map((kind) => {
    const choice: ArrivalChoice = { by: 'sourceKind', kind };

    return { choice, visitors: arrivals[kind], pressed: isChosen(narrowing, choice) };
  });
}

/** One row on the list, and whether the visitor behind it is still reporting. */
export interface LiveRow {
  readonly visitor: LiveVisitor;
  readonly stillHere: boolean;
  /**
   * When the reading this row was drawn from was taken.
   *
   * A row held after its visitor has gone keeps the one it was last seen in, so what is asked
   * about it stays the same question.
   */
  readonly at: string;
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
 *
 * Every row present is stamped with the reading it came from; a row that has gone keeps the one it
 * was last seen in.
 *
 * @param at When the reading that has just arrived was taken.
 */
export function rowsToShow(
  here: readonly LiveVisitor[],
  shown: readonly LiveRow[],
  opened: ReadonlySet<string>,
  at: string,
): readonly LiveRow[] {
  const present = new Set(here.map((visitor) => visitor.visitor));

  const gone = shown
    .filter((row) => opened.has(row.visitor.visitor) && !present.has(row.visitor.visitor))
    .map((row) => (row.stillHere ? { visitor: row.visitor, stillHere: false, at: row.at } : row));

  return [...here.map((visitor) => ({ visitor, stillHere: true, at })), ...gone];
}

/**
 * What the foot of the list says about how much of what it is kept to it shows.
 *
 * - `nothing`: everybody it is kept to is on it.
 * - `latest`: it shows the most recently active of more, or of a number nobody can know.
 * - `nobody`: nobody the half hour holds is what it is kept to.
 * - `notAmongLatest`: none of the visitors the reading carried is, though some further back are, or
 *   may be.
 */
export type ListFoot =
  | { readonly says: 'nothing' }
  | { readonly says: 'latest'; readonly shown: number }
  | { readonly says: 'nobody' }
  | { readonly says: 'notAmongLatest'; readonly carried: number };

/**
 * How many of everybody the half hour holds the list would have to show to be complete, or nothing
 * where that cannot be known.
 *
 * Everybody is counted, and so is everybody each kind of site sent. A recognised kind of visitor is
 * not: what somebody is gets worked out one visitor at a time, over the visitors the reading
 * carried, so once the reading has been cut short how many of the rest are that kind is unknown —
 * and is never guessed at.
 */
function heldInAll(answer: Live, narrowing: LiveNarrowing): number | null {
  if (narrowing === null) {
    return answer.visitorsSeen;
  }

  if (narrowing.by === 'sourceKind') {
    return answer.arrivals[narrowing.kind];
  }

  return answer.visitors.length < answer.visitorsSeen
    ? null
    : answer.visitors.filter((visitor) => keptTo(visitor, narrowing)).length;
}

/**
 * What the foot of the list says, from the reading and the rows drawn from it.
 *
 * Only the rows of visitors still here are counted as shown. A row held open after its visitor left
 * is on the list for the reader's sake and says it is gone; it is no part of who is here.
 *
 * A list kept to nothing says nothing where it shows nobody. The screen has a state of its own for a
 * half hour with nobody in it, and a line beneath an empty list is not that state.
 *
 * @param answer The reading the rows were drawn from.
 * @param rows The rows on screen, the ones held after their visitors left among them.
 * @param narrowing What the list is kept to.
 */
export function listFoot(
  answer: Live,
  rows: readonly LiveRow[],
  narrowing: LiveNarrowing,
): ListFoot {
  const shown = rows.filter((row) => row.stillHere && keptTo(row.visitor, narrowing)).length;
  const inAll = heldInAll(answer, narrowing);

  if (shown > 0) {
    return inAll !== null && inAll <= shown ? { says: 'nothing' } : { says: 'latest', shown };
  }

  if (narrowing === null) {
    return { says: 'nothing' };
  }

  return inAll === 0
    ? { says: 'nobody' }
    : { says: 'notAmongLatest', carried: answer.visitors.length };
}

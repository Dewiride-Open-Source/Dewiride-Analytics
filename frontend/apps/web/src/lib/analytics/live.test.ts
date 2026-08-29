import { describe, expect, it } from 'vitest';
import {
  activeNow,
  anybodyActive,
  anythingRead,
  type LiveRow,
  minutesSince,
  namedNow,
  rowsToShow,
  runningMinute,
} from '@/lib/analytics/live';
import type { LiveMinute, LiveVisitor } from '@/lib/api/schemas';

const AT = '2026-08-29T10:30:00.000Z';

function visitor(overrides: Partial<LiveVisitor> = {}): LiveVisitor {
  return {
    visitor: 'a1',
    firstSeen: '2026-08-29T10:20:00.000Z',
    lastSeen: '2026-08-29T10:29:40.000Z',
    pageCount: 3,
    currentPath: '/writing',
    category: null,
    strength: null,
    ruleset: null,
    supporting: [],
    contradicting: [],
    operator: '',
    network: 0,
    context: {
      source: '',
      kind: 'direct',
      countryCode: '',
      town: '',
      network: '',
      device: 'unknown',
      browser: '',
      system: '',
    },
    ...overrides,
  };
}

/** Four minutes running up to the reading, the last of which the reading was taken inside. */
const MINUTES: readonly LiveMinute[] = [
  { start: '2026-08-29T10:27:00.000Z', pageViews: 4 },
  { start: '2026-08-29T10:28:00.000Z', pageViews: 0 },
  { start: '2026-08-29T10:29:00.000Z', pageViews: 6 },
  { start: '2026-08-29T10:30:00.000Z', pageViews: 1 },
];

function row(visitorKey: string, stillHere = true): LiveRow {
  return { visitor: visitor({ visitor: visitorKey }), stillHere };
}

describe('how long ago something happened', () => {
  it('counts whole minutes back from the moment the reading was taken', () => {
    expect(minutesSince(AT, '2026-08-29T10:26:30.000Z')).toBe(3);
  });

  it('reads anything inside the last minute as no minutes at all', () => {
    expect(minutesSince(AT, '2026-08-29T10:29:59.000Z')).toBe(0);
  });

  /**
   * A report can carry a stamp a shade later than the reading that found it. A row saying somebody
   * was last active in a minute's time is worse than one saying they were active just now.
   */
  it('never reports something as having happened after the reading', () => {
    expect(minutesSince(AT, '2026-08-29T10:34:00.000Z')).toBe(0);
  });
});

describe('whether the moment is a busy one', () => {
  it('treats somebody heard from within the last few minutes as here', () => {
    expect(activeNow(AT, '2026-08-29T10:27:00.000Z')).toBe(true);
  });

  it('treats somebody who has been silent for longer as gone quiet', () => {
    expect(activeNow(AT, '2026-08-29T10:20:00.000Z')).toBe(false);
  });

  it('calls the moment busy while any one of them is still doing something', () => {
    const visitors = [
      visitor({ visitor: 'a1', lastSeen: '2026-08-29T10:05:00.000Z' }),
      visitor({ visitor: 'a2', lastSeen: '2026-08-29T10:29:00.000Z' }),
    ];

    expect(anybodyActive(visitors, AT)).toBe(true);
  });

  it('calls it over once every one of them has stopped', () => {
    const visitors = [
      visitor({ visitor: 'a1', lastSeen: '2026-08-29T10:05:00.000Z' }),
      visitor({ visitor: 'a2', lastSeen: '2026-08-29T10:12:00.000Z' }),
    ];

    expect(anybodyActive(visitors, AT)).toBe(false);
  });

  it('calls a half hour with nobody in it over rather than busy', () => {
    expect(anybodyActive([], AT)).toBe(false);
  });
});

describe('what can be named among the visitors here', () => {
  it('counts each category and puts the commonest first', () => {
    const named = namedNow([
      visitor({ visitor: 'a1', category: 'known-search-crawler' }),
      visitor({ visitor: 'a2', category: 'security-scanner' }),
      visitor({ visitor: 'a3', category: 'known-search-crawler' }),
    ]);

    expect(named.groups).toEqual([
      { category: 'known-search-crawler', visitors: 2 },
      { category: 'security-scanner', visitors: 1 },
    ]);
  });

  /**
   * A reading arriving ten seconds later must not shuffle two equal categories past each other,
   * since nothing a reader could see would have changed.
   */
  it('settles a tie by name so the order does not move on its own', () => {
    const named = namedNow([
      visitor({ visitor: 'a1', category: 'security-scanner' }),
      visitor({ visitor: 'a2', category: 'known-ai-crawler' }),
    ]);

    expect(named.groups.map((group) => group.category)).toEqual([
      'known-ai-crawler',
      'security-scanner',
    ]);
  });

  it('counts a visitor nothing has been said about apart from the named ones', () => {
    const named = namedNow([
      visitor({ visitor: 'a1', category: 'known-ai-crawler' }),
      visitor({ visitor: 'a2' }),
      visitor({ visitor: 'a3' }),
    ]);

    expect(named.groups).toEqual([{ category: 'known-ai-crawler', visitors: 1 }]);
    expect(named.unnamed).toBe(2);
  });

  it('names nothing at all where nothing has been settled about anybody', () => {
    const named = namedNow([visitor({ visitor: 'a1' }), visitor({ visitor: 'a2' })]);

    expect(named.groups).toEqual([]);
    expect(named.unnamed).toBe(2);
  });
});

describe('the rows a reading leaves on screen', () => {
  it('draws the reading in the order it arrived and never reorders it', () => {
    const here = [
      visitor({ visitor: 'a1' }),
      visitor({ visitor: 'a2' }),
      visitor({ visitor: 'a3' }),
    ];

    const shown = rowsToShow(here, [], new Set());

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1', 'a2', 'a3']);
    expect(shown.every((one) => one.stillHere)).toBe(true);
  });

  /**
   * A panel disappearing from under a reader part-way through it is the one failure this screen
   * can actually cause, so a row somebody has opened outlives the visitor behind it.
   */
  it('keeps a row somebody has open after its visitor has gone, and marks it gone', () => {
    const shown = rowsToShow([visitor({ visitor: 'a2' })], [row('a1'), row('a2')], new Set(['a1']));

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a2', 'a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([true, false]);
  });

  it('lets a row nobody has open go the moment its visitor does', () => {
    const shown = rowsToShow([visitor({ visitor: 'a2' })], [row('a1'), row('a2')], new Set());

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a2']);
  });

  it('lets a row that had been held go once the reader closes it', () => {
    const shown = rowsToShow([], [row('a1', false)], new Set());

    expect(shown).toEqual([]);
  });

  it('keeps holding a row that has already gone for as long as it stays open', () => {
    const shown = rowsToShow([], [row('a1', false)], new Set(['a1']));

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([false]);
  });

  /**
   * Somebody who left and came back inside the same half hour is one visitor, not two: the reading
   * carries them again and the held row is dropped rather than drawn a second time.
   */
  it('draws a visitor who has come back once rather than twice', () => {
    const shown = rowsToShow([visitor({ visitor: 'a1' })], [row('a1', false)], new Set(['a1']));

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([true]);
  });
});

describe('the minute that has not finished', () => {
  it('finds the minute the reading was taken inside', () => {
    expect(runningMinute(MINUTES, AT)).toBe(3);
  });

  /** The reading is taken part-way through a minute, not on the stroke of one. */
  it('stays on that minute for the whole of it', () => {
    expect(runningMinute(MINUTES, '2026-08-29T10:30:59.999Z')).toBe(3);
  });

  it('moves on as soon as the next one begins', () => {
    expect(runningMinute(MINUTES, '2026-08-29T10:29:00.000Z')).toBe(2);
  });

  /**
   * The half hour a reading covers always ends at the reading, so this cannot happen while the
   * engine is answering. It can happen to an answer left on screen after the engine stopped
   * replying, and a chart that faded the wrong column would be saying the older news twice.
   */
  it('fades nothing when the reading falls outside every minute drawn', () => {
    expect(runningMinute(MINUTES, '2026-08-29T11:15:00.000Z')).toBeNull();
  });

  it('fades nothing when there is nothing to draw', () => {
    expect(runningMinute([], AT)).toBeNull();
  });
});

describe('whether the half hour is worth drawing', () => {
  it('is worth drawing when anything at all was read', () => {
    expect(anythingRead(MINUTES)).toBe(true);
  });

  /** Thirty columns all at nought is honest, unreadable, and indistinguishable from a fault. */
  it('is not worth drawing when every minute of it was silent', () => {
    expect(anythingRead(MINUTES.map((minute) => ({ ...minute, pageViews: 0 })))).toBe(false);
  });

  it('is not worth drawing when there are no minutes at all', () => {
    expect(anythingRead([])).toBe(false);
  });
});

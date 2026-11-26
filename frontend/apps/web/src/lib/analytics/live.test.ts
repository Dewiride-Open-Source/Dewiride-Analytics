import { describe, expect, it } from 'vitest';
import {
  type ArrivalChoice,
  activeNow,
  anybodyActive,
  anythingRead,
  arrivalChips,
  cameHereFrom,
  keptTo,
  type LiveRow,
  listFoot,
  minutesSince,
  namedNow,
  pagesReadIn,
  pressChip,
  RECOGNISABLE_NOW,
  type RecognisedChoice,
  recognisedChips,
  rowsToShow,
  runningMinute,
  sameNarrowing,
} from '@/lib/analytics/live';
import {
  type Live,
  type LiveArrivals,
  type LiveMinute,
  type LiveVisitor,
  sourceKindSchema,
  type VisitContext,
} from '@/lib/api/schemas';

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
    seenArriving: true,
    ...overrides,
  };
}

/** What a visitor sent by a search engine carries. */
const FROM_GOOGLE: VisitContext = { ...visitor().context, source: 'Google', kind: 'search' };

/** What a visitor sent by an AI assistant carries. */
const FROM_CHATGPT: VisitContext = { ...visitor().context, source: 'ChatGPT', kind: 'assistant' };

/** A list kept to the visitors a search engine sent. */
const SEARCH: ArrivalChoice = { by: 'sourceKind', kind: 'search' };

/** A list kept to the visitors an AI assistant sent. */
const ASSISTANT: ArrivalChoice = { by: 'sourceKind', kind: 'assistant' };

/** A list kept to the visitors recognised as probing for a way in. */
const SCANNERS: RecognisedChoice = { by: 'category', category: 'security-scanner' };

/** A list kept to the visitors recognised as a search engine's crawler. */
const CRAWLERS: RecognisedChoice = { by: 'category', category: 'known-search-crawler' };

/** Nobody sent by any kind of site. */
const NOBODY_SENT: LiveArrivals = { search: 0, assistant: 0, social: 0, link: 0 };

/**
 * A reading carrying the visitors given and counting nobody else. How many each kind of site sent is
 * the engine's count rather than one worked out here, so a test that needs one says what it is.
 */
function reading(visitors: readonly LiveVisitor[], overrides: Partial<Live> = {}): Live {
  return {
    at: AT,
    from: '2026-08-29T10:00:00.000Z',
    visitorsSeen: visitors.length,
    arrivals: NOBODY_SENT,
    visitors: [...visitors],
    minutes: [],
    pages: [],
    ...overrides,
  };
}

/** The rows a reading puts on screen, with any held after their visitors left. */
function rowsOf(answer: Live, held: readonly LiveVisitor[] = []): readonly LiveRow[] {
  return [
    ...answer.visitors.map((one) => ({ visitor: one, stillHere: true, at: answer.at })),
    ...held.map((one) => ({ visitor: one, stillHere: false, at: answer.at })),
  ];
}

/** Four minutes running up to the reading, the last of which the reading was taken inside. */
const MINUTES: readonly LiveMinute[] = [
  { start: '2026-08-29T10:27:00.000Z', pageViews: 4 },
  { start: '2026-08-29T10:28:00.000Z', pageViews: 0 },
  { start: '2026-08-29T10:29:00.000Z', pageViews: 6 },
  { start: '2026-08-29T10:30:00.000Z', pageViews: 1 },
];

function row(visitorKey: string, stillHere = true, at = AT): LiveRow {
  return { visitor: visitor({ visitor: visitorKey }), stillHere, at };
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

describe('how a visitor here reached the site', () => {
  it('names the site that sent a visitor the half hour saw arrive', () => {
    expect(cameHereFrom(visitor({ context: FROM_GOOGLE }))).toStrictEqual({
      arrived: 'from-site',
      site: 'Google',
    });
  });

  /**
   * The site that sent somebody is said whenever one did, even where the moment they arrived lies
   * before the half hour: the sender was named on a page the half hour holds.
   */
  it('names the site that sent a visitor even where the half hour did not see them arrive', () => {
    expect(cameHereFrom(visitor({ context: FROM_GOOGLE, seenArriving: false }))).toStrictEqual({
      arrived: 'from-site',
      site: 'Google',
    });
  });

  it('says a visitor the half hour saw arrive from nowhere came straight here', () => {
    expect(cameHereFrom(visitor())).toStrictEqual({ arrived: 'directly' });
  });

  /**
   * Somebody already reading when the half hour began carries no sender either. Saying they came
   * straight here would be a guess written as a fact, so their arrival is unseen, and the row says
   * what the half hour knows: that they arrived earlier.
   */
  it('says a visitor who was already here when the half hour began was not seen arriving', () => {
    expect(cameHereFrom(visitor({ seenArriving: false }))).toStrictEqual({ arrived: 'unseen' });
  });
});

describe('what can be named among the visitors here', () => {
  it('counts each category and puts each in the place the vocabulary gives it', () => {
    const visitors = [
      visitor({ visitor: 'a1', category: 'security-scanner' }),
      visitor({ visitor: 'a2', category: 'known-search-crawler' }),
      visitor({ visitor: 'a3', category: 'security-scanner' }),
    ];

    const named = namedNow(visitors, visitors.length);

    expect(named.groups).toEqual([
      { category: 'known-search-crawler', visitors: 1 },
      { category: 'security-scanner', visitors: 2 },
    ]);
  });

  /**
   * Each category is something to press, and a reading arrives every ten seconds. Ordered by how
   * many each holds, two would trade places between the moment a reader aimed at one and the moment
   * they pressed it.
   */
  it('keeps every category where it was however the counts move between readings', () => {
    const earlier = namedNow(
      [
        visitor({ visitor: 'a1', category: 'known-ai-crawler' }),
        visitor({ visitor: 'a2', category: 'security-scanner' }),
        visitor({ visitor: 'a3', category: 'security-scanner' }),
      ],
      3,
    );
    const later = namedNow(
      [
        visitor({ visitor: 'a1', category: 'known-ai-crawler' }),
        visitor({ visitor: 'a4', category: 'known-ai-crawler' }),
        visitor({ visitor: 'a2', category: 'security-scanner' }),
      ],
      3,
    );

    expect(earlier.groups.map((group) => group.category)).toEqual([
      'known-ai-crawler',
      'security-scanner',
    ]);
    expect(later.groups.map((group) => group.category)).toEqual([
      'known-ai-crawler',
      'security-scanner',
    ]);
  });

  it('counts a visitor nothing has been said about apart from the named ones', () => {
    const visitors = [
      visitor({ visitor: 'a1', category: 'known-ai-crawler' }),
      visitor({ visitor: 'a2' }),
      visitor({ visitor: 'a3' }),
    ];

    const named = namedNow(visitors, visitors.length);

    expect(named.groups).toEqual([{ category: 'known-ai-crawler', visitors: 1 }]);
    expect(named.unnamed).toBe(2);
  });

  it('names nothing at all where nothing has been settled about anybody', () => {
    const visitors = [visitor({ visitor: 'a1' }), visitor({ visitor: 'a2' })];

    const named = namedNow(visitors, visitors.length);

    expect(named.groups).toEqual([]);
    expect(named.unnamed).toBe(2);
  });

  /**
   * The list is cut short at a hundred and the count is not. A sweep of three hundred with one
   * named would otherwise be reported as ninety-odd still being watched.
   */
  it('counts everybody the list could not carry among those still being watched', () => {
    const named = namedNow([visitor({ visitor: 'a1', category: 'security-scanner' })], 214);

    expect(named.groups).toEqual([{ category: 'security-scanner', visitors: 1 }]);
    expect(named.unnamed).toBe(213);
  });

  it('never counts fewer than nobody', () => {
    const named = namedNow([visitor({ visitor: 'a1', category: 'security-scanner' })], 0);

    expect(named.unnamed).toBe(0);
  });
});

describe('the rows a reading leaves on screen', () => {
  it('draws the reading in the order it arrived and never reorders it', () => {
    const here = [
      visitor({ visitor: 'a1' }),
      visitor({ visitor: 'a2' }),
      visitor({ visitor: 'a3' }),
    ];

    const shown = rowsToShow(here, [], new Set(), AT);

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1', 'a2', 'a3']);
    expect(shown.every((one) => one.stillHere)).toBe(true);
  });

  /**
   * A panel disappearing from under a reader part-way through it is the one failure this screen
   * can actually cause, so a row somebody has opened outlives the visitor behind it.
   */
  it('keeps a row somebody has open after its visitor has gone, and marks it gone', () => {
    const shown = rowsToShow(
      [visitor({ visitor: 'a2' })],
      [row('a1'), row('a2')],
      new Set(['a1']),
      AT,
    );

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a2', 'a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([true, false]);
  });

  it('lets a row nobody has open go the moment its visitor does', () => {
    const shown = rowsToShow([visitor({ visitor: 'a2' })], [row('a1'), row('a2')], new Set(), AT);

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a2']);
  });

  it('lets a row that had been held go once the reader closes it', () => {
    const shown = rowsToShow([], [row('a1', false)], new Set(), AT);

    expect(shown).toEqual([]);
  });

  it('keeps holding a row that has already gone for as long as it stays open', () => {
    const shown = rowsToShow([], [row('a1', false)], new Set(['a1']), AT);

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([false]);
  });

  /**
   * Somebody who left and came back inside the same half hour is one visitor, not two: the reading
   * carries them again and the held row is dropped rather than drawn a second time.
   */
  it('draws a visitor who has come back once rather than twice', () => {
    const shown = rowsToShow([visitor({ visitor: 'a1' })], [row('a1', false)], new Set(['a1']), AT);

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a1']);
    expect(shown.map((one) => one.stillHere)).toEqual([true]);
  });

  /**
   * What is asked under a row is asked over the minutes of the reading the row came from, so every
   * row carries the instant of the reading that drew it.
   */
  it('stamps every row with the reading it was drawn from', () => {
    const here = [visitor({ visitor: 'a1' }), visitor({ visitor: 'a2' })];

    const shown = rowsToShow(here, [], new Set(), AT);

    expect(shown.every((one) => one.at === AT)).toBe(true);
  });

  /**
   * A row held after its visitor has gone keeps the instant it was last seen in. Stamping it with
   * each new reading would ask a fresh question about somebody who is not in the answer.
   */
  it('keeps the reading a held row was last seen in', () => {
    const earlier = '2026-08-29T10:29:40.000Z';

    const shown = rowsToShow(
      [visitor({ visitor: 'a3' })],
      [row('a1', true, earlier), row('a2', false, earlier), row('a3', true, earlier)],
      new Set(['a1', 'a2']),
      AT,
    );

    expect(shown.map((one) => one.visitor.visitor)).toEqual(['a3', 'a1', 'a2']);
    expect(shown.map((one) => one.at)).toEqual([AT, earlier, earlier]);
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

describe('how many pages the half hour holds', () => {
  it('counts every page read in every minute of it, the one running now among them', () => {
    expect(pagesReadIn(MINUTES)).toBe(11);
  });

  it('counts none where the half hour has no minutes', () => {
    expect(pagesReadIn([])).toBe(0);
  });
});

describe('the kinds of visitor a reading of the present can recognise', () => {
  it('is machinery, wanted or not', () => {
    expect(RECOGNISABLE_NOW).toContain('known-search-crawler');
    expect(RECOGNISABLE_NOW).toContain('browser-automation');
    expect(RECOGNISABLE_NOW).toContain('security-scanner');
    expect(RECOGNISABLE_NOW).toContain('likely-analytics-spam');
  });

  /** A person is never recognised while their visit is under way, and nor is the want of an answer. */
  it('is never a person, nor the want of an answer', () => {
    expect(RECOGNISABLE_NOW).not.toContain('likely-human');
    expect(RECOGNISABLE_NOW).not.toContain('insufficient-evidence');
    expect(RECOGNISABLE_NOW).not.toContain('unknown');
  });
});

describe('which visitors a kept list keeps', () => {
  it('keeps everybody while it is kept to nothing', () => {
    expect(keptTo(visitor(), null)).toBe(true);
    expect(keptTo(visitor({ category: 'security-scanner' }), null)).toBe(true);
  });

  it('keeps a visitor recognised as the kind it is kept to, and no other kind', () => {
    const scanner = visitor({ category: 'security-scanner' });

    expect(keptTo(scanner, SCANNERS)).toBe(true);
    expect(keptTo(scanner, CRAWLERS)).toBe(false);
  });

  it('keeps nobody still being watched on a list kept to a recognised kind', () => {
    expect(keptTo(visitor(), SCANNERS)).toBe(false);
  });

  it('keeps a visitor by the kind of site their own row says sent them', () => {
    const searched = visitor({ context: FROM_GOOGLE });

    expect(keptTo(searched, SEARCH)).toBe(true);
    expect(keptTo(searched, ASSISTANT)).toBe(false);
    expect(keptTo(visitor({ context: FROM_CHATGPT }), ASSISTANT)).toBe(true);
  });

  /** The sender is named wherever one was, even where the half hour did not see them arrive. */
  it('keeps a visitor a search engine sent even where the half hour did not see them arrive', () => {
    expect(keptTo(visitor({ context: FROM_GOOGLE, seenArriving: false }), SEARCH)).toBe(true);
  });

  /**
   * Coming straight here and having arrived before the half hour began both leave no sender, and
   * neither is a way of arriving any chip stands for.
   */
  it.each(sourceKindSchema.options)(
    'keeps nobody nothing named a sender for on a list kept to %s, however they came',
    (kind) => {
      const narrowing: ArrivalChoice = { by: 'sourceKind', kind };

      expect(keptTo(visitor(), narrowing)).toBe(false);
      expect(keptTo(visitor({ seenArriving: false }), narrowing)).toBe(false);
    },
  );

  it('keeps a visitor by what they are on a list kept to a kind of visitor, whoever sent them', () => {
    const scanner = visitor({ category: 'security-scanner', context: FROM_GOOGLE });

    expect(keptTo(scanner, SCANNERS)).toBe(true);
    expect(keptTo(visitor({ context: FROM_GOOGLE }), SCANNERS)).toBe(false);
  });
});

describe('pressing a chip', () => {
  it('keeps the list to the chip pressed', () => {
    expect(pressChip(null, SEARCH)).toEqual(SEARCH);
    expect(pressChip(null, SCANNERS)).toEqual(SCANNERS);
  });

  it('shows everybody again when the chip already pressed is pressed again', () => {
    expect(pressChip(SEARCH, { by: 'sourceKind', kind: 'search' })).toBeNull();
    expect(pressChip(SCANNERS, { by: 'category', category: 'security-scanner' })).toBeNull();
  });

  /**
   * Two pressed together would have to mean "both" or "either", and somebody glancing at two lit
   * chips could not tell which.
   */
  it('keeps the list to one chip at a time', () => {
    expect(pressChip(SEARCH, ASSISTANT)).toEqual(ASSISTANT);
    expect(pressChip(SCANNERS, CRAWLERS)).toEqual(CRAWLERS);
    expect(pressChip(SEARCH, SCANNERS)).toEqual(SCANNERS);
    expect(pressChip(SCANNERS, SEARCH)).toEqual(SEARCH);
  });
});

describe('whether two choices keep the list to the same visitors', () => {
  it('keeps it to the same visitors where both show everybody or both name one choice', () => {
    expect(sameNarrowing(null, null)).toBe(true);
    expect(sameNarrowing(SEARCH, { by: 'sourceKind', kind: 'search' })).toBe(true);
    expect(sameNarrowing(SCANNERS, { by: 'category', category: 'security-scanner' })).toBe(true);
  });

  /** Showing everybody again is a change too, and is said as one. */
  it('keeps it to other visitors where one shows everybody, or the two name different choices', () => {
    expect(sameNarrowing(null, SEARCH)).toBe(false);
    expect(sameNarrowing(SEARCH, null)).toBe(false);
    expect(sameNarrowing(SEARCH, ASSISTANT)).toBe(false);
    expect(sameNarrowing(SCANNERS, CRAWLERS)).toBe(false);
    expect(sameNarrowing(SEARCH, SCANNERS)).toBe(false);
  });
});

describe('the chips on offer', () => {
  it('offers each recognised kind of visitor in the place the vocabulary gives it', () => {
    const chips = recognisedChips(
      [
        { category: 'known-search-crawler', visitors: 1 },
        { category: 'security-scanner', visitors: 4 },
      ],
      SCANNERS,
    );

    expect(chips).toEqual([
      { choice: CRAWLERS, visitors: 1, pressed: false },
      { choice: SCANNERS, visitors: 4, pressed: true },
    ]);
  });

  /**
   * A reading in which the last of them left must not take the one control that shows everybody
   * again from under the hand that pressed it.
   */
  it('keeps the recognised kind the list is kept to on offer, at nought, once nobody here is it', () => {
    const chips = recognisedChips([{ category: 'security-scanner', visitors: 2 }], {
      by: 'category',
      category: 'browser-automation',
    });

    expect(chips).toEqual([
      { choice: { by: 'category', category: 'browser-automation' }, visitors: 0, pressed: true },
      { choice: SCANNERS, visitors: 2, pressed: false },
    ]);
  });

  /** Nor may it pull a control out from under the keyboard and leave the keyboard nowhere. */
  it('keeps the recognised kind the keyboard is on on offer, at nought, once nobody here is it', () => {
    const chips = recognisedChips(
      [{ category: 'security-scanner', visitors: 2 }],
      null,
      'known-search-crawler',
    );

    expect(chips).toEqual([
      { choice: CRAWLERS, visitors: 0, pressed: false },
      { choice: SCANNERS, visitors: 2, pressed: false },
    ]);
  });

  it('offers no recognised kind nobody here is, nobody pressed and the keyboard is not on', () => {
    expect(recognisedChips([], null)).toEqual([]);
    expect(recognisedChips([], SEARCH)).toEqual([]);
    expect(recognisedChips([], null, null)).toEqual([]);
  });

  it('offers all four ways of arriving, always in the same order, noughts and all', () => {
    const chips = arrivalChips({ search: 12, assistant: 0, social: 3, link: 0 }, null);

    expect(chips).toEqual([
      { choice: SEARCH, visitors: 12, pressed: false },
      { choice: ASSISTANT, visitors: 0, pressed: false },
      { choice: { by: 'sourceKind', kind: 'social' }, visitors: 3, pressed: false },
      { choice: { by: 'sourceKind', kind: 'link' }, visitors: 0, pressed: false },
    ]);
  });

  it('says which way of arriving the list is kept to', () => {
    expect(arrivalChips(NOBODY_SENT, ASSISTANT).map((chip) => chip.pressed)).toEqual([
      false,
      true,
      false,
      false,
    ]);
  });

  it('presses no way of arriving while the list is kept to a recognised kind', () => {
    expect(arrivalChips(NOBODY_SENT, SCANNERS).some((chip) => chip.pressed)).toBe(false);
  });
});

describe('what the foot of the list says', () => {
  const searched = visitor({ visitor: 'a1', context: FROM_GOOGLE });
  const typed = visitor({ visitor: 'a2' });
  const scanner = visitor({ visitor: 'a3', category: 'security-scanner' });

  it('says nothing where everybody is listed', () => {
    const answer = reading([searched, typed]);

    expect(listFoot(answer, rowsOf(answer), null)).toEqual({ says: 'nothing' });
  });

  /** The figure above stays the true one, so this is where the list admits it is not all of them. */
  it('admits the list is the most recently active of more', () => {
    const answer = reading([searched, typed], { visitorsSeen: 214 });

    expect(listFoot(answer, rowsOf(answer), null)).toEqual({ says: 'latest', shown: 2 });
  });

  it('says nothing where a way of arriving has everybody it sent on the list', () => {
    const answer = reading([searched, typed], {
      visitorsSeen: 214,
      arrivals: { ...NOBODY_SENT, search: 1 },
    });

    expect(listFoot(answer, rowsOf(answer), SEARCH)).toEqual({ says: 'nothing' });
  });

  it('admits a list kept to a way of arriving shows the most recently active of more', () => {
    const answer = reading([searched, typed], {
      visitorsSeen: 214,
      arrivals: { ...NOBODY_SENT, search: 40 },
    });

    expect(listFoot(answer, rowsOf(answer), SEARCH)).toEqual({ says: 'latest', shown: 1 });
  });

  it('says nothing where a reading carrying everybody has every one of a recognised kind listed', () => {
    const answer = reading([scanner, typed]);

    expect(listFoot(answer, rowsOf(answer), SCANNERS)).toEqual({ says: 'nothing' });
  });

  /**
   * What somebody is gets worked out one visitor at a time, over the ones the reading carried, so
   * once it has been cut short nobody can know how many of the rest are that kind.
   */
  it('never calls a list kept to a recognised kind complete once the reading was cut short', () => {
    const answer = reading([scanner, typed], { visitorsSeen: 214 });

    expect(listFoot(answer, rowsOf(answer), SCANNERS)).toEqual({ says: 'latest', shown: 1 });
  });

  it('says nobody like that is here where no site of that kind sent anybody', () => {
    const answer = reading([searched, typed], {
      visitorsSeen: 214,
      arrivals: { ...NOBODY_SENT, search: 61 },
    });

    expect(listFoot(answer, rowsOf(answer), ASSISTANT)).toEqual({ says: 'nobody' });
  });

  it('says nobody like that is here where a reading carrying everybody holds none of a kind', () => {
    const answer = reading([searched, typed]);

    expect(listFoot(answer, rowsOf(answer), SCANNERS)).toEqual({ says: 'nobody' });
  });

  it('says the ones a kind of site sent lie further back than the list reaches', () => {
    const answer = reading([searched, typed], {
      visitorsSeen: 214,
      arrivals: { ...NOBODY_SENT, search: 1, assistant: 6 },
    });

    expect(listFoot(answer, rowsOf(answer), ASSISTANT)).toEqual({
      says: 'notAmongLatest',
      carried: 2,
    });
  });

  it('says a recognised kind may lie further back once the reading was cut short', () => {
    const answer = reading([searched, typed], { visitorsSeen: 214 });

    expect(listFoot(answer, rowsOf(answer), SCANNERS)).toEqual({
      says: 'notAmongLatest',
      carried: 2,
    });
  });

  /** A row held open after its visitor left is on the list for the reader's sake, not as one here. */
  it('counts no row held after its visitor left among those shown', () => {
    const answer = reading([typed]);

    expect(listFoot(answer, rowsOf(answer, [searched]), SEARCH)).toEqual({ says: 'nobody' });
  });

  it('leaves a half hour carrying nobody to the screen of its own for it', () => {
    const answer = reading([]);

    expect(listFoot(answer, rowsOf(answer, [searched]), null)).toEqual({ says: 'nothing' });
  });
});

import { describe, expect, it } from 'vitest';
import { UNSETTLED } from '@/lib/charts/frame';
import { type Quiet, quietened, quietensCutShort, UNQUIETENED, washOver } from '@/lib/charts/wash';
import { PALETTE } from '@/test/drawing';

/** Five buckets, named by where they sit so a stretch can be read as the buckets it runs between. */
const FIVE = ['1st', '2nd', '3rd', '4th', '5th'] as const;

/** Three months, the first and last cut short by the period. */
const THREE = ['Aug 2 – 31', 'Sep', 'Oct 1 – 30'] as const;

/** Both ends of a run cut short, and nothing still being judged. */
const CUT_BOTH_ENDS: Quiet = { cutAtStart: true, cutAtEnd: true, judging: null };

/** What the wash is laid over, as the names of the buckets each stretch runs between. */
function stretches(quiet: Quiet, labels: readonly string[] = FIVE) {
  const wash = washOver(labels, quiet, PALETTE) as
    | { readonly markArea: { readonly data: readonly { readonly xAxis: string }[][] } }
    | undefined;

  return wash?.markArea.data.map((stretch) => stretch.map((end) => end.xAxis));
}

/** A figure as a column draws it faded. */
function faded(value: number) {
  return { value, itemStyle: { opacity: UNSETTLED } };
}

describe('the wash over a run drawn as a line or an area', () => {
  it('is not laid where nothing is quietened', () => {
    expect(washOver(FIVE, UNQUIETENED, PALETTE)).toBeUndefined();
  });

  it('runs over each end the period cuts short, from its point to its neighbour’s', () => {
    expect(stretches(CUT_BOTH_ENDS)).toStrictEqual([
      ['1st', '2nd'],
      ['4th', '5th'],
    ]);
  });

  it('runs from the last point at full strength to the end over what is still being judged', () => {
    expect(stretches({ ...UNQUIETENED, judging: 3 })).toStrictEqual([['3rd', '5th']]);
  });

  /** Laid twice, the end still being judged would be drawn fainter than the rest of the wash. */
  it('is laid once over an end both cut short and still being judged', () => {
    expect(stretches({ cutAtStart: false, cutAtEnd: true, judging: 3 })).toStrictEqual([
      ['3rd', '5th'],
    ]);
  });

  /**
   * Washed at both ends, three months would be washed from end to end, and a picture faded all
   * over reads as nothing faded at all.
   */
  it('leaves the ends cut short alone where nothing between them would stay at full strength', () => {
    expect(stretches(CUT_BOTH_ENDS, THREE)).toBeUndefined();
  });

  it('still runs over what is being judged where the ends cut short are left alone', () => {
    expect(stretches({ ...CUT_BOTH_ENDS, judging: 2 })).toStrictEqual([['2nd', '5th']]);
  });

  it('runs over the whole run once where none of it is settled', () => {
    expect(stretches({ cutAtStart: true, cutAtEnd: false, judging: 0 })).toStrictEqual([
      ['1st', '5th'],
    ]);
  });

  it('is not laid over a run with nothing in it', () => {
    expect(washOver([], { ...CUT_BOTH_ENDS, judging: 0 }, PALETTE)).toBeUndefined();
  });
});

describe('a run drawn as columns', () => {
  it('fades each column cut short or still being judged on its own', () => {
    expect(
      quietened([1, 2, 3, 4, 5], { cutAtStart: true, cutAtEnd: false, judging: 4 }),
    ).toStrictEqual([faded(1), 2, 3, 4, faded(5)]);
  });

  it('fades both months cut short and leaves the whole one between them as it is', () => {
    expect(quietened([1200, 1350, 1100], CUT_BOTH_ENDS)).toStrictEqual([
      faded(1200),
      1350,
      faded(1100),
    ]);
  });

  it('leaves every column as it is where nothing is quietened', () => {
    expect(quietened([1, 2, 3], UNQUIETENED)).toStrictEqual([1, 2, 3]);
  });

  /** A month of all time never counted draws no column, faded or not. */
  it('draws nothing for a figure never counted, even where its bucket is quietened', () => {
    expect(quietened([null, 2, null], CUT_BOTH_ENDS)).toStrictEqual([null, 2, null]);
  });

  /**
   * Two months both cut short have no column between them, and both faded would be every column
   * faded — which reads as none of them faded at all.
   */
  it('leaves two columns both cut short as they are', () => {
    expect(quietened([500, 700], CUT_BOTH_ENDS)).toStrictEqual([500, 700]);
  });

  it('fades the one end cut short of two where the other stays at full strength', () => {
    expect(quietened([500, 700], { ...UNQUIETENED, cutAtStart: true })).toStrictEqual([
      faded(500),
      700,
    ]);
  });

  /** Every column between the ends still being judged leaves nothing to read the ends against. */
  it('leaves the ends cut short as they are where everything between them is being judged', () => {
    expect(quietened([1, 2, 3], { ...CUT_BOTH_ENDS, judging: 1 })).toStrictEqual([
      1,
      faded(2),
      faded(3),
    ]);
  });

  it('still fades what is being judged where the ends cut short are left as they are', () => {
    expect(quietened([500, 700], { ...CUT_BOTH_ENDS, judging: 1 })).toStrictEqual([
      500,
      faded(700),
    ]);
  });
});

describe('whether the weeks, months or years a period cuts short are drawn quietly', () => {
  it('is so as columns wherever a column between the ends stays at full strength', () => {
    expect(quietensCutShort(CUT_BOTH_ENDS, THREE.length, true)).toBe(true);
    expect(quietensCutShort({ ...UNQUIETENED, cutAtStart: true }, 2, true)).toBe(true);
  });

  it('is not so as columns where no column between the ends stays at full strength', () => {
    expect(quietensCutShort(CUT_BOTH_ENDS, 2, true)).toBe(false);
    expect(quietensCutShort({ ...CUT_BOTH_ENDS, judging: 1 }, THREE.length, true)).toBe(false);
    expect(quietensCutShort({ ...UNQUIETENED, cutAtStart: true, judging: 1 }, 2, true)).toBe(false);
  });

  it('is so as an area only where something between the ends stays at full strength', () => {
    expect(quietensCutShort(CUT_BOTH_ENDS, THREE.length, false)).toBe(false);
    expect(quietensCutShort(CUT_BOTH_ENDS, 4, false)).toBe(true);
    expect(quietensCutShort({ ...CUT_BOTH_ENDS, judging: 2 }, FIVE.length, false)).toBe(false);
  });

  it('is never so where nothing is cut short', () => {
    expect(quietensCutShort({ ...UNQUIETENED, judging: 0 }, FIVE.length, true)).toBe(false);
  });
});

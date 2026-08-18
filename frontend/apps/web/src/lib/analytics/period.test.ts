import { describe, expect, it } from 'vitest';
import { windowFor } from '@/lib/analytics/period';

/** Mid-afternoon in Kolkata, mid-morning in London, and nowhere near a day boundary. */
const NOW = new Date('2026-08-18T09:37:12Z');

describe('the period a screen asks about', () => {
  it("starts at midnight in the site's own day, not at the moment of asking", () => {
    const { from } = windowFor(7, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-08-11T18:30:00.000Z');
  });

  it('reaches back a whole month when asked for one', () => {
    const { from } = windowFor(30, 'Asia/Kolkata', NOW);

    expect(from).toBe('2026-07-19T18:30:00.000Z');
  });

  /**
   * The end has to cover everything up to now, and has to be the same answer for a while: it names
   * the cached copy of every number on the screen, and one that moved with the clock would make
   * the dashboard re-ask the engine on every render.
   */
  it('runs to the end of the hour, so the same question is asked all hour', () => {
    expect(windowFor(7, 'Etc/UTC', NOW).to).toBe('2026-08-18T10:00:00.000Z');
    expect(windowFor(7, 'Etc/UTC', new Date('2026-08-18T09:02:00Z')).to).toBe(
      '2026-08-18T10:00:00.000Z',
    );
  });

  it('counts a day west of the meridian in that place, not in UTC', () => {
    const { from } = windowFor(7, 'America/New_York', NOW);

    expect(from).toBe('2026-08-12T04:00:00.000Z');
  });

  /**
   * The clocks in New York went forward at 02:00 on 8 March 2026. Midnight that morning was still
   * on the old offset, so placing it with the offset in force later the same day lands an hour
   * early — which is the whole reason the offset is read twice.
   */
  it('places midnight on the day the clocks change with the offset that morning', () => {
    const { from } = windowFor(7, 'America/New_York', new Date('2026-03-14T12:00:00Z'));

    expect(from).toBe('2026-03-08T05:00:00.000Z');
  });

  it('treats a zone it cannot measure as UTC rather than refusing to draw anything', () => {
    const { from } = windowFor(7, 'Etc/UTC', NOW);

    expect(from).toBe('2026-08-12T00:00:00.000Z');
  });
});

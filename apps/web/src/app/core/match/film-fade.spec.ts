import { FADE_MILLISECONDS, FadeEdge, fadeAlpha, fadeEdgesFor } from './film-fade';

/**
 * The dip to dark over a cut or a jump (`replay-v4`).
 *
 * The screen darkens and clears over about 200 ms of *real* time, so the film span the dip covers scales with
 * the playback speed: the eye measures the dip, not the match.
 */

describe('fadeAlpha', () => {
  const cut: readonly FadeEdge[] = [{ filmMilliseconds: 10_000, kind: 'cut' }];

  it('is clear away from every edge', () => {
    expect(fadeAlpha(cut, 5_000, 1)).toBe(0);
    expect(fadeAlpha(cut, 15_000, 1)).toBe(0);
    expect(fadeAlpha([], 10_000, 1)).toBe(0);
  });

  it('is black at the instant of a cut and dips symmetrically around it', () => {
    const half = FADE_MILLISECONDS / 2;

    expect(fadeAlpha(cut, 10_000, 1)).toBe(1);
    expect(fadeAlpha(cut, 10_000 - half / 2, 1)).toBeCloseTo(0.5, 9);
    expect(fadeAlpha(cut, 10_000 + half / 2, 1)).toBeCloseTo(0.5, 9);
    expect(fadeAlpha(cut, 10_000 - half, 1)).toBe(0);
    expect(fadeAlpha(cut, 10_000 + half, 1)).toBe(0);
  });

  it('lasts about 200 ms of real time at every speed, so it spans more film the faster the film plays', () => {
    // At 8x the dip's half is 800 ms of film, which is still 100 ms of real time.
    expect(fadeAlpha(cut, 10_000 + 700, 8)).toBeGreaterThan(0);
    expect(fadeAlpha(cut, 10_000 + 700, 1)).toBe(0);
    expect(fadeAlpha(cut, 10_000 + 800, 8)).toBe(0);
    // And a quarter of the film at half speed.
    expect(fadeAlpha(cut, 10_000 + 60, 0.5)).toBe(0);
    expect(fadeAlpha(cut, 10_000 + 40, 0.5)).toBeGreaterThan(0);
  });

  it('only darkens up to a jump the playhead is about to make, and only clears after one it has made', () => {
    const jump: readonly FadeEdge[] = [
      { filmMilliseconds: 20_000, kind: 'leave' },
      { filmMilliseconds: 60_000, kind: 'enter' },
    ];

    expect(fadeAlpha(jump, 19_950, 1)).toBeGreaterThan(0);
    expect(fadeAlpha(jump, 20_000, 1)).toBe(1);
    expect(fadeAlpha(jump, 20_050, 1)).toBe(0);
    expect(fadeAlpha(jump, 59_950, 1)).toBe(0);
    expect(fadeAlpha(jump, 60_000, 1)).toBe(1);
    expect(fadeAlpha(jump, 60_050, 1)).toBeGreaterThan(0);
    expect(fadeAlpha(jump, 60_100, 1)).toBe(0);
  });

  it('is the darkest of the edges that reach the playhead', () => {
    const edges: readonly FadeEdge[] = [
      { filmMilliseconds: 10_000, kind: 'cut' },
      { filmMilliseconds: 10_040, kind: 'cut' },
    ];

    expect(fadeAlpha(edges, 10_040, 1)).toBe(1);
    expect(fadeAlpha(edges, 10_020, 1)).toBeCloseTo(0.8, 9);
  });

  it('copes with a nonsensical speed rather than dividing by it', () => {
    expect(Number.isFinite(fadeAlpha(cut, 10_000, 0))).toBe(true);
  });
});

describe('fadeEdgesFor', () => {
  it('has an edge for each cut', () => {
    const edges = fadeEdgesFor(
      [
        { startMilliseconds: 311_000, durationMilliseconds: 300, kind: 'kick_off' },
        { startMilliseconds: 319_000, durationMilliseconds: 300, kind: 'half_time' },
      ],
      [{ startMilliseconds: 0, endMilliseconds: 600_000 }],
    );

    expect(edges).toEqual([
      { filmMilliseconds: 311_000, kind: 'cut' },
      { filmMilliseconds: 319_000, kind: 'cut' },
    ]);
  });

  it('adds the jumps between the windows of a reel, and none at its very start or end', () => {
    const edges = fadeEdgesFor(
      [],
      [
        { startMilliseconds: 10_000, endMilliseconds: 40_000 },
        { startMilliseconds: 100_000, endMilliseconds: 130_000 },
        { startMilliseconds: 300_000, endMilliseconds: 330_000 },
      ],
    );

    expect(edges).toEqual([
      { filmMilliseconds: 40_000, kind: 'leave' },
      { filmMilliseconds: 100_000, kind: 'enter' },
      { filmMilliseconds: 130_000, kind: 'leave' },
      { filmMilliseconds: 300_000, kind: 'enter' },
    ]);
  });

  it('has nothing to fade over in an uncut film played whole', () => {
    expect(fadeEdgesFor([], [{ startMilliseconds: 0, endMilliseconds: 600_000 }])).toEqual([]);
  });
});

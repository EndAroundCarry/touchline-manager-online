import { FilmTrack } from '../../../core/match/film-timeline';
import {
  KeyframeSample,
  TrackInterpolator,
  emptySample,
  hermite,
  monotoneTangents,
  planarTangents,
} from './keyframe-interpolator';

/**
 * The interpolator's geometry guarantees (`§9.1`, `§9.4`, `replay-v4`).
 *
 * Players move on a cubic Hermite spline with planar tangents that knows how far apart in time its keyframes
 * are, and the ball moves in straight lines. What a manager would call "fluid" is asserted here as numbers: the
 * curve stays within 0.3 m of the line between the two keyframes it joins (no overshoot), has one velocity
 * where two segments meet (no kink), keeps its speed through a bend, reaches zero at a real stop, and does not
 * blend across a cut.
 */

/** The overshoot a planar spline may have, in film units along x (0.3 m of a 105 m pitch on a 10 000 grid). */
const GUARD_X = 0.3 / (105 / 10_000);
const GUARD_Y = 0.3 / (68 / 10_000);

interface Point {
  readonly t: number;
  readonly x: number;
  readonly y?: number;
  readonly z?: number;
  readonly speed?: number;
  readonly action?: string | null;
}

function trackOf(points: readonly Point[]): FilmTrack {
  return {
    entityId: 'H9',
    length: points.length,
    times: Float64Array.from(points.map((point) => point.t)),
    xs: Float64Array.from(points.map((point) => point.x)),
    ys: Float64Array.from(points.map((point) => point.y ?? 0)),
    zs: Float64Array.from(points.map((point) => point.z ?? 0)),
    speeds: Float64Array.from(points.map((point) => point.speed ?? 0)),
    actions: points.map((point) => point.action ?? null),
  };
}

function sampled(interpolator: TrackInterpolator, time: number): KeyframeSample {
  const out = emptySample();

  interpolator.sample(time, out);

  return out;
}

describe('monotoneTangents', () => {
  it('is the slope itself for points on a line, however unevenly they are spaced in time', () => {
    const tangents = monotoneTangents([0, 100, 1_000, 5_000], [0, 10, 100, 500]);

    for (const tangent of tangents) {
      expect(tangent).toBeCloseTo(0.1, 9);
    }
  });

  it('is zero at a turning point, which is what keeps a curve from swinging past it', () => {
    const tangents = monotoneTangents([0, 1_000, 2_000], [0, 100, 0]);

    expect(tangents[1]).toBe(0);
  });

  it('is zero at both ends of a flat segment', () => {
    const tangents = monotoneTangents([0, 1_000, 2_000, 3_000], [0, 50, 50, 100]);

    expect(tangents[1]).toBe(0);
    expect(tangents[2]).toBe(0);
  });

  it('is limited where a steep segment meets a shallow one, so the curve cannot overshoot the steep one', () => {
    const times = [0, 1_000, 1_001, 2_000];
    const values = [0, 0, 1_000, 1_000];
    const tangents = monotoneTangents(times, values);

    // The middle segment climbs 1,000 in a millisecond; a tangent bigger than three times its slope would
    // send the Hermite curve through the roof.
    expect(Math.abs(tangents[1])).toBeLessThanOrEqual(3 * 1_000 + 1e-9);
    expect(Math.abs(tangents[2])).toBeLessThanOrEqual(3 * 1_000 + 1e-9);
  });

  it('treats two keyframes at one instant as a break and builds each side from its own side only', () => {
    const tangents = monotoneTangents([0, 1_000, 1_000, 2_000], [0, 100, 900, 1_000]);

    expect(tangents[1]).toBeCloseTo(0.1, 9);
    expect(tangents[2]).toBeCloseTo(0.1, 9);
  });

  it('has no tangents to give a track of one keyframe', () => {
    expect([...monotoneTangents([5], [7])]).toEqual([0]);
  });
});

describe('planarTangents', () => {
  /** Metres per second of a tangent in film units per millisecond, along x. */
  const speedX = (tangent: number): number => tangent * (105 / 10_000) * 1_000;

  it('keeps most of the speed round a right-angled turn, which a per-axis spline stopped dead on', () => {
    // 5 m/s east for two seconds, then 5 m/s south: 10 m is 952 units east, 10 m is 1 470 units south.
    const { x, y } = planarTangents([0, 2_000, 4_000], [0, 1_905, 1_905], [0, 0, 2_941]);
    const speed = Math.hypot(speedX(x[1]), y[1] * (68 / 10_000) * 1_000);

    expect(speed).toBeGreaterThanOrEqual(0.6 * 5);
  });

  it('is zero at both ends of a track, and at a real stop', () => {
    const { x, y } = planarTangents(
      [0, 1_000, 2_000, 3_000],
      [0, 1_000, 1_000, 2_000],
      [0, 0, 0, 0],
    );

    expect(x[0]).toBe(0);
    expect(x[3]).toBe(0);
    expect(x[1]).toBeLessThan(1e-9);
    expect(y[1]).toBe(0);
  });

  it('is zero either side of a cut', () => {
    const { x } = planarTangents(
      [0, 1_000, 1_000, 2_000, 3_000],
      [0, 500, 4_000, 4_500, 5_000],
      [0, 0, 0, 0, 0],
    );

    expect(x[1]).toBe(0);
    expect(x[2]).toBe(0);
    expect(x[3]).toBeGreaterThan(0);
  });

  it('is the velocity itself for a straight run, however unevenly it is sampled', () => {
    const { x } = planarTangents([0, 100, 1_000, 5_000], [0, 10, 100, 500], [0, 0, 0, 0]);

    expect(x[1]).toBeCloseTo(0.1, 9);
    expect(x[2]).toBeCloseTo(0.1, 9);
  });
});

describe('hermite', () => {
  it('passes exactly through its two values at either end', () => {
    expect(hermite(3, 9, 1, -2, 1_000, 0)).toBeCloseTo(3, 9);
    expect(hermite(3, 9, 1, -2, 1_000, 1)).toBeCloseTo(9, 9);
  });

  it("is the straight line between its values when both tangents are the line's slope", () => {
    expect(hermite(0, 10, 0.01, 0.01, 1_000, 0.25)).toBeCloseTo(2.5, 9);
  });
});

describe('TrackInterpolator: holding and joining', () => {
  it('has nothing to sample for an empty track', () => {
    const interpolator = new TrackInterpolator(trackOf([]), true);

    expect(interpolator.sample(100, emptySample())).toBe(false);
    expect(interpolator.sampleAt(100, emptySample())).toBe(false);
  });

  it('holds the first keyframe before the track starts and the last after it ends', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 500, x: 1_000, y: 2_000 },
        { t: 1_500, x: 3_000, y: 4_000 },
      ]),
      true,
    );

    expect(sampled(interpolator, 0)).toMatchObject({ x: 1_000, y: 2_000 });
    expect(sampled(interpolator, 500)).toMatchObject({ x: 1_000, y: 2_000 });
    expect(sampled(interpolator, 9_000)).toMatchObject({ x: 3_000, y: 4_000 });
  });

  it('holds a single keyframe, which is a stationary anchor', () => {
    const interpolator = new TrackInterpolator(trackOf([{ t: 0, x: 7, y: 9 }]), true);

    expect(sampled(interpolator, 4_000)).toMatchObject({ x: 7, y: 9 });
  });

  it('passes through every keyframe exactly, so a strike or a dive lands when it was authored', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, y: 0 },
        { t: 1_000, x: 5_000, y: 3_000, action: 'shot' },
        { t: 2_500, x: 8_000, y: 6_000 },
      ]),
      true,
    );

    expect(sampled(interpolator, 1_000)).toMatchObject({ x: 5_000, y: 3_000 });
    expect(sampled(interpolator, 2_500)).toMatchObject({ x: 8_000, y: 6_000 });
  });

  it("joins a ball's keyframes with straight lines", () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, y: 0, z: 0 },
        { t: 1_000, x: 10_000, y: 5_000, z: 40 },
        { t: 2_000, x: 10_000, y: 5_000, z: 0 },
      ]),
      false,
    );

    expect(sampled(interpolator, 500)).toMatchObject({ x: 5_000, y: 2_500, z: 20 });
    expect(sampled(interpolator, 250)).toMatchObject({ x: 2_500, y: 1_250, z: 10 });
    expect(sampled(interpolator, 1_500)).toMatchObject({ x: 10_000, z: 20 });
  });

  it('takes the speed and the action from the keyframe the segment starts on', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, action: 'run' },
        { t: 1_000, x: 1_000, speed: 4_000, action: 'shot' },
        { t: 2_000, x: 2_000 },
      ]),
      true,
    );

    expect(sampled(interpolator, 500).action).toBe('run');
    expect(sampled(interpolator, 1_500).action).toBe('shot');
    expect(sampled(interpolator, 1_500).speed).toBe(4_000);
  });

  it("holds the last keyframe's action after the track ends", () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0 },
        { t: 1_000, x: 1_000, action: 'celebrate' },
      ]),
      true,
    );

    expect(sampled(interpolator, 9_000).action).toBe('celebrate');
  });
});

describe('TrackInterpolator: fluid movement', () => {
  /** A run that stops, doubles back, and sprints off, sampled unevenly the way the server sends it. */
  const ragged: readonly Point[] = [
    { t: 0, x: 1_000 },
    { t: 150, x: 1_200 },
    { t: 1_900, x: 4_000 },
    { t: 2_000, x: 4_100 },
    { t: 4_500, x: 2_500 },
    { t: 4_600, x: 2_400 },
    { t: 5_200, x: 7_900 },
    { t: 9_000, x: 8_000 },
  ];

  it('never strays more than 0.3 m outside the two keyframes it is between, so there is no real overshoot', () => {
    const interpolator = new TrackInterpolator(trackOf(ragged), true);

    for (let time = 0; time <= 9_000; time += 7) {
      const x = sampled(interpolator, time).x;
      const index = ragged.findIndex((point, position) => {
        const next = ragged[position + 1];

        return next !== undefined && time >= point.t && time <= next.t;
      });

      if (index < 0) {
        continue;
      }

      const low = Math.min(ragged[index].x, ragged[index + 1].x);
      const high = Math.max(ragged[index].x, ragged[index + 1].x);

      expect(x, `x at ${time} ms`).toBeGreaterThanOrEqual(low - GUARD_X);
      expect(x, `x at ${time} ms`).toBeLessThanOrEqual(high + GUARD_X);
    }
  });

  it('turns a corner without swinging past it', () => {
    // A right-angled path: out along the touchline and then in. The Catmull-Rom this replaces left the pitch.
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, y: 0 },
        { t: 1_000, x: 10_000, y: 0 },
        { t: 2_000, x: 10_000, y: 10_000 },
      ]),
      true,
    );

    for (let time = 0; time <= 2_000; time += 10) {
      const sample = sampled(interpolator, time);

      expect(sample.x).toBeLessThanOrEqual(10_000 + GUARD_X + 1e-6);
      expect(sample.x).toBeGreaterThanOrEqual(-GUARD_X - 1e-6);
      expect(sample.y).toBeGreaterThanOrEqual(-GUARD_Y - 1e-6);
      expect(sample.y).toBeLessThanOrEqual(10_000 + GUARD_Y + 1e-6);
    }
  });

  it('has one velocity where two segments meet, so a run has no kink at a keyframe', () => {
    const interpolator = new TrackInterpolator(trackOf(ragged), true);
    const step = 0.5;

    for (const point of ragged.slice(1, -1)) {
      const before = sampled(interpolator, point.t - step).x;
      const at = sampled(interpolator, point.t).x;
      const after = sampled(interpolator, point.t + step).x;
      const incoming = (at - before) / step;
      const outgoing = (after - at) / step;

      // A turning point has no velocity to be continuous about; elsewhere the two sides must agree to within
      // the tangent's own slope across half a millisecond.
      expect(Math.abs(incoming - outgoing), `the velocity at ${point.t} ms`).toBeLessThan(0.5);
    }
  });

  it('does not wobble across a long gap that follows a short one', () => {
    // 100 ms and then 4.9 s: a spline that assumed even spacing would bow the long segment.
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0 },
        { t: 100, x: 400 },
        { t: 5_000, x: 4_000 },
      ]),
      true,
    );
    let previous = -Infinity;

    for (let time = 0; time <= 5_000; time += 25) {
      const x = sampled(interpolator, time).x;

      expect(x, `x at ${time} ms`).toBeGreaterThanOrEqual(previous - 1e-6);
      previous = x;
    }
  });

  it('keeps a jump of altitude inside the range it joins', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, z: 0 },
        { t: 500, x: 0, z: 60, action: 'header' },
        { t: 1_000, x: 0, z: 0 },
      ]),
      true,
    );

    for (let time = 0; time <= 1_000; time += 10) {
      const z = sampled(interpolator, time).z;

      expect(z).toBeGreaterThanOrEqual(0);
      expect(z).toBeLessThanOrEqual(60);
    }

    expect(sampled(interpolator, 500).z).toBe(60);
  });
});

describe('TrackInterpolator: bends and stops', () => {
  it('keeps at least 60% of its speed through a right-angled turn at a keyframe', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, y: 0 },
        { t: 2_000, x: 1_905, y: 0 },
        { t: 4_000, x: 1_905, y: 2_941 },
      ]),
      true,
    );
    const step = 20;
    const before = sampled(interpolator, 2_000 - step);
    const after = sampled(interpolator, 2_000 + step);
    const metresPerSecond =
      Math.hypot(((after.x - before.x) * 105) / 10_000, ((after.y - before.y) * 68) / 10_000) /
      ((2 * step) / 1_000);

    expect(metresPerSecond).toBeGreaterThanOrEqual(0.6 * 5);
  });

  it('comes to rest where the player really stops', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0 },
        { t: 1_000, x: 500 },
        { t: 2_000, x: 500 },
        { t: 3_000, x: 1_000 },
      ]),
      true,
    );
    const step = 5;
    const speed =
      Math.abs(sampled(interpolator, 1_500 + step).x - sampled(interpolator, 1_500 - step).x) /
      (2 * step);

    expect(speed).toBeLessThan(0.0005);
  });

  it('stays within 0.3 m of the line between its keyframes on a sharp reversal', () => {
    const interpolator = new TrackInterpolator(
      trackOf([
        { t: 0, x: 0, y: 0 },
        { t: 300, x: 3_000, y: 0 },
        { t: 600, x: 0, y: 40 },
        { t: 900, x: 3_000, y: 0 },
      ]),
      true,
    );

    for (let time = 0; time <= 900; time += 5) {
      const sample = sampled(interpolator, time);

      expect(sample.x).toBeGreaterThanOrEqual(-GUARD_X - 1e-6);
      expect(sample.x).toBeLessThanOrEqual(3_000 + GUARD_X + 1e-6);
      expect(Math.abs(sample.y)).toBeLessThanOrEqual(GUARD_Y + 40 + 1e-6);
    }
  });
});

describe('TrackInterpolator: cuts', () => {
  const stepped = trackOf([
    { t: 0, x: 1_000, y: 1_000 },
    { t: 4_000, x: 2_000, y: 1_000 },
    // The film cut here: the next keyframe is at the same instant, somewhere else entirely.
    { t: 4_000, x: 5_000, y: 5_000 },
    { t: 7_000, x: 5_400, y: 5_000 },
  ]);

  it('is at the first keyframe until the instant of the cut and at the second from it', () => {
    const interpolator = new TrackInterpolator(stepped, true);

    expect(sampled(interpolator, 3_999.9)).toMatchObject({ y: 1_000 });
    expect(sampled(interpolator, 3_999.9).x).toBeCloseTo(2_000, 0);
    expect(sampled(interpolator, 4_000)).toMatchObject({ x: 5_000, y: 5_000 });
    expect(sampled(interpolator, 4_001).x).toBeGreaterThan(4_999);
  });

  it('is never between the two places, however finely it is sampled', () => {
    const interpolator = new TrackInterpolator(stepped, true);

    for (let time = 3_900; time <= 4_100; time += 0.5) {
      const { x, y } = sampled(interpolator, time);
      const before = x <= 2_000.001 && Math.abs(y - 1_000) < 1e-6;
      const after = x >= 4_999 && Math.abs(y - 5_000) < 1e-6;

      expect(before || after, `a position between the two places at ${time} ms`).toBe(true);
    }
  });

  it('does the same for a ball, which is not curved', () => {
    const interpolator = new TrackInterpolator(stepped, false);

    expect(sampled(interpolator, 3_999.9).x).toBeCloseTo(2_000, 0);
    expect(sampled(interpolator, 4_000).x).toBe(5_000);
  });
});

describe('TrackInterpolator: the cursor', () => {
  const long = trackOf(
    Array.from({ length: 200 }, (_, index) => ({
      t: index * 100,
      x: index * 50 + (index % 7) * 20,
      y: 5_000 + (index % 5) * 100,
    })),
  );

  it('gives the same answer playing forward, seeking about, and looking back along the track', () => {
    const forward = new TrackInterpolator(long, true);
    const seeking = new TrackInterpolator(long, true);
    const lookBack = new TrackInterpolator(long, true);
    const moments = [0, 1_234, 1_240, 1_300, 9_999, 40, 41, 19_900, 5_000, 5_001];

    for (const time of moments) {
      const expected = sampled(new TrackInterpolator(long, true), time);

      expect(sampled(seeking, time)).toEqual(expected);

      const out = emptySample();

      lookBack.sampleAt(time, out);

      expect(out).toEqual(expected);
    }

    // Forward through every frame of a minute of 60 fps film.
    for (let time = 0; time <= 19_900; time += 16.667) {
      expect(sampled(forward, time)).toEqual(sampled(new TrackInterpolator(long, true), time));
    }
  });

  it('does not move the cursor when it is only looking back along the track', () => {
    const interpolator = new TrackInterpolator(long, true);
    const here = sampled(interpolator, 10_000);
    const trail = emptySample();

    interpolator.sampleAt(9_000, trail);
    interpolator.sampleAt(9_620, trail);

    expect(sampled(interpolator, 10_000)).toEqual(here);
  });

  it('reuses the sample it is given rather than making one', () => {
    const interpolator = new TrackInterpolator(long, true);
    const out = emptySample();

    interpolator.sample(1_000, out);
    const first = out.x;
    interpolator.sample(3_000, out);

    expect(out.x).not.toBe(first);
  });
});

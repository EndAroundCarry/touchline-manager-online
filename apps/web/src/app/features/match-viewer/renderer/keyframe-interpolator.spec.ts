import { catmullRom, frameAt, positionAt, sampleAt } from './keyframe-interpolator';
import { RendererEntity, RendererTrack } from './renderer.models';

/** The renderer's geometry guarantees (`§9.1`, `§9.4`, master plan Stage 6). */

describe('catmullRom', () => {
  it('passes exactly through the two middle control values', () => {
    expect(catmullRom(1, 2, 3, 4, 0)).toBeCloseTo(2, 6);
    expect(catmullRom(1, 2, 3, 4, 1)).toBeCloseTo(3, 6);
  });

  it('is a straight line when the control points are evenly spaced', () => {
    expect(catmullRom(10, 20, 30, 40, 0.5)).toBeCloseTo(25, 6);
    expect(catmullRom(10, 20, 30, 40, 0.25)).toBeCloseTo(22.5, 6);
  });
});

describe('positionAt', () => {
  it('returns nothing for an empty track', () => {
    expect(positionAt([], 100)).toBeNull();
  });

  it('holds the first keyframe before the track starts', () => {
    const track = [
      { timeMilliseconds: 500, x: 1_000, y: 2_000 },
      { timeMilliseconds: 1_500, x: 3_000, y: 4_000 },
    ];

    expect(positionAt(track, 0)).toEqual({ x: 1_000, y: 2_000 });
    expect(positionAt(track, 500)).toEqual({ x: 1_000, y: 2_000 });
  });

  it('interpolates between the two keyframes that bracket the moment', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0 },
      { timeMilliseconds: 1_000, x: 10_000, y: 5_000 },
    ];

    expect(positionAt(track, 500)).toEqual({ x: 5_000, y: 2_500 });
    expect(positionAt(track, 250)).toEqual({ x: 2_500, y: 1_250 });
  });

  it('holds the last keyframe after the track ends rather than extrapolating', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0 },
      { timeMilliseconds: 1_000, x: 10_000, y: 5_000 },
    ];

    expect(positionAt(track, 5_000)).toEqual({ x: 10_000, y: 5_000 });
  });

  it('handles a single keyframe, which is a stationary anchor', () => {
    const track = [{ timeMilliseconds: 0, x: 7, y: 9 }];

    expect(positionAt(track, 4_000)).toEqual({ x: 7, y: 9 });
  });

  it('passes through a middle keyframe exactly, so a semantic anchor is not smoothed away', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0 },
      { timeMilliseconds: 1_000, x: 5_000, y: 3_000, action: 'shot' },
      { timeMilliseconds: 2_000, x: 8_000, y: 6_000 },
    ];

    expect(positionAt(track, 1_000)).toEqual({ x: 5_000, y: 3_000 });
  });

  it('keeps a spline inside the pitch even where the curve would overshoot it', () => {
    // A right-angled path spiked by its next sample: the unclamped spline reaches past 10,000, the drawn
    // one stops on the touchline rather than off the canvas.
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0 },
      { timeMilliseconds: 1_000, x: 10_000, y: 0 },
      { timeMilliseconds: 2_000, x: 10_000, y: 10_000 },
    ];

    const sample = positionAt(track, 1_500);

    expect(sample!.x).toBeLessThanOrEqual(10_000);
    expect(sample!.y).toBeGreaterThanOrEqual(0);
  });
});

describe('sampleAt', () => {
  it('interpolates altitude between the two keyframes that bracket the moment', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0, z: 0 },
      { timeMilliseconds: 1_000, x: 0, y: 0, z: 40 },
    ];

    expect(sampleAt(track, 500)!.z).toBeCloseTo(20, 6);
  });

  it('takes the speed and the action from the keyframe the segment starts on', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0, action: 'run' },
      { timeMilliseconds: 1_000, x: 1_000, y: 0, speed: 4_000, action: 'shot' },
      { timeMilliseconds: 2_000, x: 2_000, y: 0 },
    ];

    expect(sampleAt(track, 500)!.action).toBe('run');
    expect(sampleAt(track, 1_500)!.action).toBe('shot');
    expect(sampleAt(track, 1_500)!.speed).toBe(4_000);
  });

  it('holds the last keyframe after the track ends, action included', () => {
    const track = [
      { timeMilliseconds: 0, x: 0, y: 0 },
      { timeMilliseconds: 1_000, x: 1_000, y: 0, action: 'celebrate' },
    ];

    expect(sampleAt(track, 9_000)!.action).toBe('celebrate');
  });
});

describe('frameAt', () => {
  const entity = (id: string, x: number, y: number): RendererEntity => ({
    id,
    isBall: false,
    side: 'home',
    participantId: null,
    shirtNumber: 9,
    family: 'attack',
    name: null,
    anchor: { x, y },
  });

  it('interpolates the entities that have a track', () => {
    const tracks: RendererTrack[] = [
      {
        entityId: 'H9',
        keyframes: [
          { timeMilliseconds: 0, x: 0, y: 0 },
          { timeMilliseconds: 1_000, x: 10_000, y: 0 },
        ],
      },
    ];

    const frame = frameAt([entity('H9', 0, 0)], tracks, 500);

    expect(frame[0].position).toEqual({ x: 5_000, y: 0 });
  });

  it('holds an entity with no track at its anchor, at ground level', () => {
    const frame = frameAt([entity('H4', 2_000, 3_000)], [], 500);

    expect(frame[0].position).toEqual({ x: 2_000, y: 3_000 });
    expect(frame[0].z).toBe(0);
    expect(frame[0].action).toBeNull();
  });

  it('carries altitude and action through to the frame', () => {
    const tracks: RendererTrack[] = [
      {
        entityId: 'ball',
        keyframes: [
          { timeMilliseconds: 0, x: 0, y: 0, z: 0 },
          { timeMilliseconds: 1_000, x: 1_000, y: 0, z: 30 },
        ],
      },
    ];
    const ball: RendererEntity = {
      id: 'ball',
      isBall: true,
      side: null,
      participantId: null,
      shirtNumber: 0,
      family: null,
      name: null,
      anchor: { x: 0, y: 0 },
    };

    expect(frameAt([ball], tracks, 500)[0].z).toBeCloseTo(15, 6);
  });
});

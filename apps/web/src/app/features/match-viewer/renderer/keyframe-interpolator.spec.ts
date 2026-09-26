import { frameAt, positionAt } from './keyframe-interpolator';
import { RendererEntity, RendererTrack } from './renderer.models';

/** The renderer's geometry guarantees (`§9.1`, `§9.4`). */

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
});

describe('frameAt', () => {
  const entity = (id: string, x: number, y: number): RendererEntity => ({
    id,
    isBall: false,
    side: 'home',
    shirtNumber: 9,
    family: 'attack',
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

  it('holds an entity with no track at its anchor', () => {
    const frame = frameAt([entity('H4', 2_000, 3_000)], [], 500);

    expect(frame[0].position).toEqual({ x: 2_000, y: 3_000 });
  });
});

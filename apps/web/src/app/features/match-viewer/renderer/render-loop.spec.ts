import { RenderLoop } from './render-loop';

/**
 * The animation loop's lifetime guarantees (master plan `§9.4`, ADR-0006).
 *
 * The loop is the one thing an animation can leak: it holds a scheduled callback against a canvas that a
 * route change is about to throw away. So the loop is driven by a fake scheduler here rather than by a real
 * browser, and the assertions are about the contract the viewer relies on — one frame scheduled at a time,
 * a delta measured from the previous frame, and a stop that really ends the schedule, including when it is
 * asked for from inside a frame.
 */

describe('RenderLoop', () => {
  let scheduled: Map<number, FrameRequestCallback>;
  let cancelled: number[];
  let nextHandle: number;

  beforeEach(() => {
    scheduled = new Map();
    cancelled = [];
    nextHandle = 1;

    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback): number => {
      const handle = nextHandle;

      nextHandle += 1;
      scheduled.set(handle, callback);

      return handle;
    });

    vi.stubGlobal('cancelAnimationFrame', (handle: number): void => {
      cancelled.push(handle);
      scheduled.delete(handle);
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Runs every frame currently scheduled at a timestamp, as the browser would. */
  function frame(timestamp: number): void {
    const due = [...scheduled.entries()];

    scheduled.clear();

    for (const [, callback] of due) {
      callback(timestamp);
    }
  }

  it('measures a delta from the previous frame, with a zero on the first', () => {
    const deltas: number[] = [];
    const loop = new RenderLoop((delta) => deltas.push(delta));

    loop.start();

    expect(scheduled.size).toBe(1);

    frame(100);
    frame(116);
    frame(140);

    expect(deltas).toEqual([0, 16, 24]);
  });

  it('does not schedule a second frame when it is started while already running', () => {
    const deltas: number[] = [];
    const loop = new RenderLoop((delta) => deltas.push(delta));

    loop.start();
    loop.start();

    expect(scheduled.size).toBe(1);

    frame(20);
    frame(36);

    expect(deltas).toEqual([0, 16]);
  });

  it('stops the schedule and forgets the last frame, so a restart does not apply a stale delta', () => {
    const deltas: number[] = [];
    const loop = new RenderLoop((delta) => deltas.push(delta));

    loop.start();
    frame(1_000);

    loop.stop();

    expect(loop.running).toBe(false);
    expect(scheduled.size).toBe(0);
    expect(cancelled).toHaveLength(1);

    loop.start();
    frame(90_000);

    // The pause between the two runs is not a delta: the first frame of the restart starts the clock again.
    expect(deltas).toEqual([0, 0]);
  });

  it('cancels the frame it just scheduled when the callback stops it', () => {
    const loop = new RenderLoop(() => loop.stop());

    loop.start();
    frame(10);

    expect(loop.running).toBe(false);
    expect(scheduled.size).toBe(0);
    expect(cancelled).toHaveLength(1);
  });

  it('disposes by stopping, so nothing outlives the component', () => {
    const deltas: number[] = [];
    const loop = new RenderLoop((delta) => deltas.push(delta));

    loop.start();
    loop.dispose();

    expect(loop.running).toBe(false);
    expect(scheduled.size).toBe(0);
  });

  it('does nothing where the platform has no animation frame', () => {
    vi.stubGlobal('requestAnimationFrame', undefined);

    const loop = new RenderLoop(() => {
      throw new Error('a loop with nowhere to draw must not run');
    });

    loop.start();

    expect(loop.running).toBe(false);
  });
});

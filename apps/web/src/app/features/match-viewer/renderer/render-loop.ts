/**
 * The animation loop's lifetime (`§9.4`).
 *
 * A thin wrapper over `requestAnimationFrame` whose only job is to be startable and, above all, stoppable:
 * the requirement is that the loop stops on pause, route change, tab hidden, and destruction, so the thing
 * that owns it must be able to end it deterministically rather than leaving a callback scheduled against a
 * canvas that is about to be thrown away.
 *
 * It reads no clock of its own — the browser passes the frame timestamp — so a frame's cost is the only
 * thing that can make a frame long, and the player scales the delta it is given.
 */
export class RenderLoop {
  private handle: number | null = null;
  private previous: number | null = null;

  /** Initializes the loop over a per-frame callback that receives the real elapsed milliseconds. */
  constructor(private readonly onFrame: (deltaMs: number) => void) {}

  /** Whether a frame is currently scheduled. */
  get running(): boolean {
    return this.handle !== null;
  }

  /** Starts the loop. A second call while it is running does nothing. */
  start(): void {
    if (this.handle !== null || typeof requestAnimationFrame !== 'function') {
      return;
    }

    this.previous = null;

    const tick = (timestamp: number): void => {
      // Scheduled before the callback runs, so a callback that stops the loop cancels *this* frame rather
      // than the one already in flight.
      this.handle = requestAnimationFrame(tick);

      const delta = this.previous === null ? 0 : timestamp - this.previous;

      this.previous = timestamp;

      this.onFrame(delta);
    };

    this.handle = requestAnimationFrame(tick);
  }

  /** Stops the loop and forgets the last frame, so a restart does not apply a stale delta. */
  stop(): void {
    if (this.handle !== null) {
      cancelAnimationFrame(this.handle);
      this.handle = null;
    }

    this.previous = null;
  }

  /** Stops the loop. Named for symmetry with the objects that own one. */
  dispose(): void {
    this.stop();
  }
}

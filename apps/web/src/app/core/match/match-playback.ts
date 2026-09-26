import { Highlight } from './match.models';

/** What the replay is doing. */
export type PlaybackState = 'idle' | 'playing' | 'paused' | 'finished';

/** The playback speeds the viewer offers (`§9.4`). */
export const PLAYBACK_SPEEDS = [1, 2, 4] as const;

/** One of the viewer's speeds. */
export type PlaybackSpeed = (typeof PLAYBACK_SPEEDS)[number];

/**
 * The replay's playback state machine.
 *
 * Framework-neutral and free of timers: it is advanced by whatever is driving the animation — a
 * `requestAnimationFrame` loop in the browser, a plain call in a test — with the real elapsed milliseconds.
 * That is what keeps the speed control honest, because the *only* thing a speed setting does is scale the
 * elapsed time before it is applied to the playhead, so the animation and the match clock are always at the
 * same point of the same highlight.
 *
 * Highlights play in event order and never reorder: two chances in the same minute are two entries in the
 * list, and `skipCurrent` walks them one at a time rather than collapsing them (`§9.4`).
 */
export class MatchPlayback {
  private index = 0;
  private position = 0;
  private state: PlaybackState = 'idle';
  private playbackSpeed: PlaybackSpeed = 1;

  /** Initializes the player over a match's highlights, in event order. */
  constructor(private readonly highlights: readonly Highlight[]) {}

  /** Whether there is anything to play at all. */
  get hasHighlights(): boolean {
    return this.highlights.length > 0;
  }

  /** How many highlights the replay holds. */
  get count(): number {
    return this.highlights.length;
  }

  /** The highlight being played, or null when there are none. */
  get active(): Highlight | null {
    return this.highlights[this.index] ?? null;
  }

  /** The index of the highlight being played. */
  get activeIndex(): number {
    return this.index;
  }

  /** How far into the active highlight the playhead stands, in animation milliseconds. */
  get positionMs(): number {
    return this.position;
  }

  /** What the replay is doing. */
  get currentState(): PlaybackState {
    return this.state;
  }

  /** The current speed. */
  get speed(): PlaybackSpeed {
    return this.playbackSpeed;
  }

  /** How long the active highlight runs for, or zero when there are none. */
  get activeDurationMs(): number {
    return this.active?.durationMilliseconds ?? 0;
  }

  /** Starts or resumes, and has no effect once every highlight has played. */
  play(): void {
    if (!this.hasHighlights || this.state === 'finished') {
      return;
    }

    this.state = 'playing';
  }

  /** Holds the playhead where it is. */
  pause(): void {
    if (this.state === 'playing') {
      this.state = 'paused';
    }
  }

  /** Changes the speed. A speed is a scale on the elapsed time, not a jump. */
  setSpeed(speed: PlaybackSpeed): void {
    this.playbackSpeed = speed;
  }

  /**
   * Advances the playhead by the real time that has passed.
   *
   * @returns Whether the active highlight changed, which is what tells a viewer to redraw from a new track.
   */
  advance(realDeltaMs: number): boolean {
    if (this.state !== 'playing' || !this.hasHighlights || realDeltaMs <= 0) {
      return false;
    }

    this.position += realDeltaMs * this.playbackSpeed;

    let changed = false;

    // A long frame — a backgrounded tab returning, a slow device — can cross more than one highlight, so
    // the playhead is walked forward rather than assumed to land inside the current one.
    while (
      this.index < this.highlights.length &&
      this.position >= this.highlights[this.index].durationMilliseconds
    ) {
      this.position -= this.highlights[this.index].durationMilliseconds;
      this.index += 1;
      changed = true;
    }

    if (this.index >= this.highlights.length) {
      // Finished: the playhead rests at the end of the last highlight rather than falling off the list.
      this.index = this.highlights.length - 1;
      this.position = this.highlights[this.index].durationMilliseconds;
      this.state = 'finished';
      changed = true;
    }

    return changed;
  }

  /** Moves to the next highlight, or finishes when this was the last. */
  skipCurrent(): void {
    if (!this.hasHighlights) {
      return;
    }

    if (this.index + 1 >= this.highlights.length) {
      this.skipAll();

      return;
    }

    this.goTo(this.index + 1);
    this.state = 'playing';
  }

  /** Jumps to the end of the replay. */
  skipAll(): void {
    if (!this.hasHighlights) {
      return;
    }

    this.index = this.highlights.length - 1;
    this.position = this.highlights[this.index].durationMilliseconds;
    this.state = 'finished';
  }

  /** Starts the replay again from the first highlight. */
  replay(): void {
    if (!this.hasHighlights) {
      return;
    }

    this.index = 0;
    this.position = 0;
    this.state = 'playing';
  }

  /** Seeks to a highlight by index, clamped to the replay. */
  seekTo(index: number): void {
    if (!this.hasHighlights) {
      return;
    }

    if (index >= this.highlights.length) {
      this.skipAll();

      return;
    }

    this.goTo(Math.max(0, index));

    if (this.state === 'finished') {
      // Seeking backwards from the end leaves the replay paused at the chosen highlight rather than
      // claiming to be finished.
      this.state = 'paused';
    }
  }

  /** Seeks to the highlight that presents an event, if the replay holds one. */
  seekToEvent(sequence: number): boolean {
    const index = this.highlights.findIndex(
      (highlight) => highlight.sourceEventSequence === sequence,
    );

    if (index < 0) {
      return false;
    }

    this.seekTo(index);

    return true;
  }

  private goTo(index: number): void {
    this.index = index;
    this.position = 0;
  }
}

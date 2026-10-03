import { Passage, PlaybackSegment, ReelClip } from './match.models';

/** What the replay is doing. */
export type PlaybackState = 'idle' | 'playing' | 'paused' | 'finished';

/** Which playlist is playing: the whole film, or the reel of selected chance clips (`replay-v3`). */
export type PlaybackMode = 'full' | 'reel';

/** The playback speeds the viewer offers. */
export const PLAYBACK_SPEEDS = [0.5, 1, 2, 4, 8] as const;

/** One of the viewer's speeds. */
export type PlaybackSpeed = (typeof PLAYBACK_SPEEDS)[number];

/** One passage placed on the film clock. */
export interface FilmPassage {
  readonly index: number;
  readonly passage: Passage;
  readonly startMilliseconds: number;
  readonly durationMilliseconds: number;
}

/** A contiguous window of film time the active playlist plays (`replay-v3`). */
export interface PlaylistWindow {
  readonly startMilliseconds: number;
  readonly endMilliseconds: number;
}

/**
 * The replay's playback state machine over one continuous film (`replay-v3`).
 *
 * Framework-neutral and free of timers: it is advanced by whatever is driving the animation — a
 * `requestAnimationFrame` loop in the browser, a plain call in a test — with the real elapsed milliseconds.
 * That is what keeps the speed control honest, because the *only* thing a speed setting does is scale the
 * elapsed time before it is applied to the playhead.
 *
 * There is one film. Every passage the director authored plays in match order from the first kick to full
 * time, so the playhead is a *film* moment rather than an index into a list of chances. The reel is not a
 * second film: it is a playlist of windows over the same film, and `mode` decides which playlist is playing.
 * A window may open mid-passage and close mid-passage, so the reels' clips join on real film time.
 *
 * `advance(delta)` returns whether the active passage changed, which is what tells a viewer to rebuild its
 * renderer from a new set of tracks.
 */
export class MatchPlayback {
  private readonly film: readonly FilmPassage[];
  private readonly filmMilliseconds: number;
  private readonly reelWindows: readonly PlaylistWindow[];
  private readonly fullWindows: readonly PlaylistWindow[];

  private mode: PlaybackMode;
  private windowIndex = 0;
  private position = 0;
  private activeCursor = 0;
  private state: PlaybackState = 'idle';
  private playbackSpeed: PlaybackSpeed = 1;

  /**
   * Initializes the player over a match's film and reel.
   *
   * @param passages The film passages, in match order.
   * @param playback The film schedule: one `passage` segment per passage, in the same order.
   * @param reel The reel's clips, as windows over the film clock.
   * @param mode Which playlist to start on.
   */
  constructor(
    passages: readonly Passage[],
    playback: readonly PlaybackSegment[] = [],
    reel: readonly ReelClip[] = [],
    mode: PlaybackMode = 'full',
  ) {
    this.film = placePassages(passages, playback);
    this.filmMilliseconds =
      this.film.length === 0
        ? 0
        : this.film[this.film.length - 1].startMilliseconds +
          this.film[this.film.length - 1].durationMilliseconds;
    this.fullWindows = [{ startMilliseconds: 0, endMilliseconds: this.filmMilliseconds }];
    this.reelWindows = windowsFrom(reel);
    this.mode = mode;

    const windows = this.playlist;

    this.position = windows[0]?.startMilliseconds ?? 0;
  }

  /** Whether the film holds anything to play at all. */
  get hasPassages(): boolean {
    return this.film.length > 0;
  }

  /** Whether the reel differs from the whole film, so the viewer can offer a highlights playlist. */
  get hasReel(): boolean {
    return this.reelWindows.length > 0;
  }

  /** How many passages the film holds. */
  get count(): number {
    return this.film.length;
  }

  /** The passages, in match order. */
  get passages(): readonly Passage[] {
    return this.film.map((item) => item.passage);
  }

  /** Which playlist is playing. */
  get currentMode(): PlaybackMode {
    return this.mode;
  }

  /** How long the whole film runs for, in milliseconds, whichever playlist is playing. */
  get filmDurationMs(): number {
    return this.filmMilliseconds;
  }

  /**
   * The windows the active playlist plays, in playback order.
   *
   * The film jumps between them on the highlights playlist, which is what the viewer fades over.
   */
  get windows(): readonly PlaylistWindow[] {
    return this.playlist;
  }

  /** The windows the active playlist plays, in playback order. */
  private get playlist(): readonly PlaylistWindow[] {
    return this.mode === 'reel' && this.reelWindows.length > 0
      ? this.reelWindows
      : this.fullWindows;
  }

  /** The passage the playhead stands in, or null when the film is empty. */
  get active(): Passage | null {
    return this.activePassage?.passage ?? null;
  }

  /** The film-placed passage the playhead stands in, or null when the film is empty. */
  get activePassage(): FilmPassage | null {
    return this.film[this.activeIndex] ?? null;
  }

  /**
   * The index of the passage being played, which is what the narration and the report address.
   *
   * The playhead moves forward a frame at a time, so the last answer is almost always still right and is
   * checked first; a seek falls back to a search.
   */
  get activeIndex(): number {
    const cached = this.film[this.activeCursor];

    if (
      cached !== undefined &&
      this.position >= cached.startMilliseconds &&
      this.position < cached.startMilliseconds + cached.durationMilliseconds
    ) {
      return this.activeCursor;
    }

    for (let index = 0; index < this.film.length; index += 1) {
      const item = this.film[index];

      if (this.position < item.startMilliseconds + item.durationMilliseconds) {
        this.activeCursor = index;

        return index;
      }
    }

    this.activeCursor = Math.max(0, this.film.length - 1);

    return this.activeCursor;
  }

  /** How far into the active passage the playhead stands, in film milliseconds. */
  get passageTimeMs(): number {
    const active = this.activePassage;

    if (active === null) {
      return 0;
    }

    return clamp(this.position - active.startMilliseconds, 0, active.durationMilliseconds);
  }

  /** How long the active passage runs for, in film milliseconds. */
  get passageDurationMs(): number {
    return this.activePassage?.durationMilliseconds ?? 0;
  }

  /** The playhead's film moment, in milliseconds from kick-off. */
  get positionMs(): number {
    return this.position;
  }

  /** Where a passage starts on the film clock. */
  filmStartOf(index: number): number {
    return this.film[index]?.startMilliseconds ?? 0;
  }

  /** How long the active playlist runs for, in milliseconds. */
  get totalMilliseconds(): number {
    return this.playlist.reduce(
      (sum, window) => sum + (window.endMilliseconds - window.startMilliseconds),
      0,
    );
  }

  /** How much of the active playlist has played, in milliseconds. */
  get elapsedMilliseconds(): number {
    const windows = this.playlist;
    let elapsed = 0;

    for (let index = 0; index < this.windowIndex && index < windows.length; index += 1) {
      elapsed += windows[index].endMilliseconds - windows[index].startMilliseconds;
    }

    const window = windows[this.windowIndex];

    if (window !== undefined) {
      elapsed += clamp(
        this.position - window.startMilliseconds,
        0,
        window.endMilliseconds - window.startMilliseconds,
      );
    }

    return elapsed;
  }

  /** What the replay is doing. */
  get currentState(): PlaybackState {
    return this.state;
  }

  /** The current speed. */
  get speed(): PlaybackSpeed {
    return this.playbackSpeed;
  }

  /** Starts or resumes, and has no effect once the film has played out. */
  play(): void {
    if (!this.hasPassages || this.state === 'finished') {
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
   * Switches playlists, keeping the playhead on the same film moment where the new one covers it.
   *
   * A film moment the reel does not carry — the middle of a dull spell — sends the playhead to the start of
   * the reel, because there is nothing there to show.
   */
  setMode(mode: PlaybackMode): void {
    if (mode === this.mode) {
      return;
    }

    const filmPosition = this.position;

    this.mode = mode;

    const windows = this.playlist;
    const index = this.windowFor(filmPosition, windows);
    const window = windows[index];

    if (
      window !== undefined &&
      filmPosition >= window.startMilliseconds &&
      filmPosition <= window.endMilliseconds
    ) {
      this.windowIndex = index;
      this.position = filmPosition;

      return;
    }

    this.windowIndex = 0;
    this.position = windows[0]?.startMilliseconds ?? 0;
  }

  /**
   * Advances the playhead by the real time that has passed.
   *
   * @returns Whether the active passage changed, which is what tells a viewer to redraw from a new track.
   */
  advance(realDeltaMs: number): boolean {
    if (this.state !== 'playing' || realDeltaMs <= 0) {
      return false;
    }

    const windows = this.playlist;

    if (windows.length === 0) {
      return false;
    }

    const before = this.activeIndex;
    let remaining = realDeltaMs * this.playbackSpeed;

    // A long frame — a backgrounded tab returning, a slow device — can cross a window boundary, so the
    // playhead is walked forward rather than assumed to land inside the current window.
    while (remaining > 0) {
      const window = windows[this.windowIndex];
      const room = window.endMilliseconds - this.position;

      if (remaining < room) {
        this.position += remaining;

        break;
      }

      remaining -= Math.max(0, room);
      this.position = window.endMilliseconds;

      if (this.windowIndex + 1 >= windows.length) {
        this.state = 'finished';

        break;
      }

      this.windowIndex += 1;
      this.position = windows[this.windowIndex].startMilliseconds;
    }

    return this.activeIndex !== before;
  }

  /** Moves to the next passage the active playlist carries, or finishes when this was the last. */
  skipCurrent(): void {
    if (!this.hasPassages) {
      return;
    }

    for (let index = this.activeIndex + 1; index < this.film.length; index += 1) {
      if (this.seekTo(index)) {
        this.state = 'playing';

        return;
      }
    }

    this.skipAll();
  }

  /** Jumps to the end of the active playlist. */
  skipAll(): void {
    const windows = this.playlist;

    if (windows.length === 0) {
      return;
    }

    this.windowIndex = windows.length - 1;
    this.position = windows[windows.length - 1].endMilliseconds;
    this.state = 'finished';
  }

  /** Starts the film again from the beginning of the active playlist. */
  replay(): void {
    const windows = this.playlist;

    if (windows.length === 0) {
      return;
    }

    this.windowIndex = 0;
    this.position = windows[0].startMilliseconds;
    this.state = 'playing';
  }

  /** Seeks to the start of a passage, or reports that the active playlist does not carry it. */
  seekTo(index: number): boolean {
    const item = this.film[Math.max(0, index)];

    if (item === undefined) {
      return false;
    }

    const windows = this.playlist;
    const windowIndex = this.windowFor(item.startMilliseconds, windows);
    const window = windows[windowIndex];

    if (
      window === undefined ||
      item.startMilliseconds < window.startMilliseconds ||
      item.startMilliseconds >= window.endMilliseconds
    ) {
      return false;
    }

    this.windowIndex = windowIndex;
    this.position = item.startMilliseconds;

    if (this.state === 'finished') {
      // Seeking backwards from the end leaves the replay paused at the chosen passage rather than
      // claiming to be finished.
      this.state = 'paused';
    }

    return true;
  }

  /** Seeks to the passage that presents an event, if the active playlist carries one. */
  seekToEvent(sequence: number): boolean {
    const index = this.film.findIndex(
      (item) =>
        item.passage.sourceEventSequence === sequence ||
        item.passage.eventSequences.includes(sequence),
    );

    return index < 0 ? false : this.seekTo(index);
  }

  /** Seeks within the active playlist, by elapsed milliseconds. */
  seekToMilliseconds(milliseconds: number): void {
    const windows = this.playlist;

    if (windows.length === 0) {
      return;
    }

    const total = this.totalMilliseconds;
    let target = clamp(milliseconds, 0, total);
    let index = 0;

    while (
      index < windows.length - 1 &&
      target >= windows[index].endMilliseconds - windows[index].startMilliseconds
    ) {
      target -= windows[index].endMilliseconds - windows[index].startMilliseconds;
      index += 1;
    }

    this.windowIndex = index;
    this.position = windows[index].startMilliseconds + target;

    if (this.state === 'finished' && milliseconds < total) {
      this.state = 'paused';
    }
  }

  /** Where a film moment sits on the active playlist, or null when the playlist does not carry it. */
  playlistMillisecondsForFilm(filmMilliseconds: number): number | null {
    const windows = this.playlist;
    let elapsed = 0;

    for (const window of windows) {
      if (
        filmMilliseconds >= window.startMilliseconds &&
        filmMilliseconds <= window.endMilliseconds
      ) {
        return elapsed + (filmMilliseconds - window.startMilliseconds);
      }

      elapsed += window.endMilliseconds - window.startMilliseconds;
    }

    return null;
  }

  /** The first window at or after a film moment. */
  private windowFor(filmMilliseconds: number, windows: readonly PlaylistWindow[]): number {
    for (let index = 0; index < windows.length; index += 1) {
      if (filmMilliseconds < windows[index].endMilliseconds) {
        return index;
      }
    }

    return Math.max(0, windows.length - 1);
  }
}

/**
 * Places the passages on the film clock, using the schedule's offsets where it agrees on the count.
 *
 * Shared with the film timeline, so the playhead and the film it plays can never disagree about where a
 * passage starts.
 */
export function placePassages(
  passages: readonly Passage[],
  playback: readonly PlaybackSegment[],
): readonly FilmPassage[] {
  const film: FilmPassage[] = [];
  let cursor = 0;

  passages.forEach((passage, index) => {
    const schedule = playback[index];
    const scheduled = schedule !== undefined && schedule.kind === 'passage';
    const startMilliseconds = scheduled ? schedule.startMilliseconds : cursor;
    const durationMilliseconds = scheduled
      ? schedule.durationMilliseconds
      : passage.durationMilliseconds;

    film.push({ index, passage, startMilliseconds, durationMilliseconds });
    cursor = startMilliseconds + durationMilliseconds;
  });

  return film;
}

/** Merges the reel's clips into ordered, non-overlapping windows over the film clock. */
function windowsFrom(reel: readonly ReelClip[]): readonly PlaylistWindow[] {
  const ordered = reel
    .map((clip) => ({
      startMilliseconds: clip.startMilliseconds,
      endMilliseconds: clip.endMilliseconds,
    }))
    .filter((window) => window.endMilliseconds > window.startMilliseconds)
    .sort((one, other) => one.startMilliseconds - other.startMilliseconds);
  const merged: PlaylistWindow[] = [];

  for (const window of ordered) {
    const previous = merged[merged.length - 1];

    if (previous !== undefined && window.startMilliseconds <= previous.endMilliseconds) {
      merged[merged.length - 1] = {
        startMilliseconds: previous.startMilliseconds,
        endMilliseconds: Math.max(previous.endMilliseconds, window.endMilliseconds),
      };

      continue;
    }

    merged.push(window);
  }

  return merged;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

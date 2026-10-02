import { Bridge, Highlight, HighlightEntity } from './match.models';

/** What the replay is doing. */
export type PlaybackState = 'idle' | 'playing' | 'paused' | 'finished';

/** The playback speeds the viewer offers (`§9.4`, master plan Stage 5). */
export const PLAYBACK_SPEEDS = [1, 2, 4, 8] as const;

/** One of the viewer's speeds. */
export type PlaybackSpeed = (typeof PLAYBACK_SPEEDS)[number];

/** What one passage of the replay is: a highlight, or the recycling before the next one. */
export type PassageKind = 'highlight' | 'bridge';

/** One playable passage of the replay, with the highlight-shaped body the renderer draws. */
export interface PlaybackPassage {
  readonly kind: PassageKind;
  readonly sourceEventSequence: number;
  readonly durationMilliseconds: number;
  readonly highlight: Highlight;
}

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
 *
 * Passing the presentation's bridges plays the condensed replay: the recycling passage between two chances
 * is a passage of its own, so the ball is carried from one highlight's ending to the next one's beginning
 * instead of teleporting between them (`replay-v2`). Without bridges the player cuts from highlight to
 * highlight, which is what the viewer's "Highlights" mode offers; the *highlight* index is what everything
 * outside the player addresses — the timeline, a commentary line, a seek — so both modes speak the same
 * language.
 */
export class MatchPlayback {
  private readonly passages: readonly PlaybackPassage[];
  private index = 0;
  private position = 0;
  private state: PlaybackState = 'idle';
  private playbackSpeed: PlaybackSpeed = 1;

  /**
   * Initializes the player over a match's highlights.
   *
   * @param highlights The highlights, in event order.
   * @param bridges The recycling passages between them, when the condensed replay is wanted.
   */
  constructor(highlights: readonly Highlight[], bridges: readonly Bridge[] = []) {
    this.passages = passageList(highlights, bridges);
  }

  /** Whether there is anything to play at all. */
  get hasHighlights(): boolean {
    return this.highlightCount > 0;
  }

  /** How many highlights the replay holds, which is what the timeline addresses. */
  get highlightCount(): number {
    return this.passages.reduce(
      (total, passage) => total + (passage.kind === 'highlight' ? 1 : 0),
      0,
    );
  }

  /** How many passages the replay holds, bridges included. */
  get count(): number {
    return this.passages.length;
  }

  /** The body being played — the highlight, or the bridge's own movement — or null when there is none. */
  get active(): Highlight | null {
    return this.passages[this.index]?.highlight ?? null;
  }

  /** What the player is showing: a highlight or the recycling before one. */
  get activePassageKind(): PassageKind {
    return this.passages[this.index]?.kind ?? 'highlight';
  }

  /** The index of the passage being played. */
  get activeIndex(): number {
    return this.index;
  }

  /** The index of the highlight being played, whichever passage carries the playhead. */
  get activeHighlightIndex(): number {
    let seen = 0;

    for (let passage = 0; passage < this.index; passage += 1) {
      if (this.passages[passage].kind === 'highlight') {
        seen += 1;
      }
    }

    return seen;
  }

  /** How far into the active passage the playhead stands, in animation milliseconds. */
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

  /** How long the active passage runs for, or zero when there are none. */
  get activeDurationMs(): number {
    return this.passages[this.index]?.durationMilliseconds ?? 0;
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
   * @returns Whether the active passage changed, which is what tells a viewer to redraw from a new track.
   */
  advance(realDeltaMs: number): boolean {
    if (this.state !== 'playing' || !this.hasHighlights || realDeltaMs <= 0) {
      return false;
    }

    this.position += realDeltaMs * this.playbackSpeed;

    let changed = false;

    // A long frame — a backgrounded tab returning, a slow device — can cross more than one passage, so the
    // playhead is walked forward rather than assumed to land inside the current one.
    while (
      this.index < this.passages.length &&
      this.position >= this.passages[this.index].durationMilliseconds
    ) {
      this.position -= this.passages[this.index].durationMilliseconds;
      this.index += 1;
      changed = true;
    }

    if (this.index >= this.passages.length) {
      // Finished: the playhead rests at the end of the last passage rather than falling off the list.
      this.index = this.passages.length - 1;
      this.position = this.passages[this.index].durationMilliseconds;
      this.state = 'finished';
      changed = true;
    }

    return changed;
  }

  /** Moves to the next highlight, bridging over the recycling before it, or finishes when this was the last. */
  skipCurrent(): void {
    if (!this.hasHighlights) {
      return;
    }

    const next = this.nextHighlightPassage(this.index + 1);

    if (next < 0) {
      this.skipAll();

      return;
    }

    this.goTo(next);
    this.state = 'playing';
  }

  /** Jumps to the end of the replay. */
  skipAll(): void {
    if (!this.hasHighlights) {
      return;
    }

    const last = this.lastHighlightPassage();

    this.index = last;
    this.position = this.passages[last].durationMilliseconds;
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

    const passage = this.passageForHighlight(Math.max(0, index));

    if (passage < 0) {
      this.skipAll();

      return;
    }

    this.goTo(passage);

    if (this.state === 'finished') {
      // Seeking backwards from the end leaves the replay paused at the chosen highlight rather than
      // claiming to be finished.
      this.state = 'paused';
    }
  }

  /** Seeks to the highlight that presents an event, if the replay holds one. */
  seekToEvent(sequence: number): boolean {
    let highlightIndex = 0;

    for (const passage of this.passages) {
      if (passage.kind !== 'highlight') {
        continue;
      }

      if (passage.sourceEventSequence === sequence) {
        this.seekTo(highlightIndex);

        return true;
      }

      highlightIndex += 1;
    }

    return false;
  }

  /** The passage that presents a highlight, or -1 when the index names none. */
  private passageForHighlight(index: number): number {
    if (index < 0) {
      return -1;
    }

    let highlight = 0;

    for (let passage = 0; passage < this.passages.length; passage += 1) {
      if (this.passages[passage].kind !== 'highlight') {
        continue;
      }

      if (highlight === index) {
        return passage;
      }

      highlight += 1;
    }

    return -1;
  }

  /** The first highlight passage at or after an index, or -1 when there is none. */
  private nextHighlightPassage(from: number): number {
    for (let passage = Math.max(0, from); passage < this.passages.length; passage += 1) {
      if (this.passages[passage].kind === 'highlight') {
        return passage;
      }
    }

    return -1;
  }

  /** The last highlight passage in the replay. */
  private lastHighlightPassage(): number {
    for (let passage = this.passages.length - 1; passage >= 0; passage -= 1) {
      if (this.passages[passage].kind === 'highlight') {
        return passage;
      }
    }

    return this.passages.length - 1;
  }

  private goTo(index: number): void {
    this.index = index;
    this.position = 0;
  }
}

/** Lays the highlights and their bridges out as one passage list, in the order they play. */
function passageList(
  highlights: readonly Highlight[],
  bridges: readonly Bridge[],
): readonly PlaybackPassage[] {
  const bySequence = new Map(bridges.map((bridge) => [bridge.afterEventSequence, bridge]));
  const passages: PlaybackPassage[] = [];
  let previous: Highlight | null = null;

  for (const highlight of highlights) {
    const bridge = bySequence.get(highlight.sourceEventSequence);

    // No bridge precedes the first highlight: there is nothing before it to recycle from, which is the
    // director's own rule as well as this player's.
    if (bridge !== undefined && previous !== null) {
      passages.push({
        kind: 'bridge',
        sourceEventSequence: bridge.afterEventSequence,
        durationMilliseconds: bridge.durationMilliseconds,
        highlight: bridgeHighlight(bridge, previous, highlight),
      });
    }

    passages.push({
      kind: 'highlight',
      sourceEventSequence: highlight.sourceEventSequence,
      durationMilliseconds: highlight.durationMilliseconds,
      highlight,
    });

    previous = highlight;
  }

  return passages;
}

/**
 * Gives a bridge the highlight-shaped body the renderer draws.
 *
 * A bridge carries movement only — no outcome, no narration, no entity list — because the entities it moves
 * are the players the neighbouring highlights already described. The shape is completed from those
 * neighbours, preferring the passage it leads into, so the renderer can draw the recycling without knowing
 * the payload is a bridge at all.
 */
function bridgeHighlight(bridge: Bridge, previous: Highlight, next: Highlight): Highlight {
  return {
    sourceEventSequence: bridge.afterEventSequence,
    minute: next.minute,
    stoppageMinute: next.stoppageMinute,
    durationMilliseconds: bridge.durationMilliseconds,
    outcomeCode: 'bridge',
    narration: '',
    homeColour: next.homeColour,
    awayColour: next.awayColour,
    entities: mergeEntities(next.entities, previous.entities),
    tracks: bridge.tracks,
    commentary: [],
  };
}

/** The union of two entity lists, keeping the first list's version of any entity both carry. */
function mergeEntities(
  preferred: readonly HighlightEntity[],
  fallback: readonly HighlightEntity[],
): readonly HighlightEntity[] {
  const byId = new Map(preferred.map((entity) => [entity.entityId, entity]));

  for (const entity of fallback) {
    if (!byId.has(entity.entityId)) {
      byId.set(entity.entityId, entity);
    }
  }

  return [...byId.values()];
}

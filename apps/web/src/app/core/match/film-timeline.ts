import { LineupIndex, lineupIndexOf } from './match-lineups';
import { placePassages } from './match-playback';
import {
  cardKindFor,
  isGoalCommentary,
  isGoalOutcome,
  isShotOutcome,
  isStrikeTag,
  matchClockFromSeconds,
  matchClockLabel,
  passageTitle,
} from './match-presentation';
import {
  HighlightCommentary,
  HighlightEntity,
  HighlightKeyframe,
  MatchPresentation,
  Passage,
} from './match.models';

/**
 * One continuous film built from a presentation's passages (`replay-v4`).
 *
 * The server cuts the film into passages of about ten seconds so it can be cached, compressed and seeked by
 * event, but a manager does not watch passages: they watch a match. The timeline is the match as it plays —
 * every entity's movement as one track over the whole film, who stands in each slot and when, the match clock
 * through both halves, the commentary feed, the cards and the cuts — so the viewer can build its renderer
 * *once* and draw global film time, and a passage boundary is just a number that nothing on screen notices.
 *
 * It is pure data and pure functions: it reads no clock and touches no DOM, so a test can build one from a
 * handful of passages and ask it about any film moment.
 */

/** The identifier the ball's entity and track carry. */
export const BALL_ENTITY_ID = 'ball';

/**
 * The slot identifiers, in the order the server sorts its entities (ordinal), which is the order the pitch
 * has always drawn them in, so one token overlapping another is the same token on top as it ever was.
 */
export const SLOT_IDS: readonly string[] = ['A', 'H']
  .flatMap((side) => Array.from({ length: 11 }, (_, index) => `${side}${index + 1}`))
  .sort();

/** How long a goal's celebration is shown for, in film milliseconds (the film's goal hold). */
export const CELEBRATION_MILLISECONDS = 4_000;

/** How close two keyframes at one instant must be, in normalized units (about a centimetre), to be one. */
const SAME_POINT_TOLERANCE = 1;

/** The match clock's regulation length for each half, in minutes, on that half's own clock. */
const REGULATION_MINUTES = [0, 45, 90] as const;

/** The sides' colours for a film that carries none. */
const DEFAULT_HOME_COLOUR = '#38bdf8';
const DEFAULT_AWAY_COLOUR = '#fb7185';

/** The outcome code of the half-time card's passage. */
const HALF_TIME_OUTCOME = 'half_time';

/** A card a player carries. */
export type FilmCardKind = 'yellow' | 'red';

/** Who or what a token on the pitch is: a player in a slot, or the ball. */
export interface FilmEntity {
  readonly id: string;
  readonly isBall: boolean;
  readonly side: 'home' | 'away' | null;
  readonly participantId: string | null;
  readonly shirtNumber: number;
  readonly family: string | null;
  readonly name: string | null;
  readonly position: string | null;
}

/** One player's stretch in a slot: the player stands in it from `startMilliseconds` until `endMilliseconds`. */
export interface RosterStint {
  readonly entity: FilmEntity;
  readonly startMilliseconds: number;
  readonly endMilliseconds: number;
}

/**
 * One entity's movement over the whole film, as parallel arrays so the renderer reads it without allocating.
 *
 * Times are film milliseconds and never decrease. Two keyframes may share a time: that is a *step* — a cut —
 * and nothing is interpolated across it.
 */
export interface FilmTrack {
  readonly entityId: string;
  readonly length: number;
  readonly times: Float64Array;
  readonly xs: Float64Array;
  readonly ys: Float64Array;
  readonly zs: Float64Array;
  readonly speeds: Float64Array;
  readonly actions: readonly (string | null)[];
}

/** One slot on the pitch: its movement and who stood in it. */
export interface FilmSlot {
  readonly id: string;
  readonly track: FilmTrack;
  /** In order and not overlapping. A gap is a slot nobody is in — a player sent off — and draws nothing. */
  readonly stints: readonly RosterStint[];
}

/** One row of the commentary feed, at the film moment its beat happens. */
export interface FeedRow {
  readonly key: string;
  readonly filmMilliseconds: number;
  readonly clock: string;
  readonly side: string;
  readonly text: string;
  readonly isGoal: boolean;
}

/** A goal or a shot on the progress scrubber. */
export interface FilmMarker {
  readonly key: string;
  readonly kind: 'goal' | 'shot';
  /** The film moment the ball is struck at goal. */
  readonly filmMilliseconds: number;
  /** The film moment the passage that builds to it starts, which is where a manager wants to seek to. */
  readonly passageStartMilliseconds: number;
  readonly label: string;
  readonly passageIndex: number;
}

/** A goal and the moment it goes in. */
export interface FilmGoal {
  readonly filmMilliseconds: number;
  readonly side: 'home' | 'away' | null;
  readonly sequence: number;
}

/** A card, and the film moment from which its player carries it. */
export interface FilmCardMoment {
  readonly filmMilliseconds: number;
  readonly participantId: string;
  readonly kind: FilmCardKind;
}

/** A cut: the instant the players and the ball are put somewhere new, because they could not walk there. */
export interface FilmCut {
  readonly startMilliseconds: number;
  readonly durationMilliseconds: number;
  readonly kind: string;
}

/** A stretch of film the half-time card holds. */
export interface FilmInterval {
  readonly startMilliseconds: number;
  readonly endMilliseconds: number;
}

/** What the scoreboard reads at a film moment. */
export interface FilmClock {
  /** The half, 1 or 2; zero for a presentation that predates the half-aware clock. */
  readonly period: number;
  readonly matchSecond: number;
  /** The minute the live panels follow: the regulation minute, with stoppage held at 45 or 90. */
  readonly minute: number;
  readonly stoppageMinute: number;
  /** What a scoreboard writes: `23'`, `45+2'`, `HT`, `46'`, `90+3'`. */
  readonly label: string;
  readonly isHalfTime: boolean;
}

/** The continuous film a presentation plays as. */
export class FilmTimeline {
  /** How long the film runs for, in milliseconds. */
  readonly durationMilliseconds: number;

  /** Where each passage starts on the film, in order. */
  readonly passageStarts: readonly number[];

  /** Every player's slot, in the order they are drawn. */
  readonly slots: readonly FilmSlot[];

  /** The ball. */
  readonly ball: FilmTrack;

  /** The ball's entity, which never changes. */
  readonly ballEntity: FilmEntity;

  /** The colour each side plays in when the lineups name no kit, as the film's passages carry it. */
  readonly homeColour: string;
  readonly awayColour: string;

  /** The commentary feed, in film order. */
  readonly feed: readonly FeedRow[];

  /** The goals and shots the scrubber marks, in film order. */
  readonly markers: readonly FilmMarker[];

  /** The goals, in film order. */
  readonly goals: readonly FilmGoal[];

  /** The cuts, in film order. */
  readonly cuts: readonly FilmCut[];

  /** The stretches the half-time card holds. */
  readonly halfTimes: readonly FilmInterval[];

  /** The cards, in film order. */
  readonly cardMoments: readonly FilmCardMoment[];

  private readonly clockTimes: readonly number[];
  private readonly clockSeconds: readonly number[];
  private readonly clockPeriods: readonly number[];
  private readonly cardStates: readonly ReadonlyMap<string, FilmCardKind>[];
  private lastClock: FilmClock | null = null;

  /** Initializes a timeline from the pieces `buildFilmTimeline` assembles. */
  constructor(parts: FilmTimelineParts) {
    this.durationMilliseconds = parts.durationMilliseconds;
    this.passageStarts = parts.passageStarts;
    this.slots = parts.slots;
    this.ball = parts.ball;
    this.ballEntity = parts.ballEntity;
    this.homeColour = parts.homeColour;
    this.awayColour = parts.awayColour;
    this.feed = parts.feed;
    this.markers = parts.markers;
    this.goals = parts.goals;
    this.cuts = parts.cuts;
    this.halfTimes = parts.halfTimes;
    this.cardMoments = parts.cardMoments;
    this.clockTimes = parts.clockTimes;
    this.clockSeconds = parts.clockSeconds;
    this.clockPeriods = parts.clockPeriods;
    this.cardStates = cardStatesOf(parts.cardMoments);
  }

  /** Whether the film holds anything to play. */
  get isEmpty(): boolean {
    return this.durationMilliseconds <= 0;
  }

  /**
   * Who stands in a slot at a film moment, or null when nobody does and the slot draws nothing.
   *
   * @param slotIndex The slot's index in `slots`.
   * @param filmMilliseconds The film moment.
   */
  stintAt(slotIndex: number, filmMilliseconds: number): RosterStint | null {
    const stints = this.slots[slotIndex]?.stints;

    if (stints === undefined) {
      return null;
    }

    for (let index = stints.length - 1; index >= 0; index -= 1) {
      const stint = stints[index];

      if (filmMilliseconds >= stint.startMilliseconds) {
        return filmMilliseconds <= stint.endMilliseconds ? stint : null;
      }
    }

    return null;
  }

  /**
   * What the scoreboard reads at a film moment.
   *
   * Each half runs on its own clock, so the label is `45+2'` while the first half's stoppage is played, `HT`
   * through the half-time card, `46'` as the second half starts, and `90+N'` in its stoppage. The result is
   * reused while the second it reads has not changed, so asking every frame costs nothing.
   */
  clockAt(filmMilliseconds: number): FilmClock {
    const count = this.clockTimes.length;

    if (count === 0) {
      return EMPTY_CLOCK;
    }

    const lower = lastIndexAtOrBefore(this.clockTimes, filmMilliseconds);
    const index = Math.max(0, lower);
    const period = this.clockPeriods[index];
    let second = this.clockSeconds[index];

    // Between two points of one half the clock runs; across a half it stands, because the next point belongs
    // to another clock altogether.
    if (
      lower >= 0 &&
      index + 1 < count &&
      this.clockPeriods[index + 1] === period &&
      this.clockTimes[index + 1] > this.clockTimes[index]
    ) {
      const fraction = clamp(
        (filmMilliseconds - this.clockTimes[index]) /
          (this.clockTimes[index + 1] - this.clockTimes[index]),
        0,
        1,
      );

      second += (this.clockSeconds[index + 1] - second) * fraction;
    }

    const whole = Math.floor(second);
    const isHalfTime = this.isHalfTimeAt(filmMilliseconds);
    const previous = this.lastClock;

    if (
      previous !== null &&
      previous.matchSecond === whole &&
      previous.period === period &&
      previous.isHalfTime === isHalfTime
    ) {
      return previous;
    }

    const clock = clockOf(period, whole, isHalfTime);

    this.lastClock = clock;

    return clock;
  }

  /** How many feed rows the playhead has reached at a film moment. */
  feedCountAt(filmMilliseconds: number): number {
    return countAtOrBefore(this.feed, filmMilliseconds);
  }

  /** The cards every player carries at a film moment, which the pitch draws above their token. */
  cardsAt(filmMilliseconds: number): ReadonlyMap<string, FilmCardKind> {
    return this.cardStates[countAtOrBefore(this.cardMoments, filmMilliseconds)];
  }

  /**
   * The goal being celebrated at a film moment, or null when none is.
   *
   * A celebration is a film moment rather than a property of a passage, so it runs its four seconds out even
   * when the passage that scored ends in the middle of it.
   */
  activeGoalAt(filmMilliseconds: number): FilmGoal | null {
    for (let index = this.goals.length - 1; index >= 0; index -= 1) {
      const elapsed = filmMilliseconds - this.goals[index].filmMilliseconds;

      if (elapsed >= 0) {
        return elapsed < CELEBRATION_MILLISECONDS ? this.goals[index] : null;
      }
    }

    return null;
  }

  /**
   * The score at a film moment: the goals struck at or before it, home and away.
   *
   * This is what a scoreboard shows while the film plays, so the match is watched rather than read off a
   * final tally shown from the first second. A goal that names no side is counted for neither.
   */
  scoreAt(filmMilliseconds: number): { readonly home: number; readonly away: number } {
    let home = 0;
    let away = 0;

    for (const goal of this.goals) {
      if (goal.filmMilliseconds > filmMilliseconds) {
        break;
      }

      if (goal.side === 'home') {
        home += 1;
      } else if (goal.side === 'away') {
        away += 1;
      }
    }

    return { home, away };
  }

  /** How far into a goal's celebration a film moment is, or -1 when none is being celebrated. */
  celebrationAt(filmMilliseconds: number): number {
    const goal = this.activeGoalAt(filmMilliseconds);

    return goal === null ? -1 : filmMilliseconds - goal.filmMilliseconds;
  }

  /** Whether the half-time card is up at a film moment. */
  isHalfTimeAt(filmMilliseconds: number): boolean {
    for (const interval of this.halfTimes) {
      if (
        filmMilliseconds >= interval.startMilliseconds &&
        filmMilliseconds < interval.endMilliseconds
      ) {
        return true;
      }
    }

    return false;
  }
}

/** The pieces of a timeline. Public only so the builder can hand them over. */
export interface FilmTimelineParts {
  readonly durationMilliseconds: number;
  readonly passageStarts: readonly number[];
  readonly slots: readonly FilmSlot[];
  readonly ball: FilmTrack;
  readonly ballEntity: FilmEntity;
  readonly homeColour: string;
  readonly awayColour: string;
  readonly feed: readonly FeedRow[];
  readonly markers: readonly FilmMarker[];
  readonly goals: readonly FilmGoal[];
  readonly cuts: readonly FilmCut[];
  readonly halfTimes: readonly FilmInterval[];
  readonly cardMoments: readonly FilmCardMoment[];
  readonly clockTimes: readonly number[];
  readonly clockSeconds: readonly number[];
  readonly clockPeriods: readonly number[];
}

/** An empty film, for a presentation that has no passages. */
export const EMPTY_TIMELINE_CLOCK_LABEL = '';

const EMPTY_CLOCK: FilmClock = {
  period: 0,
  matchSecond: 0,
  minute: 0,
  stoppageMinute: 0,
  label: EMPTY_TIMELINE_CLOCK_LABEL,
  isHalfTime: false,
};

/**
 * Builds the timeline a presentation plays as.
 *
 * Passages are laid on the film clock by the presentation's own schedule. Each entity's keyframes are joined
 * into one track, with the boundary frame two consecutive passages share kept once — and kept twice, as a
 * step, where the two disagree because the film cut between them. A player is followed from slot to slot
 * through the roster, so a substitution switches the name and number on the same token and a player sent off
 * is simply no longer in their slot.
 */
export function buildFilmTimeline(presentation: MatchPresentation | null): FilmTimeline {
  const passages = presentation?.passages ?? [];
  const placed = placePassages(passages, presentation?.playback ?? []);
  const lineups = lineupIndexOf(presentation);
  const reportLines = new Map(
    (presentation?.commentary ?? []).map((line) => [line.sequence, line]),
  );

  const slotTracks = SLOT_IDS.map((id) => new TrackBuilder(id));
  const slotStints = SLOT_IDS.map(() => [] as StintBuilder[]);
  const slotIndex = new Map(SLOT_IDS.map((id, index) => [id, index]));
  const ballTrack = new TrackBuilder(BALL_ENTITY_ID);
  let ballEntity: FilmEntity = ballEntityOf(null);

  const clockTimes: number[] = [];
  const clockSeconds: number[] = [];
  const clockPeriods: number[] = [];
  const cuts: FilmCut[] = [];
  const halfTimes: FilmInterval[] = [];
  const goals: FilmGoal[] = [];
  const markers: FilmMarker[] = [];
  const cardMoments: FilmCardMoment[] = [];
  const feedSources: FeedSource[] = [];
  let durationMilliseconds = 0;

  placed.forEach((item, passageIndex) => {
    const passage = item.passage;
    const start = item.startMilliseconds;
    const duration = item.durationMilliseconds;
    const end = start + duration;
    const byTrack = new Map(passage.tracks.map((track) => [track.entityId, track.keyframes]));

    durationMilliseconds = Math.max(durationMilliseconds, end);

    for (const entity of passage.entities) {
      if (entity.isBall) {
        ballEntity = ballEntityOf(entity);
        ballTrack.append(start, duration, byTrack.get(entity.entityId), entity);

        continue;
      }

      const slot = slotIndex.get(entity.entityId);

      if (slot === undefined) {
        continue;
      }

      slotTracks[slot].append(start, duration, byTrack.get(entity.entityId), entity);
      joinStint(slotStints[slot], entity, start, end);
    }

    const clock = clockKeyframes(passage, duration);
    const period = passage.period ?? 0;

    for (const keyframe of clock) {
      clockTimes.push(start + keyframe.timeMilliseconds);
      clockSeconds.push(keyframe.matchSecond);
      clockPeriods.push(period);
    }

    for (const cut of passage.cuts ?? []) {
      cuts.push({
        startMilliseconds: start + cut.timeMilliseconds,
        durationMilliseconds: cut.durationMilliseconds,
        kind: cut.kind,
      });
    }

    if (passage.outcomeCode === HALF_TIME_OUTCOME) {
      halfTimes.push({ startMilliseconds: start, endMilliseconds: end });
    }

    const commentary = passage.commentary ?? [];

    commentary.forEach((token, tokenIndex) => {
      feedSources.push({
        key: `${passageIndex}:${tokenIndex}`,
        filmMilliseconds: start + clamp(token.timeMilliseconds, 0, duration),
        token,
        order: feedSources.length,
      });
    });

    if (isGoalOutcome(passage.outcomeCode)) {
      goals.push({
        filmMilliseconds: start + goalTime(passage, duration),
        side: goalSide(reportLines.get(passage.sourceEventSequence)?.side),
        sequence: passage.sourceEventSequence,
      });
    }

    const kind = markerKind(passage.outcomeCode);

    if (kind !== null) {
      markers.push({
        key: `${kind}-${passageIndex}`,
        kind,
        filmMilliseconds: start + strikeTime(passage, duration),
        passageStartMilliseconds: start,
        label: passageTitle(passage),
        passageIndex,
      });
    }

    for (const sequence of passageSequences(passage)) {
      const line = reportLines.get(sequence);
      const card = line === undefined ? null : cardKindFor(line.templateKey);

      if (line === undefined || card === null) {
        continue;
      }

      const playerId = parameter(line.parameters, 'playerId');
      const participantId =
        playerId === undefined ? undefined : lineups.participantByPlayerId.get(playerId);

      if (participantId === undefined) {
        continue;
      }

      // The booking is shown when the beat that narrates it is, and at the start of the passage when the
      // passage carries the event but no line pinned to a moment.
      const pinned = commentary.find(
        (token) =>
          token.templateKey === line.templateKey &&
          parameter(token.parameters, 'playerId') === playerId,
      );

      cardMoments.push({
        filmMilliseconds:
          start + (pinned === undefined ? 0 : clamp(pinned.timeMilliseconds, 0, duration)),
        participantId,
        kind: card,
      });
    }
  });

  const slots: FilmSlot[] = SLOT_IDS.map((id, index) => ({
    id,
    track: slotTracks[index].finish(),
    stints: slotStints[index].map((stint) => ({
      entity: stint.entity,
      startMilliseconds: stint.start,
      endMilliseconds: stint.end,
    })),
  }));

  const timelineWithoutFeed = {
    durationMilliseconds,
    passageStarts: placed.map((item) => item.startMilliseconds),
    slots,
    ball: ballTrack.finish(),
    ballEntity,
    homeColour: passages[0]?.homeColour ?? DEFAULT_HOME_COLOUR,
    awayColour: passages[0]?.awayColour ?? DEFAULT_AWAY_COLOUR,
    feed: [] as readonly FeedRow[],
    markers: markers.sort(byFilmTime),
    goals: goals.sort(byFilmTime),
    cuts: cuts.sort((one, other) => one.startMilliseconds - other.startMilliseconds),
    halfTimes,
    cardMoments: cardMoments.sort(byFilmTime),
    clockTimes,
    clockSeconds,
    clockPeriods,
  } satisfies FilmTimelineParts;

  // The feed's clock label is the film clock at the row's moment, so the clock has to exist before the rows do.
  const clockOnly = new FilmTimeline(timelineWithoutFeed);
  const feed = feedSources
    .sort((one, other) => one.filmMilliseconds - other.filmMilliseconds || one.order - other.order)
    .map<FeedRow>((source) => ({
      key: source.key,
      filmMilliseconds: source.filmMilliseconds,
      clock: clockOnly.clockAt(source.filmMilliseconds).label,
      side: tokenSide(source.token, lineups),
      text: source.token.text,
      isGoal: isGoalCommentary(source.token.templateKey),
    }));

  return new FilmTimeline({ ...timelineWithoutFeed, feed });
}

/** A feed token on its way to becoming a row. */
interface FeedSource {
  readonly key: string;
  readonly filmMilliseconds: number;
  readonly token: HighlightCommentary;
  readonly order: number;
}

/** A stint while it is still being extended. */
interface StintBuilder {
  readonly entity: FilmEntity;
  readonly identity: string;
  start: number;
  end: number;
}

/** Collects one entity's keyframes, passage after passage, into the arrays a track is. */
class TrackBuilder {
  private readonly times: number[] = [];
  private readonly xs: number[] = [];
  private readonly ys: number[] = [];
  private readonly zs: number[] = [];
  private readonly speeds: number[] = [];
  private readonly actions: (string | null)[] = [];

  constructor(private readonly entityId: string) {}

  /**
   * Adds a passage's keyframes at its place on the film.
   *
   * A passage's own first and last keyframes are its boundary: the server copies them exactly, so the one
   * a passage ends on and the next begins with are the same state and are kept once. Where they differ the
   * film cut there, and both stay, as two keyframes at one instant. An entity with no keyframes holds its
   * anchor for the passage.
   */
  append(
    start: number,
    duration: number,
    keyframes: readonly HighlightKeyframe[] | undefined,
    anchor: HighlightEntity,
  ): void {
    const source: readonly HighlightKeyframe[] =
      keyframes !== undefined && keyframes.length > 0
        ? [...keyframes].sort((one, other) => one.timeMilliseconds - other.timeMilliseconds)
        : [
            { timeMilliseconds: 0, x: anchor.x, y: anchor.y },
            { timeMilliseconds: duration, x: anchor.x, y: anchor.y },
          ];

    for (const keyframe of source) {
      const last = this.times.length - 1;
      const time = Math.max(
        start + clamp(keyframe.timeMilliseconds, 0, duration),
        last >= 0 ? this.times[last] : 0,
      );
      const z = keyframe.z ?? 0;

      if (
        last >= 0 &&
        time === this.times[last] &&
        Math.abs(keyframe.x - this.xs[last]) <= SAME_POINT_TOLERANCE &&
        Math.abs(keyframe.y - this.ys[last]) <= SAME_POINT_TOLERANCE &&
        Math.abs(z - this.zs[last]) <= SAME_POINT_TOLERANCE
      ) {
        // The same state twice. The segment that follows starts on the later keyframe, so its speed and its
        // action are the ones that matter; the position is the same either way.
        this.speeds[last] = keyframe.speed ?? 0;
        this.actions[last] = keyframe.action ?? this.actions[last];

        continue;
      }

      this.times.push(time);
      this.xs.push(keyframe.x);
      this.ys.push(keyframe.y);
      this.zs.push(z);
      this.speeds.push(keyframe.speed ?? 0);
      this.actions.push(keyframe.action ?? null);
    }
  }

  finish(): FilmTrack {
    return {
      entityId: this.entityId,
      length: this.times.length,
      times: Float64Array.from(this.times),
      xs: Float64Array.from(this.xs),
      ys: Float64Array.from(this.ys),
      zs: Float64Array.from(this.zs),
      speeds: Float64Array.from(this.speeds),
      actions: this.actions,
    };
  }
}

/** Extends a slot's last stint when the same player is still in it, and starts a new one when they are not. */
function joinStint(
  stints: StintBuilder[],
  entity: HighlightEntity,
  start: number,
  end: number,
): void {
  const identity = entity.participantId ?? `${entity.entityId}#${entity.shirtNumber}`;
  const last = stints[stints.length - 1];

  if (last !== undefined && last.identity === identity && start - last.end <= 1) {
    last.end = end;

    return;
  }

  stints.push({ entity: playerEntityOf(entity), identity, start, end });
}

function playerEntityOf(entity: HighlightEntity): FilmEntity {
  return {
    id: entity.entityId,
    isBall: false,
    side: entity.side === 'home' || entity.side === 'away' ? entity.side : null,
    participantId: entity.participantId ?? null,
    shirtNumber: entity.shirtNumber,
    family: entity.family,
    name: entity.name ?? null,
    position: entity.position ?? null,
  };
}

function ballEntityOf(entity: HighlightEntity | null): FilmEntity {
  return {
    id: entity?.entityId ?? BALL_ENTITY_ID,
    isBall: true,
    side: null,
    participantId: null,
    shirtNumber: 0,
    family: null,
    name: null,
    position: null,
  };
}

/** A passage's clock, or the straight line between its first and last match second where it carries none. */
function clockKeyframes(
  passage: Passage,
  duration: number,
): readonly { readonly timeMilliseconds: number; readonly matchSecond: number }[] {
  if (passage.clock !== null && passage.clock !== undefined && passage.clock.length > 0) {
    return passage.clock;
  }

  return [
    { timeMilliseconds: 0, matchSecond: passage.startMatchSecond },
    { timeMilliseconds: duration, matchSecond: passage.endMatchSecond },
  ];
}

/**
 * A scoreboard reading for a match second on a half's own clock.
 *
 * A half is regulation until its forty-fifth or ninetieth minute and then stoppage, so the first half's
 * stoppage reads `45+N'` and the second half starts again at `46'`. A zero period is a presentation from
 * before the clock was kept by half, whose one continuous clock reads as it always did.
 */
function clockOf(period: number, matchSecond: number, isHalfTime: boolean): FilmClock {
  if (period === 0) {
    const legacy = matchClockFromSeconds(matchSecond);

    return {
      period,
      matchSecond,
      minute: legacy.minute,
      stoppageMinute: legacy.stoppageMinute,
      label: matchClockLabel(legacy.minute, legacy.stoppageMinute),
      isHalfTime: false,
    };
  }

  const regulation = REGULATION_MINUTES[period === 1 ? 1 : 2];
  const stoppage =
    matchSecond >= regulation * 60 ? Math.floor((matchSecond - regulation * 60) / 60) + 1 : 0;
  const minute =
    stoppage > 0 ? regulation : Math.max(1, Math.floor(Math.max(0, matchSecond) / 60) + 1);

  return {
    period,
    matchSecond,
    minute,
    stoppageMinute: stoppage,
    label: isHalfTime ? 'HT' : matchClockLabel(minute, stoppage),
    isHalfTime,
  };
}

/** The moment, inside a passage, that its goal goes in: the goal's own line, or most of the way through. */
function goalTime(passage: Passage, duration: number): number {
  const line = (passage.commentary ?? []).find((token) => isGoalCommentary(token.templateKey));

  return line === undefined
    ? Math.round(duration * 0.7)
    : clamp(line.timeMilliseconds, 0, duration);
}

/**
 * The moment, inside a passage, that the ball is struck at goal.
 *
 * It is the first keyframe any player's track tags as a strike; failing that, the line that narrates the
 * chance; failing that, the start of the passage.
 */
function strikeTime(passage: Passage, duration: number): number {
  let earliest = Number.POSITIVE_INFINITY;

  for (const track of passage.tracks) {
    for (const keyframe of track.keyframes) {
      if (isStrikeTag(keyframe.action) && keyframe.timeMilliseconds < earliest) {
        earliest = keyframe.timeMilliseconds;
      }
    }
  }

  if (Number.isFinite(earliest)) {
    return clamp(earliest, 0, duration);
  }

  const line = (passage.commentary ?? []).find((token) =>
    /^match\.(goal|shot|penalty|free_kick)/.test(token.templateKey),
  );

  return line === undefined ? 0 : clamp(line.timeMilliseconds, 0, duration);
}

/** The event sequences a passage presents, its principal one first. */
function passageSequences(passage: Passage): readonly number[] {
  return passage.sourceEventSequence > 0 &&
    !passage.eventSequences.includes(passage.sourceEventSequence)
    ? [passage.sourceEventSequence, ...passage.eventSequences]
    : passage.eventSequences;
}

/** Whether a passage's outcome earns a scrubber marker, and which. */
function markerKind(outcomeCode: string): 'goal' | 'shot' | null {
  if (isGoalOutcome(outcomeCode)) {
    return 'goal';
  }

  return isShotOutcome(outcomeCode) ? 'shot' : null;
}

function goalSide(side: string | undefined): 'home' | 'away' | null {
  return side === 'home' || side === 'away' ? side : null;
}

function parameter(
  parameters: readonly { readonly name: string; readonly value: string }[],
  name: string,
): string | undefined {
  return parameters.find((candidate) => candidate.name === name)?.value;
}

/** Which side a feed token belongs to, from its own facts rather than a rendered sentence. */
function tokenSide(token: HighlightCommentary, index: LineupIndex): string {
  const playerId = parameter(token.parameters, 'playerId');

  if (playerId !== undefined) {
    return index.sideByPlayerId.get(playerId) ?? '';
  }

  const participantId = parameter(token.parameters, 'participantId');

  return participantId === undefined ? '' : (index.sideByParticipant.get(participantId) ?? '');
}

/** The cards everybody carries after each card in turn, so a frame looks a state up rather than building one. */
function cardStatesOf(
  moments: readonly FilmCardMoment[],
): readonly ReadonlyMap<string, FilmCardKind>[] {
  const states: ReadonlyMap<string, FilmCardKind>[] = [new Map()];
  const running = new Map<string, FilmCardKind>();

  for (const moment of moments) {
    running.set(moment.participantId, moment.kind);
    states.push(new Map(running));
  }

  return states;
}

function byFilmTime<T extends { readonly filmMilliseconds: number }>(one: T, other: T): number {
  return one.filmMilliseconds - other.filmMilliseconds;
}

/** The index of the last value that is at or before a moment, or -1 when every value is after it. */
function lastIndexAtOrBefore(values: readonly number[], moment: number): number {
  let low = 0;
  let high = values.length;

  while (low < high) {
    const middle = (low + high) >>> 1;

    if (values[middle] <= moment) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }

  return low - 1;
}

/** How many items of a list sorted by film time are at or before a moment. */
function countAtOrBefore(
  items: readonly { readonly filmMilliseconds: number }[],
  moment: number,
): number {
  let low = 0;
  let high = items.length;

  while (low < high) {
    const middle = (low + high) >>> 1;

    if (items[middle].filmMilliseconds <= moment) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }

  return low;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  MatchPlayback,
  PLAYBACK_SPEEDS,
  PlaybackMode,
  PlaybackSpeed,
  PlaybackState,
} from '../../core/match/match-playback';
import {
  cardKindFor,
  commentarySideLabel,
  conditionColorClass,
  formatConditionPercent,
  formatMatchRating,
  isGoalCommentary,
  isGoalOutcome,
  isShotOutcome,
  matchClockFromSeconds,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  passageIndexForLine,
  passageTitle,
  playbackClockLabel,
  ratingColorClass,
  scoreLine,
  ShotMapEntry,
  shotMapEntries,
} from '../../core/match/match-presentation';
import {
  CommentaryLine,
  HighlightCommentary,
  MatchLineupPlayer,
  MatchPresentation,
  Passage,
  PlayerLiveMetric,
} from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { formatInstant } from '../../core/world/presentation';
import { CanvasMatchRenderer } from './renderer/canvas-match-renderer';
import { CardKind } from './renderer/renderer.models';
import { RenderLoop } from './renderer/render-loop';

/** The match center's sections, in the order a manager reads them. */
const TABS = [
  { id: 'replay', label: 'Replay' },
  { id: 'report', label: 'Report' },
  { id: 'stats', label: 'Statistics' },
  { id: 'players', label: 'Players' },
] as const;

/** One of the match center's tabs. */
type TabId = (typeof TABS)[number]['id'];

/** How long the scoreboard's goal highlight stays up, in milliseconds. */
const GOAL_FLASH_MILLISECONDS = 2_500;

/** The normalized scale the pitch speaks on both axes. */
const PITCH_SCALE = 10_000;

/** How coarsely the scrubber's elapsed time is published, so a 60 Hz frame does not drive change detection. */
const ELAPSED_QUANTUM_MILLISECONDS = 250;

/** One row of the replay feed: a passage's commentary beat at a film moment. */
interface FeedRow {
  readonly key: string;
  readonly filmMilliseconds: number;
  readonly clock: string;
  readonly side: string;
  readonly text: string;
  readonly isGoal: boolean;
}

/** One marker on the progress scrubber: a goal or a shot. */
interface ReplayMarker {
  readonly key: string;
  readonly kind: 'goal' | 'shot';
  readonly percent: number;
  readonly milliseconds: number;
  readonly label: string;
}

/**
 * The FM/CM-style match center (master plan §9.5, §11.1, `replay-v3`).
 *
 * A result and its replay. The summary is the scoreboard and the scoreline; the replay is one continuous
 * film of the whole match, played beside the two lineups with a scrolling commentary feed under it and a
 * scrubber beneath that, plus three more tabs — report, statistics and shot map, player performance — that
 * do not animate. The lineups are live: as the film advances, each panel's condition bars and rating badges
 * move to the figures the server captured minute by minute.
 *
 * Two playlists share one film: *Full match* plays it whole, *Highlights* plays only the reel's chance
 * clips over the same film clock, so a chance is always preceded by the move that produced it. Both are the
 * same data; the toggle only chooses which windows of film time play.
 *
 * The animation is deliberately independent of Angular change detection: the render loop draws from the
 * playback state every frame, and signals are written only when something discrete changes — the passage,
 * the play state, the clock label, the feed's length — so a 60 Hz animation does not re-evaluate the
 * commentary list 60 times a second. The loop is stopped on pause, on the tab going hidden, and on
 * destruction, so no callback outlives the canvas it draws to.
 */
@Component({
  selector: 'app-match-viewer',
  templateUrl: './match-viewer.html',
})
export class MatchViewer implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(MatchStore);
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('pitch');
  private readonly feedScroll = viewChild<ElementRef<HTMLElement>>('feedScroll');

  private playback = new MatchPlayback([]);
  private renderer: CanvasMatchRenderer | null = null;

  /** The presentation the current playback was built from, so a load rebuilds it once. */
  private builtFrom: MatchPresentation | null = null;

  private flashTimer: ReturnType<typeof setTimeout> | null = null;
  private flashedSequence: number | null = null;

  private readonly loop = new RenderLoop((delta) => this.onFrame(delta));
  private readonly motionQuery = mediaQuery('(prefers-reduced-motion: reduce)');
  private readonly motionListener = (event: MediaQueryListEvent) => this.reduced.set(event.matches);
  private readonly visibilityListener = () => {
    if (typeof document !== 'undefined' && document.hidden) {
      this.pause();
    }
  };

  protected readonly match = this.store.match;
  protected readonly presentation = this.store.presentation;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.error;

  /** The active passage's index, published when it changes rather than every frame. */
  protected readonly activeIndex = signal(0);

  /** What the renderer draws: the passage the playhead stands in. */
  protected readonly passage = signal<Passage | null>(null);

  /** The play state, published when it changes rather than every frame. */
  protected readonly state = signal<PlaybackState>(this.playback.currentState);

  /** The speed control's value. */
  protected readonly speed = signal<PlaybackSpeed>(1);

  /** Which playlist is playing: the whole film, or the reel. */
  protected readonly mode = signal<PlaybackMode>('full');

  /** Whether the manager asked for the text-only presentation. */
  protected readonly textOnly = signal(false);

  /** Whether the system asks for reduced motion. */
  protected readonly reduced = signal(prefersReducedMotion());

  /** The active tab. */
  protected readonly activeTab = signal<TabId>('replay');

  /** The continuous scoreboard clock, written only when the label changes. */
  protected readonly clockLabel = signal('');

  /** The minute the continuous clock shows, which the live panels follow. */
  protected readonly clockMinute = signal(0);

  /** How much of the active playlist has played, in milliseconds, quantized so it does not churn. */
  protected readonly elapsedMs = signal(0);

  /** How long the active playlist runs, in milliseconds. */
  protected readonly totalMs = signal(0);

  /** How many feed rows the playhead has reached. */
  protected readonly feedCount = signal(0);

  /** The feed's rows, built once per presentation. */
  private readonly feedEntries = signal<readonly FeedRow[]>([]);

  /** Whether the feed is scrolled to its newest row. */
  protected readonly feedPinned = signal(true);

  /** Which side just scored, for the scoreboard's own flash. */
  protected readonly scoringSide = signal<'home' | 'away' | null>(null);

  protected readonly playing = computed(() => this.state() === 'playing');
  protected readonly speeds = PLAYBACK_SPEEDS;
  protected readonly tabs = TABS;
  protected readonly hasReplay = computed(() => (this.presentation()?.passages.length ?? 0) > 0);
  protected readonly activePassage = computed(
    () => this.presentation()?.passages[this.activeIndex()] ?? null,
  );
  protected readonly clock = computed(() => this.clockLabel());
  protected readonly elapsedLabel = computed(() => playbackClockLabel(this.elapsedMs()));
  protected readonly totalLabel = computed(() => playbackClockLabel(this.totalMs()));

  /** The feed rows the playhead has reached, oldest first. */
  protected readonly feed = computed(() => this.feedEntries().slice(0, this.feedCount()));

  /** The goal and shot markers the scrubber draws. */
  protected readonly markers = computed<readonly ReplayMarker[]>(() => {
    // The reel changes which film moments are reachable, so the markers are recomputed with the playlist.
    this.mode();

    const total = this.playback.totalMilliseconds;

    if (total <= 0) {
      return [];
    }

    const markers: ReplayMarker[] = [];

    this.playback.passages.forEach((passage, index) => {
      const kind = markerKind(passage.outcomeCode);

      if (kind === null) {
        return;
      }

      const milliseconds = this.playback.playlistMillisecondsForFilm(
        this.playback.filmStartOf(index),
      );

      if (milliseconds === null) {
        return;
      }

      markers.push({
        key: `${kind}-${index}`,
        kind,
        percent: (milliseconds / total) * 100,
        milliseconds,
        label: passageTitle(passage),
      });
    });

    return markers;
  });

  protected readonly statisticRows = computed(() => {
    const match = this.match();

    return match === null ? [] : matchStatisticRows(match.home.statistics, match.away.statistics);
  });
  protected readonly score = computed(() => {
    const match = this.match();

    return match === null ? '' : scoreLine(match.home.goals, match.away.goals);
  });
  protected readonly homeGoalAnimation = computed(() => this.scoringSide() === 'home');
  protected readonly awayGoalAnimation = computed(() => this.scoringSide() === 'away');

  /**
   * The scoreline the goalscorers sit under, read from the commentary rather than from the lineups.
   *
   * A lineup line carries how many a player scored but not when; the commentary carries the minute of every
   * goal as its own event, which is what a scoreboard lists.
   */
  protected readonly scorers = computed(() => {
    const view = this.presentation();

    if (view === null) {
      return [];
    }

    const names = this.lineupIndex().nameByPlayerId;
    const list: string[] = [];

    for (const line of view.commentary) {
      if (!isGoalCommentary(line.templateKey)) {
        continue;
      }

      const playerId = line.parameters.find((parameter) => parameter.name === 'playerId')?.value;
      const name = playerId === undefined ? undefined : names.get(playerId);

      list.push(
        name === undefined
          ? line.text
          : `${name} ${matchClockLabel(line.minute, line.stoppageMinute)}`,
      );
    }

    return list;
  });

  /** Every shot the film holds, for the statistics tab's shot map. */
  protected readonly shotMap = computed(() => shotMapEntries(this.presentation()?.passages ?? []));
  protected readonly homeShots = computed(() =>
    this.shotMap().filter((shot) => shot.side === 'home'),
  );
  protected readonly awayShots = computed(() =>
    this.shotMap().filter((shot) => shot.side === 'away'),
  );
  protected readonly homeColour = computed(
    () => this.presentation()?.homeLineup?.primaryColour ?? '#38bdf8',
  );
  protected readonly awayColour = computed(
    () => this.presentation()?.awayLineup?.primaryColour ?? '#fb7185',
  );
  protected readonly homeSecondaryColour = computed(
    () => this.presentation()?.homeLineup?.secondaryColour ?? '#0f172a',
  );
  protected readonly awaySecondaryColour = computed(
    () => this.presentation()?.awayLineup?.secondaryColour ?? '#0f172a',
  );

  /**
   * Every lineup player by participant, and every name and side by player.
   *
   * The panels ask about 22 players on every change-detection pass, so both lookups are built once per
   * presentation rather than searched per draw. The side maps are what let a feed row carry the right
   * accent without reading a rendered sentence.
   */
  private readonly lineupIndex = computed(() => {
    const byParticipant = new Map<string, MatchLineupPlayer>();
    const nameByPlayerId = new Map<string, string>();
    const participantByPlayerId = new Map<string, string>();
    const sideByPlayerId = new Map<string, string>();
    const sideByParticipant = new Map<string, string>();
    const view = this.presentation();

    for (const [side, lineup] of [
      ['home', view?.homeLineup],
      ['away', view?.awayLineup],
    ] as const) {
      if (lineup === null || lineup === undefined) {
        continue;
      }

      for (const player of [...lineup.starters, ...lineup.bench]) {
        byParticipant.set(player.participantId, player);
        nameByPlayerId.set(player.playerId, player.name);
        participantByPlayerId.set(player.playerId, player.participantId);
        sideByPlayerId.set(player.playerId, side);
        sideByParticipant.set(player.participantId, side);
      }
    }

    return {
      byParticipant,
      nameByPlayerId,
      participantByPlayerId,
      sideByPlayerId,
      sideByParticipant,
    };
  });

  /**
   * The card each player carries at the shown minute, which the pitch draws above their token.
   *
   * A booking is a state a player keeps for the rest of the match, so the badge appears once the film
   * reaches the passage that produced it: the card lines before the active passage's own event are read
   * from the same commentary the report lists, and the player they name is resolved through the lineups.
   */
  private readonly cardState = computed(() => {
    const view = this.presentation();
    const passage = this.activePassage();
    const cards = new Map<string, CardKind>();

    if (view === null || passage === null || this.state() === 'idle') {
      return cards;
    }

    const participants = this.lineupIndex().participantByPlayerId;
    const boundary =
      passage.sourceEventSequence > 0
        ? passage.sourceEventSequence
        : (passage.eventSequences[0] ?? Number.MAX_SAFE_INTEGER);

    for (const line of view.commentary) {
      // Commentary is in event order, so once a line belongs to the passage being shown — or to anything
      // after it — no later line can have happened yet.
      if (line.sequence >= boundary) {
        break;
      }

      const card = cardKindFor(line.templateKey);

      if (card === null) {
        continue;
      }

      const playerId = line.parameters.find((parameter) => parameter.name === 'playerId')?.value;
      const participantId = playerId === undefined ? undefined : participants.get(playerId);

      if (participantId !== undefined) {
        cards.set(participantId, card);
      }
    }

    return cards;
  });

  /** The match minute the live panels show: kickoff before the replay starts, the film's clock after. */
  private readonly metricMinute = computed(() =>
    this.state() === 'idle' ? 0 : this.clockMinute(),
  );

  /**
   * The latest captured metric per player at or before the shown minute.
   *
   * The curve is chronological, so the last snapshot that is not in the future is the state the minute
   * ended in — which is the figure the live panel showed, not an interpolation between kickoff and full
   * time.
   */
  private readonly metricLookup = computed(() => {
    const view = this.presentation();
    const minute = this.metricMinute();
    const byParticipant = new Map<string, PlayerLiveMetric>();

    for (const metric of view?.liveMetrics ?? []) {
      if (metric.minute > minute) {
        continue;
      }

      const existing = byParticipant.get(metric.participantId);

      if (existing === undefined || metric.minute >= existing.minute) {
        byParticipant.set(metric.participantId, metric);
      }
    }

    return byParticipant;
  });

  constructor() {
    const matchId = this.route.snapshot.paramMap.get('matchId');

    if (matchId !== null && matchId.length > 0) {
      this.store.load(matchId);
    }

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', this.visibilityListener);
    }

    this.motionQuery?.addEventListener('change', this.motionListener);

    // The film is built from the presentation once it loads. A frame never resets a replay that is in
    // progress: the same presentation is left alone, so a reload does not restart the film.
    effect(() => {
      const presentation = this.presentation();

      if (presentation === this.builtFrom) {
        return;
      }

      this.builtFrom = presentation;
      this.feedEntries.set(this.buildFeed(presentation));
      this.feedCount.set(0);
      this.feedPinned.set(true);

      this.playback = new MatchPlayback(
        presentation?.passages ?? [],
        presentation?.playback ?? [],
        presentation?.reel ?? [],
        untracked(() => this.mode()),
      );

      this.loop.stop();
      this.publishAll();
    });

    // The renderer is (re)built when the drawn passage, the canvas, or the text-only preference changes —
    // not on every frame, which is what keeps the animation off the change-detection path.
    effect(() => {
      const passage = this.passage();
      const canvas = this.canvas();
      const textOnly = this.textOnly();
      const cards = this.cardState();
      // Reduced motion shows the passage as a still frame at its end rather than animating it. It is the
      // *idle* state that is drawn still, so a pause mid-passage keeps the frame the manager paused on.
      const still = this.reduced() && this.state() === 'idle';

      this.renderer?.dispose();
      this.renderer = null;

      if (passage === null || canvas === undefined || textOnly) {
        return;
      }

      this.renderer = new CanvasMatchRenderer(canvas.nativeElement, passage, {
        kits: {
          home: { primary: this.homeColour(), secondary: this.homeSecondaryColour() },
          away: { primary: this.awayColour(), secondary: this.awaySecondaryColour() },
        },
        cards,
        reducedMotion: this.reduced(),
      });
      this.renderer.render(still ? passage.durationMilliseconds : this.playback.passageTimeMs);
    });
  }

  /** Stops the loop and drops the renderer, so nothing outlives the screen. */
  ngOnDestroy(): void {
    this.loop.dispose();
    this.renderer?.dispose();
    this.renderer = null;

    if (this.flashTimer !== null) {
      clearTimeout(this.flashTimer);
      this.flashTimer = null;
    }

    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityListener);
    }

    this.motionQuery?.removeEventListener('change', this.motionListener);
  }

  /** Starts or resumes the replay. */
  protected play(): void {
    this.playback.play();
    this.apply();
  }

  /** Holds the replay where it is. */
  protected pause(): void {
    this.playback.pause();
    this.apply();
  }

  /** Starts or holds the replay. */
  protected togglePlay(): void {
    if (this.playing()) {
      this.pause();
    } else {
      this.play();
    }
  }

  /** Changes the playback speed. */
  protected setSpeed(speed: PlaybackSpeed): void {
    this.speed.set(speed);
    this.playback.setSpeed(speed);
  }

  /** Chooses which playlist plays: the whole film, or the reel. */
  protected setMode(mode: PlaybackMode): void {
    if (mode === this.mode()) {
      return;
    }

    this.mode.set(mode);
    this.playback.setMode(mode);
    this.apply();
  }

  /** Moves to the next passage the active playlist carries. */
  protected skipCurrent(): void {
    this.playback.skipCurrent();
    this.apply();
  }

  /** Jumps to the end of the active playlist. */
  protected skipAll(): void {
    this.playback.skipAll();
    this.apply();
  }

  /** Starts the active playlist again. */
  protected replay(): void {
    this.playback.replay();
    this.apply();
  }

  /** Seeks to a passage by index. */
  protected seekTo(index: number): void {
    this.playback.seekTo(index);
    this.apply();
  }

  /** Seeks within the active playlist by elapsed milliseconds. */
  protected seekToMilliseconds(milliseconds: number): void {
    this.playback.seekToMilliseconds(milliseconds);
    this.apply();
  }

  /** Seeks from the scrubber's own value. */
  protected seekFromScrubber(event: Event): void {
    this.seekToMilliseconds(Number((event.target as HTMLInputElement).value));
  }

  /** Switches the presentation to text only, or back to the Canvas. */
  protected toggleTextOnly(): void {
    this.textOnly.update((value) => !value);

    if (this.textOnly()) {
      this.pause();
    }
  }

  /** Switches tabs. */
  protected setTab(tab: TabId): void {
    this.activeTab.set(tab);
  }

  /**
   * Seeks to the passage that presents a commentary line, then shows the pitch.
   *
   * A line narrates an event and a passage carries the events it produced, so a line with no passage behind
   * it offers nothing rather than a button that does nothing. In highlights mode an event outside the reel
   * switches back to the whole film, because the reel cannot show it.
   */
  protected showLineAndSwitch(line: CommentaryLine): void {
    if (!this.playback.seekToEvent(line.sequence) && this.mode() === 'reel') {
      this.mode.set('full');
      this.playback.setMode('full');
      this.playback.seekToEvent(line.sequence);
    }

    this.activeTab.set('replay');
    this.apply();
  }

  /** Whether a commentary line has a passage behind it, so the report can offer one. */
  protected hasPassage(line: CommentaryLine): boolean {
    return passageIndexForLine(this.presentation()?.passages ?? [], line) >= 0;
  }

  /** Marks which end a feed row belongs to. */
  protected sideClass(side: string): string {
    return side === 'home' ? 'text-sky-300' : side === 'away' ? 'text-rose-300' : 'text-slate-300';
  }

  /** Re-pins the feed and scrolls to its newest row. */
  protected jumpToLive(): void {
    this.feedPinned.set(true);
    this.scrollFeedToBottom();
  }

  /** Tracks whether the feed is scrolled to its newest row, or the manager has scrolled up. */
  protected onFeedScroll(): void {
    const element = this.feedScroll()?.nativeElement;

    if (element === undefined) {
      return;
    }

    const pinned = element.scrollHeight - element.scrollTop - element.clientHeight < 24;

    if (pinned !== this.feedPinned()) {
      this.feedPinned.set(pinned);
    }
  }

  /** A player's condition at the shown minute, on the 0–10,000 scale. */
  protected liveCondition(participantId: string): number {
    const metric = this.metricLookup().get(participantId);

    return metric?.conditionBasisPoints ?? this.lineupPlayer(participantId)?.kickoffCondition ?? 0;
  }

  /** A player's condition as the percentage the bar is drawn at. */
  protected liveConditionPercent(basisPoints: number): string {
    return formatConditionPercent(basisPoints);
  }

  /** A player's condition as the whole percentage the meter exposes, which a screen reader reads. */
  protected liveConditionMeterValue(participantId: string): number {
    return Math.round(this.liveCondition(participantId) / 100);
  }

  /** The colour class for a condition bar, green through red as a player tires. */
  protected conditionBarClass(basisPoints: number): string {
    return conditionColorClass(basisPoints);
  }

  /**
   * A player's live rating at the shown minute, on the 0–10,000 scale.
   *
   * A presentation carries the curve the live panel drew, so a rating is the last capture at or before the
   * minute. Without a curve it falls back to the final rating the lineup recorded.
   */
  protected liveRating(participantId: string): number {
    const metric = this.metricLookup().get(participantId);

    if (metric !== undefined) {
      return metric.ratingBasisPoints;
    }

    const player = this.lineupPlayer(participantId);
    const hasCurve = (this.presentation()?.liveMetrics?.length ?? 0) > 0;

    return player !== null && !hasCurve ? player.finalRating : 0;
  }

  /** Formats a live rating, e.g. `7.4`, or a dash before one exists. */
  protected formatRating(basisPoints: number): string {
    return formatMatchRating(basisPoints);
  }

  /** The FM-style badge colours for a live rating. */
  protected ratingBadgeClass(basisPoints: number): string {
    return ratingColorClass(basisPoints);
  }

  /** The goalscorers in one lineup's starters, for the panel's summary line. */
  protected teamGoalScorers(starters: readonly MatchLineupPlayer[]): readonly string[] {
    return starters
      .filter((player) => player.goals > 0)
      .map((player) => (player.goals > 1 ? `${player.name} (${player.goals})` : player.name));
  }

  /** Whether a commentary line reports a goal, which the report highlights. */
  protected isGoalCommentaryLine(line: CommentaryLine): boolean {
    return isGoalCommentary(line.templateKey);
  }

  /** Names the end a commentary line belongs to. */
  protected side(side: string): string {
    return commentarySideLabel(side);
  }

  /** Formats a match clock. */
  protected minute(minute: number, stoppageMinute: number): string {
    return matchClockLabel(minute, stoppageMinute);
  }

  /** Formats a kickoff in the viewer's local time. */
  protected kickoff(instant: string): string {
    return formatInstant(instant);
  }

  /** A statistics bar's width as its share of the two sides' total. */
  protected statBarWidth(home: string, away: string, isHome: boolean): string {
    const homeValue = Number.parseFloat(home) || 0;
    const awayValue = Number.parseFloat(away) || 0;
    const total = homeValue + awayValue;

    if (total <= 0) {
      return '0%';
    }

    return `${Math.round(((isHome ? homeValue : awayValue) / total) * 100)}%`;
  }

  /** A shot's X on the shot map's 105-unit pitch. */
  protected shotX(x: number): number {
    return (x / PITCH_SCALE) * 105;
  }

  /** A shot's Y on the shot map's 68-unit pitch. */
  protected shotY(y: number): number {
    return (y / PITCH_SCALE) * 68;
  }

  /** A shot's marker radius: a goal reads bigger than an attempt that did not beat the keeper. */
  protected shotRadius(shot: ShotMapEntry): number {
    return isGoalOutcome(shot.outcomeCode) ? 1.7 : 1.15;
  }

  /** A shot marker's tooltip, naming the outcome and the clock. */
  protected shotTitle(shot: ShotMapEntry): string {
    return `${outcomeLabel(shot.outcomeCode)} \u2014 ${matchClockLabel(shot.minute, shot.stoppageMinute)}`;
  }

  private lineupPlayer(participantId: string): MatchLineupPlayer | null {
    return this.lineupIndex().byParticipant.get(participantId) ?? null;
  }

  private onFrame(delta: number): void {
    const changed = this.playback.advance(delta);

    if (changed || this.playback.currentState !== this.state()) {
      this.publish();
    }

    this.updateClock();
    this.updateFeed();
    this.updateProgress();
    this.renderer?.render(this.playback.passageTimeMs);

    if (this.playback.currentState !== 'playing') {
      this.loop.stop();
    }
  }

  /** Publishes the playback's discrete state and starts or stops the loop to match it. */
  private apply(): void {
    this.publish();
    this.updateClock();
    this.updateFeed();
    this.updateProgress();

    if (this.playback.currentState === 'playing') {
      this.loop.start();
    } else {
      this.loop.stop();
      this.renderer?.render(this.playback.passageTimeMs);
    }
  }

  /** Publishes everything discrete the playback is showing. */
  private publishAll(): void {
    this.publish();
    this.updateClock();
    this.updateFeed();
    this.updateProgress();
  }

  /** Publishes what the playback is showing, flashing the scoreboard when a goal comes up. */
  private publish(): void {
    const passage = this.playback.active;

    this.activeIndex.set(this.playback.activeIndex);
    this.passage.set(passage);
    this.state.set(this.playback.currentState);

    if (passage !== null && isGoalOutcome(passage.outcomeCode)) {
      this.flashGoal(passage);
    }
  }

  /**
   * Advances the scoreboard clock over the active passage's match window.
   *
   * The engine records a match second as `(minute + stoppage) * 60`, so a passage's window can be walked
   * continuously and the label only written when it actually changes.
   */
  private updateClock(): void {
    const active = this.playback.activePassage;

    if (active === null) {
      if (this.clockLabel() !== '') {
        this.clockLabel.set('');
        this.clockMinute.set(0);
      }

      return;
    }

    const span = active.passage.endMatchSecond - active.passage.startMatchSecond;
    const fraction =
      active.durationMilliseconds > 0
        ? this.playback.passageTimeMs / active.durationMilliseconds
        : 0;
    const clock = matchClockFromSeconds(active.passage.startMatchSecond + fraction * span);
    const label = matchClockLabel(clock.minute, clock.stoppageMinute);

    if (label !== this.clockLabel()) {
      this.clockLabel.set(label);
      this.clockMinute.set(clock.minute);
    }
  }

  /** Publishes the feed's visible length as the playhead passes each row's film time. */
  private updateFeed(): void {
    const position = this.playback.positionMs;
    const entries = this.feedEntries();
    let count = 0;

    for (const entry of entries) {
      if (entry.filmMilliseconds > position) {
        break;
      }

      count += 1;
    }

    if (count !== this.feedCount()) {
      this.feedCount.set(count);

      if (this.feedPinned()) {
        setTimeout(() => this.scrollFeedToBottom(), 0);
      }
    }
  }

  /** Publishes the scrubber's elapsed and total film time, quantized so a frame does not churn it. */
  private updateProgress(): void {
    const total = Math.round(this.playback.totalMilliseconds);
    const elapsed =
      Math.round(this.playback.elapsedMilliseconds / ELAPSED_QUANTUM_MILLISECONDS) *
      ELAPSED_QUANTUM_MILLISECONDS;

    if (total !== this.totalMs()) {
      this.totalMs.set(total);
    }

    if (elapsed !== this.elapsedMs()) {
      this.elapsedMs.set(elapsed);
    }
  }

  /** Scrolls the feed to its newest row, when the element exists. */
  private scrollFeedToBottom(): void {
    const element = this.feedScroll()?.nativeElement;

    if (element !== undefined) {
      element.scrollTop = element.scrollHeight;
    }
  }

  /** Builds every feed row a presentation offers, in film order. */
  private buildFeed(presentation: MatchPresentation | null): readonly FeedRow[] {
    if (presentation === null) {
      return [];
    }

    const starts = filmStarts(presentation);
    const index = this.lineupIndex();
    const rows: FeedRow[] = [];

    presentation.passages.forEach((passage, passageIndex) => {
      let tokenIndex = 0;

      for (const token of passage.commentary ?? []) {
        rows.push({
          key: `${passageIndex}:${tokenIndex}`,
          filmMilliseconds: starts[passageIndex] + token.timeMilliseconds,
          clock: tokenClock(passage, token),
          side: tokenSide(token, index),
          text: token.text,
          isGoal: isGoalCommentary(token.templateKey),
        });

        tokenIndex += 1;
      }
    });

    return rows.sort((one, other) => one.filmMilliseconds - other.filmMilliseconds);
  }

  /** Flashes the scoreboard for a goal, once per passage. */
  private flashGoal(passage: Passage): void {
    if (this.flashedSequence === passage.sourceEventSequence) {
      return;
    }

    const side = this.goalSide(passage);

    if (side === null) {
      return;
    }

    this.flashedSequence = passage.sourceEventSequence;
    this.scoringSide.set(side);

    if (this.flashTimer !== null) {
      clearTimeout(this.flashTimer);
    }

    this.flashTimer = setTimeout(() => {
      this.flashTimer = null;
      this.flashedSequence = null;
      this.scoringSide.set(null);
    }, GOAL_FLASH_MILLISECONDS);
  }

  /** Which side scored the passage's goal, read from the commentary line that narrates it. */
  private goalSide(passage: Passage): 'home' | 'away' | null {
    const line = this.presentation()?.commentary.find(
      (candidate) => candidate.sequence === passage.sourceEventSequence,
    );

    return line === undefined || (line.side !== 'home' && line.side !== 'away') ? null : line.side;
  }
}

/** The film offset of each passage, from the presentation's own schedule. */
function filmStarts(presentation: MatchPresentation): readonly number[] {
  let cursor = 0;

  return presentation.passages.map((passage, index) => {
    const schedule = presentation.playback?.[index];
    const start =
      schedule !== undefined && schedule.kind === 'passage' ? schedule.startMilliseconds : cursor;

    cursor =
      start +
      (schedule !== undefined && schedule.kind === 'passage'
        ? schedule.durationMilliseconds
        : passage.durationMilliseconds);

    return start;
  });
}

/** A feed token's clock, walked continuously over its passage's match window. */
function tokenClock(passage: Passage, token: HighlightCommentary): string {
  const span = passage.endMatchSecond - passage.startMatchSecond;
  const fraction =
    passage.durationMilliseconds > 0 ? token.timeMilliseconds / passage.durationMilliseconds : 0;
  const clock = matchClockFromSeconds(passage.startMatchSecond + fraction * span);

  return matchClockLabel(clock.minute, clock.stoppageMinute);
}

/** Which side a feed token belongs to, from its own facts rather than a rendered sentence. */
function tokenSide(
  token: HighlightCommentary,
  index: {
    readonly sideByPlayerId: ReadonlyMap<string, string>;
    readonly sideByParticipant: ReadonlyMap<string, string>;
  },
): string {
  const playerId = token.parameters.find((parameter) => parameter.name === 'playerId')?.value;

  if (playerId !== undefined) {
    return index.sideByPlayerId.get(playerId) ?? '';
  }

  const participantId = token.parameters.find(
    (parameter) => parameter.name === 'participantId',
  )?.value;

  return participantId === undefined ? '' : (index.sideByParticipant.get(participantId) ?? '');
}

/** Whether a passage's outcome earns a scrubber marker, and which. */
function markerKind(outcomeCode: string): 'goal' | 'shot' | null {
  if (isGoalOutcome(outcomeCode)) {
    return 'goal';
  }

  return isShotOutcome(outcomeCode) ? 'shot' : null;
}

/** Builds a media-query list where the platform has one, and nothing where it does not. */
function mediaQuery(query: string): MediaQueryList | null {
  return typeof matchMedia === 'function' ? matchMedia(query) : null;
}

/** Whether the platform asks for reduced motion. */
function prefersReducedMotion(): boolean {
  return mediaQuery('(prefers-reduced-motion: reduce)')?.matches ?? false;
}

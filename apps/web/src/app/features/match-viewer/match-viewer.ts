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
import { FadeEdge, fadeAlpha, fadeEdgesFor } from '../../core/match/film-fade';
import { FilmTimeline, buildFilmTimeline } from '../../core/match/film-timeline';
import { lineupIndexOf } from '../../core/match/match-lineups';
import {
  MatchPlayback,
  PLAYBACK_SPEEDS,
  PlaybackMode,
  PlaybackSpeed,
  PlaybackState,
} from '../../core/match/match-playback';
import {
  commentarySideLabel,
  conditionColorClass,
  formatConditionPercent,
  formatMatchRating,
  isGoalCommentary,
  isGoalOutcome,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  passageIndexForLine,
  playbackClockLabel,
  ratingColorClass,
  scoreLine,
  ShotMapEntry,
  shotMapEntries,
} from '../../core/match/match-presentation';
import {
  CommentaryLine,
  MatchLineupPlayer,
  MatchPresentation,
  PlayerLiveMetric,
} from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
import { formatInstant } from '../../core/world/presentation';
import { CanvasMatchRenderer } from './renderer/canvas-match-renderer';
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

/** The normalized scale the pitch speaks on both axes. */
const PITCH_SCALE = 10_000;

/** How coarsely the scrubber's elapsed time is published, so a 60 Hz frame does not drive change detection. */
const ELAPSED_QUANTUM_MILLISECONDS = 250;

/** One marker on the progress scrubber: a goal or a shot. */
interface ReplayMarker {
  readonly key: string;
  readonly kind: 'goal' | 'shot';
  /** Where the dot sits on the scrubber: the moment the ball is struck. */
  readonly percent: number;
  /** Where clicking it seeks to: the start of the passage that builds to the strike. */
  readonly milliseconds: number;
  readonly label: string;
}

/**
 * The FM/CM-style match center (master plan §9.5, §11.1, `replay-v3`, `replay-v4`).
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
 * The film is *one* thing on screen (`replay-v4`). The presentation's passages are built into a
 * `FilmTimeline` once, and the viewer makes one renderer for it and draws global film time: the passage the
 * playhead is in is not something the picture knows about, so nothing is rebuilt or redrawn at a stale time
 * when it moves on. The clock, the feed, the cards and the live panels are all read from the same film moment.
 * The screen dips to dark over a cut and over a jump in the highlights reel.
 *
 * The animation is deliberately independent of Angular change detection: the render loop draws from the
 * playback state every frame, and signals are written only when something discrete changes — the play
 * state, the clock label, the feed's length — so a 60 Hz animation does not re-evaluate the commentary list
 * 60 times a second. The loop is stopped on pause, on the tab going hidden, and on destruction, so no
 * callback outlives the canvas it draws to.
 */
@Component({
  selector: 'app-match-viewer',
  templateUrl: './match-viewer.html',
  // The viewer is drawn on dark ground in either theme, so it keeps the dark tokens (`.theme-dark`, styles.css).
  host: { class: 'theme-dark block' },
})
export class MatchViewer implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(MatchStore);
  private readonly reveals = inject(ResultRevealStore);
  private readonly matchId = this.route.snapshot.paramMap.get('matchId');
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('pitch');
  private readonly feedScroll = viewChild<ElementRef<HTMLElement>>('feedScroll');

  private playback = new MatchPlayback([]);
  private renderer: CanvasMatchRenderer | null = null;

  /** The presentation the current playback was built from, so a load rebuilds it once. */
  private builtFrom: MatchPresentation | null = null;

  /** Where the screen dips to dark, for the playlist being played. */
  private fadeEdges: readonly FadeEdge[] = [];

  private readonly loop = new RenderLoop((delta) => this.onFrame(delta));
  private readonly motionQuery = mediaQuery('(prefers-reduced-motion: reduce)');
  private readonly motionListener = (event: MediaQueryListEvent) => this.reduced.set(event.matches);
  private readonly visibilityListener = () => {
    if (typeof document !== 'undefined' && document.hidden) {
      this.pause();
    }
  };

  /**
   * Whether the manager has seen the result: they watched the match to its end, skipped to the end, or asked
   * for it. Until then the screen keeps the score, the scorers, the goal markers and the post-match panels
   * back, so the match is a game to watch rather than a result to be told.
   */
  protected readonly revealed = computed(() => this.reveals.isRevealed(this.matchId));

  protected readonly match = this.store.match;
  protected readonly presentation = this.store.presentation;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.error;

  /** The film the presentation plays as, built once per presentation. */
  private readonly film = signal<FilmTimeline>(buildFilmTimeline(null));

  /** The active passage's index, which the narration follows, published when it changes rather than every frame. */
  protected readonly activeIndex = signal(0);

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

  /** The continuous scoreboard clock, read from the film's half-aware clock and written only when it changes. */
  protected readonly clockLabel = signal('');

  /** The minute the continuous clock shows, which the live panels follow. */
  protected readonly clockMinute = signal(0);

  /** How much of the active playlist has played, in milliseconds, quantized so it does not churn. */
  protected readonly elapsedMs = signal(0);

  /** How long the active playlist runs, in milliseconds. */
  protected readonly totalMs = signal(0);

  /** How many feed rows the playhead has reached. */
  protected readonly feedCount = signal(0);

  /** Whether the feed is scrolled to its newest row. */
  protected readonly feedPinned = signal(true);

  /** The goals struck so far at the playhead, home and away: what the scoreboard shows while the film plays. */
  private readonly liveScore = signal<{ home: number; away: number }>({ home: 0, away: 0 });

  /**
   * The home goals the scoreboard shows. The count follows the playhead, so a goal is seen when it is scored;
   * a match whose result the manager already has, and has not started again, shows its final score.
   */
  protected readonly homeScore = computed(() =>
    this.state() === 'idle' && this.revealed()
      ? (this.presentation()?.homeGoals ?? 0)
      : this.liveScore().home,
  );

  /** The away goals the scoreboard shows, on the same terms as the home goals. */
  protected readonly awayScore = computed(() =>
    this.state() === 'idle' && this.revealed()
      ? (this.presentation()?.awayGoals ?? 0)
      : this.liveScore().away,
  );

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
  protected readonly feed = computed(() => this.film().feed.slice(0, this.feedCount()));

  /** The goal and shot markers the scrubber draws. */
  protected readonly markers = computed<readonly ReplayMarker[]>(() => {
    // The reel changes which film moments are reachable, so the markers are recomputed with the playlist.
    this.mode();

    const total = this.playback.totalMilliseconds;

    if (total <= 0) {
      return [];
    }

    const markers: ReplayMarker[] = [];

    for (const marker of this.film().markers) {
      // A goal marker is a spoiler: where it sits on the scrubber says that a goal comes, and when.
      if (marker.kind === 'goal' && !this.revealed()) {
        continue;
      }

      const struck = this.playback.playlistMillisecondsForFilm(marker.filmMilliseconds);

      if (struck === null) {
        continue;
      }

      markers.push({
        key: marker.key,
        kind: marker.kind,
        percent: (struck / total) * 100,
        // The build-up is what a manager wants to see, so a click starts where the passage did, and where
        // the playlist does not carry that — a reel clip that opens mid-passage — at the strike itself.
        milliseconds:
          this.playback.playlistMillisecondsForFilm(marker.passageStartMilliseconds) ?? struck,
        label: marker.label,
      });
    }

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
   * The panels ask about 22 players on every change-detection pass, so the lookups are built once per
   * presentation rather than searched per draw. The film timeline reads the same index to put a feed row on
   * the right side and a card on the right token.
   */
  private readonly lineupIndex = computed(() => lineupIndexOf(this.presentation()));

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
    if (this.matchId !== null && this.matchId.length > 0) {
      this.store.load(this.matchId);
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

      const timeline = buildFilmTimeline(presentation);

      this.feedCount.set(0);
      this.feedPinned.set(true);

      this.playback = new MatchPlayback(
        presentation?.passages ?? [],
        presentation?.playback ?? [],
        presentation?.reel ?? [],
        untracked(() => this.mode()),
      );

      this.film.set(timeline);
      this.refreshFadeEdges();
      this.loop.stop();
      this.publishAll();
    });

    // The renderer is made once for the film, and again only when the film, the canvas, the kits, the
    // text-only preference or the reduced-motion preference change — never when the playhead moves into the
    // next passage, which is what keeps the picture continuous and the animation off the change-detection path.
    effect(() => {
      const film = this.film();
      const canvas = this.canvas();
      const textOnly = this.textOnly();
      const kits = {
        home: { primary: this.homeColour(), secondary: this.homeSecondaryColour() },
        away: { primary: this.awayColour(), secondary: this.awaySecondaryColour() },
      };
      const reducedMotion = this.reduced();

      this.renderer?.dispose();
      this.renderer = null;

      if (film.isEmpty || canvas === undefined || textOnly) {
        return;
      }

      this.renderer = new CanvasMatchRenderer(canvas.nativeElement, film, { kits, reducedMotion });
      this.renderFrame();
    });
  }

  /** Stops the loop and drops the renderer, so nothing outlives the screen. */
  ngOnDestroy(): void {
    this.loop.dispose();
    this.renderer?.dispose();
    this.renderer = null;

    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityListener);
    }

    this.motionQuery?.removeEventListener('change', this.motionListener);
  }

  /** Shows the result without watching the match. */
  protected revealResult(): void {
    this.reveals.reveal(this.matchId);
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
    this.refreshFadeEdges();
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
      this.refreshFadeEdges();
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
    this.updateGoal();
    this.updateProgress();
    this.renderFrame();

    if (this.playback.currentState !== 'playing') {
      this.loop.stop();
    }
  }

  /** Publishes the playback's discrete state and starts or stops the loop to match it. */
  private apply(): void {
    this.publish();
    this.updateClock();
    this.updateFeed();
    this.updateGoal();
    this.updateProgress();

    if (this.playback.currentState === 'playing') {
      this.loop.start();
    } else {
      this.loop.stop();
      this.renderFrame();
    }
  }

  /** Publishes everything discrete the playback is showing. */
  private publishAll(): void {
    this.publish();
    this.updateClock();
    this.updateFeed();
    this.updateGoal();
    this.updateProgress();
  }

  /** Publishes what the playback is showing: the passage the narration follows, and the play state. */
  private publish(): void {
    this.activeIndex.set(this.playback.activeIndex);
    this.state.set(this.playback.currentState);

    // Having watched the match to its end, or skipped there, the manager has seen the result.
    if (this.playback.currentState === 'finished') {
      this.revealResult();
    }
  }

  /** Draws the film at the playhead, dipped to dark where a cut or a jump in the reel is being faded over. */
  private renderFrame(): void {
    const position = this.playback.positionMs;
    const overlay =
      this.playback.currentState === 'playing'
        ? fadeAlpha(this.fadeEdges, position, this.playback.speed)
        : 0;

    this.renderer?.render(position, overlay);
  }

  /** Works out where the screen dips to dark: at every cut, and between the windows of the reel. */
  private refreshFadeEdges(): void {
    this.fadeEdges = fadeEdgesFor(this.film().cuts, this.playback.windows);
  }

  /**
   * Publishes the scoreboard clock for the playhead.
   *
   * Each half runs on its own clock, so the film reads `45+2'` through the first half's stoppage, `HT` at
   * the interval, `46'` as the second half begins, and `90+N'` in its stoppage; the label is only written
   * when it changes.
   */
  private updateClock(): void {
    if (!this.playback.hasPassages) {
      if (this.clockLabel() !== '') {
        this.clockLabel.set('');
        this.clockMinute.set(0);
      }

      return;
    }

    const clock = this.film().clockAt(this.playback.positionMs);

    if (clock.label !== this.clockLabel()) {
      this.clockLabel.set(clock.label);
      this.clockMinute.set(clock.minute);
    }
  }

  /** Publishes the feed's visible length as the playhead passes each row's film time. */
  private updateFeed(): void {
    const count = this.film().feedCountAt(this.playback.positionMs);

    if (count !== this.feedCount()) {
      this.feedCount.set(count);

      if (this.feedPinned()) {
        setTimeout(() => this.scrollFeedToBottom(), 0);
      }
    }
  }

  /**
   * Lights the scoreboard for the goal being celebrated at the playhead.
   *
   * The highlight follows the film rather than a timer, so it lasts as long as the pitch's own GOAL! badge
   * does at whatever speed is playing, and a seek into a celebration shows both.
   */
  private updateGoal(): void {
    const score = this.film().scoreAt(this.playback.positionMs);

    if (score.home !== this.liveScore().home || score.away !== this.liveScore().away) {
      this.liveScore.set(score);
    }

    const side = this.film().activeGoalAt(this.playback.positionMs)?.side ?? null;

    if (side !== this.scoringSide()) {
      this.scoringSide.set(side);
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
}

/** Builds a media-query list where the platform has one, and nothing where it does not. */
function mediaQuery(query: string): MediaQueryList | null {
  return typeof matchMedia === 'function' ? matchMedia(query) : null;
}

/** Whether the platform asks for reduced motion. */
function prefersReducedMotion(): boolean {
  return mediaQuery('(prefers-reduced-motion: reduce)')?.matches ?? false;
}

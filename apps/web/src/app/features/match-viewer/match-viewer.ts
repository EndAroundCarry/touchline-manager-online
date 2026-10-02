import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  MatchPlayback,
  PLAYBACK_SPEEDS,
  PassageKind,
  PlaybackState,
  PlaybackSpeed,
} from '../../core/match/match-playback';
import {
  commentarySideLabel,
  conditionColorClass,
  formatConditionPercent,
  formatMatchRating,
  highlightIndexForLine,
  highlightTitle,
  isGoalCommentary,
  isGoalOutcome,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  ratingColorClass,
  scoreLine,
  ShotMapEntry,
  shotMapEntries,
} from '../../core/match/match-presentation';
import {
  CommentaryLine,
  Highlight,
  MatchLineupPlayer,
  MatchPresentation,
  PlayerLiveMetric,
} from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
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

/** How long a goal's flash and score highlight stay up, in milliseconds. */
const GOAL_FLASH_MILLISECONDS = 2_500;

/** The normalized scale the pitch speaks on both axes. */
const PITCH_SCALE = 10_000;

/**
 * The FM/CM-style match center (master plan §9.5, §11.1, Stage 5).
 *
 * A result and its replay. The summary is the scoreboard and the scoreline; the replay is the pitch beside
 * the two lineups, the commentary ticker under it, and three more tabs — report, statistics and shot map,
 * player performance — that do not animate. The lineups are live: as the replay advances, each panel's
 * condition bars and rating badges move to the figures the server captured minute by minute, so a manager
 * watching a replay sees the same panel they would have seen live.
 *
 * The animation is deliberately independent of Angular change detection: the render loop draws from the
 * playback state every frame, and signals are written only when something discrete changes — the passage,
 * the play state, the ticker line — so a 60 Hz animation does not re-evaluate the commentary list 60 times a
 * second (§9.4). The loop is stopped on pause, on the tab going hidden, and on destruction, so no callback
 * outlives the canvas it draws to.
 *
 * Two viewing modes share one player: *Highlights* cuts from chance to chance, *Condensed* plays the
 * recycling passages the director laid out between them, so the ball is carried rather than teleported.
 * Both are the same presentation; the toggle only decides whether the bridges play.
 */
@Component({
  selector: 'app-match-viewer',
  templateUrl: './match-viewer.html',
})
export class MatchViewer implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(MatchStore);
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('pitch');

  private playback = new MatchPlayback([]);
  private renderer: CanvasMatchRenderer | null = null;

  /** The presentation the current playback was built from, so a load or a mode change rebuilds it once. */
  private builtFrom: MatchPresentation | null = null;
  private builtCondensed = true;

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

  /** The active highlight's index, published when it changes rather than every frame. */
  protected readonly activeIndex = signal(0);

  /** What the renderer draws: a highlight, or the bridge's own movement in condensed mode. */
  protected readonly passage = signal<Highlight | null>(null);

  /** Whether the active passage is a highlight or recycling. */
  protected readonly passageKind = signal<PassageKind>('highlight');

  /** The play state, published when it changes rather than every frame. */
  protected readonly state = signal<PlaybackState>(this.playback.currentState);

  /** The speed control's value. */
  protected readonly speed = signal<PlaybackSpeed>(1);

  /** Whether the manager asked for the text-only presentation. */
  protected readonly textOnly = signal(false);

  /** Whether the system asks for reduced motion. */
  protected readonly reduced = signal(prefersReducedMotion());

  /** The active tab. */
  protected readonly activeTab = signal<TabId>('replay');

  /** Whether the condensed replay plays the recycling passages between highlights. */
  protected readonly condensed = signal(true);

  /** The ticker's current line, overwritten as the passage's own commentary reaches its offsets. */
  protected readonly tickerText = signal('');

  /** Whether a goal's flash and score highlight are up. */
  protected readonly goalFlash = signal(false);

  /** Which side just scored, for the scoreboard's own flash. */
  protected readonly scoringSide = signal<'home' | 'away' | null>(null);

  protected readonly playing = computed(() => this.state() === 'playing');
  protected readonly speeds = PLAYBACK_SPEEDS;
  protected readonly tabs = TABS;
  protected readonly activeHighlight = computed(
    () => this.presentation()?.highlights[this.activeIndex()] ?? null,
  );
  protected readonly clock = computed(() => {
    const highlight = this.activeHighlight();

    return highlight === null ? '' : matchClockLabel(highlight.minute, highlight.stoppageMinute);
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
   * goal as its own event, which is what a scoreboard lists. The player is named through the token's own
   * `playerId` parameter, so nothing here reads a rendered sentence.
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

  /** Every shot the replay holds, for the statistics tab's shot map. */
  protected readonly shotMap = computed(() =>
    shotMapEntries(this.presentation()?.highlights ?? []),
  );
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
   * Every lineup player by participant, and every name by player.
   *
   * The panels ask about 22 players on every change-detection pass, so both lookups are built once per
   * presentation rather than searched per draw.
   */
  private readonly lineupIndex = computed(() => {
    const byParticipant = new Map<string, MatchLineupPlayer>();
    const nameByPlayerId = new Map<string, string>();
    const view = this.presentation();

    for (const lineup of [view?.homeLineup, view?.awayLineup]) {
      if (lineup === null || lineup === undefined) {
        continue;
      }

      for (const player of [...lineup.starters, ...lineup.bench]) {
        byParticipant.set(player.participantId, player);
        nameByPlayerId.set(player.playerId, player.name);
      }
    }

    return { byParticipant, nameByPlayerId };
  });

  /** The match minute the live panels show: kickoff before the replay starts, the active passage after. */
  private readonly metricMinute = computed(() => {
    const highlight = this.activeHighlight();

    if (highlight === null) {
      return 0;
    }

    return this.state() === 'idle' ? 0 : highlight.minute;
  });

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

    // The replay is built from the presentation once it loads, and rebuilt when the viewing mode changes.
    // A frame never resets a replay that is in progress: the same presentation and mode is left alone, and a
    // mode change resumes on the same highlight rather than at kickoff.
    effect(() => {
      const presentation = this.presentation();
      const condensed = this.condensed();

      if (presentation === this.builtFrom && condensed === this.builtCondensed) {
        return;
      }

      const rebuilding = this.builtFrom !== null;
      const wasPlaying = rebuilding && this.playback.currentState === 'playing';
      const highlight = this.playback.activeHighlightIndex;

      this.builtFrom = presentation;
      this.builtCondensed = condensed;

      this.playback = new MatchPlayback(
        presentation?.highlights ?? [],
        condensed ? (presentation?.bridges ?? []) : [],
      );

      if (rebuilding) {
        this.playback.seekTo(highlight);

        if (wasPlaying) {
          this.playback.play();
        }
      }

      this.publish();
      this.updateTicker();

      if (wasPlaying) {
        this.loop.start();
      } else {
        this.loop.stop();
      }
    });

    // The renderer is (re)built when the drawn passage, the canvas, or the presentation mode changes — not
    // on every frame, which is what keeps the animation off the change-detection path.
    effect(() => {
      const passage = this.passage();
      const canvas = this.canvas();
      const textOnly = this.textOnly();
      // Reduced motion shows the passage as a still frame at its end rather than animating it, which is the
      // "static event diagram" the plan asks for (`§9.4`). It is the *idle* state that is drawn still, so a
      // pause mid-highlight keeps the frame the manager paused on rather than jumping to the end; pressing
      // play is an explicit choice, so the loop animates once it is asked to.
      const still = this.reduced() && this.state() === 'idle';

      this.renderer?.dispose();
      this.renderer = null;

      if (passage === null || canvas === undefined || textOnly) {
        return;
      }

      this.renderer = new CanvasMatchRenderer(canvas.nativeElement, passage);
      this.renderer.render(still ? passage.durationMilliseconds : this.playback.positionMs);
    });
  }

  /** Stops the loop and drops the renderer, so nothing outlives the screen (§9.4). */
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

  /** Chooses whether the recycling passages between highlights play. */
  protected setCondensed(value: boolean): void {
    this.condensed.set(value);
  }

  /** Moves to the next highlight. */
  protected skipCurrent(): void {
    this.playback.skipCurrent();
    this.apply();
  }

  /** Starts the replay again. */
  protected replay(): void {
    this.playback.replay();
    this.apply();
  }

  /** Seeks to a highlight by index. */
  protected seekTo(index: number): void {
    this.playback.seekTo(index);
    this.apply();
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
   * Seeks to the highlight that presents a commentary line, then shows the pitch.
   *
   * The timeline is the event navigation: not every narrated event is worth replaying (§9.2), so a line
   * with no highlight behind it offers nothing rather than a button that does nothing.
   */
  protected showLineAndSwitch(line: CommentaryLine): void {
    this.playback.seekToEvent(line.sequence);
    this.activeTab.set('replay');
    this.apply();
  }

  /** Whether a commentary line has a highlight behind it, so the timeline can offer one. */
  protected hasHighlight(line: CommentaryLine): boolean {
    return highlightIndexForLine(this.presentation()?.highlights ?? [], line) >= 0;
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

  /** The colour class for a condition bar, green through red as a player tires. */
  protected conditionBarClass(basisPoints: number): string {
    return conditionColorClass(basisPoints);
  }

  /**
   * A player's live rating at the shown minute, on the 0–10,000 scale.
   *
   * A `replay-v2` presentation carries the curve the live panel drew, so a rating is the last capture at or
   * before the minute — twelve hundredths either way of what the panel showed. A v1 presentation carries no
   * curve at all, so it falls back to the final rating the lineup recorded, which is the only rating it has.
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

  /** Whether a highlight is a goal, which the timeline draws differently. */
  protected isGoalHighlight(highlight: Highlight): boolean {
    return isGoalOutcome(highlight.outcomeCode);
  }

  /** Whether a commentary line reports a goal, which the report highlights. */
  protected isGoalCommentaryLine(line: CommentaryLine): boolean {
    return isGoalCommentary(line.templateKey);
  }

  /** Names the end a commentary line belongs to. */
  protected side(side: string): string {
    return commentarySideLabel(side);
  }

  /** A short title for a highlighted event. */
  protected title(index: number): string {
    const highlight = this.presentation()?.highlights[index];

    return highlight === undefined ? '' : highlightTitle(highlight);
  }

  /** Formats a highlight's clock. */
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

    this.updateTicker();
    this.renderer?.render(this.playback.positionMs);

    if (this.playback.currentState !== 'playing') {
      this.loop.stop();
    }
  }

  /** Publishes the playback's discrete state and starts or stops the loop to match it. */
  private apply(): void {
    this.publish();
    this.updateTicker();

    if (this.playback.currentState === 'playing') {
      this.loop.start();
    } else {
      this.loop.stop();
      this.renderer?.render(this.playback.positionMs);
    }
  }

  /** Publishes what the playback is showing, flashing the scoreboard when a goal comes up. */
  private publish(): void {
    const highlight = this.playback.active;
    const kind = this.playback.activePassageKind;

    this.activeIndex.set(this.playback.activeHighlightIndex);
    this.passageKind.set(kind);
    this.passage.set(highlight);
    this.state.set(this.playback.currentState);

    if (highlight !== null && kind === 'highlight' && isGoalOutcome(highlight.outcomeCode)) {
      this.flashGoal(highlight);
    }
  }

  /**
   * Overwrites the ticker with the passage's commentary line for the playhead.
   *
   * The tokens carry the milliseconds they happen at, so the ticker follows the action rather than the
   * minute. A bridge has no narration of its own, so it leaves the last line up — which is what recycling
   * between chances reads as — and the signal is only written when the line actually changes, so a 60 Hz
   * frame does not re-render the screen.
   */
  private updateTicker(): void {
    if (this.playback.activePassageKind === 'bridge') {
      return;
    }

    const highlight = this.playback.active;

    if (highlight === null) {
      return;
    }

    const tokens = highlight.commentary ?? [];
    let index = 0;

    for (let token = 0; token < tokens.length; token += 1) {
      if (tokens[token].timeMilliseconds <= this.playback.positionMs) {
        index = token;
      }
    }

    const text = tokens[index]?.text ?? highlight.narration;

    if (text !== this.tickerText()) {
      this.tickerText.set(text);
    }
  }

  /** Flashes the scoreboard for a goal, once per highlight. */
  private flashGoal(highlight: Highlight): void {
    if (this.flashedSequence === highlight.sourceEventSequence) {
      return;
    }

    const side = this.goalSide(highlight);

    if (side === null) {
      return;
    }

    this.flashedSequence = highlight.sourceEventSequence;
    this.scoringSide.set(side);
    this.goalFlash.set(true);

    if (this.flashTimer !== null) {
      clearTimeout(this.flashTimer);
    }

    this.flashTimer = setTimeout(() => {
      this.flashTimer = null;
      this.flashedSequence = null;
      this.scoringSide.set(null);
      this.goalFlash.set(false);
    }, GOAL_FLASH_MILLISECONDS);
  }

  /** Which side scored the highlight's goal, read from the commentary line that narrates it. */
  private goalSide(highlight: Highlight): 'home' | 'away' | null {
    const line = this.presentation()?.commentary.find(
      (candidate) => candidate.sequence === highlight.sourceEventSequence,
    );

    return line === undefined || (line.side !== 'home' && line.side !== 'away') ? null : line.side;
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

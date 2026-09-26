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
import { MatchPlayback, PLAYBACK_SPEEDS, PlaybackSpeed } from '../../core/match/match-playback';
import {
  commentarySideLabel,
  highlightIndexForLine,
  highlightTitle,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  scoreLine,
} from '../../core/match/match-presentation';
import { CommentaryLine } from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';
import { CanvasMatchRenderer } from './renderer/canvas-match-renderer';
import { RenderLoop } from './renderer/render-loop';

/**
 * The match center (master plan §9.5, §11.1).
 *
 * A result and its replay. The summary is the scoreline, the statistics, and where the match was played;
 * the replay is the commentary timeline beside a Canvas that interpolates the server's keyframe highlights.
 *
 * The animation is deliberately independent of Angular change detection: the render loop draws from the
 * playback state every frame, and signals are written only when something discrete changes — the highlight,
 * the play state, the speed — so a 60 Hz animation does not re-evaluate the commentary list 60 times a
 * second (§9.4). The loop is stopped on pause, on the tab going hidden, and on destruction, so no callback
 * outlives the canvas it draws to.
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

  /** The play state, published when it changes rather than every frame. */
  protected readonly state = signal(this.playback.currentState);

  /** The speed control's value. */
  protected readonly speed = signal<PlaybackSpeed>(1);

  /** Whether the manager asked for the text-only presentation. */
  protected readonly textOnly = signal(false);

  /** Whether the system asks for reduced motion. */
  protected readonly reduced = signal(prefersReducedMotion());

  protected readonly playing = computed(() => this.state() === 'playing');
  protected readonly speeds = PLAYBACK_SPEEDS;
  protected readonly totalHighlights = computed(() => this.presentation()?.highlights.length ?? 0);
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

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  constructor() {
    const matchId = this.route.snapshot.paramMap.get('matchId');

    if (matchId !== null && matchId.length > 0) {
      this.store.load(matchId);
    }

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', this.visibilityListener);
    }

    this.motionQuery?.addEventListener('change', this.motionListener);

    // The renderer is (re)built when the highlight, the canvas, or the presentation mode changes — not on
    // every frame, which is what keeps the animation off the change-detection path.
    effect(() => {
      const highlight = this.activeHighlight();
      const canvas = this.canvas();
      const textOnly = this.textOnly();
      // Reduced motion shows the passage as a still frame at its end rather than animating it, which is the
      // "static event diagram" the plan asks for (`§9.4`). It is the *idle* state that is drawn still, so a
      // pause mid-highlight keeps the frame the manager paused on rather than jumping to the end; pressing
      // play is an explicit choice, so the loop animates once it is asked to.
      const still = this.reduced() && this.state() === 'idle';

      this.renderer?.dispose();
      this.renderer = null;

      if (highlight === null || canvas === undefined || textOnly) {
        return;
      }

      this.renderer = new CanvasMatchRenderer(canvas.nativeElement, highlight);
      this.renderer.render(still ? highlight.durationMilliseconds : this.playback.positionMs);
    });
  }

  /** Stops the loop and drops the renderer, so nothing outlives the screen (§9.4). */
  ngOnDestroy(): void {
    this.loop.dispose();
    this.renderer?.dispose();
    this.renderer = null;

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

  /** Moves to the next highlight. */
  protected skipCurrent(): void {
    this.playback.skipCurrent();
    this.apply();
  }

  /** Jumps to the end of the replay. */
  protected skipAll(): void {
    this.playback.skipAll();
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

  /**
   * Seeks to the highlight that presents a commentary line, when there is one.
   *
   * The timeline is the event navigation: not every narrated event is worth replaying (§9.2), so a line
   * with no highlight behind it offers nothing rather than a button that does nothing.
   */
  protected showLine(line: CommentaryLine): void {
    this.playback.seekToEvent(line.sequence);
    this.apply();
  }

  /** Whether a commentary line has a highlight behind it, so the timeline can offer one. */
  protected hasHighlight(line: CommentaryLine): boolean {
    return highlightIndexForLine(this.presentation()?.highlights ?? [], line) >= 0;
  }

  /** Formats a kickoff in the viewer's local time. */
  protected kickoff(instant: string): string {
    return formatInstant(instant);
  }

  /** Names a highlight's outcome, falling back to its code. */
  protected outcome(code: string): string {
    return outcomeLabel(code);
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

  private onFrame(delta: number): void {
    const changed = this.playback.advance(delta);

    if (changed || this.playback.currentState !== this.state()) {
      this.activeIndex.set(this.playback.activeIndex);
      this.state.set(this.playback.currentState);
    }

    this.renderer?.render(this.playback.positionMs);

    if (this.playback.currentState !== 'playing') {
      this.loop.stop();
    }
  }

  /** Publishes the playback's discrete state and starts or stops the loop to match it. */
  private apply(): void {
    this.activeIndex.set(this.playback.activeIndex);
    this.state.set(this.playback.currentState);

    if (this.playback.currentState === 'playing') {
      this.loop.start();
    } else {
      this.loop.stop();
      this.renderer?.render(this.playback.positionMs);
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

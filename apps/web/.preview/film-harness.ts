import { fadeAlpha, fadeEdgesFor } from '../src/app/core/match/film-fade';
import { FilmTimeline, buildFilmTimeline } from '../src/app/core/match/film-timeline';
import { MatchPlayback, PlaybackMode, PlaybackSpeed } from '../src/app/core/match/match-playback';
import {
  cardKindFor,
  isGoalCommentary,
  matchClockLabel,
} from '../src/app/core/match/match-presentation';
import { MatchPresentation } from '../src/app/core/match/match.models';
import { CanvasMatchRenderer } from '../src/app/features/match-viewer/renderer/canvas-match-renderer';
import {
  TrackInterpolator,
  emptySample,
} from '../src/app/features/match-viewer/renderer/keyframe-interpolator';
import { RenderLoop } from '../src/app/features/match-viewer/renderer/render-loop';

/**
 * The fluidity harness's page (`replay-v4`, M3).
 *
 * It plays a *dumped* presentation — the JSON the API returns, written by `simulation-benchmarks replay
 * --dump` — through the real `MatchPlayback`, `RenderLoop`, fade and `CanvasMatchRenderer`, exactly as the
 * match center wires them, and measures what is actually drawn at the browser's own frame cadence. `capture.mjs`
 * drives it in headless Chromium and prints the numbers.
 *
 * "Fluid" is measured, not eyeballed: how many frames a second, how far the ball and each player moved between
 * two drawn frames outside the cuts (and whether that is faster than the film's speed caps allow), and how
 * much of the film the ball stands still for.
 */

/** The speed caps the film is built under, in metres per real second; the film plays them times its pace. */
const BALL_CAP = 27;
const OUTFIELD_CAP = 8;
const KEEPER_CAP = 12;

/** How much faster than its cap a token may be seen to move between two frames before that is called a jump. */
const JUMP_TOLERANCE = 1.25;
const JUMP_SLACK_METRES = 0.05;

/** A frame longer than this, in milliseconds, is a dropped frame at 60 Hz. */
const DROPPED_FRAME_MILLISECONDS = 25;

/** The ball is standing still when it moves slower than this, in metres per film second. */
const STILL_SPEED = 0.05;

interface RunOptions {
  readonly fromMilliseconds: number;
  readonly seconds: number;
  readonly speed: PlaybackSpeed;
  readonly mode?: PlaybackMode;
}

interface Distribution {
  readonly p50: number;
  readonly p95: number;
  readonly p99: number;
  readonly max: number;
}

interface RunReport {
  readonly fromMilliseconds: number;
  readonly speed: number;
  readonly frames: number;
  readonly realSeconds: number;
  readonly filmSeconds: number;
  readonly fps: number;
  readonly frameMilliseconds: Distribution;
  readonly droppedFrames: number;
  readonly drawMilliseconds: Distribution;
  readonly comparedFrames: number;
  readonly cutsCrossed: number;
  readonly jumpsSkipped: number;
  readonly ballMetresPerFrame: Distribution;
  readonly playerMetresPerFrame: Distribution;
  readonly keeperMetresPerFrame: Distribution;
  readonly jumps: number;
  readonly worstJump: string;
}

const status = document.getElementById('status') as HTMLElement;
const canvas = document.getElementById('pitch') as HTMLCanvasElement;
const response = await fetch('/presentation.json');
const presentation = (await response.json()) as MatchPresentation;
const timeline = buildFilmTimeline(presentation);
const pace = (presentation.paceMilli ?? 2_200) / 1_000;

const renderer = new CanvasMatchRenderer(canvas, timeline, {
  kits: {
    home: {
      primary: presentation.homeLineup?.primaryColour ?? '#38bdf8',
      secondary: presentation.homeLineup?.secondaryColour ?? '#0f172a',
    },
    away: {
      primary: presentation.awayLineup?.primaryColour ?? '#fb7185',
      secondary: presentation.awayLineup?.secondaryColour ?? '#0f172a',
    },
  },
});

status.textContent = `${presentation.passages.length} passages, ${(timeline.durationMilliseconds / 1_000).toFixed(1)} s of film at ${pace.toFixed(2)}x`;

function distribution(values: readonly number[]): Distribution {
  if (values.length === 0) {
    return { p50: 0, p95: 0, p99: 0, max: 0 };
  }

  const sorted = [...values].sort((one, other) => one - other);
  const at = (share: number) => sorted[Math.min(sorted.length - 1, Math.floor(share * sorted.length))];

  return { p50: at(0.5), p95: at(0.95), p99: at(0.99), max: sorted[sorted.length - 1] };
}

function metres(dx: number, dy: number): number {
  return Math.hypot((dx * 105) / 10_000, (dy * 68) / 10_000);
}

/** Renders the film at a moment, paused, so it can be photographed. */
function show(milliseconds: number): void {
  renderer.render(milliseconds, 0);
}

/** Plays a stretch of the film in real time through the same loop the match center uses, and measures it. */
function run(options: RunOptions): Promise<RunReport> {
  const playback = new MatchPlayback(
    presentation.passages,
    presentation.playback ?? [],
    presentation.reel ?? [],
    options.mode ?? 'full',
  );
  const edges = fadeEdgesFor(timeline.cuts, playback.windows);
  const speed = options.speed;
  const frameTimes: number[] = [];
  const drawTimes: number[] = [];
  const ballMoves: number[] = [];
  const playerMoves: number[] = [];
  const keeperMoves: number[] = [];
  const previous = new Map<string, { x: number; y: number }>();
  let previousPosition = -1;
  let cutsCrossed = 0;
  let jumpsSkipped = 0;
  let jumps = 0;
  let worst = 0;
  let worstJump = '';
  let compared = 0;
  let started = 0;
  let filmStart = 0;

  playback.setSpeed(speed);
  playback.seekToMilliseconds(options.fromMilliseconds);
  playback.play();
  filmStart = playback.positionMs;

  return new Promise((resolve) => {
    const loop = new RenderLoop((delta) => {
      if (started === 0) {
        started = performance.now();
      }

      const before = playback.positionMs;

      playback.advance(delta);

      const after = playback.positionMs;
      const overlay = playback.currentState === 'playing' ? fadeAlpha(edges, after, speed) : 0;

      renderer.render(after, overlay);
      drawTimes.push(renderer.metrics.milliseconds);

      if (delta > 0) {
        frameTimes.push(delta);
      }

      // Compare this frame with the last one drawn, where the film moved on by exactly what the frame was
      // long: a cut or a jump in the reel is allowed to put things somewhere new.
      const filmDelta = after - before;
      const crossedCut = timeline.cuts.some(
        (cut) => cut.startMilliseconds > before && cut.startMilliseconds <= after,
      );

      if (crossedCut) {
        cutsCrossed += 1;
      }

      const continuous =
        delta > 0 && previousPosition >= 0 && Math.abs(filmDelta - delta * speed) < 1e-6;

      if (!continuous && delta > 0 && previousPosition >= 0 && !crossedCut) {
        jumpsSkipped += 1;
      }

      const now = new Map<string, { x: number; y: number }>();

      for (const item of renderer.drawn) {
        const id = item.entity.isBall ? 'ball' : item.entity.id;
        const point = { x: item.position.x, y: item.position.y };

        now.set(id, point);

        const before = previous.get(id);

        if (!continuous || crossedCut || before === undefined) {
          continue;
        }

        const moved = metres(point.x - before.x, point.y - before.y);
        const isKeeper = item.entity.family === 'goalkeeper';
        const cap = item.entity.isBall ? BALL_CAP : isKeeper ? KEEPER_CAP : OUTFIELD_CAP;
        const allowed = cap * pace * (filmDelta / 1_000) * JUMP_TOLERANCE + JUMP_SLACK_METRES;

        (item.entity.isBall ? ballMoves : isKeeper ? keeperMoves : playerMoves).push(moved);
        compared += 1;

        if (moved > allowed) {
          jumps += 1;

          if (moved / allowed > worst) {
            worst = moved / allowed;
            worstJump = `${id} moved ${moved.toFixed(2)} m in ${filmDelta.toFixed(1)} ms of film at ${(after / 1_000).toFixed(2)} s (allowed ${allowed.toFixed(2)} m)`;
          }
        }
      }

      previous.clear();

      for (const [id, point] of now) {
        previous.set(id, point);
      }

      previousPosition = after;

      const elapsed = (performance.now() - started) / 1_000;

      if (elapsed >= options.seconds || playback.currentState !== 'playing') {
        loop.stop();

        resolve({
          fromMilliseconds: filmStart,
          speed,
          frames: frameTimes.length,
          realSeconds: elapsed,
          filmSeconds: (playback.positionMs - filmStart) / 1_000,
          fps: frameTimes.length / elapsed,
          frameMilliseconds: distribution(frameTimes),
          droppedFrames: frameTimes.filter((time) => time > DROPPED_FRAME_MILLISECONDS).length,
          drawMilliseconds: distribution(drawTimes),
          comparedFrames: compared,
          cutsCrossed,
          jumpsSkipped,
          ballMetresPerFrame: distribution(ballMoves),
          playerMetresPerFrame: distribution(playerMoves),
          keeperMetresPerFrame: distribution(keeperMoves),
          jumps,
          worstJump,
        });
      }
    });

    loop.start();
  });
}

/**
 * How much of the film the ball stands still for, read straight off the timeline.
 *
 * Time that is *meant* to be still — the half-time card, a goal's celebration, and the second after a cut —
 * is left out of the second figure. The server's own count leaves out every dead-ball hold (restarts, a card,
 * a substitution), which the page cannot see, so this one is the stricter.
 */
function still(): {
  readonly allShare: number;
  readonly outsideHoldsShare: number;
  readonly longestStillSeconds: number;
} {
  const interpolator = new TrackInterpolator(timeline.ball, false);
  const here = emptySample();
  const there = emptySample();
  const step = 100;
  let all = 0;
  let stillAll = 0;
  let outside = 0;
  let stillOutside = 0;
  let run = 0;
  let longest = 0;

  for (let time = 0; time + step <= timeline.durationMilliseconds; time += step) {
    interpolator.sample(time, here);
    interpolator.sample(time + step, there);

    const speed = metres(there.x - here.x, there.y - here.y) / (step / 1_000);
    const isStill = speed < STILL_SPEED;
    const held =
      timeline.isHalfTimeAt(time) ||
      timeline.celebrationAt(time) >= 0 ||
      timeline.cuts.some(
        (cut) => time >= cut.startMilliseconds - 200 && time < cut.startMilliseconds + 1_200,
      );

    all += 1;
    stillAll += isStill ? 1 : 0;

    if (!held) {
      outside += 1;
      stillOutside += isStill ? 1 : 0;
    }

    run = isStill ? run + step / 1_000 : 0;
    longest = Math.max(longest, run);
  }

  return {
    allShare: stillAll / Math.max(1, all),
    outsideHoldsShare: stillOutside / Math.max(1, outside),
    longestStillSeconds: longest,
  };
}

/**
 * Whether the clock the viewer shows at each goal and each card is the minute the engine stamped on it.
 *
 * The film is the match played at one pace, so how the clock *reads* at an event is the thing a manager would
 * notice being wrong: a goal at 23' that the scoreboard calls 21'.
 */
function clockChecks(): {
  readonly checked: number;
  readonly mismatches: readonly { readonly what: string; readonly stamped: string; readonly shown: string }[];
} {
  const mismatches: { what: string; stamped: string; shown: string }[] = [];
  const stamped = (line: { minute: number; stoppageMinute: number }) =>
    matchClockLabel(line.minute, line.stoppageMinute);
  const goalLines = presentation.commentary.filter((line) => isGoalCommentary(line.templateKey));
  const cardLines = presentation.commentary.filter((line) => cardKindFor(line.templateKey) !== null);
  let checked = 0;

  timeline.goals.forEach((goal, index) => {
    const line = goalLines[index];

    if (line === undefined) {
      return;
    }

    checked += 1;

    const shown = timeline.clockAt(goal.filmMilliseconds).label;

    if (shown !== stamped(line)) {
      mismatches.push({ what: `goal ${index + 1}`, stamped: stamped(line), shown });
    }
  });

  timeline.cardMoments.forEach((card, index) => {
    const line = cardLines[index];

    if (line === undefined) {
      return;
    }

    checked += 1;

    const shown = timeline.clockAt(card.filmMilliseconds).label;

    if (shown !== stamped(line)) {
      mismatches.push({ what: `card ${index + 1}`, stamped: stamped(line), shown });
    }
  });

  return { checked, mismatches };
}

/** How finely the film is stepped when the drawn motion is measured, in milliseconds of film. */
const FLUIDITY_STEP = 40;

/** A token stutters when its drawn speed falls below this share of its average over the ±300 ms around it... */
const STUTTER_DIP = 0.35;

/** ...and is back above this share of that average within 400 ms. */
const STUTTER_RECOVERY = 0.7;

/** A token moving slower than this on average, in metres per film second, is not running, so slowing is not a stutter. */
const STUTTER_MINIMUM_AVERAGE = 2;

/** Two tokens are covered when their centres are closer than this share of a token's radius. */
const COVERED_SHARE = 0.6;

/** A token that moves further than this between two steps was put somewhere new, not moved. */
const STEP_JUMP_METRES = 4;

/**
 * Steps through the film, outside the cuts, and counts what a manager would see as stiffness (`tick-film-v1`, M0).
 *
 * A **stutter** is a token whose drawn speed falls below 35% of its average over ±300 ms and recovers within 400 ms: the
 * slow-down at a keyframe the interpolator can draw, which looks like a player pausing mid-run. A **covered pair** is two
 * player tokens whose drawn centres are closer than 0.6 of a token radius, so one hides the other. Both are counted
 * from what is *drawn*, so a change to the interpolator or to the renderer's own placement shows up here.
 */
function fluidity(): {
  readonly filmMinutes: number;
  readonly stutters: number;
  readonly stuttersPerFilmMinute: number;
  readonly coveredPairSteps: number;
  readonly coveredShareOfSteps: number;
  readonly coveredPairsPerFilmMinute: number;
  readonly tokenRadiusMetres: number;
} {
  const steps = Math.floor(timeline.durationMilliseconds / FLUIDITY_STEP);
  const ids: string[] = [];
  const index = new Map<string, number>();
  const xs: Float32Array[] = [];
  const ys: Float32Array[] = [];
  const broken = new Uint8Array(steps + 1);
  let coveredPairSteps = 0;
  let coveredSteps = 0;
  let radius = 0;

  for (let step = 0; step <= steps; step += 1) {
    const time = step * FLUIDITY_STEP;

    renderer.render(time, 0);
    radius = renderer.tokenRadiusMetres;

    const cutHere = timeline.cuts.some(
      (cut) => cut.startMilliseconds > time - FLUIDITY_STEP && cut.startMilliseconds <= time,
    );

    broken[step] = cutHere || timeline.isHalfTimeAt(time) ? 1 : 0;

    const players: { x: number; y: number }[] = [];

    for (const item of renderer.drawn) {
      if (item.entity.isBall) {
        continue;
      }

      let slot = index.get(item.entity.id);

      if (slot === undefined) {
        slot = ids.length;
        index.set(item.entity.id, slot);
        ids.push(item.entity.id);
        xs.push(new Float32Array(steps + 1).fill(Number.NaN));
        ys.push(new Float32Array(steps + 1).fill(Number.NaN));
      }

      const x = (item.position.x * 105) / 10_000;
      const y = (item.position.y * 68) / 10_000;

      xs[slot][step] = x;
      ys[slot][step] = y;
      players.push({ x, y });
    }

    let covered = 0;

    for (let one = 0; one < players.length; one += 1) {
      for (let other = one + 1; other < players.length; other += 1) {
        if (
          Math.hypot(players[one].x - players[other].x, players[one].y - players[other].y) <
          COVERED_SHARE * radius
        ) {
          covered += 1;
        }
      }
    }

    coveredPairSteps += covered;
    coveredSteps += covered > 0 ? 1 : 0;
  }

  // Speeds in metres per film second, with a gap wherever the token was put somewhere new or is not drawn.
  let stutters = 0;
  const window = Math.round(300 / FLUIDITY_STEP);
  const recovery = Math.round(400 / FLUIDITY_STEP);

  for (let slot = 0; slot < ids.length; slot += 1) {
    const speed = new Float32Array(steps + 1).fill(Number.NaN);

    for (let step = 1; step <= steps; step += 1) {
      const moved = Math.hypot(xs[slot][step] - xs[slot][step - 1], ys[slot][step] - ys[slot][step - 1]);

      if (!Number.isNaN(moved) && moved < STEP_JUMP_METRES && broken[step] === 0) {
        speed[step] = moved / (FLUIDITY_STEP / 1_000);
      }
    }

    for (let step = window + 1; step < steps - window - recovery; step += 1) {
      if (Number.isNaN(speed[step])) {
        continue;
      }

      let total = 0;
      let count = 0;

      for (let near = step - window; near <= step + window; near += 1) {
        if (!Number.isNaN(speed[near])) {
          total += speed[near];
          count += 1;
        }
      }

      // The average counts only if the whole ±300 ms is one unbroken run.
      const average = total / Math.max(1, count);

      if (count < 2 * window + 1 || average < STUTTER_MINIMUM_AVERAGE || speed[step] >= STUTTER_DIP * average) {
        continue;
      }

      for (let later = step + 1; later <= step + recovery; later += 1) {
        if (!Number.isNaN(speed[later]) && speed[later] >= STUTTER_RECOVERY * average) {
          stutters += 1;
          step = later;

          break;
        }
      }
    }
  }

  const filmMinutes = timeline.durationMilliseconds / 60_000;

  return {
    filmMinutes,
    stutters,
    stuttersPerFilmMinute: stutters / Math.max(0.001, filmMinutes),
    coveredPairSteps,
    coveredShareOfSteps: coveredSteps / Math.max(1, steps + 1),
    coveredPairsPerFilmMinute: coveredPairSteps / Math.max(0.001, filmMinutes),
    tokenRadiusMetres: radius,
  };
}

/** What the film holds, so the driver knows where to look. */
function info(timelineToDescribe: FilmTimeline = timeline) {
  return {
    passages: presentation.passages.length,
    durationMilliseconds: timelineToDescribe.durationMilliseconds,
    paceMilli: presentation.paceMilli ?? null,
    reelMilliseconds: presentation.reel.reduce(
      (sum, clip) => sum + (clip.endMilliseconds - clip.startMilliseconds),
      0,
    ),
    cuts: timelineToDescribe.cuts,
    goals: timelineToDescribe.goals,
    halfTimes: timelineToDescribe.halfTimes,
    markers: timelineToDescribe.markers.map((marker) => ({
      kind: marker.kind,
      filmMilliseconds: marker.filmMilliseconds,
      label: marker.label,
    })),
    cards: timelineToDescribe.cardMoments,
    passageStarts: timelineToDescribe.passageStarts,
    substitutions: timelineToDescribe.slots.filter((slot) => slot.stints.length > 1).length,
  };
}

declare global {
  interface Window {
    film: {
      readonly info: typeof info;
      readonly clockAt: (milliseconds: number) => string;
      readonly show: typeof show;
      readonly run: typeof run;
      readonly still: typeof still;
      readonly fluidity: typeof fluidity;
      readonly clockChecks: typeof clockChecks;
    };
  }
}

window.film = {
  info,
  clockAt: (milliseconds) => timeline.clockAt(milliseconds).label,
  show,
  run,
  still,
  fluidity,
  clockChecks,
};

show(0);

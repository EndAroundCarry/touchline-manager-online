import { Highlight, HighlightEntity, HighlightTrack } from '../src/app/core/match/match.models';
import { CanvasMatchRenderer } from '../src/app/features/match-viewer/renderer/canvas-match-renderer';

/**
 * A scratch harness for the Stage 6 renderer: it animates one synthetic goal passage through the real
 * `CanvasMatchRenderer` and reports the frame rate, so the pitch, the ball's altitude, the effects, and
 * the celebration can be inspected in a browser without a seeded world behind them.
 */

function slot(
  entityId: string,
  side: 'home' | 'away',
  shirt: number,
  family: string,
  name: string,
  x: number,
  y: number,
): HighlightEntity {
  return {
    entityId,
    isBall: false,
    side,
    participantId: entityId,
    shirtNumber: shirt,
    family,
    x,
    y,
    name,
  };
}

function shape(side: 'home' | 'away', offset: number): readonly HighlightEntity[] {
  const prefix = side === 'home' ? 'H' : 'A';

  return [
    slot(`${prefix}1`, side, 1, 'goalkeeper', `${side} keeper`, side === 'home' ? 500 : 9_500, 5_000),
    slot(`${prefix}2`, side, 2, 'defence', `${side} right back`, side === 'home' ? 2_500 + offset : 7_500 + offset, 1_800),
    slot(`${prefix}5`, side, 5, 'defence', `${side} centre back`, side === 'home' ? 2_400 + offset : 7_600 + offset, 3_600),
    slot(`${prefix}6`, side, 6, 'defence', `${side} centre back`, side === 'home' ? 2_400 + offset : 7_600 + offset, 6_400),
    slot(`${prefix}3`, side, 3, 'defence', `${side} left back`, side === 'home' ? 2_500 + offset : 7_500 + offset, 8_200),
    slot(`${prefix}7`, side, 7, 'midfield', `${side} winger`, side === 'home' ? 4_600 : 5_400, 1_600),
    slot(`${prefix}8`, side, 8, 'midfield', `${side} engine`, side === 'home' ? 4_400 : 5_600, 4_400),
    slot(`${prefix}4`, side, 4, 'midfield', `${side} holder`, side === 'home' ? 4_400 : 5_600, 5_600),
    slot(`${prefix}11`, side, 11, 'midfield', `${side} winger`, side === 'home' ? 4_600 : 5_400, 8_400),
    slot(`${prefix}10`, side, 10, 'attack', `${side} ten`, side === 'home' ? 6_400 : 3_600, 5_000),
    slot(`${prefix}9`, side, 9, 'attack', `${side} nine`, side === 'home' ? 6_600 : 3_400, 3_400),
  ];
}

function track(entityId: string, keyframes: HighlightTrack['keyframes']): HighlightTrack {
  return { entityId, keyframes };
}

const duration = 20_000;
const strike = 11_000;

const home = shape('home', 0);
const away = shape('away', 0);

const entities: readonly HighlightEntity[] = [
  ...home,
  ...away,
  { entityId: 'ball', isBall: true, side: null, participantId: null, shirtNumber: 0, family: null, x: 7_400, y: 5_000 },
];

const tracks: readonly HighlightTrack[] = [
  // The shape shifts with the play, home pushing on and the away block dropping off.
  ...home.map((entity) =>
    track(entity.entityId, [
      { timeMilliseconds: 0, x: entity.x, y: entity.y },
      { timeMilliseconds: (duration * 2) / 3, x: Math.min(9_000, entity.x + 500), y: entity.y + (5_000 - entity.y) / 8 },
      { timeMilliseconds: duration, x: Math.min(9_000, entity.x + 500), y: entity.y + (5_000 - entity.y) / 8 },
    ]),
  ),
  ...away.map((entity) =>
    track(entity.entityId, [
      { timeMilliseconds: 0, x: entity.x, y: entity.y },
      { timeMilliseconds: (duration * 2) / 3, x: Math.max(1_000, entity.x - 400), y: entity.y + (5_000 - entity.y) / 6 },
      { timeMilliseconds: duration, x: Math.max(1_000, entity.x - 400), y: entity.y + (5_000 - entity.y) / 6 },
    ]),
  ),
  // The striker: start, close the ball, strike it, celebrate.
  track('H9', [
    { timeMilliseconds: 0, x: 6_600, y: 3_400 },
    { timeMilliseconds: strike - 1_200, x: 7_100, y: 4_300, action: 'run' },
    { timeMilliseconds: strike, x: 7_400, y: 5_000, action: 'shot' },
    { timeMilliseconds: duration, x: 7_800, y: 5_400, action: 'celebrate' },
  ]),
  // A defender closes him down, which is the duel the clash ring marks.
  track('A6', [
    { timeMilliseconds: 0, x: 7_600, y: 6_400 },
    { timeMilliseconds: strike, x: 7_580, y: 5_120, action: 'tackle' },
    { timeMilliseconds: duration, x: 8_200, y: 5_600 },
  ]),
  // The keeper: hold, come for the ball's line, recover.
  track('A1', [
    { timeMilliseconds: 0, x: 9_500, y: 5_000 },
    { timeMilliseconds: (duration * 2) / 3, x: 9_200, y: 5_000, action: 'save' },
    { timeMilliseconds: duration, x: 9_500, y: 5_000 },
  ]),
  // The ball: a build-up pass into the striker, struck at goal, climbing to 55.
  track('ball', [
    { timeMilliseconds: 0, x: 4_000, y: 2_400, z: 0, speed: 1_800 },
    { timeMilliseconds: strike - 1_000, x: 7_200, y: 4_800, z: 6, speed: 2_600 },
    { timeMilliseconds: strike, x: 7_400, y: 5_000, z: 12, speed: 4_600 },
    { timeMilliseconds: strike + 900, x: 9_300, y: 5_050, z: 55, speed: 4_200 },
    { timeMilliseconds: strike + 1_800, x: 10_000, y: 5_000, z: 0, speed: 1_500 },
    { timeMilliseconds: duration, x: 10_000, y: 5_000, z: 0 },
  ]),
];

const highlight: Highlight = {
  sourceEventSequence: 42,
  minute: 67,
  stoppageMinute: 0,
  durationMilliseconds: duration,
  outcomeCode: 'goal',
  narration: 'Goal — H nine, 67.',
  homeColour: '#1f4e79',
  awayColour: '#8c2f39',
  entities,
  tracks,
  commentary: [
    {
      timeMilliseconds: 0,
      templateKey: 'match.passage.build_up',
      variantKey: 'match.passage.build_up.v1',
      parameters: [],
      text: 'The move is built.',
    },
    {
      timeMilliseconds: strike,
      templateKey: 'match.passage.shot',
      variantKey: 'match.passage.shot.v1',
      parameters: [],
      text: 'The shot is struck.',
    },
    {
      timeMilliseconds: strike + 900,
      templateKey: 'match.goal',
      variantKey: 'match.goal.v1',
      parameters: [],
      text: 'Goal!',
    },
  ],
};

const canvas = document.getElementById('pitch') as HTMLCanvasElement;
const fpsLabel = document.getElementById('fps') as HTMLElement;
const timeLabel = document.getElementById('time') as HTMLElement;
const renderer = new CanvasMatchRenderer(canvas, highlight, {
  kits: {
    home: { primary: '#1f4e79', secondary: '#d6e4f0' },
    away: { primary: '#8c2f39', secondary: '#f2e3e5' },
  },
  cards: new Map([['A6', 'yellow']]),
});

const params = new URLSearchParams(location.search);
let position = Number(params.get('t') ?? 0);
let playing = params.get('paused') !== '1';
let speed = Number(params.get('speed') ?? 1);
let previous = performance.now();
let frames = 0;
let reportedAt = previous;
let worst = 0;

function frame(now: number): void {
  const delta = playing ? now - previous : 0;

  previous = now;
  position = position + delta * speed;

  if (position >= duration) {
    position = 0;
  }

  renderer.render(position);

  const cost = renderer.metrics.milliseconds;
  worst = Math.max(worst, cost);
  frames += 1;

  if (now - reportedAt >= 500) {
    fpsLabel.textContent = `${Math.round((frames * 1_000) / (now - reportedAt))} fps`;
    timeLabel.textContent = `${(position / 1_000).toFixed(1)}s of ${duration / 1_000}s — render ${cost.toFixed(1)} ms, worst ${worst.toFixed(1)} ms`;

    frames = 0;
    reportedAt = now;
    worst = 0;
  }

  requestAnimationFrame(frame);
}

document.getElementById('play')?.addEventListener('click', () => {
  playing = !playing;
});

document.querySelectorAll<HTMLButtonElement>('button[data-speed]').forEach((button) => {
  button.addEventListener('click', () => {
    speed = Number(button.dataset.speed);
  });
});

// A seek hook, so a particular moment — the strike, the flight, the celebration — can be screenshotted.
(window as unknown as { stage6: unknown }).stage6 = {
  render: (at: number) => {
    position = at;
    playing = false;
    renderer.render(at);
  },
  metrics: () => renderer.metrics,
};

requestAnimationFrame(frame);

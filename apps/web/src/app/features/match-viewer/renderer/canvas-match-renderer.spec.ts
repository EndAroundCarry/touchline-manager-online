import { FilmTimeline, buildFilmTimeline } from '../../../core/match/film-timeline';
import {
  HighlightEntity,
  HighlightKeyframe,
  MatchPresentation,
  Passage,
  PassageCut,
} from '../../../core/match/match.models';
import { CanvasMatchRenderer, readableInk } from './canvas-match-renderer';
import { altitudeLift, altitudeScale, pitchRect, toCanvasPoint } from './pitch-layout';

/**
 * The renderer's drawing guarantees (Stage 6, `§9.4`, `replay-v4`).
 *
 * A headless DOM has no 2D context, so the renderer is given a recording one: every call it makes is kept,
 * and the assertions read what it drew — a keeper in the second colour, a ball above its shadow, a booking
 * above a token, a goal's badge — without a real rasterizer.
 *
 * Since `replay-v4` there is one renderer for the whole film, so most of what is asserted about the seams
 * between passages is asserted here, by drawing across one with the same renderer and reading what changed.
 */

interface DrawCall {
  readonly op: string;
  readonly args: readonly number[];
  readonly text?: string;
  readonly fillStyle?: string;
  readonly strokeStyle?: string;
  readonly lineWidth?: number;
  readonly font?: string;
}

class RecordingContext {
  fillStyle = '';
  strokeStyle = '';
  lineWidth = 0;
  font = '';
  textAlign = '';
  textBaseline = '';

  readonly calls: DrawCall[] = [];

  clearRect(...args: number[]): void {
    this.calls.push({ op: 'clearRect', args });
  }

  fillRect(...args: number[]): void {
    this.calls.push({ op: 'fillRect', args, fillStyle: this.fillStyle });
  }

  strokeRect(...args: number[]): void {
    this.calls.push({
      op: 'strokeRect',
      args,
      strokeStyle: this.strokeStyle,
      lineWidth: this.lineWidth,
    });
  }

  beginPath(): void {
    this.calls.push({ op: 'beginPath', args: [] });
  }

  closePath(): void {
    this.calls.push({ op: 'closePath', args: [] });
  }

  moveTo(...args: number[]): void {
    this.calls.push({ op: 'moveTo', args });
  }

  lineTo(...args: number[]): void {
    this.calls.push({ op: 'lineTo', args });
  }

  arc(...args: number[]): void {
    this.calls.push({ op: 'arc', args, fillStyle: this.fillStyle, strokeStyle: this.strokeStyle });
  }

  ellipse(...args: number[]): void {
    this.calls.push({ op: 'ellipse', args, fillStyle: this.fillStyle });
  }

  fill(): void {
    this.calls.push({ op: 'fill', args: [], fillStyle: this.fillStyle });
  }

  stroke(): void {
    this.calls.push({
      op: 'stroke',
      args: [],
      strokeStyle: this.strokeStyle,
      lineWidth: this.lineWidth,
    });
  }

  clip(): void {
    this.calls.push({ op: 'clip', args: [] });
  }

  fillText(text: string, x: number, y: number): void {
    this.calls.push({
      op: 'fillText',
      args: [x, y],
      text,
      fillStyle: this.fillStyle,
      font: this.font,
    });
  }

  save(): void {
    this.calls.push({ op: 'save', args: [] });
  }

  restore(): void {
    this.calls.push({ op: 'restore', args: [] });
  }

  setTransform(...args: number[]): void {
    this.calls.push({ op: 'setTransform', args });
  }

  drawImage(...args: unknown[]): void {
    this.calls.push({ op: 'drawImage', args: [args.length] });
  }
}

function canvasWithContext(): { canvas: HTMLCanvasElement; context: RecordingContext } {
  const canvas = document.createElement('canvas');
  const context = new RecordingContext();

  Object.defineProperty(canvas, 'getContext', { value: () => context });

  return { canvas, context };
}

/** The renderer is given no offscreen layer unless a test asks for one, so the pitch is drawn in place. */
const DIRECT = { createPitchLayer: () => null };

// ---- Fixtures ------------------------------------------------------------------------------------------

function keyframe(
  timeMilliseconds: number,
  x: number,
  y: number,
  extra: Partial<HighlightKeyframe> = {},
): HighlightKeyframe {
  return { timeMilliseconds, x, y, ...extra };
}

function player(
  entityId: string,
  participantId: string,
  shirtNumber: number,
  family: string,
  x: number,
  y: number,
  name: string,
): HighlightEntity {
  return {
    entityId,
    isBall: false,
    side: entityId.startsWith('H') ? 'home' : 'away',
    participantId,
    shirtNumber,
    family,
    x,
    y,
    name,
  };
}

const BALL: HighlightEntity = {
  entityId: 'ball',
  isBall: true,
  side: null,
  participantId: null,
  shirtNumber: 0,
  family: null,
  x: 7_000,
  y: 4_000,
};

/** A twenty-second passage with a keeper, a striker, a defender, and the ball. */
function highlight(overrides: Partial<Passage> = {}): Passage {
  return {
    sourceEventSequence: 9,
    minute: 22,
    stoppageMinute: 0,
    startMatchSecond: 1_320,
    endMatchSecond: 1_360,
    durationMilliseconds: 20_000,
    outcomeCode: 'goal',
    narration: 'Goal.',
    homeColour: '#1f4e79',
    awayColour: '#8c2f39',
    entities: [
      player('H1', 'gk', 1, 'goalkeeper', 500, 5_000, 'A Keeper'),
      player('H9', 'p9', 9, 'attack', 7_000, 4_000, 'A Striker'),
      player('A5', 'p5', 5, 'defence', 6_000, 2_000, 'A Defender'),
      BALL,
    ],
    tracks: [
      {
        entityId: 'ball',
        keyframes: [
          keyframe(0, 5_000, 5_000, { z: 0, speed: 4_000 }),
          keyframe(10_000, 5_000, 5_000, { z: 100 }),
          keyframe(20_000, 5_000, 5_000, { z: 100 }),
        ],
      },
      {
        entityId: 'H9',
        keyframes: [
          keyframe(0, 7_000, 4_000),
          keyframe(5_000, 7_400, 4_100, { action: 'shot' }),
          keyframe(20_000, 7_400, 4_100),
        ],
      },
    ],
    commentary: [
      {
        timeMilliseconds: 3_000,
        templateKey: 'match.goal',
        variantKey: 'match.goal.v1',
        parameters: [],
        text: 'Goal!',
      },
    ],
    eventSequences: [9],
    ...overrides,
  };
}

function filmOf(
  passages: readonly Passage[],
  overrides: Partial<MatchPresentation> = {},
): FilmTimeline {
  return buildFilmTimeline({
    matchId: 'm1',
    presentationVersion: 'replay-v4',
    engineVersion: 'engine-v5',
    homeGoals: 1,
    awayGoals: 0,
    commentary: [],
    passages,
    reel: [],
    estimatedPayloadBytes: 0,
    homeLineup: {
      clubName: 'Home',
      shortName: 'HOM',
      primaryColour: '#1f4e79',
      secondaryColour: '#d6e4f0',
      formation: '4-4-2',
      starters: [],
      bench: [],
    },
    awayLineup: null,
    ...overrides,
  });
}

/** Where a shirt number was drawn on a canvas, which is where its token is. */
function numberAt(context: RecordingContext, text: string): readonly number[] | undefined {
  return context.calls.find((call) => call.op === 'fillText' && call.text === text)?.args;
}

function numbers(context: RecordingContext): string[] {
  return context.calls.filter((call) => call.op === 'fillText').map((call) => call.text ?? '');
}

describe('CanvasMatchRenderer', () => {
  it('reports what it drew, entities included', () => {
    const { canvas } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT);

    renderer.render(200);

    // A keeper, a striker, a defender, and the ball.
    expect(renderer.metrics.entities).toBe(4);
    expect(renderer.metrics.interpolated).toBe(4);
  });

  it('exposes what it drew, so a harness can measure how far a token moved between frames', () => {
    const { canvas } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT);

    renderer.render(5_000);

    const drawn = renderer.drawn;
    const striker = drawn.find((item) => item.entity.id === 'H9');
    const ball = drawn.find((item) => item.entity.isBall);

    expect(drawn).toHaveLength(4);
    expect(striker?.position).toEqual({ x: 7_400, y: 4_100 });
    expect(striker?.action).toBe('shot');
    expect(ball?.entity.isBall).toBe(true);
  });

  it('draws a mown pitch with its markings and goals', () => {
    const { canvas, context } = canvasWithContext();
    const rect = pitchRect(300, 150);

    new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT).render(0);

    const stripes = context.calls.filter(
      (call) => call.op === 'fillRect' && call.fillStyle === '#1a7d3c',
    );

    expect(stripes.length).toBeGreaterThan(1);
    expect(stripes[0].args[2]).toBeCloseTo(rect.width / 10, 3);

    // Both penalty areas, both six-yard boxes, both goals, and the boundary.
    const boxes = context.calls.filter((call) => call.op === 'strokeRect');

    expect(boxes.length).toBeGreaterThanOrEqual(7);
  });

  it("gives the goalkeeper the club's second colour and the outfield player the first", () => {
    const { canvas, context } = canvasWithContext();

    new CanvasMatchRenderer(canvas, filmOf([highlight()]), {
      ...DIRECT,
      kits: {
        home: { primary: '#1f4e79', secondary: '#d6e4f0' },
        away: { primary: '#8c2f39', secondary: '#f2e3e5' },
      },
    }).render(0);

    const fills = context.calls.filter((call) => call.op === 'fill');

    expect(
      fills.some((call) => call.fillStyle === '#d6e4f0'),
      'the keeper wears the second colour',
    ).toBe(true);
    expect(
      fills.some((call) => call.fillStyle === '#1f4e79'),
      'the outfield wears the first',
    ).toBe(true);
    expect(numbers(context)).toContain('9');
  });

  it("falls back to the film's own colours where no kit is given", () => {
    const { canvas, context } = canvasWithContext();

    new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT).render(0);

    expect(context.calls.some((call) => call.op === 'fill' && call.fillStyle === '#1f4e79')).toBe(
      true,
    );
    expect(context.calls.some((call) => call.op === 'fill' && call.fillStyle === '#8c2f39')).toBe(
      true,
    );
  });

  it('lifts a high ball off its shadow and grows it', () => {
    const { canvas, context } = canvasWithContext();
    const rect = pitchRect(300, 150);
    const ground = toCanvasPoint({ x: 5_000, y: 5_000 }, rect);
    const lift = altitudeLift(rect, 100);

    new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT).render(10_000);

    const baseRadius = Math.max(3.5, Math.min(6.5, rect.height / 105));
    const drawnRadius = baseRadius * altitudeScale(100);
    const shadow = context.calls.find((call) => call.op === 'ellipse');
    const ball = context.calls.find(
      (call) => call.op === 'arc' && Math.abs(call.args[2] - drawnRadius) < 0.05,
    );

    expect(shadow?.args[0]).toBeCloseTo(ground.x, 4);
    expect(shadow?.args[1]).toBeCloseTo(ground.y, 4);
    expect(ball?.args[1]).toBeCloseTo(ground.y - lift, 4);
    expect(drawnRadius).toBeGreaterThan(baseRadius);
  });

  it("marks a booking above the offending player's token from the moment the film reaches it", () => {
    const { canvas, context } = canvasWithContext();
    const film = filmOf(
      [
        highlight({
          sourceEventSequence: 4,
          eventSequences: [4],
          outcomeCode: 'play',
          commentary: [
            {
              timeMilliseconds: 6_000,
              templateKey: 'match.card.yellow',
              variantKey: 'v1',
              parameters: [{ name: 'playerId', value: 'p9' }],
              text: 'Booked.',
            },
          ],
        }),
      ],
      {
        commentary: [
          {
            sequence: 4,
            minute: 22,
            stoppageMinute: 0,
            side: 'home',
            templateKey: 'match.card.yellow',
            variantKey: 'v1',
            parameters: [{ name: 'playerId', value: 'p9' }],
            text: 'Booked.',
          },
        ],
        homeLineup: {
          clubName: 'Home',
          shortName: 'HOM',
          primaryColour: '#1f4e79',
          secondaryColour: '#d6e4f0',
          formation: '4-4-2',
          starters: [
            {
              participantId: 'p9',
              playerId: 'pl9',
              shirtNumber: 9,
              name: 'A Striker',
              position: 'ST',
              family: 'attack',
              isStarter: true,
              slotNumber: 9,
              kickoffCondition: 10_000,
              finalCondition: 8_000,
              finalRating: 6_500,
              goals: 0,
              assists: 0,
              yellowCards: 1,
              sentOff: false,
              subbedOutMinute: null,
              subbedInMinute: null,
              isInjured: false,
            },
          ],
          bench: [],
        },
      },
    );
    const renderer = new CanvasMatchRenderer(canvas, film, DIRECT);

    renderer.render(5_000);

    expect(context.calls.some((call) => call.fillStyle === '#facc15')).toBe(false);

    renderer.render(6_500);

    expect(context.calls.some((call) => call.fillStyle === '#facc15')).toBe(true);
  });

  it('flashes a goal badge once the film reaches the goal', () => {
    const { canvas, context } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT);

    renderer.render(2_000);

    expect(context.calls.some((call) => call.text === 'GOAL!')).toBe(false);

    renderer.render(3_500);

    expect(context.calls.some((call) => call.text === 'GOAL!')).toBe(true);
  });

  it('runs a celebration on through the next passage, which does not know a goal was scored', () => {
    const { canvas, context } = canvasWithContext();
    const scoring = highlight({
      durationMilliseconds: 4_000,
      commentary: [
        {
          timeMilliseconds: 3_500,
          templateKey: 'match.goal',
          variantKey: 'v1',
          parameters: [],
          text: 'Goal!',
        },
      ],
    });
    const next = highlight({
      durationMilliseconds: 8_000,
      outcomeCode: 'play',
      commentary: [],
      eventSequences: [],
      sourceEventSequence: 0,
    });
    const renderer = new CanvasMatchRenderer(canvas, filmOf([scoring, next]), DIRECT);

    // 500 ms after the passage ended, 1,000 ms into the celebration.
    renderer.render(4_500);

    expect(context.calls.some((call) => call.text === 'GOAL!')).toBe(true);
  });

  it('leaves a trail behind an airborne ball', () => {
    const { canvas, context } = canvasWithContext();

    // A quarter of the way along the track the ball is at 40, high enough to streak.
    new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT).render(4_000);

    const trail = context.calls.filter(
      (call) => call.op === 'stroke' && (call.strokeStyle ?? '').startsWith('rgba(248, 250, 252'),
    );

    expect(trail.length).toBeGreaterThan(0);
  });

  it('leaves a ball on the grass with no shadow and no trail', () => {
    const { canvas, context } = canvasWithContext();
    const grounded = highlight({
      tracks: [
        {
          entityId: 'ball',
          keyframes: [
            keyframe(0, 5_000, 5_000, { z: 0 }),
            keyframe(20_000, 6_000, 5_000, { z: 0 }),
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, filmOf([grounded]), DIRECT).render(5_000);

    expect(context.calls.some((call) => call.op === 'ellipse')).toBe(false);
  });

  it('draws both teams as dots, told apart by border rather than shape', () => {
    const { canvas, context } = canvasWithContext();

    new CanvasMatchRenderer(canvas, filmOf([highlight()]), {
      ...DIRECT,
      kits: {
        home: { primary: '#1f4e79', secondary: '#d6e4f0' },
        away: { primary: '#8c2f39', secondary: '#f2e3e5' },
      },
    }).render(0);

    const dots = context.calls.filter((call) => call.op === 'arc');

    expect(dots.some((call) => call.strokeStyle === '#ffffff')).toBe(true);
    expect(dots.some((call) => call.strokeStyle === '#0f172a')).toBe(true);
  });

  it('lifts a jumping player off a ground shadow', () => {
    const { canvas, context } = canvasWithContext();
    const jumping = highlight({
      tracks: [
        {
          entityId: 'H9',
          keyframes: [
            keyframe(0, 7_000, 4_000, { z: 0 }),
            keyframe(5_000, 7_000, 4_000, { z: 60, action: 'header' }),
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, filmOf([jumping]), DIRECT).render(5_000);

    expect(
      context.calls.some(
        (call) => call.op === 'ellipse' && (call.fillStyle ?? '').startsWith('rgba(2, 6, 23'),
      ),
    ).toBe(true);
  });

  it('draws a keeper going down as a lateral streak with no shadow', () => {
    const { canvas, context } = canvasWithContext();
    const diving = highlight({
      tracks: [
        {
          entityId: 'H1',
          keyframes: [
            keyframe(0, 500, 5_000, { z: 0 }),
            keyframe(5_000, 1_500, 5_000, { z: 0, action: 'save' }),
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, filmOf([diving]), DIRECT).render(5_000);

    expect(
      context.calls.some(
        (call) => call.op === 'stroke' && call.strokeStyle === 'rgba(248, 250, 252, 0.55)',
      ),
    ).toBe(true);
    expect(context.calls.some((call) => call.op === 'ellipse')).toBe(false);
  });

  describe('mistakes', () => {
    /** A film in which `entityId` makes a mistake, tagged `action`, five seconds in. */
    function mistake(entityId: string, action: string): FilmTimeline {
      return filmOf([
        highlight({
          tracks: [
            {
              entityId,
              keyframes: [
                keyframe(0, 7_000, 4_000),
                keyframe(5_000, 7_400, 4_100, { action }),
                keyframe(20_000, 7_400, 4_100),
              ],
            },
          ],
        }),
      ]);
    }

    function drawnAt(film: FilmTimeline, timeMs: number): RecordingContext {
      const { canvas, context } = canvasWithContext();

      new CanvasMatchRenderer(canvas, film, DIRECT).render(timeMs);

      return context;
    }

    const alphaOf = (style: string | undefined): number =>
      Number(/, ([\d.]+)\)$/.exec(style ?? '')?.[1]);

    // The recording context keeps no state stack, so the ring's colour is still set when the ball is drawn after it:
    // a ring is the red arc as big as a token and more, which the ball is not.
    const rings = (context: RecordingContext): DrawCall[] =>
      context.calls.filter(
        (call) =>
          call.op === 'arc' &&
          (call.strokeStyle ?? '').startsWith('rgba(239, 68, 68') &&
          call.args[2] > 6,
      );

    const chevrons = (context: RecordingContext): DrawCall[] =>
      context.calls.filter(
        (call) => call.op === 'stroke' && (call.strokeStyle ?? '').startsWith('rgba(245, 158, 11'),
      );

    it('rings the man who lost the ball, labels him, and lets the ring fade out', () => {
      const film = mistake('H9', 'dispossessed');
      const justAfter = drawnAt(film, 5_200);
      const later = drawnAt(film, 6_200);

      expect(rings(justAfter)).toHaveLength(1);
      expect(chevrons(justAfter)).toHaveLength(0);
      expect(numbers(justAfter)).toContain('Dispossessed');
      expect(rings(later)).toHaveLength(1);
      expect(alphaOf(rings(later)[0].strokeStyle)).toBeLessThan(
        alphaOf(rings(justAfter)[0].strokeStyle),
      );
    });

    it('puts the marker on the token it belongs to', () => {
      const context = drawnAt(mistake('H9', 'misplaced'), 5_200);
      const token = numberAt(context, '9');
      const ring = rings(context)[0];

      expect(numbers(context)).toContain('Misplaced pass');
      expect(token).toBeDefined();
      expect(ring.args[0]).toBeCloseTo(token![0], 1);
    });

    it('marks a man who was beaten with an amber chevron rather than a ring', () => {
      const context = drawnAt(mistake('A5', 'beaten'), 5_200);

      expect(chevrons(context).length).toBeGreaterThan(0);
      expect(rings(context)).toHaveLength(0);
      expect(numbers(context)).toContain('Beaten');
      expect(numbers(drawnAt(mistake('A5', 'bypassed'), 5_200))).toContain('Outrun');
    });

    it('draws no marker before the mistake, nor once it has faded', () => {
      const film = mistake('H9', 'dispossessed');

      expect(rings(drawnAt(film, 4_900))).toHaveLength(0);
      expect(rings(drawnAt(film, 6_600))).toHaveLength(0);
      expect(numbers(drawnAt(film, 6_600))).not.toContain('Dispossessed');
    });

    it('shows the same marker on a seek as it does playing through', () => {
      const film = mistake('H9', 'dispossessed');
      const { canvas, context } = canvasWithContext();
      const renderer = new CanvasMatchRenderer(canvas, film, DIRECT);

      for (let time = 4_000; time <= 5_400; time += 200) {
        renderer.render(time);
      }

      context.calls.length = 0;
      renderer.render(5_400);

      const played = rings(context).map((call) => call.strokeStyle);

      expect(played).toEqual(rings(drawnAt(film, 5_400)).map((call) => call.strokeStyle));
    });
  });

  it('clears the canvas and releases the pointer when it is disposed', () => {
    const { canvas, context } = canvasWithContext();
    const removeSpy = vi.spyOn(canvas, 'removeEventListener');
    const renderer = new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT);

    renderer.render(0);
    context.calls.length = 0;
    renderer.dispose();

    expect(removeSpy).toHaveBeenCalledWith('pointermove', expect.any(Function));
    expect(context.calls.some((call) => call.op === 'clearRect')).toBe(true);
  });
});

describe('CanvasMatchRenderer across the film', () => {
  /** Two passages, the second of which opens exactly where the first closed. */
  function twoPassages(secondOverrides: Partial<Passage> = {}): FilmTimeline {
    const first = highlight({
      durationMilliseconds: 4_000,
      outcomeCode: 'play',
      commentary: [],
      eventSequences: [],
      sourceEventSequence: 0,
      tracks: [
        { entityId: 'H9', keyframes: [keyframe(0, 6_000, 4_000), keyframe(4_000, 7_000, 4_000)] },
        { entityId: 'ball', keyframes: [keyframe(0, 5_000, 5_000), keyframe(4_000, 6_000, 5_000)] },
      ],
    });
    const second = highlight({
      durationMilliseconds: 4_000,
      outcomeCode: 'play',
      commentary: [],
      eventSequences: [],
      sourceEventSequence: 0,
      tracks: [
        { entityId: 'H9', keyframes: [keyframe(0, 7_000, 4_000), keyframe(4_000, 8_000, 4_000)] },
        { entityId: 'ball', keyframes: [keyframe(0, 6_000, 5_000), keyframe(4_000, 7_000, 5_000)] },
      ],
      ...secondOverrides,
    });

    return filmOf([first, second]);
  }

  it('draws a passage boundary with one renderer and one continuous picture', () => {
    const { canvas, context } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, twoPassages(), DIRECT);
    const strikerAt: number[] = [];

    for (const time of [3_900, 3_999, 4_000, 4_001, 4_100]) {
      context.calls.length = 0;
      renderer.render(time);
      strikerAt.push(numberAt(context, '9')![0]);
    }

    // The striker moves right at a steady pace across the seam: no frame is drawn at a stale time, so there
    // is no step back and no pause.
    for (let index = 1; index < strikerAt.length; index += 1) {
      expect(strikerAt[index]).toBeGreaterThanOrEqual(strikerAt[index - 1]);
    }

    const step = strikerAt[3] - strikerAt[2];
    const across = strikerAt[2] - strikerAt[1];

    expect(Math.abs(step - across)).toBeLessThan(0.05);
  });

  it('switches the number on a token when a substitute comes on, without moving the token', () => {
    const { canvas, context } = canvasWithContext();
    const substitute = player('H9', 'p19', 19, 'attack', 7_000, 4_000, 'A Substitute');
    const film = twoPassages({
      entities: [
        player('H1', 'gk', 1, 'goalkeeper', 500, 5_000, 'A Keeper'),
        substitute,
        player('A5', 'p5', 5, 'defence', 6_000, 2_000, 'A Defender'),
        BALL,
      ],
    });
    const renderer = new CanvasMatchRenderer(canvas, film, DIRECT);

    renderer.render(3_900);

    expect(numbers(context)).toContain('9');
    expect(numbers(context)).not.toContain('19');

    const before = numberAt(context, '9')!;

    context.calls.length = 0;
    renderer.render(4_100);

    expect(numbers(context)).toContain('19');
    expect(numbers(context)).not.toContain('9');
    // The same token carried on along the same run, now wearing a different number.
    expect(Math.abs(numberAt(context, '19')![0] - before[0])).toBeLessThan(2);
    expect(Math.abs(numberAt(context, '19')![1] - before[1])).toBeLessThan(0.01);
  });

  it('stops drawing a player once they have been sent off', () => {
    const { canvas, context } = canvasWithContext();
    const film = twoPassages({
      entities: [
        player('H1', 'gk', 1, 'goalkeeper', 500, 5_000, 'A Keeper'),
        player('A5', 'p5', 5, 'defence', 6_000, 2_000, 'A Defender'),
        BALL,
      ],
    });
    const renderer = new CanvasMatchRenderer(canvas, film, DIRECT);

    renderer.render(3_900);

    expect(numbers(context)).toContain('9');

    context.calls.length = 0;
    renderer.render(4_100);

    expect(numbers(context)).not.toContain('9');
    expect(numbers(context)).toContain('5');
    expect(renderer.metrics.entities).toBe(3);
  });

  it('puts a player somewhere new at a cut rather than sliding them there', () => {
    const { canvas, context } = canvasWithContext();
    const cut: PassageCut = { timeMilliseconds: 0, durationMilliseconds: 300, kind: 'kick_off' };
    const film = twoPassages({
      cuts: [cut],
      tracks: [
        { entityId: 'H9', keyframes: [keyframe(0, 5_000, 5_000), keyframe(4_000, 5_400, 5_000)] },
        { entityId: 'ball', keyframes: [keyframe(0, 5_000, 5_000), keyframe(4_000, 5_000, 5_000)] },
      ],
    });
    const renderer = new CanvasMatchRenderer(canvas, film, DIRECT);
    const rect = pitchRect(300, 150);
    const before = toCanvasPoint({ x: 7_000, y: 4_000 }, rect);
    const after = toCanvasPoint({ x: 5_000, y: 5_000 }, rect);

    renderer.render(3_999.9);

    expect(numberAt(context, '9')![0]).toBeCloseTo(before.x, 0);

    context.calls.length = 0;
    renderer.render(4_000);

    expect(numberAt(context, '9')![0]).toBeCloseTo(after.x, 3);
  });

  it('shows the half-time card while the film holds at the interval, and not otherwise', () => {
    const { canvas, context } = canvasWithContext();
    const interval = highlight({
      durationMilliseconds: 3_000,
      outcomeCode: 'half_time',
      commentary: [],
      eventSequences: [],
      sourceEventSequence: 0,
    });
    const play = highlight({
      durationMilliseconds: 4_000,
      outcomeCode: 'play',
      commentary: [],
      eventSequences: [],
      sourceEventSequence: 0,
    });
    const renderer = new CanvasMatchRenderer(canvas, filmOf([play, interval, play]), DIRECT);

    renderer.render(2_000);

    expect(context.calls.some((call) => call.text === 'HALF TIME')).toBe(false);

    renderer.render(5_000);

    expect(context.calls.some((call) => call.text === 'HALF TIME')).toBe(true);

    context.calls.length = 0;
    renderer.render(8_000);

    expect(context.calls.some((call) => call.text === 'HALF TIME')).toBe(false);
  });

  it('lays a dark overlay over the whole canvas as the last thing it draws, in proportion to the fade', () => {
    const { canvas, context } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, filmOf([highlight()]), DIRECT);

    renderer.render(1_000, 0);

    expect(
      context.calls.some(
        (call) => call.op === 'fillRect' && call.fillStyle?.startsWith('rgba(2, 6, 23, 0.'),
      ),
      'no overlay is drawn when there is no fade',
    ).toBe(false);

    context.calls.length = 0;
    renderer.render(1_000, 0.5);

    const last = context.calls.filter((call) => call.op === 'fillRect').pop();

    expect(last?.fillStyle).toBe('rgba(2, 6, 23, 0.500)');
    expect(last?.args).toEqual([0, 0, 300, 150]);
  });

  it('draws the same film again, exactly, at the same moment', () => {
    const first = canvasWithContext();
    const second = canvasWithContext();
    const film = twoPassages();

    new CanvasMatchRenderer(first.canvas, film, DIRECT).render(2_345);
    new CanvasMatchRenderer(second.canvas, film, DIRECT).render(2_345);

    expect(first.context.calls).toEqual(second.context.calls);
  });
});

describe('CanvasMatchRenderer: the pitch layer and the page', () => {
  let observers: { callback: ResizeObserverCallback; observed: Element[]; disconnected: boolean }[];

  beforeEach(() => {
    observers = [];

    vi.stubGlobal(
      'ResizeObserver',
      class {
        private readonly record: (typeof observers)[number];

        constructor(callback: ResizeObserverCallback) {
          this.record = { callback, observed: [], disconnected: false };
          observers.push(this.record);
        }

        observe(element: Element): void {
          this.record.observed.push(element);
        }

        disconnect(): void {
          this.record.disconnected = true;
        }
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function resize(width: number, height: number): void {
    observers[0].callback(
      [{ contentRect: { width, height } } as unknown as ResizeObserverEntry],
      {} as ResizeObserver,
    );
  }

  const stripes = (context: RecordingContext) =>
    context.calls.filter((call) => call.op === 'fillRect' && call.fillStyle === '#1a7d3c');

  it('draws the pitch once onto its own layer and copies it to the canvas on every frame', () => {
    const main = canvasWithContext();
    const layer = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), {
      createPitchLayer: () => layer.canvas,
    });

    renderer.render(0);
    renderer.render(500);
    renderer.render(1_000);

    expect(stripes(layer.context).length, 'the layer holds the stripes, drawn once').toBe(5);
    expect(stripes(main.context), 'the canvas is not redrawn with them').toHaveLength(0);
    expect(main.context.calls.filter((call) => call.op === 'drawImage')).toHaveLength(3);
  });

  it('draws the pitch in place where there is no layer to put it on', () => {
    const main = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);

    renderer.render(0);
    renderer.render(500);

    expect(stripes(main.context).length).toBe(10);
    expect(main.context.calls.some((call) => call.op === 'drawImage')).toBe(false);
  });

  it('redraws the layer when the canvas changes size, and not otherwise', () => {
    const main = canvasWithContext();
    const layer = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), {
      createPitchLayer: () => layer.canvas,
    });

    renderer.render(0);

    const drawn = stripes(layer.context).length;

    renderer.render(100);
    renderer.render(200);

    expect(stripes(layer.context)).toHaveLength(drawn);

    resize(600, 300);

    expect(stripes(layer.context).length).toBeGreaterThan(drawn);
    expect(main.canvas.width).toBe(600);
    expect(main.canvas.height).toBe(300);
    expect(layer.canvas.width).toBe(600);
  });

  it('draws the frame again at the new size, so a resize never leaves a stretched picture', () => {
    const main = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);

    renderer.render(1_000);
    main.context.calls.length = 0;

    resize(600, 300);

    expect(main.context.calls.some((call) => call.op === 'clearRect')).toBe(true);
    expect(numbers(main.context)).toContain('9');
  });

  it('draws nothing in response to a resize before it has drawn anything', () => {
    const main = canvasWithContext();

    new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);
    resize(600, 300);

    expect(main.context.calls).toHaveLength(0);
  });

  it('ignores a canvas that is not laid out, which is what the text-only view reports', () => {
    const main = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);

    renderer.render(0);
    resize(0, 0);
    renderer.render(100);

    expect(main.canvas.width).toBe(300);
  });

  it('reads the canvas size once and never asks the page for its layout again on a frame', () => {
    const main = canvasWithContext();
    const reads = vi.fn(() => 300);

    Object.defineProperty(main.canvas, 'clientWidth', { get: reads });
    Object.defineProperty(main.canvas, 'clientHeight', { get: reads });

    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);
    const afterConstruction = reads.mock.calls.length;

    for (let time = 0; time < 5_000; time += 16) {
      renderer.render(time);
    }

    expect(reads.mock.calls.length).toBe(afterConstruction);
  });

  it('observes the canvas, and lets go of it when it is disposed', () => {
    const main = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);

    expect(observers).toHaveLength(1);
    expect(observers[0].observed).toEqual([main.canvas]);

    renderer.dispose();

    expect(observers[0].disconnected).toBe(true);
  });

  it('draws at the device pixel ratio without changing the geometry it draws in', () => {
    vi.stubGlobal('devicePixelRatio', 2);

    const main = canvasWithContext();
    const renderer = new CanvasMatchRenderer(main.canvas, filmOf([highlight()]), DIRECT);

    renderer.render(0);

    expect(main.canvas.width).toBe(600);
    expect(main.canvas.height).toBe(300);
    expect(
      main.context.calls.some((call) => call.op === 'setTransform' && call.args[0] === 2),
    ).toBe(true);
  });
});

describe('readableInk', () => {
  it('keeps dark ink on light kits and light ink on dark ones', () => {
    expect(readableInk('#d6e4f0')).toBe('#0f172a');
    expect(readableInk('#1f4e79')).toBe('#ffffff');
    expect(readableInk('not-a-colour')).toBe('#ffffff');
  });
});

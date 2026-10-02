import { Passage } from '../../../core/match/match.models';
import { CanvasMatchRenderer, readableInk } from './canvas-match-renderer';
import { altitudeLift, altitudeScale, pitchRect, toCanvasPoint } from './pitch-layout';

/**
 * The renderer's drawing guarantees (Stage 6, `§9.4`).
 *
 * A headless DOM has no 2D context, so the renderer is given a recording one: every call it makes is kept,
 * and the assertions read what it drew — a keeper in the second colour, a ball above its shadow, a booking
 * above a token, a goal's badge — without a real rasterizer.
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
}

function canvasWithContext(): { canvas: HTMLCanvasElement; context: RecordingContext } {
  const canvas = document.createElement('canvas');
  const context = new RecordingContext();

  Object.defineProperty(canvas, 'getContext', { value: () => context });

  return { canvas, context };
}

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
      {
        entityId: 'H1',
        isBall: false,
        side: 'home',
        participantId: 'gk',
        shirtNumber: 1,
        family: 'goalkeeper',
        x: 500,
        y: 5_000,
        name: 'A Keeper',
      },
      {
        entityId: 'H9',
        isBall: false,
        side: 'home',
        participantId: 'p9',
        shirtNumber: 9,
        family: 'attack',
        x: 7_000,
        y: 4_000,
        name: 'A Striker',
      },
      {
        entityId: 'A5',
        isBall: false,
        side: 'away',
        participantId: 'p5',
        shirtNumber: 5,
        family: 'defence',
        x: 6_000,
        y: 2_000,
        name: 'A Defender',
      },
      {
        entityId: 'ball',
        isBall: true,
        side: null,
        participantId: null,
        shirtNumber: 0,
        family: null,
        x: 7_000,
        y: 4_000,
      },
    ],
    tracks: [
      {
        entityId: 'ball',
        keyframes: [
          { timeMilliseconds: 0, x: 5_000, y: 5_000, z: 0, speed: 4_000 },
          { timeMilliseconds: 10_000, x: 5_000, y: 5_000, z: 100 },
        ],
      },
      {
        entityId: 'H9',
        keyframes: [
          { timeMilliseconds: 0, x: 7_000, y: 4_000 },
          { timeMilliseconds: 5_000, x: 7_400, y: 4_100, action: 'shot' },
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

describe('CanvasMatchRenderer', () => {
  it('reports what it drew, entities and tracks included', () => {
    const { canvas } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, highlight());

    renderer.render(200);

    expect(renderer.metrics.entities).toBe(4);
    expect(renderer.metrics.interpolated).toBe(2);
  });

  it('draws a mown pitch with its markings and goals', () => {
    const { canvas, context } = canvasWithContext();
    const rect = pitchRect(300, 150);

    new CanvasMatchRenderer(canvas, highlight()).render(0);

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

    new CanvasMatchRenderer(canvas, highlight(), {
      kits: {
        home: { primary: '#1f4e79', secondary: '#d6e4f0' },
        away: { primary: '#8c2f39', secondary: '#f2e3e5' },
      },
    }).render(0);

    const fills = context.calls.filter((call) => call.op === 'fill');
    const numbers = context.calls.filter((call) => call.op === 'fillText');

    expect(
      fills.some((call) => call.fillStyle === '#d6e4f0'),
      'the keeper wears the second colour',
    ).toBe(true);
    expect(
      fills.some((call) => call.fillStyle === '#1f4e79'),
      'the outfield wears the first',
    ).toBe(true);
    expect(numbers.some((call) => call.text === '9')).toBe(true);
  });

  it('lifts a high ball off its shadow and grows it', () => {
    const { canvas, context } = canvasWithContext();
    const rect = pitchRect(300, 150);
    const ground = toCanvasPoint({ x: 5_000, y: 5_000 }, rect);
    const lift = altitudeLift(rect, 100);

    new CanvasMatchRenderer(canvas, highlight()).render(10_000);

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

  it("marks a booking above the offending player's token", () => {
    const { canvas, context } = canvasWithContext();

    new CanvasMatchRenderer(canvas, highlight(), { cards: new Map([['p9', 'yellow']]) }).render(0);

    expect(context.calls.some((call) => call.fillStyle === '#facc15')).toBe(true);
  });

  it('flashes a goal badge once the passage reaches the goal', () => {
    const { canvas, context } = canvasWithContext();
    const renderer = new CanvasMatchRenderer(canvas, highlight());

    renderer.render(2_000);

    expect(context.calls.some((call) => call.text === 'GOAL!')).toBe(false);

    renderer.render(3_500);

    expect(context.calls.some((call) => call.text === 'GOAL!')).toBe(true);
  });

  it('leaves a trail behind an airborne ball', () => {
    const { canvas, context } = canvasWithContext();

    // A quarter of the way along the track the ball is at 40, high enough to streak.
    new CanvasMatchRenderer(canvas, highlight()).render(4_000);

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
            { timeMilliseconds: 0, x: 5_000, y: 5_000, z: 0 },
            { timeMilliseconds: 20_000, x: 6_000, y: 5_000, z: 0 },
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, grounded).render(5_000);

    expect(context.calls.some((call) => call.op === 'ellipse')).toBe(false);
  });

  it('draws both teams as dots, told apart by border rather than shape', () => {
    const { canvas, context } = canvasWithContext();

    new CanvasMatchRenderer(canvas, highlight(), {
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
            { timeMilliseconds: 0, x: 7_000, y: 4_000, z: 0 },
            { timeMilliseconds: 5_000, x: 7_000, y: 4_000, z: 60, action: 'header' },
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, jumping).render(5_000);

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
            { timeMilliseconds: 0, x: 500, y: 5_000, z: 0 },
            { timeMilliseconds: 5_000, x: 1_500, y: 5_000, z: 0, action: 'save' },
          ],
        },
      ],
    });

    new CanvasMatchRenderer(canvas, diving).render(5_000);

    expect(
      context.calls.some(
        (call) => call.op === 'stroke' && call.strokeStyle === 'rgba(248, 250, 252, 0.55)',
      ),
    ).toBe(true);
    expect(context.calls.some((call) => call.op === 'ellipse')).toBe(false);
  });

  it('clears the canvas and releases the pointer when it is disposed', () => {
    const { canvas, context } = canvasWithContext();
    const removeSpy = vi.spyOn(canvas, 'removeEventListener');
    const renderer = new CanvasMatchRenderer(canvas, highlight());

    renderer.render(0);
    context.calls.length = 0;
    renderer.dispose();

    expect(removeSpy).toHaveBeenCalledWith('pointermove', expect.any(Function));
    expect(context.calls.some((call) => call.op === 'clearRect')).toBe(true);
  });
});

describe('readableInk', () => {
  it('keeps dark ink on light kits and light ink on dark ones', () => {
    expect(readableInk('#d6e4f0')).toBe('#0f172a');
    expect(readableInk('#1f4e79')).toBe('#ffffff');
    expect(readableInk('not-a-colour')).toBe('#ffffff');
  });
});

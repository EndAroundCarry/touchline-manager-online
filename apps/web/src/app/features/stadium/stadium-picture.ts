import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import {
  PITCH,
  SCENE_HEIGHT,
  SCENE_WIDTH,
  SceneKind,
  buildScene,
  clampLevel,
  safeColour,
} from '../../core/stadium/stadium-scene';

/** One rectangle of the drawing with its paint resolved, so the template only places it. */
interface DrawnRect {
  readonly kind: SceneKind;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly fill: string;
  readonly fillOpacity: number;
  readonly stroke: string;
  readonly strokeWidth: number;
  /** The dashed centre line that reads as the windows of a hospitality box. */
  readonly windows: {
    readonly x1: number;
    readonly y1: number;
    readonly x2: number;
    readonly y2: number;
  } | null;
}

let nextInstance = 0;

/**
 * A top-down picture of the club's stadium at one level (`STAD-1`).
 *
 * A drawing rather than a bitmap: it is crisp at every size, costs no download, and — the point — draws the
 * seats in the club's own colour. Each of the ten levels is a distinct ground, and a level is always the
 * previous one with something added, so a manager watches the same stadium grow. The geometry lives in
 * {@link buildScene}; this component only paints it.
 *
 * The picture carries its meaning in words (`aria-label`), because a drawing alone is not an alternative for
 * anyone who cannot see it, and the screen beside it states the level and the capacity in text.
 */
@Component({
  selector: 'app-stadium-picture',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.viewBox]="viewBox"
      [attr.role]="decorative() ? null : 'img'"
      [attr.aria-label]="decorative() ? null : label()"
      [attr.aria-hidden]="decorative() ? 'true' : null"
      [attr.data-level]="level()"
      preserveAspectRatio="xMidYMid meet"
      class="block h-auto w-full rounded"
      data-testid="stadium-picture"
    >
      <defs>
        <pattern [attr.id]="seatsId" width="5" height="4" patternUnits="userSpaceOnUse">
          <rect width="5" height="4" fill="#10161d" />
          <rect x="0.35" y="0.35" width="4.3" height="3.3" rx="1" [attr.fill]="primary()" />
        </pattern>
        <pattern [attr.id]="terraceId" width="4" height="4" patternUnits="userSpaceOnUse">
          <rect width="4" height="4" fill="#55606c" />
          <circle cx="1" cy="1" r="0.7" fill="#a4afbb" />
          <circle cx="3" cy="3" r="0.7" fill="#a4afbb" />
        </pattern>
      </defs>

      <rect [attr.width]="width" [attr.height]="height" fill="#243029" />

      <!-- The apron and the pitch, mown in stripes. -->
      <rect
        [attr.x]="pitch.x - 10"
        [attr.y]="pitch.y - 10"
        [attr.width]="pitch.width + 20"
        [attr.height]="pitch.height + 20"
        fill="#2c6a3b"
      />
      @for (stripe of stripes; track stripe) {
        <rect
          [attr.x]="pitch.x + stripe * (pitch.width / 8)"
          [attr.y]="pitch.y"
          [attr.width]="pitch.width / 8"
          [attr.height]="pitch.height"
          [attr.fill]="stripe % 2 === 0 ? '#3f8f4f' : '#37834a'"
        />
      }
      <g fill="none" stroke="#e8f3ea" stroke-opacity="0.7" stroke-width="0.9">
        <rect
          [attr.x]="pitch.x"
          [attr.y]="pitch.y"
          [attr.width]="pitch.width"
          [attr.height]="pitch.height"
        />
        <line
          [attr.x1]="pitch.x + pitch.width / 2"
          [attr.y1]="pitch.y"
          [attr.x2]="pitch.x + pitch.width / 2"
          [attr.y2]="pitch.y + pitch.height"
        />
        <circle
          [attr.cx]="pitch.x + pitch.width / 2"
          [attr.cy]="pitch.y + pitch.height / 2"
          r="12"
        />
        <rect [attr.x]="pitch.x" [attr.y]="pitch.y + 26" width="22" height="44" />
        <rect
          [attr.x]="pitch.x + pitch.width - 22"
          [attr.y]="pitch.y + 26"
          width="22"
          height="44"
        />
      </g>

      <!-- The stands: terraces, seats in the club colour, hospitality boxes, then the roofs over them. -->
      @for (rect of drawn(); track $index) {
        <rect
          [attr.x]="rect.x"
          [attr.y]="rect.y"
          [attr.width]="rect.width"
          [attr.height]="rect.height"
          [attr.fill]="rect.fill"
          [attr.fill-opacity]="rect.fillOpacity"
          [attr.stroke]="rect.stroke"
          [attr.stroke-width]="rect.strokeWidth"
          [attr.data-kind]="rect.kind"
        />
        @if (rect.windows; as line) {
          <line
            [attr.x1]="line.x1"
            [attr.y1]="line.y1"
            [attr.x2]="line.x2"
            [attr.y2]="line.y2"
            [attr.stroke]="secondary()"
            stroke-width="1.4"
            stroke-dasharray="3 2"
          />
        }
      }

      @if (scene().ring) {
        <rect
          [attr.x]="scene().bounds.x - 10"
          [attr.y]="scene().bounds.y - 10"
          [attr.width]="scene().bounds.width + 20"
          [attr.height]="scene().bounds.height + 20"
          rx="14"
          fill="none"
          stroke="#8f9cac"
          stroke-width="2"
        />
      }

      @for (mast of scene().masts; track $index) {
        <circle [attr.cx]="mast.x" [attr.cy]="mast.y" r="9" fill="#ffe27a" fill-opacity="0.22" />
        <circle [attr.cx]="mast.x" [attr.cy]="mast.y" r="2.8" fill="#fff7c2" />
      }

      @if (scene().banner) {
        <g data-testid="stadium-banner">
          <rect
            [attr.x]="scene().bounds.x + scene().bounds.width / 2 - 32"
            y="6"
            width="64"
            height="11"
            [attr.fill]="primary()"
          />
          <rect
            [attr.x]="scene().bounds.x + scene().bounds.width / 2 - 32"
            y="10.5"
            width="64"
            height="2.5"
            [attr.fill]="secondary()"
          />
        </g>
      }
    </svg>
  `,
})
export class StadiumPicture {
  /** The stadium level to draw, 1 to 10. */
  readonly level = input.required<number>();

  /** The club's colour, which the seats are drawn in. */
  readonly primaryColour = input<string | null | undefined>(null);

  /** The club's second colour, for the roof trim and the banner stripe. */
  readonly secondaryColour = input<string | null | undefined>(null);

  /** Whether the picture is purely decorative, because the level is already stated beside it. */
  readonly decorative = input(false);

  protected readonly width = SCENE_WIDTH;
  protected readonly height = SCENE_HEIGHT;
  protected readonly viewBox = `0 0 ${SCENE_WIDTH} ${SCENE_HEIGHT}`;
  protected readonly pitch = PITCH;
  protected readonly stripes = [0, 1, 2, 3, 4, 5, 6, 7];

  private readonly instance = ++nextInstance;

  /** Pattern identities are per picture, so two pictures on one page never share a fill. */
  protected readonly seatsId = `stadium-seats-${this.instance}`;
  protected readonly terraceId = `stadium-terrace-${this.instance}`;

  protected readonly primary = computed(() => safeColour(this.primaryColour()));
  protected readonly secondary = computed(() => safeColour(this.secondaryColour(), '#d6e4f0'));
  protected readonly scene = computed(() => buildScene(this.level()));

  protected readonly label = computed(
    () => `Stadium at level ${clampLevel(this.level())} of 10, with seats in the club colour.`,
  );

  protected readonly drawn = computed<readonly DrawnRect[]>(() =>
    this.scene().rects.map((rect) => this.paint(rect)),
  );

  private paint(rect: {
    readonly kind: SceneKind;
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
  }): DrawnRect {
    switch (rect.kind) {
      case 'seats':
        return {
          ...rect,
          fill: `url(#${this.seatsId})`,
          fillOpacity: 1,
          stroke: '#0e1319',
          strokeWidth: 0.6,
          windows: null,
        };
      case 'terrace':
        return {
          ...rect,
          fill: `url(#${this.terraceId})`,
          fillOpacity: 1,
          stroke: '#0e1319',
          strokeWidth: 0.6,
          windows: null,
        };
      case 'vip':
        return {
          ...rect,
          fill: '#0f151c',
          fillOpacity: 1,
          stroke: this.secondary(),
          strokeWidth: 0.6,
          windows:
            rect.width >= rect.height
              ? {
                  x1: rect.x + 2,
                  y1: rect.y + rect.height / 2,
                  x2: rect.x + rect.width - 2,
                  y2: rect.y + rect.height / 2,
                }
              : {
                  x1: rect.x + rect.width / 2,
                  y1: rect.y + 2,
                  x2: rect.x + rect.width / 2,
                  y2: rect.y + rect.height - 2,
                },
        };
      case 'roof':
        return {
          ...rect,
          fill: '#e6ecf3',
          fillOpacity: 0.16,
          stroke: this.secondary(),
          strokeWidth: 1.2,
          windows: null,
        };
    }
  }
}

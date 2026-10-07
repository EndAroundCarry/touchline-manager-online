import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { VIEW_WIDTH } from '../../core/stadium/stadium-camera';
import {
  SceneDetail,
  ScenePart,
  buildScene,
  clampLevel,
  safeColour,
} from '../../core/stadium/stadium-scene';
import { StadiumStandCode } from '../../core/stadium/stadium.models';

/** One shape of the drawing with its paint resolved, so the template only places it. */
interface DrawnPart {
  readonly role: ScenePart['role'];
  readonly d: string;
  readonly fill: string;
  readonly stroke: string | null;
  readonly width: number | null;
  readonly dash: string | null;
  readonly offset: number | null;
  readonly opacity: number;
  readonly region: StadiumStandCode | null;
}

/** How strongly the plane of a kind of place shows when a manager points at that kind. */
const GLOW_OPACITY = 0.55;

/** The size of a stand's name on the screen, in the units of a full-size drawing. */
const LABEL_SIZE = 15;

/**
 * The club's stadium at one level, drawn in three dimensions (`STAD-1`).
 *
 * The ground is seen from one corner, the way a management game shows it: the pitch, the stands round it with every
 * row and sector, the roofs, the hospitality boxes, the floodlights. A drawing rather than a bitmap: it is crisp at
 * every size, costs no download, and — the point — draws the seats in the club's own two colours. Each of the ten
 * levels is a distinct ground, and a level is always the previous one with something added, so a manager watches the
 * same stadium grow. The geometry lives in {@link buildScene}; this component only paints it.
 *
 * The picture carries its meaning in words (`aria-label`), because a drawing alone is not an alternative for anyone
 * who cannot see it, and the screen beside it states the level, the capacity and every kind of place in text.
 */
@Component({
  selector: 'app-stadium-picture',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.viewBox]="viewBox()"
      [attr.role]="decorative() ? null : 'img'"
      [attr.aria-label]="decorative() ? null : label()"
      [attr.aria-hidden]="decorative() ? 'true' : null"
      [attr.data-level]="level()"
      [attr.data-highlight]="highlight()"
      preserveAspectRatio="xMidYMid meet"
      class="block h-auto w-full rounded"
      data-testid="stadium-picture"
    >
      @for (part of drawn(); track $index) {
        <path
          [attr.d]="part.d"
          [attr.fill]="part.fill"
          [attr.stroke]="part.stroke"
          [attr.stroke-width]="part.width"
          [attr.stroke-dasharray]="part.dash"
          [attr.stroke-dashoffset]="part.offset"
          [attr.opacity]="part.role === 'region' ? glow(part) : part.opacity"
          [attr.data-role]="part.role"
          [attr.data-region]="part.region"
        />
      }
      @if (labels()) {
        <g
          fill="#f4f7fb"
          stroke="#0f1318"
          stroke-linejoin="round"
          paint-order="stroke"
          text-anchor="middle"
          font-family="'Barlow Semi Condensed', 'Arial Narrow', sans-serif"
          font-weight="600"
          [attr.font-size]="labelSize()"
          [attr.stroke-width]="labelSize() * 0.3"
          data-testid="stadium-labels"
        >
          @for (name of scene().labels; track name.text) {
            <text [attr.x]="name.x" [attr.y]="name.y">{{ name.text }}</text>
          }
        </g>
      }
    </svg>
  `,
})
export class StadiumPicture {
  /** The stadium level to draw, 1 to 10. */
  readonly level = input.required<number>();

  /** The club's colour: the seats, the boards, the flag. */
  readonly primaryColour = input<string | null | undefined>(null);

  /** The club's second colour: the second band of seats, the trim of the roofs, the frames of the boxes. */
  readonly secondaryColour = input<string | null | undefined>(null);

  /** Whether the picture is purely decorative, because the level is already stated beside it. */
  readonly decorative = input(false);

  /** `low` draws fewer rows, for a small picture. */
  readonly detail = input<SceneDetail>('full');

  /** The kind of place to light up, or null for none. */
  readonly highlight = input<StadiumStandCode | null>(null);

  /** Whether to write the name of each stand beside it. */
  readonly labels = input(false);

  protected readonly primary = computed(() => safeColour(this.primaryColour()));
  protected readonly secondary = computed(() => safeColour(this.secondaryColour(), '#d6e4f0'));
  protected readonly scene = computed(() => buildScene(this.level(), this.detail()));

  protected readonly viewBox = computed(() => {
    const { x, y, width, height } = this.scene().view;

    return `${x} ${y} ${width} ${height}`;
  });

  /** A name is the same size on the screen whatever the frame, so it grows with the part of the drawing shown. */
  protected readonly labelSize = computed(
    () => Math.round(LABEL_SIZE * (this.scene().view.width / VIEW_WIDTH) * 10) / 10,
  );

  protected readonly label = computed(
    () =>
      `Stadium at level ${clampLevel(this.level())} of 10, seen from a corner, with seats in the club's two colours.`,
  );

  protected readonly drawn = computed<readonly DrawnPart[]>(() => {
    const primary = this.primary();
    const secondary = this.secondary();

    const paint = (value: string | null): string | null =>
      value === 'primary' ? primary : value === 'secondary' ? secondary : value;

    return this.scene().parts.map((part) => ({
      role: part.role,
      d: part.d,
      fill: paint(part.fill) ?? 'none',
      stroke: part.role === 'region' ? '#fff3b0' : paint(part.stroke),
      width: part.role === 'region' ? 1.2 : part.width > 0 ? part.width : null,
      dash: part.dash,
      offset: part.offset !== 0 ? part.offset : null,
      opacity: part.opacity,
      region: part.region,
    }));
  });

  /** How strongly a region's plane shows: lit when it is the kind pointed at, invisible otherwise. */
  protected glow(part: DrawnPart): number {
    return part.region !== null && part.region === this.highlight() ? GLOW_OPACITY : 0;
  }
}

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { safeColour } from '../../core/stadium/stadium-canvas';
import { kindLabel } from '../../core/structures/structures-presentation';
import { StructureKind } from '../../core/structures/structures.models';
import {
  STRUCTURE_LEVELS,
  ScenePart,
  buildStructureScene,
  clampStructureLevel,
} from '../../core/structures/structure-scene';

/** One shape of the drawing with its paint resolved, so the template only places it. */
interface DrawnPart {
  readonly role: ScenePart['role'];
  readonly d: string;
  readonly fill: string;
  readonly stroke: string | null;
  readonly width: number | null;
  readonly opacity: number;
}

/**
 * A club structure at one level, drawn in three dimensions (`STRC-1`), as the stadium is.
 *
 * The snack bar or the merchandise shop on its plot, seen from the stadium's corner: the building, its awnings and
 * signs, the tables and parasols or the stall and the shirts, and the fans about it, in the club's own two colours.
 * Each level is the one before with something added, so a manager watches the building grow. The geometry lives in
 * {@link buildStructureScene}; this component only paints it.
 *
 * The picture carries its meaning in words (`aria-label`), and the screen beside it states the level in text.
 */
@Component({
  selector: 'app-structure-picture',
  host: { class: 'block h-full w-full' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.viewBox]="viewBox()"
      role="img"
      [attr.aria-label]="label()"
      [attr.data-kind]="kind()"
      [attr.data-level]="scene().level"
      preserveAspectRatio="xMidYMid meet"
      class="block h-full w-full rounded"
      data-testid="structure-picture"
    >
      @for (part of drawn(); track $index) {
        <path
          [attr.d]="part.d"
          [attr.fill]="part.fill"
          [attr.stroke]="part.stroke"
          [attr.stroke-width]="part.width"
          [attr.opacity]="part.opacity"
          [attr.data-role]="part.role"
        />
      }
    </svg>
  `,
})
export class StructurePicture {
  /** Which building to draw. */
  readonly kind = input.required<StructureKind>();

  /** The building's level, 1 to 10. */
  readonly level = input.required<number>();

  /** The club's colour: the awnings, the signs, the parasols, the shirts. */
  readonly primaryColour = input<string | null | undefined>(null);

  /** The club's second colour: the other stripe of every awning and parasol, the roof trim, the shirts' trim. */
  readonly secondaryColour = input<string | null | undefined>(null);

  protected readonly scene = computed(() => buildStructureScene(this.kind(), this.level()));

  protected readonly viewBox = computed(() => {
    const { x, y, width, height } = this.scene().view;

    return `${x} ${y} ${width} ${height}`;
  });

  protected readonly label = computed(
    () =>
      `${kindLabel(this.kind())} at level ${clampStructureLevel(this.level())} of ${STRUCTURE_LEVELS}, seen from a corner, in the club's two colours.`,
  );

  protected readonly drawn = computed<readonly DrawnPart[]>(() => {
    const primary = safeColour(this.primaryColour());
    const secondary = safeColour(this.secondaryColour(), '#d6e4f0');

    const paint = (value: string | null): string | null =>
      value === 'primary' ? primary : value === 'secondary' ? secondary : value;

    return this.scene().parts.map((part) => ({
      role: part.role,
      d: part.d,
      fill: paint(part.fill) ?? 'none',
      stroke: paint(part.stroke),
      width: part.width > 0 ? part.width : null,
      opacity: part.opacity,
    }));
  });
}

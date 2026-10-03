import { Component, computed, input } from '@angular/core';
import { attributeBand } from '../../../core/squad/squad-presentation';

/**
 * Displays one attribute as a compact tile: its number over a short name.
 *
 * Master plan §11.3 requires an attribute colour to also carry a number, an icon, or text, so the tint is
 * decoration and the number is what communicates the value. The word for the band is still rendered, but
 * only for assistive technology: the tile is too small for it, and the profile shows a legend of the bands
 * above the tiles instead. The full attribute name rides on the tile's tooltip and is read out too.
 */
@Component({
  selector: 'app-attribute-value',
  templateUrl: './attribute-value.html',
  host: { class: 'block min-w-0' },
})
export class AttributeValue {
  /** The displayed attribute value, 1–20 (`TRN-4`). */
  readonly value = input.required<number>();

  /** The attribute's full name. */
  readonly label = input.required<string>();

  /** A short form of the name for the tile; defaults to the full name when none is given. */
  readonly shortLabel = input<string | null>(null);

  /** The band the value falls into, with its word and tint. */
  protected readonly band = computed(() => attributeBand(this.value()));

  /** What the tile shows as its name. */
  protected readonly visibleLabel = computed(() => this.shortLabel() ?? this.label());

  /** The tile's classes. `[class]` replaces the attribute, so everything goes in one string. */
  protected readonly tileClass = computed(
    () =>
      `flex aspect-square w-full flex-col items-center justify-center rounded border border-slate-200 bg-white leading-none ${this.band().className}`,
  );
}

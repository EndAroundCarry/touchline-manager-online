import { Component, computed, input } from '@angular/core';
import { attributeBand } from '../../../core/squad/squad-presentation';

/**
 * Displays one attribute as a compact line: its name and its number.
 *
 * Master plan §11.3 requires an attribute colour to also carry a number, an icon, or text, so the tint is
 * decoration and the number is what communicates the value. The word for the band is still rendered, but
 * only for assistive technology: the profile's four category squares are too narrow for it, so the profile
 * shows a legend of the bands above them instead.
 *
 * The type is deliberately small below the `sm` breakpoint so all four category squares fit side by side
 * across a phone's width.
 */
@Component({
  selector: 'app-attribute-value',
  templateUrl: './attribute-value.html',
  host: { class: 'block' },
})
export class AttributeValue {
  /** The displayed attribute value, 1–20 (`TRN-4`). */
  readonly value = input.required<number>();

  /** The attribute's name. */
  readonly label = input.required<string>();

  /** The band the value falls into, with its word and tint. */
  protected readonly band = computed(() => attributeBand(this.value()));

  /** The number's classes. `[class]` replaces the attribute, so everything goes in one string. */
  protected readonly valueClass = computed(
    () => `shrink-0 text-[0.625rem] font-semibold tabular-nums sm:text-sm ${this.band().className}`,
  );
}

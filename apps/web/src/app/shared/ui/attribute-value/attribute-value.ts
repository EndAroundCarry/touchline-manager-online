import { Component, computed, input } from '@angular/core';
import { attributeBand } from '../../../core/squad/squad-presentation';

/**
 * Displays one attribute: its number, its name, and the word for its band.
 *
 * Master plan §11.3 requires an attribute colour to also carry a number, an icon, or text, so the tint is
 * decoration and the word beside the number is what actually communicates the band. Screen readers get the
 * same information, because the word is rendered rather than hidden.
 */
@Component({
  selector: 'app-attribute-value',
  templateUrl: './attribute-value.html',
})
export class AttributeValue {
  /** The displayed attribute value, 1–20 (`TRN-4`). */
  readonly value = input.required<number>();

  /** The attribute's name. */
  readonly label = input.required<string>();

  /** The band the value falls into, with its word and tint. */
  protected readonly band = computed(() => attributeBand(this.value()));

  /** The number's classes. `[class]` replaces the attribute, so everything goes in one string. */
  protected readonly valueClass = computed(() => `text-base font-semibold ${this.band().className}`);

  /** The band word's classes. */
  protected readonly wordClass = computed(() => `text-xs font-medium ${this.band().className}`);
}

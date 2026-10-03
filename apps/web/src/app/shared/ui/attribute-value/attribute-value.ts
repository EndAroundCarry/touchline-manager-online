import { Component, computed, input } from '@angular/core';
import { attributeBand } from '../../../core/squad/squad-presentation';

/**
 * Displays one attribute as a line: its name on the left, its number and the word for its band on the right.
 *
 * Master plan §11.3 requires an attribute colour to also carry a number, an icon, or text, so the tint is
 * decoration and the number and word are what communicate the band. Below the `sm` breakpoint the four
 * columns of the profile are too narrow to show the word, so it is shown from `sm` up and always read out
 * to assistive technology; the profile's legend gives the bands on a phone.
 *
 * The type is deliberately small below `sm` so all four columns fit side by side across a phone's width.
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
    () => `text-[0.6875rem] font-semibold tabular-nums sm:text-base ${this.band().className}`,
  );

  /** The band word's classes; it appears from `sm` up, where the column is wide enough to hold it. */
  protected readonly wordClass = computed(
    () => `hidden text-xs font-medium sm:inline ${this.band().className}`,
  );
}

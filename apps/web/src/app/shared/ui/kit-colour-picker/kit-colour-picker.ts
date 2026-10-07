import { Component, computed, input, model, signal } from '@angular/core';
import { TEXT_INPUT } from '../../forms/control-styles';
import { isKitPair, KIT_PRESETS, normaliseHex, readableOn } from './kit-colours';

/** Which of the two colours a control belongs to. */
type Slot = 'primary' | 'secondary';

/**
 * Chooses the two colours a club plays in.
 *
 * Each colour has the browser's own colour picker, which offers the whole spectrum, a hex field for a colour
 * that is already known, and a row of shortcut swatches for the usual ones. A shirt beside them shows the
 * pair as it will be worn — the first colour as the body, the second as the sleeves and collar — so a
 * manager judges the combination and not two chips in isolation.
 *
 * The component only edits the pair. Saving it, and what happens afterwards, belong to the screen that
 * hosts it.
 */
@Component({
  selector: 'app-kit-colour-picker',
  templateUrl: './kit-colour-picker.html',
})
export class KitColourPicker {
  /** The main colour as `#rrggbb`. */
  readonly primary = model.required<string>();

  /** The second colour as `#rrggbb`. */
  readonly secondary = model.required<string>();

  /** Whether the picker is read-only, for example while a save is under way. */
  readonly disabled = input(false);

  /** The text a manager is part-way through typing, per colour, until it is a whole colour. */
  protected readonly typed = signal<Record<Slot, string | null>>({
    primary: null,
    secondary: null,
  });

  /** The two colours, in the order they are asked for. */
  protected readonly slots: readonly { slot: Slot; label: string; hint: string }[] = [
    {
      slot: 'primary',
      label: 'Main colour',
      hint: 'The shirt, and the colour your team is known by.',
    },
    {
      slot: 'secondary',
      label: 'Second colour',
      hint: 'The sleeves and collar. It must differ from the main colour.',
    },
  ];

  protected readonly presets = KIT_PRESETS;
  protected readonly textInputClass = TEXT_INPUT;

  /** Whether the pair can be saved: two valid colours that differ. */
  readonly valid = computed(() => isKitPair(this.primary(), this.secondary()));

  /** Whether the two colours are the same, which is the one mistake worth naming. */
  protected readonly sameColour = computed(
    () =>
      normaliseHex(this.primary()) !== null &&
      normaliseHex(this.primary()) === normaliseHex(this.secondary()),
  );

  /** The text shown in a colour's hex field: what is being typed, or the colour itself. */
  protected hexText(slot: Slot): string {
    return this.typed()[slot] ?? this.valueOf(slot);
  }

  /** Whether the text in a colour's hex field is not yet a whole colour. */
  protected hexInvalid(slot: Slot): boolean {
    const text = this.typed()[slot];

    return text !== null && normaliseHex(text) === null;
  }

  /** The label colour that stays readable on a swatch. */
  protected readable(colour: string): string {
    return readableOn(colour);
  }

  /** Adopts a colour from the spectrum picker or a swatch. */
  protected choose(slot: Slot, value: string): void {
    const colour = normaliseHex(value);

    if (colour === null) {
      return;
    }

    this.typed.update((state) => ({ ...state, [slot]: null }));
    this.set(slot, colour);
  }

  /** Adopts what was typed once it is a whole colour, and holds it as text until then. */
  protected type(slot: Slot, value: string): void {
    const colour = normaliseHex(value);

    if (colour === null) {
      this.typed.update((state) => ({ ...state, [slot]: value }));

      return;
    }

    this.choose(slot, colour);
  }

  /** Puts a half-typed field back to the colour in use when it is left. */
  protected settle(slot: Slot): void {
    this.typed.update((state) => ({ ...state, [slot]: null }));
  }

  private valueOf(slot: Slot): string {
    return slot === 'primary' ? this.primary() : this.secondary();
  }

  private set(slot: Slot, colour: string): void {
    if (slot === 'primary') {
      this.primary.set(colour);
    } else {
      this.secondary.set(colour);
    }
  }
}

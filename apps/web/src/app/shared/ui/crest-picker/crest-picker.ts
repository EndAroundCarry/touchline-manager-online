import { Component, input, model } from '@angular/core';
import { SELECT_INPUT } from '../../forms/control-styles';
import { ClubCrest } from '../club-crest/club-crest';
import { CREST_PATTERNS, CREST_SHAPES } from '../club-crest/crest-designs';

/** Gives each picker on a page its own element ids, so two of them never share a label. */
let nextPickerId = 0;

/**
 * Chooses a club's crest: a shape from one dropdown, and the stripes it wears from another.
 *
 * The two are independent, so any shape can wear any stripes. A crest beside the dropdowns shows the pair in
 * the club's own two colours, so a manager judges the crest as it will be worn.
 *
 * The component only edits the choice. Saving it belongs to the screen that hosts it.
 */
@Component({
  selector: 'app-crest-picker',
  imports: [ClubCrest],
  templateUrl: './crest-picker.html',
})
export class CrestPicker {
  /** The chosen shape's code. */
  readonly shape = model.required<string>();

  /** The chosen stripes' code. */
  readonly pattern = model.required<string>();

  /** The club's main colour as `#rrggbb`, which the crest is drawn in. */
  readonly primary = input.required<string>();

  /** The club's second colour as `#rrggbb`. */
  readonly secondary = input.required<string>();

  /** Whether the picker is read-only, for example while a save is under way. */
  readonly disabled = input(false);

  protected readonly shapes = CREST_SHAPES;
  protected readonly patterns = CREST_PATTERNS;
  protected readonly selectClass = SELECT_INPUT;
  protected readonly shapeId = `crest-shape-${nextPickerId}`;
  protected readonly patternId = `crest-pattern-${nextPickerId++}`;
}

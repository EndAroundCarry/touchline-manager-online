import { Component, computed, input } from '@angular/core';
import {
  CREST_BORDER,
  CREST_HEIGHT,
  CREST_WIDTH,
  CrestPiece,
  crestPatternFor,
  crestShapeFor,
} from './crest-designs';

/** Gives each crest on a page its own clip, because two clips sharing an id would clip one another's art. */
let nextCrestId = 0;

/**
 * Draws a club's crest: a shape wearing a kind of stripes, in the club's two colours.
 *
 * The size is whatever the host gives it (`class="h-16"` is enough); the drawing keeps its proportions. It is
 * a picture and not a control, so the host supplies any label that matters; `label` is for when the crest
 * stands alone, and an empty one hides it from assistive technology when a name sits beside it already.
 */
@Component({
  selector: 'app-club-crest',
  templateUrl: './club-crest.html',
  host: { class: 'inline-block leading-none' },
})
export class ClubCrest {
  /** The shape's code, as the server stores it. */
  readonly shape = input.required<string>();

  /** The stripes' code, as the server stores it. */
  readonly pattern = input.required<string>();

  /** The field colour as `#rrggbb`. */
  readonly primary = input.required<string>();

  /** The colour laid over the field, and the border, as `#rrggbb`. */
  readonly secondary = input.required<string>();

  /** What assistive technology announces. Leave empty when the crest decorates a name beside it. */
  readonly label = input('');

  protected readonly clipId = `crest-clip-${nextCrestId++}`;
  protected readonly box = `0 0 ${CREST_WIDTH} ${CREST_HEIGHT}`;
  protected readonly width = CREST_WIDTH;
  protected readonly height = CREST_HEIGHT;
  protected readonly border = CREST_BORDER;

  /** The outline of the shape. */
  protected readonly outline = computed(() => crestShapeFor(this.shape()).path);

  /** What is laid over the field. */
  protected readonly pieces = computed<readonly CrestPiece[]>(
    () => crestPatternFor(this.pattern()).pieces,
  );
}

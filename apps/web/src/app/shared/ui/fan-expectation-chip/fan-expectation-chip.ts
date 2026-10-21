import { Component, computed, input } from '@angular/core';
import { FanExpectation } from '../../../core/fans/fan.models';
import {
  chancesLabel,
  expectationToneClass,
  expectationTone,
  predictedScore,
} from '../../../core/fans/fan-presentation';

/**
 * What the fans expect from a match, as a chip (`FAN-4`): the label in words, and under it the chances they give
 * and the score they would bet on.
 *
 * The tint follows the colours the fixtures screen gives a win, a draw and a defeat, but the label is always
 * written out, so the colour is never the only signal. The score is the club's goals first, the way every other
 * screen lists a fixture.
 */
@Component({
  selector: 'app-fan-expectation-chip',
  templateUrl: './fan-expectation-chip.html',
  host: { class: 'inline-flex flex-col items-start gap-1' },
})
export class FanExpectationChip {
  /** What the fans expect. */
  readonly expectation = input.required<FanExpectation>();

  /** Whether to print the chances and the predicted score under the label. */
  readonly detail = input(true);

  /** Whether the expectation is final. A live one can still move until the team sheets lock. */
  readonly frozen = input<boolean | null>(null);

  protected readonly toneClass = computed(() =>
    expectationToneClass(expectationTone(this.expectation().labelCode)),
  );
  protected readonly chances = computed(() => chancesLabel(this.expectation()));
  protected readonly score = computed(() => predictedScore(this.expectation()));
}

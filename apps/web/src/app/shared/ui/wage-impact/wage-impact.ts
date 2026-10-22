import { Component, computed, input } from '@angular/core';
import {
  moneyToneClass,
  redFromSentence,
  wageImpactHeadline,
  wageImpactTone,
} from '../../../core/finance/finance-presentation';
import { WageImpact } from '../../../core/finance/finance.models';
import { Money } from '../money/money';

/**
 * What a deal would do to the viewer's club, as the server worked it out (`WAGE-1`, plan toasty-scott section 5).
 *
 * A level in words (never colour alone), and under it the wage bill after the deal against the budget, the cash
 * at the season's end after the fee, and when the club would first be in the red. It opens by itself when the
 * level asks for a confirm tick, and stays folded when the deal fits, so a table of bids is not a wall of figures.
 * It is a warning, never a block: the deal goes ahead whatever the level.
 */
@Component({
  selector: 'app-wage-impact',
  imports: [Money],
  templateUrl: './wage-impact.html',
})
export class WageImpactPanel {
  /** The impact the server computed for the viewer's club. */
  readonly impact = input.required<WageImpact>();

  /** What the deal is, so the panel can say "this signing" or "this renewal". */
  readonly subject = input('signing');

  protected readonly headline = computed(() => wageImpactHeadline(this.impact().level));
  protected readonly toneClass = computed(() =>
    moneyToneClass(wageImpactTone(this.impact().level)),
  );
  protected readonly red = computed(() => redFromSentence(this.impact()));
  protected readonly open = computed(() => this.impact().needsConfirmation);
  protected readonly share = computed(() => Math.round(this.impact().wageShareBp / 100));
}

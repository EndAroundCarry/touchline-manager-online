import { Component, computed, input } from '@angular/core';
import { formatFunds, formatFundsCompact } from '../../../core/world/presentation';

/**
 * An amount of money, shown short with the full figure on hover (plan toasty-scott, M7).
 *
 * At the real-football scale a ledger figure is eight or nine digits, which no card can show whole. The short
 * form (12.4M, 850K) is what the eye reads; the exact amount is the `title`, and a screen reader is given it too,
 * so nothing is rounded without the exact figure being reachable. A signed amount carries a plus or a minus as
 * text, so a debit and a credit never differ by colour alone (`§11.3`).
 */
@Component({
  selector: 'app-money',
  template: `<span [attr.title]="full()" data-testid="money"
    >{{ sign() }}{{ short() }}<span class="sr-only"> ({{ full() }})</span></span
  >`,
})
export class Money {
  /** The amount, in minor units. */
  readonly minor = input.required<number>();

  /** Whether to print a plus or a minus in front, for a movement rather than a balance. */
  readonly signed = input(false);

  protected readonly short = computed(() => formatFundsCompact(this.magnitude()));
  protected readonly full = computed(() => formatFunds(this.magnitude()));
  protected readonly sign = computed(() => {
    const value = this.minor();

    if (!this.signed()) {
      return value < 0 ? '−' : '';
    }

    return value === 0 ? '' : value > 0 ? '+' : '−';
  });

  private magnitude(): number {
    return Math.abs(this.minor());
  }
}

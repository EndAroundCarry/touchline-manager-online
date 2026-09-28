import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FinanceStore } from '../../core/finance/finance-store';
import { creditsOf, debitsOf, signedAmount } from '../../core/finance/finance-presentation';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  PAGE_HEADING,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';

/**
 * The finance screen (master plan §11.1; `FIN-3`…`FIN-9`).
 *
 * It shows what the ledger says and nothing it does not: the balances are the running total of the entries
 * below, the weekly figures are the rule-set values for the club's tier, and the season totals are the
 * categories the entries fall into. Every movement is a line, so a manager can follow a balance back to the
 * decision that produced it.
 */
@Component({
  selector: 'app-finances',
  imports: [RouterLink],
  templateUrl: './finances.html',
})
export class Finances {
  private readonly store = inject(FinanceStore);

  protected readonly summary = this.store.summary;
  protected readonly ledger = this.store.ledger;
  protected readonly hasMore = this.store.hasMore;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly error = this.store.error;

  /** The season's money in, labelled. */
  protected readonly credits = computed(() => creditsOf(this.summary()?.totals ?? []));

  /** The season's money out, labelled. */
  protected readonly debits = computed(() => debitsOf(this.summary()?.totals ?? []));

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;

  constructor() {
    this.store.load();
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats a signed movement so a debit and a credit differ by more than colour. */
  protected signed(minorUnits: number): string {
    return signedAmount(minorUnits, formatFunds);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** Reads the next page of the ledger. */
  protected loadMore(): void {
    this.store.loadMore();
  }
}

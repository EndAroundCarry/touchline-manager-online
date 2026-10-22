import { Component, computed, input, output, signal } from '@angular/core';
import { TransferListing } from '../../../core/transfers/transfers.models';
import {
  CHECKBOX_INPUT,
  CHECKBOX_ROW,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../forms/control-styles';
import { WageImpactPanel } from '../wage-impact/wage-impact';

/**
 * A bid on a listing, with what the signing would do to the club's wage bill under it (`WAGE-1`).
 *
 * The same form serves a club's listing and a free agent, in a table row or on a phone's card. The server works out
 * the wage impact for the viewer's club; when it asks for a confirm tick (the club would run out of cash, or end the
 * season past its overdraft limit) the bid waits for the tick. The server never refuses a bid for it, so the tick is
 * the form's own courtesy, and the only things that stop a bid are the cash for the fee and the embargo (`DEBT-3`).
 */
@Component({
  selector: 'app-bid-form',
  imports: [WageImpactPanel],
  templateUrl: './bid-form.html',
})
export class BidForm {
  /** The listing bid on. */
  readonly listing = input.required<TransferListing>();

  /** Prefixes the amount field's id, so a table row and a card for the same listing never share one. */
  readonly idPrefix = input('bid-');

  /** Whether writes are refused (offline, or read-only). */
  readonly disabled = input(false);

  /** The amount the manager bid, in minor units. */
  readonly bid = output<number>();

  protected readonly confirmed = signal(false);

  protected readonly fieldId = computed(() => `${this.idPrefix()}${this.listing().listingId}`);
  protected readonly confirmId = computed(() => `${this.fieldId()}-confirm`);
  protected readonly needsConfirmation = computed(
    () => this.listing().wageImpact?.needsConfirmation === true,
  );
  protected readonly blocked = computed(() => this.needsConfirmation() && !this.confirmed());

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly checkboxClass = CHECKBOX_INPUT;
  protected readonly checkboxRowClass = CHECKBOX_ROW;

  /** Records the confirm tick. */
  protected confirm(event: Event): void {
    this.confirmed.set((event.target as HTMLInputElement).checked);
  }

  /** Places the bid, unless the wage impact is waiting for its confirm tick. */
  protected submit(event: Event): void {
    event.preventDefault();

    if (this.blocked()) {
      return;
    }

    const data = new FormData(event.target as HTMLFormElement);

    this.bid.emit(Number(data.get('amount') ?? 0));
  }
}

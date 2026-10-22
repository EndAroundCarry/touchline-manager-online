import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LINK } from '../../forms/control-styles';

/**
 * Why a bid or a building order was refused while the club is in the red, and where to see it (`DEBT-3`).
 *
 * It sits under the refusal the server worded, so a manager who is stopped is told what ends the stop rather than
 * only that they were stopped. No interest is charged and the fans do not mind; the embargo is the whole cost.
 */
@Component({
  selector: 'app-embargo-notice',
  imports: [RouterLink],
  template: `
    <p class="mt-2 text-sm text-ink-2" data-testid="embargo-notice">
      Bids for players and new building are stopped while the club's cash is below zero. It ends by
      itself when the cash is back above zero.
      <a routerLink="/finances" [class]="linkClass">See your finances</a>
    </p>
  `,
})
export class EmbargoNotice {
  protected readonly linkClass = LINK;
}

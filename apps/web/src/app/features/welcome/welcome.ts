import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

/** The six fictional national pyramids, in the product's display order (WORLD-2). */
const LAUNCH_COUNTRIES = ['England', 'Spain', 'Germany', 'Italy', 'France', 'Romania'] as const;

/**
 * Public landing page.
 *
 * It states the game's contract plainly: three matchdays a week, prepare before a deadline, results
 * decided by the server. Nothing here promises a feature that has not shipped.
 */
@Component({
  selector: 'app-welcome',
  imports: [RouterLink, ButtonModule],
  templateUrl: './welcome.html',
})
export class Welcome {
  protected readonly countries = LAUNCH_COUNTRIES;

  protected readonly loop = [
    'Take over a club that is already running: its squad, contracts, cash, and table position are inherited exactly as they are.',
    'Set your lineup, formation, roles, and instructions before the deadline.',
    'Train, renew contracts, and work the transfer market between matchdays.',
    'The server locks your team sheet thirty minutes before kick-off and plays the match without you.',
    'Read the report, watch the highlights, and adjust before the next one.',
  ];
}
